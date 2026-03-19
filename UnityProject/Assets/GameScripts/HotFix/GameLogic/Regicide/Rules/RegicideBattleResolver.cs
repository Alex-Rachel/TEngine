using System.Collections.Generic;
using System.Text;

namespace GameLogic.Regicide
{
    public static class RegicideBattleResolver
    {
        public static RegicideActionResult ApplyAction(RegicideBattleState state, RegicidePlayerAction action, RegicideRuleConfig config)
        {
            if (!RegicideActionValidator.Validate(state, action, out string validationError))
            {
                return new RegicideActionResult
                {
                    Success = false,
                    Error = validationError,
                    AppliedSequence = state != null ? state.AppliedSequence : 0,
                    State = state,
                    StateHash = state != null ? RegicideStateHasher.ComputeHash(state) : string.Empty,
                };
            }

            if (state == null || config == null)
            {
                return new RegicideActionResult
                {
                    Success = false,
                    Error = "状态或规则配置为空。",
                    AppliedSequence = state != null ? state.AppliedSequence : 0,
                    State = state,
                    StateHash = state != null ? RegicideStateHasher.ComputeHash(state) : string.Empty,
                };
            }

            RegicidePlayerState currentPlayer = state.Players[state.CurrentPlayerIndex];
            state.AppliedSequence++;

            bool shouldAdvanceTurn = false;
            int forcedNextPlayerIndex = -1;
            switch (action.ActionType)
            {
                case RegicideActionType.PlayCard:
                    ResolvePlayCard(state, currentPlayer, action, out shouldAdvanceTurn, out forcedNextPlayerIndex);
                    break;

                case RegicideActionType.Pass:
                    ResolvePass(state, currentPlayer, out shouldAdvanceTurn);
                    break;

                case RegicideActionType.Discard:
                    ResolveDiscard(state, currentPlayer, action, out shouldAdvanceTurn);
                    break;

                default:
                    return new RegicideActionResult
                    {
                        Success = false,
                        Error = "不支持的动作类型。",
                        AppliedSequence = state.AppliedSequence,
                        State = state,
                        StateHash = RegicideStateHasher.ComputeHash(state),
                    };
            }

            if (!state.IsGameOver && shouldAdvanceTurn)
            {
                AdvanceTurn(state, forcedNextPlayerIndex);
            }

            return new RegicideActionResult
            {
                Success = true,
                Error = string.Empty,
                AppliedSequence = state.AppliedSequence,
                State = state,
                StateHash = RegicideStateHasher.ComputeHash(state),
            };
        }

        private static void ResolvePlayCard(
            RegicideBattleState state,
            RegicidePlayerState currentPlayer,
            RegicidePlayerAction action,
            out bool shouldAdvanceTurn,
            out int forcedNextPlayerIndex)
        {
            shouldAdvanceTurn = false;
            forcedNextPlayerIndex = -1;

            if (state.CurrentEnemy == null || state.CurrentEnemy.Defeated)
            {
                SpawnNextEnemyOrWin(state);
                if (state.IsGameOver)
                {
                    return;
                }
            }

            List<int> selectedIndices = RegicideActionValidator.NormalizeCardIndices(action.CardIndices);
            if (selectedIndices.Count <= 0 && action.CardIndex >= 0)
            {
                selectedIndices.Add(action.CardIndex);
            }

            if (!RegicideActionValidator.CanPlayCard(state, currentPlayer, selectedIndices, out int attackValue, out bool isJester, out _))
            {
                return;
            }

            List<RegicideCard> playedCards = RemoveCardsFromHand(currentPlayer, selectedIndices);

            if (isJester)
            {
                AppendCardsToDiscard(state, playedCards);
                state.IsEnemyImmunityDisabledByJester = true;
                forcedNextPlayerIndex = ResolveNextPlayerIndex(state, action.NextPlayerIndex);
                string nextPlayerName = GetPlayerIdByIndex(state, forcedNextPlayerIndex);
                state.BattleLog.Add($"{currentPlayer.PlayerId} 打出小丑：本回合跳过伤害与承伤，敌人花色免疫失效；下一位行动者为 {nextPlayerName}。");
                shouldAdvanceTurn = true;
                return;
            }

            RegicideEnemyState enemy = state.CurrentEnemy;
            int enemyHpBefore = enemy.Health;
            int enemyAtkBefore = enemy.Attack;

            bool hasHeart = HasSuit(playedCards, RegicideSuit.Heart);
            bool hasDiamond = HasSuit(playedCards, RegicideSuit.Diamond);
            bool hasSpade = HasSuit(playedCards, RegicideSuit.Spade);
            bool hasClub = HasSuit(playedCards, RegicideSuit.Club);

            bool heartBlocked = IsSuitBlocked(state, RegicideSuit.Heart);
            bool diamondBlocked = IsSuitBlocked(state, RegicideSuit.Diamond);
            bool spadeBlocked = IsSuitBlocked(state, RegicideSuit.Spade);
            bool clubBlocked = IsSuitBlocked(state, RegicideSuit.Club);

            int recycled = 0;
            int drawn = 0;
            int reducedAttack = 0;
            int damage = attackValue;

            // 顺序：红桃 -> 方块 -> 黑桃 -> 梅花翻倍 -> 结算伤害。
            if (hasHeart && !heartBlocked)
            {
                recycled = RecycleDiscardToDrawPile(state, attackValue);
            }

            if (hasDiamond && !diamondBlocked)
            {
                drawn = DrawCards(state, currentPlayer, attackValue);
            }

            if (hasSpade && !spadeBlocked)
            {
                int targetAtk = enemy.Attack - attackValue;
                enemy.Attack = targetAtk > 0 ? targetAtk : 0;
                reducedAttack = enemyAtkBefore - enemy.Attack;
            }

            if (hasClub && !clubBlocked)
            {
                damage = attackValue * 2;
            }

            enemy.Health -= damage;
            int enemyHpAfter = enemy.Health > 0 ? enemy.Health : 0;

            AppendCardsToDiscard(state, playedCards);

            state.BattleLog.Add(
                $"{currentPlayer.PlayerId} 打出 {FormatCards(playedCards)}，攻击值 {attackValue}，造成伤害 {damage}；" +
                $"敌人生命 {enemyHpBefore}->{enemyHpAfter}，攻击 {enemyAtkBefore}->{enemy.Attack}。");

            AppendSuitEffectLog(state, hasHeart, heartBlocked, "红桃", $"回收 {recycled} 张弃牌到牌库");
            AppendSuitEffectLog(state, hasDiamond, diamondBlocked, "方块", $"摸牌 {drawn} 张");
            AppendSuitEffectLog(state, hasSpade, spadeBlocked, "黑桃", $"敌人攻击 -{reducedAttack}");
            AppendSuitEffectLog(state, hasClub, clubBlocked, "梅花", hasClub && !clubBlocked ? "本次伤害翻倍" : string.Empty);

            if (enemy.Health <= 0)
            {
                enemy.Defeated = true;
                state.BattleLog.Add($"敌人 {enemy.Name} 被击败。");
                SpawnNextEnemyOrWin(state);
                shouldAdvanceTurn = !state.IsGameOver;
                return;
            }

            BeginCounterAttackDiscardStage(state, currentPlayer);
            if (!state.IsGameOver && !state.IsAwaitingDiscard)
            {
                shouldAdvanceTurn = true;
            }
        }

        private static void ResolvePass(RegicideBattleState state, RegicidePlayerState currentPlayer, out bool shouldAdvanceTurn)
        {
            shouldAdvanceTurn = false;

            if (state.CurrentEnemy == null || state.CurrentEnemy.Defeated)
            {
                SpawnNextEnemyOrWin(state);
                if (state.IsGameOver)
                {
                    return;
                }
            }

            state.BattleLog.Add($"{currentPlayer.PlayerId} 选择跳过，本回合未造成伤害。");
            BeginCounterAttackDiscardStage(state, currentPlayer);
            if (!state.IsGameOver && !state.IsAwaitingDiscard)
            {
                shouldAdvanceTurn = true;
            }
        }

        private static void ResolveDiscard(
            RegicideBattleState state,
            RegicidePlayerState currentPlayer,
            RegicidePlayerAction action,
            out bool shouldAdvanceTurn)
        {
            shouldAdvanceTurn = false;
            List<int> selectedIndices = RegicideActionValidator.NormalizeCardIndices(action.CardIndices);
            if (selectedIndices.Count <= 0 && action.CardIndex >= 0)
            {
                selectedIndices.Add(action.CardIndex);
            }

            if (!RegicideActionValidator.CanDiscardForDamage(state, currentPlayer, selectedIndices, out int discardedValue, out _))
            {
                return;
            }

            List<RegicideCard> discardedCards = RemoveCardsFromHand(currentPlayer, selectedIndices);
            AppendCardsToDiscard(state, discardedCards);

            int required = state.PendingDiscardRequiredValue;
            ResetPendingDiscard(state);
            state.BattleLog.Add(
                $"{currentPlayer.PlayerId} 承伤弃牌：{FormatCards(discardedCards)}，共 {discardedValue} 点（需求 {required} 点）。");
            shouldAdvanceTurn = true;
        }

        private static void BeginCounterAttackDiscardStage(RegicideBattleState state, RegicidePlayerState player)
        {
            if (state == null || player == null || state.CurrentEnemy == null || state.IsGameOver)
            {
                return;
            }

            int attack = state.CurrentEnemy.Attack;
            if (attack <= 0)
            {
                ResetPendingDiscard(state);
                state.BattleLog.Add("敌人攻击为 0，本回合无需承伤弃牌。");
                return;
            }

            int totalHandValue = RegicideActionValidator.GetHandTotalValue(player);
            if (totalHandValue < attack)
            {
                state.IsGameOver = true;
                state.IsVictory = false;
                state.BattleLog.Add(
                    $"敌人反击 {attack}，{player.PlayerId} 可弃总点数仅 {totalHandValue}，无法承伤，全队失败。");
                return;
            }

            state.IsAwaitingDiscard = true;
            state.PendingDiscardTargetPlayerIndex = state.CurrentPlayerIndex;
            state.PendingDiscardRequiredValue = attack;
            state.BattleLog.Add($"敌人反击 {attack}：请 {player.PlayerId} 选择弃牌承伤。");
        }

        private static void ResetPendingDiscard(RegicideBattleState state)
        {
            state.IsAwaitingDiscard = false;
            state.PendingDiscardTargetPlayerIndex = -1;
            state.PendingDiscardRequiredValue = 0;
        }

        private static void AdvanceTurn(RegicideBattleState state, int forcedNextPlayerIndex)
        {
            if (state == null || state.Players.Count <= 0 || state.IsGameOver)
            {
                return;
            }

            if (forcedNextPlayerIndex >= 0 && forcedNextPlayerIndex < state.Players.Count)
            {
                state.CurrentPlayerIndex = forcedNextPlayerIndex;
            }
            else
            {
                state.CurrentPlayerIndex = (state.CurrentPlayerIndex + 1) % state.Players.Count;
            }

            state.Round++;
        }

        private static void SpawnNextEnemyOrWin(RegicideBattleState state)
        {
            state.IsEnemyImmunityDisabledByJester = false;
            ResetPendingDiscard(state);

            if (state.RemainingEnemies.Count <= 0)
            {
                state.IsGameOver = true;
                state.IsVictory = true;
                state.BattleLog.Add("胜利：所有敌人已被击败。");
                return;
            }

            state.CurrentEnemy = state.RemainingEnemies[0];
            state.RemainingEnemies.RemoveAt(0);
            state.EnemyCursor++;
            state.BattleLog.Add(
                $"新的敌人登场：{state.CurrentEnemy.Name}（生命 {state.CurrentEnemy.Health} / 攻击 {state.CurrentEnemy.Attack}，免疫 {GetSuitDisplay(state.CurrentEnemy.Suit)}）。");
        }

        private static int DrawCards(RegicideBattleState state, RegicidePlayerState player, int count)
        {
            int drawn = 0;
            int handLimit = state.HandLimitPerPlayer > 0 ? state.HandLimitPerPlayer : int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (state.DrawPile.Count <= 0)
                {
                    break;
                }

                if (player.Hand.Count >= handLimit)
                {
                    break;
                }

                player.Hand.Add(state.DrawPile[0]);
                state.DrawPile.RemoveAt(0);
                drawn++;
            }

            return drawn;
        }

        private static int RecycleDiscardToDrawPile(RegicideBattleState state, int count)
        {
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                if (state.DiscardPile.Count <= 0)
                {
                    break;
                }

                int last = state.DiscardPile.Count - 1;
                RegicideCard card = state.DiscardPile[last];
                state.DiscardPile.RemoveAt(last);
                state.DrawPile.Add(card);
                moved++;
            }

            return moved;
        }

        private static int ResolveNextPlayerIndex(RegicideBattleState state, int requested)
        {
            if (state == null || state.Players.Count <= 0)
            {
                return -1;
            }

            if (requested >= 0 && requested < state.Players.Count)
            {
                return requested;
            }

            return (state.CurrentPlayerIndex + 1) % state.Players.Count;
        }

        private static string GetPlayerIdByIndex(RegicideBattleState state, int index)
        {
            if (state == null || index < 0 || index >= state.Players.Count)
            {
                return "-";
            }

            RegicidePlayerState player = state.Players[index];
            return player != null ? player.PlayerId : "-";
        }

        private static bool IsSuitBlocked(RegicideBattleState state, RegicideSuit suit)
        {
            if (state == null || state.CurrentEnemy == null)
            {
                return false;
            }

            if (state.IsEnemyImmunityDisabledByJester)
            {
                return false;
            }

            return state.CurrentEnemy.Suit == suit;
        }

        private static bool HasSuit(List<RegicideCard> cards, RegicideSuit suit)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Suit == suit)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendSuitEffectLog(RegicideBattleState state, bool used, bool blocked, string suitName, string effectText)
        {
            if (!used)
            {
                return;
            }

            if (blocked)
            {
                state.BattleLog.Add($"{suitName}效果被敌人同花色免疫。");
                return;
            }

            if (!string.IsNullOrEmpty(effectText))
            {
                state.BattleLog.Add($"{suitName}效果：{effectText}。");
            }
        }

        private static void AppendCardsToDiscard(RegicideBattleState state, List<RegicideCard> cards)
        {
            if (state == null || cards == null || cards.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                state.DiscardPile.Add(cards[i]);
            }
        }

        private static List<RegicideCard> RemoveCardsFromHand(RegicidePlayerState player, List<int> indices)
        {
            List<RegicideCard> removed = new List<RegicideCard>(indices.Count);
            if (player == null || indices == null || indices.Count <= 0)
            {
                return removed;
            }

            List<int> sorted = new List<int>(indices);
            sorted.Sort((a, b) => b.CompareTo(a));
            for (int i = 0; i < sorted.Count; i++)
            {
                int index = sorted[i];
                if (index < 0 || index >= player.Hand.Count)
                {
                    continue;
                }

                RegicideCard card = player.Hand[index];
                player.Hand.RemoveAt(index);
                removed.Add(card);
            }

            removed.Reverse();
            return removed;
        }

        private static string FormatCards(List<RegicideCard> cards)
        {
            if (cards == null || cards.Count <= 0)
            {
                return "-";
            }

            StringBuilder builder = new StringBuilder(cards.Count * 6);
            for (int i = 0; i < cards.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" + ");
                }

                builder.Append(GetSuitDisplay(cards[i].Suit)).Append(GetRankDisplay(cards[i].Rank));
            }

            return builder.ToString();
        }

        private static string GetSuitDisplay(RegicideSuit suit)
        {
            switch (suit)
            {
                case RegicideSuit.Spade:
                    return "黑桃";
                case RegicideSuit.Heart:
                    return "红桃";
                case RegicideSuit.Club:
                    return "梅花";
                case RegicideSuit.Diamond:
                    return "方块";
                default:
                    return "小丑";
            }
        }

        private static string GetRankDisplay(int rank)
        {
            switch (rank)
            {
                case 0:
                    return "Jester";
                case 1:
                    return "A";
                case 11:
                    return "J";
                case 12:
                    return "Q";
                case 13:
                    return "K";
                default:
                    return rank.ToString();
            }
        }
    }
}
