using System.Collections.Generic;
using Mirror;
using GameProto.Regicide;
using TEngine;
using UnityEngine;

namespace Regicide.Main
{
    /// <summary>
    /// Mirror bridge that isolates transport concerns in non-hotfix assembly.
    /// Hotfix communicates through GameEvent + shared DTOs.
    /// </summary>
    public sealed class RegicideMirrorBridge : MonoBehaviour
    {
        private struct RegicideIntentMirrorMessage : NetworkMessage
        {
            public string Json;
        }

        private struct RegicideRoomSnapshotMirrorMessage : NetworkMessage
        {
            public string Json;
        }

        private struct RegicideStateSnapshotMirrorMessage : NetworkMessage
        {
            public string Json;
        }

        private struct RegicideErrorMirrorMessage : NetworkMessage
        {
            public string Json;
        }

        private struct RegicidePublicStateSnapshotMirrorMessage : NetworkMessage
        {
            public string Json;
        }

        private struct RegicideActionBroadcastMirrorMessage : NetworkMessage
        {
            public string Json;
        }

        private const int MaxConnections = 8;
        private const int ActionHistoryLimit = 512;

        [SerializeField] private Transport transport;
        [SerializeField] private bool keepServerAliveWhenNoClient;

        private readonly Dictionary<int, string> _connectionPlayers = new Dictionary<int, string>();
        private readonly List<RegicideStateSnapshotPayload> _stateHistory = new List<RegicideStateSnapshotPayload>();
        private readonly List<RegicideActionBroadcastPayload> _actionHistory = new List<RegicideActionBroadcastPayload>();
        private readonly HashSet<long> _actionHistorySequenceSet = new HashSet<long>();
        private RegicideRoomSnapshot _latestRoom;
        private RegicideStateSnapshotPayload _latestState;
        private RegicidePublicStateSnapshotPayload _latestPublicState;
        private RegicideErrorPayload _latestError;
        private bool _handlersRegistered;
        private string _localPlayerId = string.Empty;

        private void Awake()
        {
            if (transport != null)
            {
                Transport.active = transport;
            }
        }

        private void OnEnable()
        {
            GameEvent.AddEventListener<RegicideConnectionRequest>(RegicideBridgeEvents.ClientConnectRequest, OnClientConnectRequest);
            GameEvent.AddEventListener(RegicideBridgeEvents.ClientDisconnectRequest, OnClientDisconnectRequest);
            GameEvent.AddEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientJoinRoomRequest, OnClientJoinRoomRequest);
            GameEvent.AddEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientLeaveRoomRequest, OnClientLeaveRoomRequest);
            GameEvent.AddEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientReadyRequest, OnClientReadyRequest);
            GameEvent.AddEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientStartRequest, OnClientStartRequest);
            GameEvent.AddEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientIntentRequest, OnClientIntentRequest);

            GameEvent.AddEventListener<RegicideRoomSnapshot>(RegicideBridgeEvents.ServerPublishRoomSnapshot, OnServerPublishRoomSnapshot);
            GameEvent.AddEventListener<RegicideStateSnapshotPayload>(RegicideBridgeEvents.ServerPublishState, OnServerPublishState);
            GameEvent.AddEventListener<RegicidePublicStateSnapshotPayload>(RegicideBridgeEvents.ServerPublishPublicStateSnapshot, OnServerPublishPublicStateSnapshot);
            GameEvent.AddEventListener<RegicideActionBroadcastPayload>(RegicideBridgeEvents.ServerPublishActionBroadcast, OnServerPublishActionBroadcast);
            GameEvent.AddEventListener<RegicideErrorPayload>(RegicideBridgeEvents.ServerPublishError, OnServerPublishError);

            RegisterNetworkHandlers();
        }

        private void OnDisable()
        {
            GameEvent.RemoveEventListener<RegicideConnectionRequest>(RegicideBridgeEvents.ClientConnectRequest, OnClientConnectRequest);
            GameEvent.RemoveEventListener(RegicideBridgeEvents.ClientDisconnectRequest, OnClientDisconnectRequest);
            GameEvent.RemoveEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientJoinRoomRequest, OnClientJoinRoomRequest);
            GameEvent.RemoveEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientLeaveRoomRequest, OnClientLeaveRoomRequest);
            GameEvent.RemoveEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientReadyRequest, OnClientReadyRequest);
            GameEvent.RemoveEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientStartRequest, OnClientStartRequest);
            GameEvent.RemoveEventListener<RegicideClientIntentPayload>(RegicideBridgeEvents.ClientIntentRequest, OnClientIntentRequest);

            GameEvent.RemoveEventListener<RegicideRoomSnapshot>(RegicideBridgeEvents.ServerPublishRoomSnapshot, OnServerPublishRoomSnapshot);
            GameEvent.RemoveEventListener<RegicideStateSnapshotPayload>(RegicideBridgeEvents.ServerPublishState, OnServerPublishState);
            GameEvent.RemoveEventListener<RegicidePublicStateSnapshotPayload>(RegicideBridgeEvents.ServerPublishPublicStateSnapshot, OnServerPublishPublicStateSnapshot);
            GameEvent.RemoveEventListener<RegicideActionBroadcastPayload>(RegicideBridgeEvents.ServerPublishActionBroadcast, OnServerPublishActionBroadcast);
            GameEvent.RemoveEventListener<RegicideErrorPayload>(RegicideBridgeEvents.ServerPublishError, OnServerPublishError);

            UnregisterNetworkHandlers();
        }

        private void RegisterNetworkHandlers()
        {
            if (_handlersRegistered)
            {
                return;
            }

            _handlersRegistered = true;

            NetworkClient.RegisterHandler<RegicideRoomSnapshotMirrorMessage>(OnClientRoomSnapshotMessage, false);
            NetworkClient.RegisterHandler<RegicideStateSnapshotMirrorMessage>(OnClientStateSnapshotMessage, false);
            NetworkClient.RegisterHandler<RegicidePublicStateSnapshotMirrorMessage>(OnClientPublicStateSnapshotMessage, false);
            NetworkClient.RegisterHandler<RegicideActionBroadcastMirrorMessage>(OnClientActionBroadcastMessage, false);
            NetworkClient.RegisterHandler<RegicideErrorMirrorMessage>(OnClientErrorMessage, false);

            NetworkServer.RegisterHandler<RegicideIntentMirrorMessage>(OnServerIntentMessage, false);

            NetworkClient.OnConnectedEvent += OnClientConnected;
            NetworkClient.OnDisconnectedEvent += OnClientDisconnected;
            NetworkServer.OnConnectedEvent += OnServerConnected;
            NetworkServer.OnDisconnectedEvent += OnServerDisconnected;
        }

        private void UnregisterNetworkHandlers()
        {
            if (!_handlersRegistered)
            {
                return;
            }

            _handlersRegistered = false;

            NetworkClient.UnregisterHandler<RegicideRoomSnapshotMirrorMessage>();
            NetworkClient.UnregisterHandler<RegicideStateSnapshotMirrorMessage>();
            NetworkClient.UnregisterHandler<RegicidePublicStateSnapshotMirrorMessage>();
            NetworkClient.UnregisterHandler<RegicideActionBroadcastMirrorMessage>();
            NetworkClient.UnregisterHandler<RegicideErrorMirrorMessage>();
            NetworkServer.UnregisterHandler<RegicideIntentMirrorMessage>();

            NetworkClient.OnConnectedEvent -= OnClientConnected;
            NetworkClient.OnDisconnectedEvent -= OnClientDisconnected;
            NetworkServer.OnConnectedEvent -= OnServerConnected;
            NetworkServer.OnDisconnectedEvent -= OnServerDisconnected;
        }

        private void OnClientConnectRequest(RegicideConnectionRequest request)
        {
            _localPlayerId = request != null ? request.PlayerId : string.Empty;
            ConfigureTransport(request);

            if (request != null && request.AsServerOnly)
            {
                if (!NetworkServer.active)
                {
                    NetworkServer.Listen(MaxConnections);
                }

                PublishConnectionState();
                return;
            }

            if (request != null && request.AsHost)
            {
                if (!NetworkServer.active)
                {
                    NetworkServer.Listen(MaxConnections);
                }

                if (!NetworkClient.active)
                {
                    NetworkClient.ConnectHost();
                }
            }
            else
            {
                if (!NetworkClient.active)
                {
                    string address = request != null ? request.Address : "127.0.0.1";
                    NetworkClient.Connect(address);
                }
            }

            PublishConnectionState();
        }

        private static void ConfigureTransport(RegicideConnectionRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (Transport.active is PortTransport portTransport)
            {
                portTransport.Port = request.Port;
            }
        }

        private void OnClientDisconnectRequest()
        {
            if (NetworkClient.active)
            {
                NetworkClient.Disconnect();
            }

            if (NetworkServer.active && !keepServerAliveWhenNoClient)
            {
                CleanupServerSession();
                NetworkServer.Shutdown();
            }

            PublishDisconnected();
        }

        private void OnClientJoinRoomRequest(RegicideClientIntentPayload payload)
        {
            payload.IntentType = RegicideClientIntentType.JoinRoom;
            SendIntentToServer(payload);
        }

        private void OnClientLeaveRoomRequest(RegicideClientIntentPayload payload)
        {
            payload.IntentType = RegicideClientIntentType.LeaveRoom;
            SendIntentToServer(payload);
        }

        private void OnClientReadyRequest(RegicideClientIntentPayload payload)
        {
            payload.IntentType = RegicideClientIntentType.Ready;
            SendIntentToServer(payload);
        }

        private void OnClientStartRequest(RegicideClientIntentPayload payload)
        {
            payload.IntentType = RegicideClientIntentType.StartMatch;
            SendIntentToServer(payload);
        }

        private void OnClientIntentRequest(RegicideClientIntentPayload payload)
        {
            SendIntentToServer(payload);
        }

        private void SendIntentToServer(RegicideClientIntentPayload payload)
        {
            if (!NetworkClient.isConnected && !NetworkServer.activeHost)
            {
                PublishClientError(new RegicideErrorPayload
                {
                    SessionId = payload != null ? payload.SessionId : string.Empty,
                    Code = RegicideErrorCode.NotConnected,
                    Message = "Not connected to server.",
                    RelatedSequence = payload != null ? payload.ClientSequence : 0,
                    Timestamp = RegicideClock.NowUnixMilliseconds(),
                });
                return;
            }

            if (payload == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(payload.PlayerId))
            {
                payload.PlayerId = _localPlayerId;
            }

            RegicideIntentMirrorMessage message = new RegicideIntentMirrorMessage
            {
                Json = JsonUtility.ToJson(payload),
            };
            NetworkClient.Send(message);
        }

        private void OnServerIntentMessage(NetworkConnectionToClient connection, RegicideIntentMirrorMessage message)
        {
            if (string.IsNullOrEmpty(message.Json))
            {
                return;
            }

            RegicideClientIntentPayload payload = JsonUtility.FromJson<RegicideClientIntentPayload>(message.Json);
            if (payload == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(payload.PlayerId))
            {
                payload.PlayerId = $"Conn-{connection.connectionId}";
            }

            _connectionPlayers[connection.connectionId] = payload.PlayerId;
            GameEvent.Send(RegicideBridgeEvents.ServerIntentReceived, payload);
        }

        private void OnServerPublishRoomSnapshot(RegicideRoomSnapshot room)
        {
            _latestRoom = room;
            if (room == null || !room.Started)
            {
                // 房间回到待机态时清空上一局战斗缓存，避免新一局收到旧状态快照。
                _latestState = null;
                _stateHistory.Clear();
                _latestPublicState = null;
                _actionHistory.Clear();
                _actionHistorySequenceSet.Clear();
            }

            string json = JsonUtility.ToJson(room);
            NetworkServer.SendToAll(new RegicideRoomSnapshotMirrorMessage { Json = json });

            if (NetworkServer.activeHost)
            {
                GameEvent.Send(RegicideBridgeEvents.ClientRoomSnapshot, room);
            }
        }

        private void OnServerPublishState(RegicideStateSnapshotPayload state)
        {
            _latestState = state;
            _stateHistory.Add(state);
            if (_stateHistory.Count > 256)
            {
                _stateHistory.RemoveAt(0);
            }

            string json = JsonUtility.ToJson(state);
            NetworkServer.SendToAll(new RegicideStateSnapshotMirrorMessage { Json = json });

            if (NetworkServer.activeHost)
            {
                GameEvent.Send(RegicideBridgeEvents.ClientStateSnapshot, state);
            }
        }

        private void OnServerPublishPublicStateSnapshot(RegicidePublicStateSnapshotPayload snapshot)
        {
            _latestPublicState = snapshot;
            string json = JsonUtility.ToJson(snapshot);
            NetworkServer.SendToAll(new RegicidePublicStateSnapshotMirrorMessage { Json = json });

            if (NetworkServer.activeHost)
            {
                GameEvent.Send(RegicideBridgeEvents.ClientPublicStateSnapshot, snapshot);
            }
        }

        private void OnServerPublishActionBroadcast(RegicideActionBroadcastPayload payload)
        {
            if (payload == null)
            {
                return;
            }

            if (!RegisterActionHistory(payload))
            {
                return;
            }

            string json = JsonUtility.ToJson(payload);
            NetworkServer.SendToAll(new RegicideActionBroadcastMirrorMessage { Json = json });

            if (NetworkServer.activeHost)
            {
                GameEvent.Send(RegicideBridgeEvents.ClientActionBroadcast, payload);
            }
        }

        private void OnServerPublishError(RegicideErrorPayload error)
        {
            _latestError = error;
            string json = JsonUtility.ToJson(error);
            NetworkServer.SendToAll(new RegicideErrorMirrorMessage { Json = json });

            if (NetworkServer.activeHost)
            {
                GameEvent.Send(RegicideBridgeEvents.ClientError, error);
            }
        }

        private void OnClientRoomSnapshotMessage(RegicideRoomSnapshotMirrorMessage message)
        {
            if (string.IsNullOrEmpty(message.Json))
            {
                return;
            }

            RegicideRoomSnapshot room = JsonUtility.FromJson<RegicideRoomSnapshot>(message.Json);
            GameEvent.Send(RegicideBridgeEvents.ClientRoomSnapshot, room);
        }

        private void OnClientStateSnapshotMessage(RegicideStateSnapshotMirrorMessage message)
        {
            if (string.IsNullOrEmpty(message.Json))
            {
                return;
            }

            RegicideStateSnapshotPayload snapshot = JsonUtility.FromJson<RegicideStateSnapshotPayload>(message.Json);
            GameEvent.Send(RegicideBridgeEvents.ClientStateSnapshot, snapshot);
        }

        private void OnClientPublicStateSnapshotMessage(RegicidePublicStateSnapshotMirrorMessage message)
        {
            if (string.IsNullOrEmpty(message.Json))
            {
                return;
            }

            RegicidePublicStateSnapshotPayload snapshot = JsonUtility.FromJson<RegicidePublicStateSnapshotPayload>(message.Json);
            GameEvent.Send(RegicideBridgeEvents.ClientPublicStateSnapshot, snapshot);
        }

        private void OnClientActionBroadcastMessage(RegicideActionBroadcastMirrorMessage message)
        {
            if (string.IsNullOrEmpty(message.Json))
            {
                return;
            }

            RegicideActionBroadcastPayload payload = JsonUtility.FromJson<RegicideActionBroadcastPayload>(message.Json);
            GameEvent.Send(RegicideBridgeEvents.ClientActionBroadcast, payload);
        }

        private void OnClientErrorMessage(RegicideErrorMirrorMessage message)
        {
            if (string.IsNullOrEmpty(message.Json))
            {
                return;
            }

            RegicideErrorPayload payload = JsonUtility.FromJson<RegicideErrorPayload>(message.Json);
            GameEvent.Send(RegicideBridgeEvents.ClientError, payload);
        }

        private void OnClientConnected()
        {
            PublishConnectionState();
            if (_latestRoom != null && !string.IsNullOrEmpty(_latestRoom.SessionId))
            {
                GameEvent.Send(RegicideBridgeEvents.ClientRoomSnapshot, _latestRoom);
            }

            if (_latestState != null && !string.IsNullOrEmpty(_latestState.SessionId))
            {
                GameEvent.Send(RegicideBridgeEvents.ClientStateSnapshot, _latestState);
            }

            if (_latestPublicState != null && !string.IsNullOrEmpty(_latestPublicState.SessionId))
            {
                GameEvent.Send(RegicideBridgeEvents.ClientPublicStateSnapshot, _latestPublicState);
            }

            for (int i = 0; i < _actionHistory.Count; i++)
            {
                GameEvent.Send(RegicideBridgeEvents.ClientActionBroadcast, _actionHistory[i]);
            }
        }

        private void OnClientDisconnected()
        {
            PublishDisconnected();
        }

        private void OnServerConnected(NetworkConnectionToClient connection)
        {
            if (_latestRoom != null && !string.IsNullOrEmpty(_latestRoom.SessionId))
            {
                connection.Send(new RegicideRoomSnapshotMirrorMessage
                {
                    Json = JsonUtility.ToJson(_latestRoom),
                });
            }

            if (_latestState != null && !string.IsNullOrEmpty(_latestState.SessionId))
            {
                connection.Send(new RegicideStateSnapshotMirrorMessage
                {
                    Json = JsonUtility.ToJson(_latestState),
                });
            }

            if (_latestPublicState != null && !string.IsNullOrEmpty(_latestPublicState.SessionId))
            {
                connection.Send(new RegicidePublicStateSnapshotMirrorMessage
                {
                    Json = JsonUtility.ToJson(_latestPublicState),
                });
            }

            ReplayActionHistory(connection);
        }

        private void OnServerDisconnected(NetworkConnectionToClient connection)
        {
            _connectionPlayers.Remove(connection.connectionId);
            if (NetworkServer.connections.Count == 0 && !keepServerAliveWhenNoClient)
            {
                CleanupServerSession();
            }
        }

        private bool RegisterActionHistory(RegicideActionBroadcastPayload payload)
        {
            if (payload == null)
            {
                return false;
            }

            if (payload.ServerSequence > 0)
            {
                if (!_actionHistorySequenceSet.Add(payload.ServerSequence))
                {
                    return false;
                }
            }

            _actionHistory.Add(payload);
            while (_actionHistory.Count > ActionHistoryLimit)
            {
                RegicideActionBroadcastPayload evicted = _actionHistory[0];
                _actionHistory.RemoveAt(0);
                if (evicted != null && evicted.ServerSequence > 0)
                {
                    _actionHistorySequenceSet.Remove(evicted.ServerSequence);
                }
            }

            return true;
        }

        private void ReplayActionHistory(NetworkConnectionToClient connection)
        {
            if (connection == null || _actionHistory.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < _actionHistory.Count; i++)
            {
                RegicideActionBroadcastPayload payload = _actionHistory[i];
                if (payload == null)
                {
                    continue;
                }

                connection.Send(new RegicideActionBroadcastMirrorMessage
                {
                    Json = JsonUtility.ToJson(payload),
                });
            }
        }

        private void PublishConnectionState()
        {
            RegicideConnectionState state = new RegicideConnectionState
            {
                IsConnected = NetworkClient.isConnected || NetworkServer.activeHost || NetworkServer.active,
                IsHost = NetworkServer.activeHost,
                IsServer = NetworkServer.active,
                Address = Transport.active != null ? Transport.active.ServerUri().Host : string.Empty,
                Port = Transport.active is PortTransport portTransport ? (ushort)portTransport.Port : (ushort)0,
                Timestamp = RegicideClock.NowUnixMilliseconds(),
            };
            GameEvent.Send(RegicideBridgeEvents.ClientConnected, state);
        }

        private static void PublishDisconnected()
        {
            GameEvent.Send(RegicideBridgeEvents.ClientDisconnected);
        }

        private static void PublishClientError(RegicideErrorPayload payload)
        {
            GameEvent.Send(RegicideBridgeEvents.ClientError, payload);
        }

        private void CleanupServerSession()
        {
            _connectionPlayers.Clear();
            _stateHistory.Clear();
            _actionHistory.Clear();
            _actionHistorySequenceSet.Clear();
            _latestRoom = null;
            _latestState = null;
            _latestPublicState = null;
            _latestError = null;
        }
    }
}


