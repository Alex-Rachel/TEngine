using System;
using System.Collections.Generic;

namespace GameProto.Regicide
{
    [Serializable]
    public sealed class RegicideAvailableActionSnapshot
    {
        public bool CanPlayCard;
        public bool CanPass;
        public bool CanDefend;
        public int SelectedCardIndex = -1;
        public int SelectedCardDamage;
        public bool IsCurrentSelectionPlayable;
        public bool IsCurrentSelectionJester;
        public int CurrentSelectionAttackValue;
        public int CurrentSelectionDamageValue;
        public int SuggestedNextPlayerIndex = -1;
        public bool IsAwaitingDiscard;
        public int PendingDiscardRequiredValue;
        public int PendingDiscardTargetPlayerIndex = -1;
        public string Message = string.Empty;
        public List<int> PlayableCardIndices = new List<int>();
        public List<int> SelectedCardIndices = new List<int>();
        public List<int> SelectableNextPlayerIndices = new List<int>();
    }

    [Serializable]
    public enum RegicideClientEnvironment
    {
        LocalSinglePlayer = 0,
        NetworkPlay = 1,
        DedicatedServer = 2,
    }

    [Serializable]
    public enum RegicideClientIntentType
    {
        Unknown = 0,
        JoinRoom = 1,
        LeaveRoom = 2,
        Ready = 3,
        StartMatch = 4,
        PlayCard = 5,
        Pass = 6,
        Defend = 7,
        CancelReady = 8,
        ConfigureRoom = 9,
        DiscardForDamage = 10,
    }

    [Serializable]
    public enum RegicideErrorCode
    {
        None = 0,
        NotConnected = 1,
        RoomFull = 2,
        RoomNotFound = 3,
        InvalidState = 4,
        NotYourTurn = 5,
        InvalidAction = 6,
        SessionClosed = 7,
        Unknown = 1000,
    }

    [Serializable]
    public sealed class RegicideConnectionRequest
    {
        public string Address = "127.0.0.1";
        public ushort Port = 7777;
        public bool AsHost;
        public bool AsServerOnly;
        public bool AutoCreateRoom = true;
        public string PlayerId = string.Empty;
    }

    [Serializable]
    public sealed class RegicideConnectionState
    {
        public bool IsConnected;
        public bool IsHost;
        public bool IsServer;
        public string Address = string.Empty;
        public ushort Port;
        public long Timestamp;
    }

    [Serializable]
    public sealed class RegicideRoomSeatSnapshot
    {
        public int SeatIndex;
        public string PlayerId = string.Empty;
        public bool IsReady;
        public bool IsOnline;
    }

    [Serializable]
    public sealed class RegicideRoomSnapshot
    {
        public string SessionId = string.Empty;
        public bool Started;
        public int MaxPlayers = 4;
        public int TargetPlayers = 4;
        public int ConnectedPlayers;
        public int ReadyPlayers;
        public long ServerSequence;
        public List<RegicideRoomSeatSnapshot> Seats = new List<RegicideRoomSeatSnapshot>();
    }

    [Serializable]
    public sealed class RegicideClientIntentPayload
    {
        public string SessionId = string.Empty;
        public string PlayerId = string.Empty;
        public RegicideClientIntentType IntentType = RegicideClientIntentType.Unknown;
        public int TargetPlayers;
        public int CardIndex = -1;
        public int CardRank = -1;
        public int CardSuit = -1;
        public int NextPlayerIndex = -1;
        public List<int> CardIndices = new List<int>();
        public long ClientSequence;
        public long Timestamp;
    }

    [Serializable]
    public sealed class RegicideStateSnapshotPayload
    {
        public string SessionId = string.Empty;
        public long ServerSequence;
        public bool IsGameOver;
        public bool IsVictory;
        public int CurrentPlayerIndex;
        public string StateHash = string.Empty;
        public string StateJson = string.Empty;
        public long Timestamp;
    }

    [Serializable]
    public sealed class RegicideErrorPayload
    {
        public string SessionId = string.Empty;
        public RegicideErrorCode Code = RegicideErrorCode.Unknown;
        public string Message = string.Empty;
        public long RelatedSequence;
        public long Timestamp;
    }

    [Serializable]
    public sealed class RegicideLongSessionMetrics
    {
        public long TotalMessagesIn;
        public long TotalMessagesOut;
        public long TotalBytesIn;
        public long TotalBytesOut;
        public long SessionTicks;
        public float PeakFrameMillis;
        public long GcAllocatedBytesDelta;
    }
}
