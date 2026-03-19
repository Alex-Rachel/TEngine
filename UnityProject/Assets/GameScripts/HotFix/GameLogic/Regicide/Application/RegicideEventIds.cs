using TEngine;

namespace GameLogic.Regicide
{
    public static class RegicideEventIds
    {
        public static readonly int ConnectionStateChanged = RuntimeId.ToRuntimeId("regicide.event.connection.changed");
        public static readonly int RoomSnapshotUpdated = RuntimeId.ToRuntimeId("regicide.event.room.snapshot.updated");
        public static readonly int BattleSnapshotUpdated = RuntimeId.ToRuntimeId("regicide.event.battle.snapshot.updated");
        public static readonly int PublicStateSnapshotUpdated = RuntimeId.ToRuntimeId("regicide.event.public.state.snapshot.updated");
        public static readonly int ActionBroadcastReceived = RuntimeId.ToRuntimeId("regicide.event.action.broadcast.received");
        public static readonly int BattleErrorReceived = RuntimeId.ToRuntimeId("regicide.event.battle.error.received");
        public static readonly int UiNavigateLobby = RuntimeId.ToRuntimeId("regicide.event.ui.navigate.lobby");
        public static readonly int UiNavigateRoom = RuntimeId.ToRuntimeId("regicide.event.ui.navigate.room");
        public static readonly int UiNavigateBattle = RuntimeId.ToRuntimeId("regicide.event.ui.navigate.battle");
        public static readonly int UiNavigateResult = RuntimeId.ToRuntimeId("regicide.event.ui.navigate.result");
    }
}
