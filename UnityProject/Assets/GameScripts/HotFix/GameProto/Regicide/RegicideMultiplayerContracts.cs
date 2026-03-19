using System;
using System.Collections.Generic;

namespace GameProto.Regicide
{
    [Serializable]
    public enum RegicideActionBroadcastType
    {
        Unknown = 0,
        PlayCard = 1,
        Pass = 2,
        DiscardForDamage = 3,
        StartMatch = 4,
    }

    [Serializable]
    public sealed class RegicidePublicPlayerState
    {
        public string PlayerId = string.Empty;
        public int SeatIndex = -1;
        public bool IsOnline = true;
        public bool IsReady;
        public bool IsCurrentTurn;
        public bool IsPendingDiscardTarget;
        public int HandCount;
        public string Phase = string.Empty;
    }

    [Serializable]
    public sealed class RegicidePublicStateSnapshotPayload
    {
        public string SessionId = string.Empty;
        public long ServerSequence;
        public int CurrentPlayerIndex = -1;
        public bool IsAwaitingDiscard;
        public int PendingDiscardTargetPlayerIndex = -1;
        public List<RegicidePublicPlayerState> Players = new List<RegicidePublicPlayerState>();
        public long Timestamp;
    }

    [Serializable]
    public sealed class RegicideActionBroadcastPayload
    {
        public string SessionId = string.Empty;
        public long ServerSequence;
        public string ActorPlayerId = string.Empty;
        public RegicideActionBroadcastType ActionType = RegicideActionBroadcastType.Unknown;
        public List<string> PublicCards = new List<string>();
        public string Summary = string.Empty;
        public int EnemyHealthBefore = -1;
        public int EnemyHealthAfter = -1;
        public int EnemyAttackBefore = -1;
        public int EnemyAttackAfter = -1;
        public bool IsGameOver;
        public bool IsVictory;
        public long Timestamp;
    }
}
