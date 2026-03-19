using System.Collections.Generic;
using System.Text;
using GameProto.Regicide;
using TEngine;
using UnityEngine;

namespace GameLogic.Regicide
{
    /// <summary>
    /// Runs authoritative battle logic when the process hosts a server.
    /// </summary>
    public sealed class RegicideAuthorityModule : Singleton<RegicideAuthorityModule>
    {
        private sealed class AuthoritySession
        {
            public string SessionId;
            public int RuleId;
            public int Seed;
            public int MaxPlayers;
            public int TargetPlayers;
            public List<string> PlayerIds = new List<string>();
            public HashSet<string> ReadyPlayers = new HashSet<string>();
            public RegicideBattleState State;
            public long ServerSequence;
        }

        private readonly Dictionary<string, AuthoritySession> _sessions = new Dictionary<string, AuthoritySession>();

        protected override void OnInit()
        {
            GameEvent.AddEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ServerIntentReceived, OnServerIntentReceived);
        }

        protected override void OnRelease()
        {
            GameEvent.RemoveEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ServerIntentReceived, OnServerIntentReceived);
            _sessions.Clear();
        }

        private void OnServerIntentReceived(RegicideClientIntentPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.SessionId))
            {
                return;
            }

            AuthoritySession session = GetOrCreateSession(payload.SessionId);
            switch (payload.IntentType)
            {
                case RegicideClientIntentType.JoinRoom:
                    HandleJoin(session, payload);
                    break;

                case RegicideClientIntentType.LeaveRoom:
                    HandleLeave(session, payload);
                    break;

                case RegicideClientIntentType.Ready:
                    HandleReady(session, payload);
                    break;

                case RegicideClientIntentType.CancelReady:
                    HandleCancelReady(session, payload);
                    break;

                case RegicideClientIntentType.ConfigureRoom:
                    HandleConfigureRoom(session, payload);
                    break;

                case RegicideClientIntentType.StartMatch:
                    HandleStart(session, payload);
                    break;

                case RegicideClientIntentType.PlayCard:
                case RegicideClientIntentType.Pass:
                case RegicideClientIntentType.Defend:
                case RegicideClientIntentType.DiscardForDamage:
                    HandleBattleIntent(session, payload);
                    break;

                default:
                    PublishError(payload.SessionId, RegicideErrorCode.InvalidAction, "Unsupported intent type.", payload.ClientSequence);
                    break;
            }
        }

        private AuthoritySession GetOrCreateSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out AuthoritySession session))
            {
                return session;
            }

            RegicideRuntimeConfig runtime = RegicideRuntimeConfig.Load();
            RegicideRuleConfig rule = RegicideRuleConfigProvider.Instance.GetByRuleId(runtime.RuleId);
            int maxPlayers = Mathf.Clamp(rule != null ? rule.MaxPlayers : 4, 1, 4);

            session = new AuthoritySession
            {
                SessionId = sessionId,
                RuleId = runtime.RuleId,
                Seed = runtime.RandomSeed,
                MaxPlayers = maxPlayers,
                TargetPlayers = maxPlayers,
                ServerSequence = 0,
            };
            _sessions[sessionId] = session;
            return session;
        }

        private void HandleJoin(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (string.IsNullOrEmpty(payload.PlayerId))
            {
                PublishError(session.SessionId, RegicideErrorCode.InvalidAction, "Player id is empty.", payload.ClientSequence);
                return;
            }

            if (session.PlayerIds.Contains(payload.PlayerId))
            {
                session.ServerSequence++;
                PublishRoom(session);
                return;
            }

            if (session.PlayerIds.Count >= session.TargetPlayers || session.PlayerIds.Count >= session.MaxPlayers)
            {
                PublishError(session.SessionId, RegicideErrorCode.RoomFull, "Room is full.", payload.ClientSequence);
                return;
            }

            session.PlayerIds.Add(payload.PlayerId);
            session.ServerSequence++;
            PublishRoom(session);
        }

        private void HandleLeave(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (!session.PlayerIds.Contains(payload.PlayerId))
            {
                PublishError(session.SessionId, RegicideErrorCode.RoomNotFound, "Player not in room.", payload.ClientSequence);
                return;
            }

            session.PlayerIds.Remove(payload.PlayerId);
            session.ReadyPlayers.Remove(payload.PlayerId);

            // 有玩家离开时，当前对局状态无效，回到房间待机态。
            session.State = null;

            if (session.PlayerIds.Count <= 0)
            {
                session.TargetPlayers = 1;
                session.ReadyPlayers.Clear();
            }
            else if (session.TargetPlayers > session.PlayerIds.Count && session.TargetPlayers > 1)
            {
                session.TargetPlayers = session.PlayerIds.Count;
                if (session.TargetPlayers < 1)
                {
                    session.TargetPlayers = 1;
                }
            }

            session.ServerSequence++;
            PublishRoom(session);

            if (session.PlayerIds.Count <= 0)
            {
                _sessions.Remove(session.SessionId);
            }
        }

        private void HandleReady(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (!session.PlayerIds.Contains(payload.PlayerId))
            {
                PublishError(session.SessionId, RegicideErrorCode.RoomNotFound, "Player not in room.", payload.ClientSequence);
                return;
            }

            session.ReadyPlayers.Add(payload.PlayerId);
            session.ServerSequence++;
            PublishRoom(session);
            TryAutoStartSinglePlayer(session, payload);
        }

        private void HandleCancelReady(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (!session.PlayerIds.Contains(payload.PlayerId))
            {
                PublishError(session.SessionId, RegicideErrorCode.RoomNotFound, "Player not in room.", payload.ClientSequence);
                return;
            }

            session.ReadyPlayers.Remove(payload.PlayerId);
            session.ServerSequence++;
            PublishRoom(session);
        }

        private void HandleConfigureRoom(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (!session.PlayerIds.Contains(payload.PlayerId))
            {
                PublishError(session.SessionId, RegicideErrorCode.RoomNotFound, "Player not in room.", payload.ClientSequence);
                return;
            }

            int targetPlayers = Mathf.Clamp(payload.TargetPlayers, 1, session.MaxPlayers);
            if (targetPlayers < session.PlayerIds.Count)
            {
                PublishError(
                    session.SessionId,
                    RegicideErrorCode.InvalidState,
                    $"Target players {targetPlayers} cannot be lower than connected players {session.PlayerIds.Count}.",
                    payload.ClientSequence);
                return;
            }

            session.TargetPlayers = targetPlayers;
            session.ServerSequence++;
            PublishRoom(session);
            TryAutoStartSinglePlayer(session, payload);
        }

        private void HandleStart(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (session.State != null)
            {
                if (session.State.IsGameOver)
                {
                    ResetAfterGameOver(session);
                }
                else
                {
                    PublishError(session.SessionId, RegicideErrorCode.InvalidState, "Match already started.", payload.ClientSequence);
                    return;
                }
            }

            if (session.State != null)
            {
                PublishError(session.SessionId, RegicideErrorCode.InvalidState, "Match already started.", payload.ClientSequence);
                return;
            }

            if (session.PlayerIds.Count < session.TargetPlayers)
            {
                PublishError(
                    session.SessionId,
                    RegicideErrorCode.InvalidState,
                    $"Waiting for players ({session.PlayerIds.Count}/{session.TargetPlayers}).",
                    payload.ClientSequence);
                return;
            }

            if (session.ReadyPlayers.Count < session.TargetPlayers)
            {
                PublishError(
                    session.SessionId,
                    RegicideErrorCode.InvalidState,
                    $"Not all players are ready ({session.ReadyPlayers.Count}/{session.TargetPlayers}).",
                    payload.ClientSequence);
                return;
            }

            session.State = RegicideBattleInitializer.Create(
                session.SessionId,
                session.RuleId,
                session.Seed,
                session.PlayerIds,
                RegicideRuleConfigProvider.Instance);

            if (session.State == null)
            {
                PublishError(session.SessionId, RegicideErrorCode.InvalidState, "Failed to initialize battle state.", payload.ClientSequence);
                return;
            }

            session.ServerSequence++;
            session.State.AppliedSequence = session.ServerSequence;
            PublishState(session, session.State);
            PublishPublicStateSnapshot(session);
            PublishActionBroadcast(session, new RegicideActionBroadcastPayload
            {
                SessionId = session.SessionId,
                ServerSequence = session.ServerSequence,
                ActorPlayerId = payload.PlayerId,
                ActionType = RegicideActionBroadcastType.StartMatch,
                Summary = "对局开始。",
                EnemyHealthBefore = session.State.CurrentEnemy != null ? Mathf.Max(0, session.State.CurrentEnemy.Health) : -1,
                EnemyHealthAfter = session.State.CurrentEnemy != null ? Mathf.Max(0, session.State.CurrentEnemy.Health) : -1,
                EnemyAttackBefore = session.State.CurrentEnemy != null ? Mathf.Max(0, session.State.CurrentEnemy.Attack) : -1,
                EnemyAttackAfter = session.State.CurrentEnemy != null ? Mathf.Max(0, session.State.CurrentEnemy.Attack) : -1,
                IsGameOver = session.State.IsGameOver,
                IsVictory = session.State.IsVictory,
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            });
            PublishRoom(session);
        }

        private void HandleBattleIntent(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (session.State == null)
            {
                PublishError(session.SessionId, RegicideErrorCode.InvalidState, "Match has not started.", payload.ClientSequence);
                return;
            }

            if (!session.PlayerIds.Contains(payload.PlayerId))
            {
                PublishError(session.SessionId, RegicideErrorCode.RoomNotFound, "Player not in room.", payload.ClientSequence);
                return;
            }

            if (session.State.CurrentPlayerIndex < 0 || session.State.CurrentPlayerIndex >= session.State.Players.Count)
            {
                PublishError(session.SessionId, RegicideErrorCode.InvalidState, "Current player index is invalid.", payload.ClientSequence);
                return;
            }

            string currentTurnPlayer = session.State.Players[session.State.CurrentPlayerIndex].PlayerId;
            if (!string.Equals(currentTurnPlayer, payload.PlayerId))
            {
                PublishError(
                    session.SessionId,
                    RegicideErrorCode.NotYourTurn,
                    $"Not your turn. current={currentTurnPlayer}",
                    payload.ClientSequence);
                Debug.LogWarning(
                    $"Regicide authority rejected non-turn action. session={session.SessionId} actor={payload.PlayerId} current={currentTurnPlayer} intent={payload.IntentType}");
                return;
            }

            int battleLogStart = session.State.BattleLog != null ? session.State.BattleLog.Count : 0;
            int enemyHealthBefore = session.State.CurrentEnemy != null ? Mathf.Max(0, session.State.CurrentEnemy.Health) : -1;
            int enemyAttackBefore = session.State.CurrentEnemy != null ? Mathf.Max(0, session.State.CurrentEnemy.Attack) : -1;
            List<string> publicCards = CapturePublicCards(session.State, payload);

            RegicidePlayerAction action = new RegicidePlayerAction
            {
                SessionId = payload.SessionId,
                PlayerId = payload.PlayerId,
                ActionType = ConvertIntent(payload.IntentType),
                CardIndex = payload.CardIndex,
                CardIndices = payload.CardIndices != null ? new List<int>(payload.CardIndices) : new List<int>(),
                NextPlayerIndex = payload.NextPlayerIndex,
                ClientSequence = payload.ClientSequence,
                Timestamp = payload.Timestamp,
            };

            RegicideRuleConfig config = RegicideRuleConfigProvider.Instance.GetByRuleId(session.RuleId);
            RegicideActionResult result = RegicideBattleResolver.ApplyAction(session.State, action, config);
            if (!result.Success)
            {
                PublishError(session.SessionId, RegicideErrorCode.InvalidAction, result.Error, payload.ClientSequence);
                return;
            }

            session.ServerSequence = result.AppliedSequence;
            PublishState(session, result.State);
            PublishPublicStateSnapshot(session);
            PublishActionBroadcast(
                session,
                BuildActionBroadcast(
                    session,
                    payload,
                    result.State,
                    publicCards,
                    battleLogStart,
                    enemyHealthBefore,
                    enemyAttackBefore));

            if (result.State != null && result.State.IsGameOver)
            {
                // 对局结束后立即回到待机房间，允许同一房间开始下一局。
                ResetAfterGameOver(session);
            }
        }

        private static RegicideActionType ConvertIntent(RegicideClientIntentType intentType)
        {
            return intentType switch
            {
                RegicideClientIntentType.PlayCard => RegicideActionType.PlayCard,
                RegicideClientIntentType.Pass => RegicideActionType.Pass,
                RegicideClientIntentType.Defend => RegicideActionType.Defend,
                RegicideClientIntentType.DiscardForDamage => RegicideActionType.Discard,
                _ => RegicideActionType.Unknown,
            };
        }

        private static void PublishRoom(AuthoritySession session)
        {
            RegicideRoomSnapshot room = new RegicideRoomSnapshot
            {
                SessionId = session.SessionId,
                Started = session.State != null,
                MaxPlayers = session.MaxPlayers,
                TargetPlayers = session.TargetPlayers,
                ConnectedPlayers = session.PlayerIds.Count,
                ReadyPlayers = session.ReadyPlayers.Count,
                ServerSequence = session.ServerSequence,
                Seats = new List<RegicideRoomSeatSnapshot>(),
            };

            for (int i = 0; i < session.PlayerIds.Count; i++)
            {
                string playerId = session.PlayerIds[i];
                room.Seats.Add(new RegicideRoomSeatSnapshot
                {
                    SeatIndex = i,
                    PlayerId = playerId,
                    IsReady = session.ReadyPlayers.Contains(playerId),
                    IsOnline = true,
                });
            }

            GameEvent.Send(RegicideBridgeEvents.ServerPublishRoomSnapshot, room);
            PublishPublicStateSnapshot(session);
        }

        private static void PublishState(AuthoritySession session, RegicideBattleState state)
        {
            RegicideStateSnapshotPayload payload = new RegicideStateSnapshotPayload
            {
                SessionId = session.SessionId,
                ServerSequence = session.ServerSequence,
                IsGameOver = state.IsGameOver,
                IsVictory = state.IsVictory,
                CurrentPlayerIndex = state.CurrentPlayerIndex,
                StateHash = RegicideStateHasher.ComputeHash(state),
                StateJson = JsonUtility.ToJson(state),
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };

            GameEvent.Send(RegicideBridgeEvents.ServerPublishState, payload);
        }

        private static void PublishPublicStateSnapshot(AuthoritySession session)
        {
            if (session == null)
            {
                return;
            }

            RegicidePublicStateSnapshotPayload payload = new RegicidePublicStateSnapshotPayload
            {
                SessionId = session.SessionId,
                ServerSequence = session.ServerSequence,
                Timestamp = RegicideClock.NowUnixMilliseconds(),
                IsAwaitingDiscard = session.State != null && session.State.IsAwaitingDiscard,
                PendingDiscardTargetPlayerIndex = session.State != null ? session.State.PendingDiscardTargetPlayerIndex : -1,
                CurrentPlayerIndex = session.State != null ? session.State.CurrentPlayerIndex : -1,
                Players = new List<RegicidePublicPlayerState>(),
            };

            Dictionary<string, RegicidePlayerState> statePlayers = new Dictionary<string, RegicidePlayerState>();
            if (session.State != null && session.State.Players != null)
            {
                for (int i = 0; i < session.State.Players.Count; i++)
                {
                    RegicidePlayerState player = session.State.Players[i];
                    if (player == null || string.IsNullOrEmpty(player.PlayerId))
                    {
                        continue;
                    }

                    statePlayers[player.PlayerId] = player;
                }
            }

            for (int i = 0; i < session.PlayerIds.Count; i++)
            {
                string playerId = session.PlayerIds[i];
                statePlayers.TryGetValue(playerId, out RegicidePlayerState statePlayer);
                int statePlayerIndex = ResolvePlayerIndex(session.State, playerId);
                bool isCurrentTurn = session.State != null && statePlayerIndex == session.State.CurrentPlayerIndex;
                bool isPendingTarget = session.State != null &&
                                       session.State.IsAwaitingDiscard &&
                                       statePlayerIndex == session.State.PendingDiscardTargetPlayerIndex;

                payload.Players.Add(new RegicidePublicPlayerState
                {
                    PlayerId = playerId,
                    SeatIndex = i,
                    IsOnline = true,
                    IsReady = session.ReadyPlayers.Contains(playerId),
                    IsCurrentTurn = isCurrentTurn,
                    IsPendingDiscardTarget = isPendingTarget,
                    HandCount = statePlayer != null && statePlayer.Hand != null ? statePlayer.Hand.Count : 0,
                    Phase = ResolvePublicPhase(session, playerId, isCurrentTurn, isPendingTarget),
                });
            }

            GameEvent.Send(RegicideBridgeEvents.ServerPublishPublicStateSnapshot, payload);
        }

        private static void PublishActionBroadcast(AuthoritySession session, RegicideActionBroadcastPayload payload)
        {
            if (session == null || payload == null)
            {
                return;
            }

            payload.SessionId = session.SessionId;
            payload.ServerSequence = session.ServerSequence;
            if (payload.PublicCards == null)
            {
                payload.PublicCards = new List<string>();
            }

            if (payload.Timestamp <= 0)
            {
                payload.Timestamp = RegicideClock.NowUnixMilliseconds();
            }

            GameEvent.Send(RegicideBridgeEvents.ServerPublishActionBroadcast, payload);
        }

        private static void PublishError(string sessionId, RegicideErrorCode code, string message, long relatedSequence)
        {
            GameEvent.Send(RegicideBridgeEvents.ServerPublishError, new RegicideErrorPayload
            {
                SessionId = sessionId,
                Code = code,
                Message = message,
                RelatedSequence = relatedSequence,
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            });
        }

        private static void ResetAfterGameOver(AuthoritySession session)
        {
            if (session == null)
            {
                return;
            }

            session.State = null;
            session.ReadyPlayers.Clear();
            session.Seed += 1;
            session.ServerSequence++;
            PublishRoom(session);
        }

        private void TryAutoStartSinglePlayer(AuthoritySession session, RegicideClientIntentPayload payload)
        {
            if (session == null || payload == null)
            {
                return;
            }

            if (session.State != null || session.TargetPlayers != 1 || session.PlayerIds.Count < 1)
            {
                return;
            }

            string playerId = session.PlayerIds[0];
            if (!session.ReadyPlayers.Contains(playerId))
            {
                return;
            }

            RegicideClientIntentPayload autoStartPayload = new RegicideClientIntentPayload
            {
                SessionId = session.SessionId,
                PlayerId = playerId,
                IntentType = RegicideClientIntentType.StartMatch,
                ClientSequence = payload.ClientSequence,
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };

            // 复用开始流程，满足单人房 Ready 后自动开局。
            HandleStart(session, autoStartPayload);
        }

        private static RegicideActionBroadcastPayload BuildActionBroadcast(
            AuthoritySession session,
            RegicideClientIntentPayload payload,
            RegicideBattleState state,
            List<string> publicCards,
            int battleLogStart,
            int enemyHealthBefore,
            int enemyAttackBefore)
        {
            StringBuilder summaryBuilder = new StringBuilder(96);
            if (state != null && state.BattleLog != null && battleLogStart >= 0 && battleLogStart < state.BattleLog.Count)
            {
                int limit = Mathf.Min(state.BattleLog.Count, battleLogStart + 3);
                for (int i = battleLogStart; i < limit; i++)
                {
                    if (summaryBuilder.Length > 0)
                    {
                        summaryBuilder.Append(" | ");
                    }

                    summaryBuilder.Append(state.BattleLog[i]);
                }
            }

            if (summaryBuilder.Length <= 0)
            {
                summaryBuilder.Append(payload.IntentType);
            }

            return new RegicideActionBroadcastPayload
            {
                SessionId = session.SessionId,
                ServerSequence = session.ServerSequence,
                ActorPlayerId = payload.PlayerId,
                ActionType = ConvertBroadcastType(payload.IntentType),
                PublicCards = publicCards ?? new List<string>(),
                Summary = summaryBuilder.ToString(),
                EnemyHealthBefore = enemyHealthBefore,
                EnemyHealthAfter = state != null && state.CurrentEnemy != null ? Mathf.Max(0, state.CurrentEnemy.Health) : -1,
                EnemyAttackBefore = enemyAttackBefore,
                EnemyAttackAfter = state != null && state.CurrentEnemy != null ? Mathf.Max(0, state.CurrentEnemy.Attack) : -1,
                IsGameOver = state != null && state.IsGameOver,
                IsVictory = state != null && state.IsVictory,
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };
        }

        private static RegicideActionBroadcastType ConvertBroadcastType(RegicideClientIntentType intentType)
        {
            return intentType switch
            {
                RegicideClientIntentType.PlayCard => RegicideActionBroadcastType.PlayCard,
                RegicideClientIntentType.Pass => RegicideActionBroadcastType.Pass,
                RegicideClientIntentType.DiscardForDamage => RegicideActionBroadcastType.DiscardForDamage,
                RegicideClientIntentType.StartMatch => RegicideActionBroadcastType.StartMatch,
                _ => RegicideActionBroadcastType.Unknown,
            };
        }

        private static List<string> CapturePublicCards(RegicideBattleState state, RegicideClientIntentPayload payload)
        {
            List<string> cards = new List<string>();
            if (state == null || payload == null || state.Players == null)
            {
                return cards;
            }

            int playerIndex = ResolvePlayerIndex(state, payload.PlayerId);
            if (playerIndex < 0 || playerIndex >= state.Players.Count)
            {
                return cards;
            }

            RegicidePlayerState player = state.Players[playerIndex];
            if (player == null || player.Hand == null)
            {
                return cards;
            }

            List<int> normalized = RegicideActionValidator.NormalizeCardIndices(payload.CardIndices);
            if (normalized.Count <= 0 && payload.CardIndex >= 0)
            {
                normalized.Add(payload.CardIndex);
            }

            HashSet<int> unique = new HashSet<int>();
            for (int i = 0; i < normalized.Count; i++)
            {
                int index = normalized[i];
                if (!unique.Add(index))
                {
                    continue;
                }

                if (index < 0 || index >= player.Hand.Count)
                {
                    continue;
                }

                cards.Add(ToPublicCard(player.Hand[index]));
            }

            return cards;
        }

        private static int ResolvePlayerIndex(RegicideBattleState state, string playerId)
        {
            if (state == null || state.Players == null || string.IsNullOrEmpty(playerId))
            {
                return -1;
            }

            for (int i = 0; i < state.Players.Count; i++)
            {
                RegicidePlayerState player = state.Players[i];
                if (player != null && string.Equals(player.PlayerId, playerId))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string ResolvePublicPhase(AuthoritySession session, string playerId, bool isCurrentTurn, bool isPendingDiscardTarget)
        {
            if (session == null)
            {
                return "Unknown";
            }

            if (session.State == null)
            {
                return session.ReadyPlayers.Contains(playerId) ? "Ready" : "Waiting";
            }

            if (session.State.IsGameOver)
            {
                return "GameOver";
            }

            if (isPendingDiscardTarget)
            {
                return "Discarding";
            }

            if (session.State.IsAwaitingDiscard)
            {
                return "WaitingDiscard";
            }

            return isCurrentTurn ? "Acting" : "WaitingTurn";
        }

        private static string ToPublicCard(RegicideCard card)
        {
            if (card == null)
            {
                return "?";
            }

            string suit = card.Suit switch
            {
                RegicideSuit.Spade => "S",
                RegicideSuit.Heart => "H",
                RegicideSuit.Club => "C",
                RegicideSuit.Diamond => "D",
                RegicideSuit.Joker => "Joker",
                _ => "U",
            };

            string rank = card.Rank switch
            {
                0 => "Jester",
                1 => "A",
                11 => "J",
                12 => "Q",
                13 => "K",
                _ => card.Rank.ToString(),
            };

            return $"{suit}{rank}";
        }
    }
}
