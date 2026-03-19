using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameProto.Regicide;
using TEngine;
using UnityEngine;

namespace GameLogic.Regicide
{
    public sealed class RegicideBattleModule : Singleton<RegicideBattleModule>
    {
        private RegicideRuntimeConfig _runtimeConfig;
        private RegicideBattleState _state;
        private string _localPlayerId = string.Empty;
        private RegicidePublicStateSnapshotPayload _publicStateSnapshot = new RegicidePublicStateSnapshotPayload();
        private readonly List<RegicideActionBroadcastPayload> _actionBroadcasts = new List<RegicideActionBroadcastPayload>();
        private readonly HashSet<long> _actionSequenceSet = new HashSet<long>();
        private long _latestActionSequence;

        public RegicideBattleState State => _state;
        public string LocalPlayerId => _localPlayerId;
        public RegicidePublicStateSnapshotPayload PublicStateSnapshot => _publicStateSnapshot;
        public IReadOnlyList<RegicideActionBroadcastPayload> ActionBroadcasts => _actionBroadcasts;
        public bool HasState => _state != null;
        public bool IsMyTurn => _state != null &&
                                _state.CurrentPlayerIndex >= 0 &&
                                _state.CurrentPlayerIndex < _state.Players.Count &&
                                _state.Players[_state.CurrentPlayerIndex].PlayerId == _localPlayerId;

        protected override void OnInit()
        {
            GameEvent.AddEventListener<RegicideStateSnapshotPayload>(RegicideEventIds.BattleSnapshotUpdated, OnRemoteBattleSnapshot);
            GameEvent.AddEventListener<RegicidePublicStateSnapshotPayload>(RegicideEventIds.PublicStateSnapshotUpdated, OnPublicStateSnapshotUpdated);
            GameEvent.AddEventListener<RegicideActionBroadcastPayload>(RegicideEventIds.ActionBroadcastReceived, OnActionBroadcastReceived);
        }

        protected override void OnRelease()
        {
            GameEvent.RemoveEventListener<RegicideStateSnapshotPayload>(RegicideEventIds.BattleSnapshotUpdated, OnRemoteBattleSnapshot);
            GameEvent.RemoveEventListener<RegicidePublicStateSnapshotPayload>(RegicideEventIds.PublicStateSnapshotUpdated, OnPublicStateSnapshotUpdated);
            GameEvent.RemoveEventListener<RegicideActionBroadcastPayload>(RegicideEventIds.ActionBroadcastReceived, OnActionBroadcastReceived);
            ResetMultiplayerViewState(clearPublicSnapshot: true);
        }

        public void Setup(RegicideRuntimeConfig runtimeConfig)
        {
            _runtimeConfig = runtimeConfig;
            _localPlayerId = runtimeConfig.PlayerId;
            _state = null;
            ResetMultiplayerViewState(clearPublicSnapshot: true);
        }

        public bool TryStartLocalSinglePlayer(string sessionId = "LOCAL-REGICIDE")
        {
            if (_runtimeConfig == null || _runtimeConfig.Environment != RegicideClientEnvironment.LocalSinglePlayer)
            {
                return false;
            }

            IRegicideRuleConfigProvider configProvider = RegicideRuleConfigProvider.Instance;
            List<string> players = new List<string> { _localPlayerId };
            _state = RegicideBattleInitializer.Create(sessionId, _runtimeConfig.RuleId, _runtimeConfig.RandomSeed, players, configProvider);
            if (_state == null)
            {
                return false;
            }

            PublishLocalSnapshot();
            return true;
        }

        public UniTask<bool> PlayCardAsync(int cardIndex, CancellationToken cancellationToken = default)
        {
            List<int> selection = new List<int> { cardIndex };
            return PlayCardAsync(selection, -1, cancellationToken);
        }

        public async UniTask<bool> PlayCardAsync(
            IList<int> cardIndices,
            int nextPlayerIndex = -1,
            CancellationToken cancellationToken = default)
        {
            if (_runtimeConfig.Environment == RegicideClientEnvironment.LocalSinglePlayer)
            {
                return ApplyLocalAction(RegicideActionType.PlayCard, cardIndices, nextPlayerIndex);
            }

            if (_state == null)
            {
                return false;
            }

            return await RegicideNetworkModule.Instance.PlayCardAsync(
                _state.SessionId,
                _localPlayerId,
                cardIndices,
                nextPlayerIndex,
                cancellationToken);
        }

        public async UniTask<bool> PassAsync(CancellationToken cancellationToken = default)
        {
            if (_runtimeConfig.Environment == RegicideClientEnvironment.LocalSinglePlayer)
            {
                return ApplyLocalAction(RegicideActionType.Pass, null, -1);
            }

            if (_state == null)
            {
                return false;
            }

            return await RegicideNetworkModule.Instance.PassAsync(_state.SessionId, _localPlayerId, cancellationToken);
        }

        public async UniTask<bool> DiscardForDamageAsync(IList<int> cardIndices, CancellationToken cancellationToken = default)
        {
            if (_runtimeConfig.Environment == RegicideClientEnvironment.LocalSinglePlayer)
            {
                return ApplyLocalAction(RegicideActionType.Discard, cardIndices, -1);
            }

            if (_state == null)
            {
                return false;
            }

            return await RegicideNetworkModule.Instance.DiscardForDamageAsync(_state.SessionId, _localPlayerId, cardIndices, cancellationToken);
        }

        public UniTask<bool> DefendAsync(CancellationToken cancellationToken = default)
        {
            // 保留旧接口以兼容历史调用，转发为“弃牌承伤”动作（无选牌时会被规则校验拒绝）。
            return DiscardForDamageAsync(null, cancellationToken);
        }

        public RegicidePlayerState GetLocalPlayerState()
        {
            if (_state == null || string.IsNullOrEmpty(_localPlayerId))
            {
                return null;
            }

            for (int i = 0; i < _state.Players.Count; i++)
            {
                RegicidePlayerState player = _state.Players[i];
                if (player != null && player.PlayerId == _localPlayerId)
                {
                    return player;
                }
            }

            return null;
        }

        public IReadOnlyList<RegicidePublicPlayerState> GetOtherPlayersPublicStates()
        {
            if (_publicStateSnapshot == null || _publicStateSnapshot.Players == null || _publicStateSnapshot.Players.Count <= 0)
            {
                return System.Array.Empty<RegicidePublicPlayerState>();
            }

            List<RegicidePublicPlayerState> others = new List<RegicidePublicPlayerState>();
            for (int i = 0; i < _publicStateSnapshot.Players.Count; i++)
            {
                RegicidePublicPlayerState player = _publicStateSnapshot.Players[i];
                if (player == null || string.Equals(player.PlayerId, _localPlayerId))
                {
                    continue;
                }

                others.Add(player);
            }

            return others;
        }

        public RegicideAvailableActionSnapshot GetAvailableActionSnapshot(int preferredCardIndex = -1)
        {
            List<int> preferred = preferredCardIndex >= 0 ? new List<int> { preferredCardIndex } : null;
            return BuildAvailableActionSnapshot(preferred, -1);
        }

        public RegicideAvailableActionSnapshot GetAvailableActionSnapshot(IList<int> preferredCardIndices, int preferredNextPlayerIndex = -1)
        {
            return BuildAvailableActionSnapshot(preferredCardIndices, preferredNextPlayerIndex);
        }

        private bool ApplyLocalAction(RegicideActionType actionType, IList<int> cardIndices, int nextPlayerIndex)
        {
            if (_state == null)
            {
                return false;
            }

            RegicideRuleConfig config = RegicideRuleConfigProvider.Instance.GetByRuleId(_state.RuleId);
            RegicidePlayerAction action = new RegicidePlayerAction
            {
                SessionId = _state.SessionId,
                PlayerId = _localPlayerId,
                ActionType = actionType,
                CardIndex = -1,
                CardIndices = RegicideActionValidator.NormalizeCardIndices(cardIndices),
                NextPlayerIndex = nextPlayerIndex,
                ClientSequence = _state.AppliedSequence + 1,
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };

            if (action.CardIndices.Count == 1)
            {
                action.CardIndex = action.CardIndices[0];
            }

            RegicideActionResult result = RegicideBattleResolver.ApplyAction(_state, action, config);
            if (!result.Success)
            {
                GameEvent.Send(RegicideEventIds.BattleErrorReceived, new RegicideErrorPayload
                {
                    SessionId = _state.SessionId,
                    Code = RegicideErrorCode.InvalidAction,
                    Message = result.Error,
                    RelatedSequence = action.ClientSequence,
                    Timestamp = RegicideClock.NowUnixMilliseconds(),
                });
                return false;
            }

            _state = result.State;
            PublishLocalSnapshot();
            return true;
        }

        private RegicideAvailableActionSnapshot BuildAvailableActionSnapshot(IList<int> preferredCardIndices, int preferredNextPlayerIndex)
        {
            RegicideAvailableActionSnapshot snapshot = new RegicideAvailableActionSnapshot();
            if (_state == null)
            {
                snapshot.Message = "状态未初始化。";
                return snapshot;
            }

            if (_state.IsGameOver)
            {
                snapshot.Message = _state.IsVictory ? "对局已胜利。" : "对局已失败。";
                return snapshot;
            }

            RegicidePlayerState localPlayer = GetLocalPlayerState();
            if (localPlayer == null)
            {
                snapshot.Message = "未找到本地玩家。";
                return snapshot;
            }

            List<int> selected = RegicideActionValidator.NormalizeCardIndices(preferredCardIndices);
            snapshot.SelectedCardIndices.AddRange(selected);
            snapshot.SelectedCardIndex = selected.Count > 0 ? selected[0] : -1;
            snapshot.SuggestedNextPlayerIndex = ResolveSuggestedNextPlayer(preferredNextPlayerIndex);

            for (int i = 0; i < _state.Players.Count; i++)
            {
                snapshot.SelectableNextPlayerIndices.Add(i);
            }

            if (!IsMyTurn)
            {
                snapshot.Message = "等待其他玩家行动。";
                return snapshot;
            }

            snapshot.IsAwaitingDiscard = _state.IsAwaitingDiscard;
            snapshot.PendingDiscardRequiredValue = _state.PendingDiscardRequiredValue;
            snapshot.PendingDiscardTargetPlayerIndex = _state.PendingDiscardTargetPlayerIndex;

            if (_state.IsAwaitingDiscard)
            {
                for (int i = 0; i < localPlayer.Hand.Count; i++)
                {
                    snapshot.PlayableCardIndices.Add(i);
                }

                snapshot.CanPass = false;
                snapshot.CanPlayCard = false;
                snapshot.IsCurrentSelectionJester = false;

                if (selected.Count <= 0)
                {
                    snapshot.IsCurrentSelectionPlayable = false;
                    snapshot.CurrentSelectionAttackValue = 0;
                    snapshot.CurrentSelectionDamageValue = 0;
                    snapshot.CanDefend = false;
                    snapshot.Message = $"请选择弃牌承伤（至少 {_state.PendingDiscardRequiredValue} 点）。";
                    return snapshot;
                }

                if (RegicideActionValidator.CanDiscardForDamage(_state, localPlayer, selected, out int discardValue, out string discardError))
                {
                    snapshot.IsCurrentSelectionPlayable = true;
                    snapshot.CurrentSelectionAttackValue = discardValue;
                    snapshot.CurrentSelectionDamageValue = discardValue;
                    snapshot.CanDefend = true;
                    snapshot.Message = $"可承伤：{discardValue}/{_state.PendingDiscardRequiredValue}。";
                }
                else
                {
                    snapshot.IsCurrentSelectionPlayable = false;
                    snapshot.CurrentSelectionAttackValue = discardValue;
                    snapshot.CurrentSelectionDamageValue = discardValue;
                    snapshot.CanDefend = false;
                    snapshot.Message = discardError;
                }

                return snapshot;
            }

            for (int i = 0; i < localPlayer.Hand.Count; i++)
            {
                snapshot.PlayableCardIndices.Add(i);
            }

            snapshot.CanPass = RegicideActionValidator.CanPass(_state, out _);
            snapshot.CanDefend = false;

            if (selected.Count <= 0)
            {
                snapshot.CanPlayCard = false;
                snapshot.IsCurrentSelectionPlayable = false;
                snapshot.IsCurrentSelectionJester = false;
                snapshot.CurrentSelectionAttackValue = 0;
                snapshot.CurrentSelectionDamageValue = 0;
                snapshot.Message = "请选择要打出的牌。";
                return snapshot;
            }

            if (RegicideActionValidator.CanPlayCard(_state, localPlayer, selected, out int attackValue, out bool isJester, out string validationError))
            {
                snapshot.CanPlayCard = true;
                snapshot.IsCurrentSelectionPlayable = true;
                snapshot.IsCurrentSelectionJester = isJester;
                snapshot.CurrentSelectionAttackValue = attackValue;
                snapshot.CurrentSelectionDamageValue = attackValue;
                snapshot.Message = "请选择行动。";
            }
            else
            {
                snapshot.CanPlayCard = false;
                snapshot.IsCurrentSelectionPlayable = false;
                snapshot.IsCurrentSelectionJester = false;
                snapshot.CurrentSelectionAttackValue = 0;
                snapshot.CurrentSelectionDamageValue = 0;
                snapshot.Message = validationError;
            }

            if (selected.Count > 0)
            {
                int first = selected[0];
                if (first >= 0 && first < localPlayer.Hand.Count)
                {
                    snapshot.SelectedCardDamage = localPlayer.Hand[first].DamageValue;
                }
            }

            return snapshot;
        }

        private int ResolveSuggestedNextPlayer(int preferredNextPlayerIndex)
        {
            if (_state == null || _state.Players.Count <= 0)
            {
                return -1;
            }

            if (preferredNextPlayerIndex >= 0 && preferredNextPlayerIndex < _state.Players.Count)
            {
                return preferredNextPlayerIndex;
            }

            return (_state.CurrentPlayerIndex + 1) % _state.Players.Count;
        }

        private void PublishLocalSnapshot()
        {
            if (_state == null)
            {
                return;
            }

            RegicideStateSnapshotPayload payload = new RegicideStateSnapshotPayload
            {
                SessionId = _state.SessionId,
                ServerSequence = _state.AppliedSequence,
                IsGameOver = _state.IsGameOver,
                IsVictory = _state.IsVictory,
                CurrentPlayerIndex = _state.CurrentPlayerIndex,
                StateHash = RegicideStateHasher.ComputeHash(_state),
                StateJson = JsonUtility.ToJson(_state),
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };

            GameEvent.Send(RegicideEventIds.BattleSnapshotUpdated, payload);
        }

        private void OnRemoteBattleSnapshot(RegicideStateSnapshotPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.StateJson))
            {
                return;
            }

            if (_state != null &&
                !string.IsNullOrEmpty(_state.SessionId) &&
                !string.IsNullOrEmpty(payload.SessionId) &&
                !string.Equals(_state.SessionId, payload.SessionId))
            {
                ResetMultiplayerViewState(clearPublicSnapshot: true);
            }

            RegicideBattleState remoteState = JsonUtility.FromJson<RegicideBattleState>(payload.StateJson);
            if (remoteState != null)
            {
                _state = remoteState;
            }
        }

        private void OnPublicStateSnapshotUpdated(RegicidePublicStateSnapshotPayload payload)
        {
            if (payload == null)
            {
                _publicStateSnapshot = new RegicidePublicStateSnapshotPayload();
                ResetMultiplayerViewState(clearPublicSnapshot: false);
                return;
            }

            bool switchedSession = !string.IsNullOrEmpty(_publicStateSnapshot.SessionId) &&
                                   !string.IsNullOrEmpty(payload.SessionId) &&
                                   !string.Equals(_publicStateSnapshot.SessionId, payload.SessionId);
            if (switchedSession)
            {
                ResetMultiplayerViewState(clearPublicSnapshot: false);
            }

            _publicStateSnapshot = payload;
            if (_latestActionSequence < payload.ServerSequence)
            {
                _latestActionSequence = payload.ServerSequence;
            }
        }

        private void OnActionBroadcastReceived(RegicideActionBroadcastPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.SessionId))
            {
                return;
            }

            if (_actionSequenceSet.Contains(payload.ServerSequence))
            {
                return;
            }

            if (!string.IsNullOrEmpty(_publicStateSnapshot.SessionId) &&
                !string.Equals(_publicStateSnapshot.SessionId, payload.SessionId))
            {
                ResetMultiplayerViewState(clearPublicSnapshot: true);
            }

            _actionSequenceSet.Add(payload.ServerSequence);
            _actionBroadcasts.Add(payload);
            if (_actionBroadcasts.Count > 160)
            {
                RegicideActionBroadcastPayload evicted = _actionBroadcasts[0];
                _actionBroadcasts.RemoveAt(0);
                _actionSequenceSet.Remove(evicted.ServerSequence);
            }

            if (_latestActionSequence < payload.ServerSequence)
            {
                _latestActionSequence = payload.ServerSequence;
            }
        }

        private void ResetMultiplayerViewState(bool clearPublicSnapshot)
        {
            _actionBroadcasts.Clear();
            _actionSequenceSet.Clear();
            _latestActionSequence = 0;

            if (clearPublicSnapshot)
            {
                _publicStateSnapshot = new RegicidePublicStateSnapshotPayload();
            }
        }
    }
}
