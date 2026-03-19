using System;
using System.Collections.Generic;

namespace GameLogic.Regicide
{
    [Serializable]
    public enum RegicideSuit
    {
        Spade = 0,
        Heart = 1,
        Club = 2,
        Diamond = 3,
        Joker = 4,
    }

    [Serializable]
    public enum RegicideActionType
    {
        Unknown = 0,
        PlayCard = 1,
        Pass = 2,
        Defend = 3,
        Discard = 4,
    }

    [Serializable]
    public sealed class RegicideCard
    {
        public RegicideSuit Suit;
        public int Rank;

        public bool IsJester => Suit == RegicideSuit.Joker;

        public int AttackValue
        {
            get
            {
                if (IsJester)
                {
                    return 0;
                }

                if (Rank <= 1)
                {
                    return 1;
                }

                return Rank > 10 ? 10 : Rank;
            }
        }

        public int DamageValue => AttackValue;
    }

    [Serializable]
    public sealed class RegicideSuitEffect
    {
        public RegicideSuit Suit;
        public int DamageBonus;
        public int ShieldBonus;
        public bool DrawExtraCard;
    }

    [Serializable]
    public sealed class RegicideEnemyState
    {
        public int EnemyId;
        public string Name = string.Empty;
        public RegicideSuit Suit = RegicideSuit.Spade;
        public int Rank = 11;
        public int Health;
        public int Attack;
        public bool Defeated;
    }

    [Serializable]
    public sealed class RegicidePlayerState
    {
        public string PlayerId = string.Empty;
        public int SeatIndex;
        public int Shield;
        public List<RegicideCard> Hand = new List<RegicideCard>();
    }

    [Serializable]
    public sealed class RegicideRuleConfig
    {
        public int RuleId;
        public int MaxPlayers = 4;
        public int InitialHandSize = 8;
        public int BaseShield = 1;
        public int DefeatHandPenaltyPerOverflow = 1;
        public List<RegicideEnemyState> EnemyQueue = new List<RegicideEnemyState>();
        public List<RegicideSuitEffect> SuitEffects = new List<RegicideSuitEffect>();
    }

    [Serializable]
    public sealed class RegicideBattleState
    {
        public string SessionId = string.Empty;
        public int RuleId;
        public int Seed;
        public int HandLimitPerPlayer = 8;
        public int Round;
        public int CurrentPlayerIndex;
        public int EnemyCursor;
        public long AppliedSequence;
        public bool IsGameOver;
        public bool IsVictory;
        public bool IsEnemyImmunityDisabledByJester;
        public bool IsAwaitingDiscard;
        public int PendingDiscardTargetPlayerIndex = -1;
        public int PendingDiscardRequiredValue;

        public RegicideEnemyState CurrentEnemy = new RegicideEnemyState();
        public List<RegicideEnemyState> RemainingEnemies = new List<RegicideEnemyState>();
        public List<RegicidePlayerState> Players = new List<RegicidePlayerState>();
        public List<RegicideCard> DrawPile = new List<RegicideCard>();
        public List<RegicideCard> DiscardPile = new List<RegicideCard>();
        public List<string> BattleLog = new List<string>();
    }

    [Serializable]
    public sealed class RegicidePlayerAction
    {
        public string SessionId = string.Empty;
        public string PlayerId = string.Empty;
        public RegicideActionType ActionType = RegicideActionType.Unknown;
        public int CardIndex = -1;
        public int NextPlayerIndex = -1;
        public List<int> CardIndices = new List<int>();
        public long ClientSequence;
        public long Timestamp;
    }

    [Serializable]
    public sealed class RegicideActionResult
    {
        public bool Success;
        public string Error = string.Empty;
        public long AppliedSequence;
        public string StateHash = string.Empty;
        public RegicideBattleState State = null;
    }

    [Serializable]
    public sealed class RegicideReplayResult
    {
        public bool Success;
        public string Error = string.Empty;
        public List<string> KeyFrameHashes = new List<string>();
        public RegicideBattleState FinalState = null;
    }
}
