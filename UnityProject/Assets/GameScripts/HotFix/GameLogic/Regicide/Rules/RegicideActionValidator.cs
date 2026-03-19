using System.Collections.Generic;

namespace GameLogic.Regicide
{
    public static class RegicideActionValidator
    {
        public static bool CanPlayCard(RegicideBattleState state, RegicidePlayerState player, int cardIndex, out string error)
        {
            List<int> indices = new List<int> { cardIndex };
            return CanPlayCard(state, player, indices, out _, out _, out error);
        }

        public static bool CanPlayCard(
            RegicideBattleState state,
            RegicidePlayerState player,
            IList<int> cardIndices,
            out int attackValue,
            out bool isJester,
            out string error)
        {
            attackValue = 0;
            isJester = false;

            if (state == null)
            {
                error = "战斗状态为空。";
                return false;
            }

            if (state.IsGameOver)
            {
                error = "对局已结束。";
                return false;
            }

            if (state.IsAwaitingDiscard)
            {
                error = "当前处于承伤弃牌阶段，不能出牌。";
                return false;
            }

            if (player == null)
            {
                error = "玩家状态为空。";
                return false;
            }

            List<int> normalized = NormalizeCardIndices(cardIndices);
            if (normalized.Count <= 0)
            {
                error = "请先选择要打出的牌。";
                return false;
            }

            List<RegicideCard> cards = new List<RegicideCard>(normalized.Count);
            HashSet<int> unique = new HashSet<int>();
            for (int i = 0; i < normalized.Count; i++)
            {
                int index = normalized[i];
                if (index < 0 || index >= player.Hand.Count)
                {
                    error = "所选牌索引越界。";
                    return false;
                }

                if (!unique.Add(index))
                {
                    error = "不能重复选择同一张牌。";
                    return false;
                }

                cards.Add(player.Hand[index]);
            }

            if (cards.Count == 1 && cards[0] != null && cards[0].IsJester)
            {
                attackValue = 0;
                isJester = true;
                error = string.Empty;
                return true;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null)
                {
                    error = "选中的牌无效。";
                    return false;
                }

                if (cards[i].IsJester)
                {
                    error = "小丑只能单独打出。";
                    return false;
                }
            }

            if (cards.Count == 1)
            {
                attackValue = cards[0].AttackValue;
                if (attackValue > 10)
                {
                    error = "单牌攻击值不能大于 10。";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            // A + 非 A 的配对。
            if (cards.Count == 2 && ((cards[0].Rank == 1 && cards[1].Rank != 1) || (cards[1].Rank == 1 && cards[0].Rank != 1)))
            {
                attackValue = cards[0].AttackValue + cards[1].AttackValue;
                if (attackValue > 10)
                {
                    error = "A 配对后的攻击值不能大于 10。";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            if (cards.Count > 4)
            {
                error = "组合出牌最多 4 张。";
                return false;
            }

            int rank = cards[0].Rank;
            int total = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Rank != rank)
                {
                    error = "组合出牌必须同点数（A+非A配对除外）。";
                    return false;
                }

                total += cards[i].AttackValue;
            }

            if (total > 10)
            {
                error = "组合总攻击值不能大于 10。";
                return false;
            }

            attackValue = total;
            error = string.Empty;
            return true;
        }

        public static bool CanPass(RegicideBattleState state, out string error)
        {
            if (state == null)
            {
                error = "战斗状态为空。";
                return false;
            }

            if (state.IsGameOver)
            {
                error = "对局已结束。";
                return false;
            }

            if (state.IsAwaitingDiscard)
            {
                error = "当前处于承伤弃牌阶段，不能跳过。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool CanDefend(RegicideBattleState state, RegicidePlayerState player, out string error)
        {
            error = "当前规则没有独立的防御阶段。";
            return false;
        }

        public static bool CanDiscardForDamage(
            RegicideBattleState state,
            RegicidePlayerState player,
            IList<int> cardIndices,
            out int totalValue,
            out string error)
        {
            totalValue = 0;

            if (state == null)
            {
                error = "战斗状态为空。";
                return false;
            }

            if (!state.IsAwaitingDiscard)
            {
                error = "当前不需要弃牌承伤。";
                return false;
            }

            if (player == null)
            {
                error = "玩家状态为空。";
                return false;
            }

            List<int> normalized = NormalizeCardIndices(cardIndices);
            if (normalized.Count <= 0)
            {
                error = $"请选择弃牌，至少凑够 {state.PendingDiscardRequiredValue} 点。";
                return false;
            }

            HashSet<int> unique = new HashSet<int>();
            for (int i = 0; i < normalized.Count; i++)
            {
                int index = normalized[i];
                if (index < 0 || index >= player.Hand.Count)
                {
                    error = "弃牌索引越界。";
                    return false;
                }

                if (!unique.Add(index))
                {
                    error = "不能重复选择同一张弃牌。";
                    return false;
                }

                totalValue += player.Hand[index].AttackValue;
            }

            if (totalValue < state.PendingDiscardRequiredValue)
            {
                error = $"弃牌点数不足：{totalValue}/{state.PendingDiscardRequiredValue}。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool Validate(RegicideBattleState state, RegicidePlayerAction action, out string error)
        {
            if (state == null)
            {
                error = "战斗状态为空。";
                return false;
            }

            if (action == null)
            {
                error = "动作为空。";
                return false;
            }

            if (state.IsGameOver)
            {
                error = "对局已结束。";
                return false;
            }

            if (state.SessionId != action.SessionId)
            {
                error = "会话不匹配。";
                return false;
            }

            if (state.CurrentPlayerIndex < 0 || state.CurrentPlayerIndex >= state.Players.Count)
            {
                error = "当前回合玩家索引无效。";
                return false;
            }

            RegicidePlayerState currentPlayer = state.Players[state.CurrentPlayerIndex];
            if (currentPlayer.PlayerId != action.PlayerId)
            {
                error = "不是你的回合。";
                return false;
            }

            if (state.IsAwaitingDiscard)
            {
                if (action.ActionType != RegicideActionType.Discard)
                {
                    error = "当前必须先完成承伤弃牌。";
                    return false;
                }

                if (state.PendingDiscardTargetPlayerIndex != state.CurrentPlayerIndex)
                {
                    error = "当前弃牌目标玩家不匹配。";
                    return false;
                }

                List<int> discardIndices = NormalizeCardIndices(action.CardIndices);
                if (discardIndices.Count <= 0 && action.CardIndex >= 0)
                {
                    discardIndices.Add(action.CardIndex);
                }

                return CanDiscardForDamage(state, currentPlayer, discardIndices, out _, out error);
            }

            switch (action.ActionType)
            {
                case RegicideActionType.PlayCard:
                {
                    List<int> indices = NormalizeCardIndices(action.CardIndices);
                    if (indices.Count <= 0 && action.CardIndex >= 0)
                    {
                        indices.Add(action.CardIndex);
                    }

                    if (!CanPlayCard(state, currentPlayer, indices, out _, out bool isJester, out error))
                    {
                        return false;
                    }

                    if (isJester && state.Players.Count > 1)
                    {
                        if (action.NextPlayerIndex < 0 || action.NextPlayerIndex >= state.Players.Count)
                        {
                            error = "打出小丑时必须指定下一位行动者。";
                            return false;
                        }
                    }

                    break;
                }

                case RegicideActionType.Pass:
                    if (!CanPass(state, out error))
                    {
                        return false;
                    }
                    break;

                case RegicideActionType.Defend:
                case RegicideActionType.Discard:
                    error = "当前动作类型不支持。";
                    return false;

                default:
                    error = "未知动作类型。";
                    return false;
            }

            error = string.Empty;
            return true;
        }

        public static int GetHandTotalValue(RegicidePlayerState player)
        {
            if (player == null || player.Hand == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < player.Hand.Count; i++)
            {
                total += player.Hand[i].AttackValue;
            }

            return total;
        }

        public static List<int> NormalizeCardIndices(IList<int> raw)
        {
            if (raw == null || raw.Count <= 0)
            {
                return new List<int>();
            }

            List<int> normalized = new List<int>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                normalized.Add(raw[i]);
            }

            normalized.Sort();
            return normalized;
        }
    }
}
