using System;
using System.Collections.Generic;

namespace GameLogic.Regicide
{
    public static class RegicideBattleInitializer
    {
        public static RegicideBattleState Create(string sessionId, int ruleId, int seed, IList<string> playerIds, IRegicideRuleConfigProvider configProvider)
        {
            RegicideRuleConfig config = configProvider.GetByRuleId(ruleId);
            if (config == null)
            {
                return null;
            }

            if (playerIds == null || playerIds.Count <= 0)
            {
                return null;
            }

            int playerCount = playerIds.Count;
            int handLimit = ResolveHandLimit(playerCount);
            int jokerCount = ResolveJokerCount(playerCount);

            List<RegicideCard> deck = BuildDeck(jokerCount);
            RemoveEnemyFaceCards(deck, config.EnemyQueue);
            Shuffle(deck, seed);

            RegicideBattleState state = new RegicideBattleState
            {
                SessionId = sessionId,
                RuleId = ruleId,
                Seed = seed,
                HandLimitPerPlayer = handLimit,
                Round = 1,
                CurrentPlayerIndex = 0,
                EnemyCursor = 0,
                AppliedSequence = 0,
                IsGameOver = false,
                IsVictory = false,
                IsEnemyImmunityDisabledByJester = false,
                IsAwaitingDiscard = false,
                PendingDiscardTargetPlayerIndex = -1,
                PendingDiscardRequiredValue = 0,
                Players = new List<RegicidePlayerState>(),
                DrawPile = new List<RegicideCard>(),
                DiscardPile = new List<RegicideCard>(),
                RemainingEnemies = CloneEnemies(config.EnemyQueue),
            };

            for (int i = 0; i < playerIds.Count; i++)
            {
                state.Players.Add(new RegicidePlayerState
                {
                    PlayerId = playerIds[i],
                    SeatIndex = i,
                    Shield = 0,
                    Hand = new List<RegicideCard>(),
                });
            }

            for (int i = 0; i < handLimit; i++)
            {
                for (int seat = 0; seat < state.Players.Count; seat++)
                {
                    if (deck.Count <= 0)
                    {
                        break;
                    }

                    RegicidePlayerState player = state.Players[seat];
                    player.Hand.Add(deck[0]);
                    deck.RemoveAt(0);
                }
            }

            state.DrawPile.AddRange(deck);
            if (state.RemainingEnemies.Count > 0)
            {
                state.CurrentEnemy = state.RemainingEnemies[0];
                state.RemainingEnemies.RemoveAt(0);
            }

            state.BattleLog.Add($"战斗初始化：{playerCount} 人局，每人手牌上限 {handLimit}，小丑 {jokerCount} 张。");
            if (state.CurrentEnemy != null)
            {
                state.BattleLog.Add($"敌人登场：{state.CurrentEnemy.Name}（生命 {state.CurrentEnemy.Health} / 攻击 {state.CurrentEnemy.Attack}）。");
            }

            return state;
        }

        private static int ResolveHandLimit(int playerCount)
        {
            switch (playerCount)
            {
                case 1:
                    return 8;
                case 2:
                    return 7;
                case 3:
                    return 6;
                default:
                    return 5;
            }
        }

        private static int ResolveJokerCount(int playerCount)
        {
            if (playerCount >= 4)
            {
                return 2;
            }

            if (playerCount == 3)
            {
                return 1;
            }

            return 0;
        }

        private static List<RegicideCard> BuildDeck(int jokerCount)
        {
            List<RegicideCard> cards = new List<RegicideCard>(52 + jokerCount);
            for (int suit = 0; suit < 4; suit++)
            {
                for (int rank = 1; rank <= 13; rank++)
                {
                    cards.Add(new RegicideCard
                    {
                        Suit = (RegicideSuit)suit,
                        Rank = rank,
                    });
                }
            }

            for (int i = 0; i < jokerCount; i++)
            {
                cards.Add(new RegicideCard
                {
                    Suit = RegicideSuit.Joker,
                    Rank = 0,
                });
            }

            return cards;
        }

        private static void RemoveEnemyFaceCards(List<RegicideCard> deck, List<RegicideEnemyState> enemies)
        {
            if (deck == null || enemies == null)
            {
                return;
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                RegicideEnemyState enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                int index = deck.FindIndex(card =>
                    card.Suit == enemy.Suit &&
                    card.Rank == enemy.Rank &&
                    enemy.Rank >= 11 &&
                    enemy.Rank <= 13);
                if (index >= 0)
                {
                    deck.RemoveAt(index);
                }
            }
        }

        private static List<RegicideEnemyState> CloneEnemies(List<RegicideEnemyState> src)
        {
            List<RegicideEnemyState> list = new List<RegicideEnemyState>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                RegicideEnemyState enemy = src[i];
                list.Add(new RegicideEnemyState
                {
                    EnemyId = enemy.EnemyId,
                    Name = enemy.Name,
                    Suit = enemy.Suit,
                    Rank = enemy.Rank,
                    Health = enemy.Health,
                    Attack = enemy.Attack,
                    Defeated = false,
                });
            }

            return list;
        }

        private static void Shuffle(List<RegicideCard> cards, int seed)
        {
            Random random = new Random(seed);
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(0, i + 1);
                RegicideCard temp = cards[i];
                cards[i] = cards[swapIndex];
                cards[swapIndex] = temp;
            }
        }
    }
}
