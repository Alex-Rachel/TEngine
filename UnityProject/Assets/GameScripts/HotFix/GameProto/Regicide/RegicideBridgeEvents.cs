namespace GameProto.Regicide
{
    /// <summary>
    /// Shared event names for communication between main assembly bridge and hotfix gameplay.
    /// </summary>
    public static class RegicideBridgeEvents
    {
        public const string ClientConnectRequest = "regicide.bridge.client.connect.request";
        public const string ClientDisconnectRequest = "regicide.bridge.client.disconnect.request";
        public const string ClientJoinRoomRequest = "regicide.bridge.client.room.join.request";
        public const string ClientLeaveRoomRequest = "regicide.bridge.client.room.leave.request";
        public const string ClientReadyRequest = "regicide.bridge.client.room.ready.request";
        public const string ClientStartRequest = "regicide.bridge.client.room.start.request";
        public const string ClientIntentRequest = "regicide.bridge.client.intent.request";

        public const string ClientConnected = "regicide.bridge.client.connected";
        public const string ClientDisconnected = "regicide.bridge.client.disconnected";
        public const string ClientRoomSnapshot = "regicide.bridge.client.room.snapshot";
        public const string ClientStateSnapshot = "regicide.bridge.client.state.snapshot";
        public const string ClientPublicStateSnapshot = "regicide.bridge.client.public.state.snapshot";
        public const string ClientActionBroadcast = "regicide.bridge.client.action.broadcast";
        public const string ClientError = "regicide.bridge.client.error";

        public const string ServerIntentReceived = "regicide.bridge.server.intent.received";
        public const string ServerPublishRoomSnapshot = "regicide.bridge.server.publish.room.snapshot";
        public const string ServerPublishState = "regicide.bridge.server.publish.state";
        public const string ServerPublishPublicStateSnapshot = "regicide.bridge.server.publish.public.state.snapshot";
        public const string ServerPublishActionBroadcast = "regicide.bridge.server.publish.action.broadcast";
        public const string ServerPublishError = "regicide.bridge.server.publish.error";
    }
}
