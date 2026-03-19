using System.Collections.Generic;

namespace GameLogic.Regicide
{
    public static class RegicideReplayExecutor
    {
        public static RegicideReplayResult Replay(
            string sessionId,
            int ruleId,
            int seed,
            IList<string> playerIds,
            IList<RegicidePlayerAction> actions,
            IRegicideRuleConfigProvider configProvider)
        {
            RegicideRuleConfig config = configProvider.GetByRuleId(ruleId);
            if (config == null)
            {
                return new RegicideReplayResult
                {
                    Success = false,
                    Error = $"Rule config {ruleId} not found.",
                };
            }

            RegicideBattleState state = RegicideBattleInitializer.Create(sessionId, ruleId, seed, playerIds, configProvider);
            if (state == null)
            {
                return new RegicideReplayResult
                {
                    Success = false,
                    Error = "Failed to initialize replay battle state.",
                };
            }

            RegicideReplayResult result = new RegicideReplayResult
            {
                Success = true,
                FinalState = state,
                KeyFrameHashes = new List<string> { RegicideStateHasher.ComputeHash(state) },
            };

            for (int i = 0; i < actions.Count; i++)
            {
                RegicideActionResult actionResult = RegicideBattleResolver.ApplyAction(state, actions[i], config);
                if (!actionResult.Success)
                {
                    result.Success = false;
                    result.Error = $"Replay stopped at action {i}: {actionResult.Error}";
                    result.FinalState = state;
                    return result;
                }

                result.KeyFrameHashes.Add(actionResult.StateHash);
                if (state.IsGameOver)
                {
                    break;
                }
            }

            result.FinalState = state;
            return result;
        }
    }
}
