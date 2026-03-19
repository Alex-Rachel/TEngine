using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameProto.Regicide;
using TEngine;
using UnityEngine;

namespace GameLogic.Regicide
{
    public sealed class RegicideNetworkModule : Singleton<RegicideNetworkModule>
    {
        private const int ActionSequenceWindow = 512;

        private RegicideConnectionState _connectionState = new RegicideConnectionState();
        private RegicideRoomSnapshot _latestRoomSnapshot = new RegicideRoomSnapshot();
        private RegicideStateSnapshotPayload _latestStateSnapshot = new RegicideStateSnapshotPayload();
        private RegicidePublicStateSnapshotPayload _latestPublicStateSnapshot = new RegicidePublicStateSnapshotPayload();
        private RegicideErrorPayload _latestError = new RegicideErrorPayload();
        private CancellationTokenSource _lifetimeCts;
        private long _clientSequence;

        private UniTaskCompletionSource<bool> _connectTcs;
        private UniTaskCompletionSource<bool> _roomTcs;
        private readonly Dictionary<long, UniTaskCompletionSource<bool>> _pendingIntent = new Dictionary<long, UniTaskCompletionSource<bool>>();
        private readonly List<RegicideActionBroadcastPayload> _actionBroadcastHistory = new List<RegicideActionBroadcastPayload>();
        private readonly HashSet<long> _processedActionSequences = new HashSet<long>();
        private readonly Queue<long> _processedActionSequenceQueue = new Queue<long>();
        private readonly RegicideLongSessionMetrics _metrics = new RegicideLongSessionMetrics();

        public RegicideConnectionState ConnectionState => _connectionState;
        public RegicideRoomSnapshot RoomSnapshot => _latestRoomSnapshot;
        public RegicideStateSnapshotPayload StateSnapshot => _latestStateSnapshot;
        public RegicidePublicStateSnapshotPayload PublicStateSnapshot => _latestPublicStateSnapshot;
        public IReadOnlyList<RegicideActionBroadcastPayload> ActionBroadcastHistory => _actionBroadcastHistory;
        public RegicideErrorPayload LastError => _latestError;

        public RegicideLongSessionMetrics GetMetricsSnapshot()
        {
            return new RegicideLongSessionMetrics
            {
                TotalMessagesIn = _metrics.TotalMessagesIn,
                TotalMessagesOut = _metrics.TotalMessagesOut,
                TotalBytesIn = _metrics.TotalBytesIn,
                TotalBytesOut = _metrics.TotalBytesOut,
                SessionTicks = _metrics.SessionTicks,
                PeakFrameMillis = _metrics.PeakFrameMillis,
                GcAllocatedBytesDelta = _metrics.GcAllocatedBytesDelta,
            };
        }

        public void ResetMetrics()
        {
            _metrics.TotalMessagesIn = 0;
            _metrics.TotalMessagesOut = 0;
            _metrics.TotalBytesIn = 0;
            _metrics.TotalBytesOut = 0;
            _metrics.SessionTicks = 0;
            _metrics.PeakFrameMillis = 0;
            _metrics.GcAllocatedBytesDelta = 0;
        }

        protected override void OnInit()
        {
            _lifetimeCts = new CancellationTokenSource();

            GameEvent.AddEventListener<RegicideConnectionState>(RegicideBridgeEvents.ClientConnected, OnClientConnected);
            GameEvent.AddEventListener(RegicideBridgeEvents.ClientDisconnected, OnClientDisconnected);
            GameEvent.AddEventListener<RegicideRoomSnapshot>(RegicideBridgeEvents.ClientRoomSnapshot, OnRoomSnapshot);
            GameEvent.AddEventListener<RegicideStateSnapshotPayload>(RegicideBridgeEvents.ClientStateSnapshot, OnStateSnapshot);
            GameEvent.AddEventListener<RegicidePublicStateSnapshotPayload>(RegicideBridgeEvents.ClientPublicStateSnapshot, OnPublicStateSnapshot);
            GameEvent.AddEventListener<RegicideActionBroadcastPayload>(RegicideBridgeEvents.ClientActionBroadcast, OnActionBroadcast);
            GameEvent.AddEventListener<RegicideErrorPayload>(RegicideBridgeEvents.ClientError, OnClientError);
        }

        protected override void OnRelease()
        {
            GameEvent.RemoveEventListener<RegicideConnectionState>(RegicideBridgeEvents.ClientConnected, OnClientConnected);
            GameEvent.RemoveEventListener(RegicideBridgeEvents.ClientDisconnected, OnClientDisconnected);
            GameEvent.RemoveEventListener<RegicideRoomSnapshot>(RegicideBridgeEvents.ClientRoomSnapshot, OnRoomSnapshot);
            GameEvent.RemoveEventListener<RegicideStateSnapshotPayload>(RegicideBridgeEvents.ClientStateSnapshot, OnStateSnapshot);
            GameEvent.RemoveEventListener<RegicidePublicStateSnapshotPayload>(RegicideBridgeEvents.ClientPublicStateSnapshot, OnPublicStateSnapshot);
            GameEvent.RemoveEventListener<RegicideActionBroadcastPayload>(RegicideBridgeEvents.ClientActionBroadcast, OnActionBroadcast);
            GameEvent.RemoveEventListener<RegicideErrorPayload>(RegicideBridgeEvents.ClientError, OnClientError);

            if (_lifetimeCts != null)
            {
                _lifetimeCts.Cancel();
                _lifetimeCts.Dispose();
                _lifetimeCts = null;
            }

            foreach (KeyValuePair<long, UniTaskCompletionSource<bool>> pair in _pendingIntent)
            {
                pair.Value.TrySetCanceled();
            }

            _pendingIntent.Clear();
            ClearMultiplayerCaches(clearPublicSnapshot: true);
        }

        public async UniTask<bool> ConnectAsync(RegicideRuntimeConfig runtimeConfig, bool asHost, int timeoutMs = 8000, CancellationToken cancellationToken = default)
        {
            _connectTcs = new UniTaskCompletionSource<bool>();
            RegicideConnectionRequest request = new RegicideConnectionRequest
            {
                Address = runtimeConfig.ServerAddress,
                Port = runtimeConfig.ServerPort,
                AsHost = asHost,
                AsServerOnly = false,
                AutoCreateRoom = true,
                PlayerId = runtimeConfig.PlayerId,
            };

            GameEvent.Send(RegicideBridgeEvents.ClientConnectRequest, request);
            UniTask<bool> waitConnect = _connectTcs.Task.AttachExternalCancellation(cancellationToken);
            UniTask timeoutTask = UniTask.Delay(timeoutMs, cancellationToken: cancellationToken);
            (bool hasConnectResult, bool connectResult) = await UniTask.WhenAny(waitConnect, timeoutTask);
            if (!hasConnectResult)
            {
                return false;
            }

            return connectResult;
        }

        public async UniTask<bool> StartServerAsync(RegicideRuntimeConfig runtimeConfig, int timeoutMs = 8000, CancellationToken cancellationToken = default)
        {
            _connectTcs = new UniTaskCompletionSource<bool>();
            RegicideConnectionRequest request = new RegicideConnectionRequest
            {
                Address = runtimeConfig.ServerAddress,
                Port = runtimeConfig.ServerPort,
                AsHost = false,
                AsServerOnly = true,
                AutoCreateRoom = false,
                PlayerId = runtimeConfig.PlayerId,
            };

            GameEvent.Send(RegicideBridgeEvents.ClientConnectRequest, request);
            UniTask<bool> waitConnect = _connectTcs.Task.AttachExternalCancellation(cancellationToken);
            UniTask timeoutTask = UniTask.Delay(timeoutMs, cancellationToken: cancellationToken);
            (bool hasConnectResult, bool connectResult) = await UniTask.WhenAny(waitConnect, timeoutTask);
            if (!hasConnectResult)
            {
                return false;
            }

            return connectResult;
        }

        public void Disconnect()
        {
            GameEvent.Send(RegicideBridgeEvents.ClientDisconnectRequest);
        }

        public async UniTask<bool> JoinRoomAsync(string sessionId, string playerId, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            _roomTcs = new UniTaskCompletionSource<bool>();
            RegicideClientIntentPayload payload = new RegicideClientIntentPayload
            {
                SessionId = sessionId,
                PlayerId = playerId,
                IntentType = RegicideClientIntentType.JoinRoom,
                ClientSequence = NextClientSequence(),
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };

            // Host/Server 本地模式优先直连 authority，避免 Host 首帧网络时序导致 Join 超时。
            if (IsLocalHostOrServer())
            {
                GameEvent.Send(RegicideBridgeEvents.ServerIntentReceived, payload);
            }
            else
            {
                GameEvent.Send(RegicideBridgeEvents.ClientJoinRoomRequest, payload);
            }

            TrackOutbound(payload);

            UniTask<bool> waitJoin = _roomTcs.Task.AttachExternalCancellation(cancellationToken);
            UniTask timeoutTask = UniTask.Delay(timeoutMs, cancellationToken: cancellationToken);
            (bool hasJoinResult, bool joinResult) = await UniTask.WhenAny(waitJoin, timeoutTask);
            if (!hasJoinResult)
            {
                return false;
            }

            return joinResult;
        }

        public UniTask<bool> SetReadyAsync(string sessionId, string playerId, bool ready, CancellationToken cancellationToken = default)
        {
            return SendIntentAsync(
                sessionId,
                playerId,
                ready ? RegicideClientIntentType.Ready : RegicideClientIntentType.CancelReady,
                -1,
                0,
                null,
                -1,
                cancellationToken);
        }

        public UniTask<bool> LeaveRoomAsync(string sessionId, string playerId, CancellationToken cancellationToken = default)
        {
            return SendIntentAsync(
                sessionId,
                playerId,
                RegicideClientIntentType.LeaveRoom,
                -1,
                0,
                null,
                -1,
                cancellationToken);
        }

        public UniTask<bool> SetRoomTargetPlayersAsync(string sessionId, string playerId, int targetPlayers, CancellationToken cancellationToken = default)
        {
            return SendIntentAsync(
                sessionId,
                playerId,
                RegicideClientIntentType.ConfigureRoom,
                -1,
                targetPlayers,
                null,
                -1,
                cancellationToken);
        }

        public UniTask<bool> StartMatchAsync(string sessionId, string playerId, CancellationToken cancellationToken = default)
        {
            return SendIntentAsync(sessionId, playerId, RegicideClientIntentType.StartMatch, -1, 0, null, -1, cancellationToken);
        }

        public UniTask<bool> PlayCardAsync(string sessionId, string playerId, int cardIndex, CancellationToken cancellationToken = default)
        {
            List<int> cardIndices = new List<int> { cardIndex };
            return SendIntentAsync(sessionId, playerId, RegicideClientIntentType.PlayCard, cardIndex, 0, cardIndices, -1, cancellationToken);
        }

        public UniTask<bool> PlayCardAsync(
            string sessionId,
            string playerId,
            IList<int> cardIndices,
            int nextPlayerIndex,
            CancellationToken cancellationToken = default)
        {
            List<int> normalized = cardIndices != null ? new List<int>(cardIndices) : new List<int>();
            int singleIndex = normalized.Count == 1 ? normalized[0] : -1;
            return SendIntentAsync(sessionId, playerId, RegicideClientIntentType.PlayCard, singleIndex, 0, normalized, nextPlayerIndex, cancellationToken);
        }

        public UniTask<bool> PassAsync(string sessionId, string playerId, CancellationToken cancellationToken = default)
        {
            return SendIntentAsync(sessionId, playerId, RegicideClientIntentType.Pass, -1, 0, null, -1, cancellationToken);
        }

        public UniTask<bool> DefendAsync(string sessionId, string playerId, CancellationToken cancellationToken = default)
        {
            return SendIntentAsync(sessionId, playerId, RegicideClientIntentType.Defend, -1, 0, null, -1, cancellationToken);
        }

        public UniTask<bool> DiscardForDamageAsync(
            string sessionId,
            string playerId,
            IList<int> cardIndices,
            CancellationToken cancellationToken = default)
        {
            List<int> normalized = cardIndices != null ? new List<int>(cardIndices) : new List<int>();
            int singleIndex = normalized.Count == 1 ? normalized[0] : -1;
            return SendIntentAsync(
                sessionId,
                playerId,
                RegicideClientIntentType.DiscardForDamage,
                singleIndex,
                0,
                normalized,
                -1,
                cancellationToken);
        }

        private async UniTask<bool> SendIntentAsync(
            string sessionId,
            string playerId,
            RegicideClientIntentType intentType,
            int cardIndex,
            int targetPlayers,
            IList<int> cardIndices,
            int nextPlayerIndex,
            CancellationToken cancellationToken)
        {
            RegicideClientIntentPayload payload = new RegicideClientIntentPayload
            {
                SessionId = sessionId,
                PlayerId = playerId,
                IntentType = intentType,
                TargetPlayers = targetPlayers,
                CardIndex = cardIndex,
                NextPlayerIndex = nextPlayerIndex,
                CardIndices = cardIndices != null ? new List<int>(cardIndices) : new List<int>(),
                ClientSequence = NextClientSequence(),
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };

            UniTaskCompletionSource<bool> intentTcs = new UniTaskCompletionSource<bool>();
            _pendingIntent[payload.ClientSequence] = intentTcs;

            if (IsLocalHostOrServer())
            {
                GameEvent.Send(RegicideBridgeEvents.ServerIntentReceived, payload);
            }
            else
            {
                switch (intentType)
                {
                    case RegicideClientIntentType.JoinRoom:
                        GameEvent.Send(RegicideBridgeEvents.ClientJoinRoomRequest, payload);
                        break;
                    case RegicideClientIntentType.LeaveRoom:
                        GameEvent.Send(RegicideBridgeEvents.ClientLeaveRoomRequest, payload);
                        break;
                    case RegicideClientIntentType.Ready:
                        GameEvent.Send(RegicideBridgeEvents.ClientReadyRequest, payload);
                        break;
                    case RegicideClientIntentType.StartMatch:
                        GameEvent.Send(RegicideBridgeEvents.ClientStartRequest, payload);
                        break;
                    default:
                        GameEvent.Send(RegicideBridgeEvents.ClientIntentRequest, payload);
                        break;
                }
            }

            TrackOutbound(payload);

            using CancellationTokenRegistration _ = cancellationToken.Register(() =>
            {
                if (_pendingIntent.Remove(payload.ClientSequence, out UniTaskCompletionSource<bool> tcs))
                {
                    tcs.TrySetCanceled();
                }
            });

            return await intentTcs.Task;
        }

        private long NextClientSequence()
        {
            _clientSequence++;
            return _clientSequence;
        }

        private void OnClientConnected(RegicideConnectionState state)
        {
            TrackInbound(state);
            _connectionState = state ?? new RegicideConnectionState();
            _connectTcs?.TrySetResult(_connectionState.IsConnected);
            GameEvent.Send(RegicideEventIds.ConnectionStateChanged, _connectionState);
        }

        private void OnClientDisconnected()
        {
            _connectionState = new RegicideConnectionState();
            _latestRoomSnapshot = new RegicideRoomSnapshot();
            _latestStateSnapshot = new RegicideStateSnapshotPayload();
            _latestError = new RegicideErrorPayload();
            ClearMultiplayerCaches(clearPublicSnapshot: true);
            _connectTcs?.TrySetResult(false);
            GameEvent.Send(RegicideEventIds.ConnectionStateChanged, _connectionState);
        }

        private void OnRoomSnapshot(RegicideRoomSnapshot snapshot)
        {
            TrackInbound(snapshot);

            string previousSessionId = _latestRoomSnapshot != null ? _latestRoomSnapshot.SessionId : string.Empty;
            _latestRoomSnapshot = snapshot ?? new RegicideRoomSnapshot();

            bool switchedSession = !string.IsNullOrEmpty(previousSessionId) &&
                                   !string.IsNullOrEmpty(_latestRoomSnapshot.SessionId) &&
                                   !string.Equals(previousSessionId, _latestRoomSnapshot.SessionId);
            if (switchedSession || !_latestRoomSnapshot.Started)
            {
                ClearMultiplayerCaches(clearPublicSnapshot: true);
            }

            _roomTcs?.TrySetResult(true);
            CompleteIntentUpTo(_latestRoomSnapshot.ServerSequence);
            GameEvent.Send(RegicideEventIds.RoomSnapshotUpdated, _latestRoomSnapshot);
        }

        private void OnStateSnapshot(RegicideStateSnapshotPayload snapshot)
        {
            TrackInbound(snapshot);
            _latestStateSnapshot = snapshot ?? new RegicideStateSnapshotPayload();
            CompleteIntentUpTo(snapshot != null ? snapshot.ServerSequence : 0);
            GameEvent.Send(RegicideEventIds.BattleSnapshotUpdated, _latestStateSnapshot);
        }

        private void OnPublicStateSnapshot(RegicidePublicStateSnapshotPayload snapshot)
        {
            TrackInbound(snapshot);

            bool switchedSession = !string.IsNullOrEmpty(_latestPublicStateSnapshot.SessionId) &&
                                   !string.IsNullOrEmpty(snapshot != null ? snapshot.SessionId : string.Empty) &&
                                   !string.Equals(_latestPublicStateSnapshot.SessionId, snapshot.SessionId);
            if (switchedSession)
            {
                ClearMultiplayerCaches(clearPublicSnapshot: false);
            }

            _latestPublicStateSnapshot = snapshot ?? new RegicidePublicStateSnapshotPayload();
            CompleteIntentUpTo(snapshot != null ? snapshot.ServerSequence : 0);
            GameEvent.Send(RegicideEventIds.PublicStateSnapshotUpdated, _latestPublicStateSnapshot);
        }

        private void OnActionBroadcast(RegicideActionBroadcastPayload payload)
        {
            TrackInbound(payload);
            if (payload == null || string.IsNullOrEmpty(payload.SessionId))
            {
                return;
            }

            if (!RegisterActionSequence(payload.ServerSequence))
            {
                return;
            }

            if (_actionBroadcastHistory.Count > 0)
            {
                string currentSessionId = _actionBroadcastHistory[0].SessionId;
                if (!string.IsNullOrEmpty(currentSessionId) && !string.Equals(currentSessionId, payload.SessionId))
                {
                    ClearMultiplayerCaches(clearPublicSnapshot: false);
                }
            }

            _actionBroadcastHistory.Add(payload);
            if (_actionBroadcastHistory.Count > 160)
            {
                _actionBroadcastHistory.RemoveAt(0);
            }

            CompleteIntentUpTo(payload.ServerSequence);
            GameEvent.Send(RegicideEventIds.ActionBroadcastReceived, payload);
        }

        private void OnClientError(RegicideErrorPayload payload)
        {
            TrackInbound(payload);
            _latestError = payload ?? new RegicideErrorPayload();
            if (_pendingIntent.Remove(_latestError.RelatedSequence, out UniTaskCompletionSource<bool> tcs))
            {
                tcs.TrySetResult(false);
            }

            GameEvent.Send(RegicideEventIds.BattleErrorReceived, _latestError);
        }

        private void CompleteIntentUpTo(long serverSequence)
        {
            List<long> toComplete = new List<long>();
            foreach (KeyValuePair<long, UniTaskCompletionSource<bool>> pair in _pendingIntent)
            {
                if (pair.Key <= serverSequence)
                {
                    pair.Value.TrySetResult(true);
                    toComplete.Add(pair.Key);
                }
            }

            for (int i = 0; i < toComplete.Count; i++)
            {
                _pendingIntent.Remove(toComplete[i]);
            }
        }

        private void TrackOutbound(RegicideClientIntentPayload payload)
        {
            _metrics.TotalMessagesOut++;
            _metrics.TotalBytesOut += JsonUtility.ToJson(payload).Length;
        }

        private void TrackInbound<T>(T payload)
        {
            _metrics.TotalMessagesIn++;
            _metrics.TotalBytesIn += JsonUtility.ToJson(payload).Length;
        }

        private bool RegisterActionSequence(long serverSequence)
        {
            if (serverSequence <= 0)
            {
                return true;
            }

            if (!_processedActionSequences.Add(serverSequence))
            {
                return false;
            }

            _processedActionSequenceQueue.Enqueue(serverSequence);
            while (_processedActionSequenceQueue.Count > ActionSequenceWindow)
            {
                long evicted = _processedActionSequenceQueue.Dequeue();
                _processedActionSequences.Remove(evicted);
            }

            return true;
        }

        private void ClearMultiplayerCaches(bool clearPublicSnapshot)
        {
            _actionBroadcastHistory.Clear();
            _processedActionSequences.Clear();
            _processedActionSequenceQueue.Clear();

            if (clearPublicSnapshot)
            {
                _latestPublicStateSnapshot = new RegicidePublicStateSnapshotPayload();
            }
        }

        private bool IsLocalHostOrServer()
        {
            return _connectionState != null && (_connectionState.IsHost || _connectionState.IsServer);
        }
    }
}
