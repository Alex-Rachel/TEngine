using System.Collections.Generic;
using System.Text;
using TEngine;

namespace GameLogic.Regicide
{
    public static class RegicideSelfTests
    {
        public static bool RunRuleTests(out string report)
        {
            StringBuilder sb = new StringBuilder();
            bool ok = true;

            IRegicideRuleConfigProvider provider = RegicideRuleConfigProvider.Instance;
            RegicideBattleState state = RegicideBattleInitializer.Create(
                "TEST-RULE",
                1,
                42,
                new List<string> { "P1", "P2" },
                provider);

            if (state == null)
            {
                report = "Init test failed: state is null.";
                return false;
            }

            sb.AppendLine("Init test passed.");

            RegicidePlayerAction validAction = new RegicidePlayerAction
            {
                SessionId = state.SessionId,
                PlayerId = state.Players[state.CurrentPlayerIndex].PlayerId,
                ActionType = RegicideActionType.PlayCard,
                CardIndex = 0,
                ClientSequence = 1,
            };

            RegicideRuleConfig config = provider.GetByRuleId(1);
            RegicideActionResult validResult = RegicideBattleResolver.ApplyAction(state, validAction, config);
            if (!validResult.Success)
            {
                ok = false;
                sb.AppendLine($"Valid action failed: {validResult.Error}");
            }
            else
            {
                sb.AppendLine("Valid action test passed.");
            }

            RegicidePlayerAction invalidAction = new RegicidePlayerAction
            {
                SessionId = state.SessionId,
                PlayerId = "NotCurrentPlayer",
                ActionType = RegicideActionType.PlayCard,
                CardIndex = 0,
                ClientSequence = 2,
            };

            RegicideActionResult invalidResult = RegicideBattleResolver.ApplyAction(state, invalidAction, config);
            if (invalidResult.Success)
            {
                ok = false;
                sb.AppendLine("Invalid action test failed: expected reject.");
            }
            else
            {
                sb.AppendLine("Invalid action test passed.");
            }

            report = sb.ToString();
            return ok;
        }

        public static bool RunReplayConsistencyTest(out string report)
        {
            IRegicideRuleConfigProvider provider = RegicideRuleConfigProvider.Instance;
            List<string> players = new List<string> { "A", "B" };
            List<RegicidePlayerAction> actions = new List<RegicidePlayerAction>
            {
                new RegicidePlayerAction { SessionId = "REPLAY", PlayerId = "A", ActionType = RegicideActionType.PlayCard, CardIndex = 0, ClientSequence = 1 },
                new RegicidePlayerAction { SessionId = "REPLAY", PlayerId = "B", ActionType = RegicideActionType.PlayCard, CardIndex = 0, ClientSequence = 2 },
                new RegicidePlayerAction { SessionId = "REPLAY", PlayerId = "A", ActionType = RegicideActionType.PlayCard, CardIndex = 0, ClientSequence = 3 },
            };

            RegicideReplayResult run1 = RegicideReplayExecutor.Replay("REPLAY", 1, 99, players, actions, provider);
            RegicideReplayResult run2 = RegicideReplayExecutor.Replay("REPLAY", 1, 99, players, actions, provider);

            if (!run1.Success || !run2.Success)
            {
                report = $"Replay execution failed. run1={run1.Success}, run2={run2.Success}";
                return false;
            }

            if (run1.KeyFrameHashes.Count != run2.KeyFrameHashes.Count)
            {
                report = "Replay hash count mismatch.";
                return false;
            }

            for (int i = 0; i < run1.KeyFrameHashes.Count; i++)
            {
                if (run1.KeyFrameHashes[i] != run2.KeyFrameHashes[i])
                {
                    report = $"Replay hash mismatch at index {i}.";
                    return false;
                }
            }

            report = "Replay consistency test passed.";
            return true;
        }

        public static bool RunSinglePlayerPlayableLoopTest(out string report)
        {
            IRegicideRuleConfigProvider provider = RegicideRuleConfigProvider.Instance;
            RegicideBattleState state = RegicideBattleInitializer.Create(
                "TEST-SINGLE",
                1,
                20260318,
                new List<string> { "Solo" },
                provider);
            if (state == null)
            {
                report = "Single player init failed.";
                return false;
            }

            RegicideRuleConfig config = provider.GetByRuleId(1);
            if (config == null)
            {
                report = "Rule config missing.";
                return false;
            }

            int step = 0;
            const int MaxSteps = 256;
            while (!state.IsGameOver && step < MaxSteps)
            {
                RegicidePlayerState player = state.Players[state.CurrentPlayerIndex];
                if (player == null)
                {
                    report = $"Invalid player at step {step}.";
                    return false;
                }

                RegicidePlayerAction action = new RegicidePlayerAction
                {
                    SessionId = state.SessionId,
                    PlayerId = player.PlayerId,
                    ClientSequence = step + 1,
                    ActionType = RegicideActionType.PlayCard,
                    CardIndices = new List<int>(),
                };

                if (state.IsAwaitingDiscard)
                {
                    action.ActionType = RegicideActionType.Discard;
                    action.CardIndices.AddRange(SelectDiscardIndices(player, state.PendingDiscardRequiredValue));
                    if (action.CardIndices.Count > 0)
                    {
                        action.CardIndex = action.CardIndices[0];
                    }
                }
                else if (player.Hand.Count > 0)
                {
                    action.CardIndex = 0;
                    action.CardIndices.Add(0);
                }
                else
                {
                    action.ActionType = RegicideActionType.Pass;
                }

                RegicideActionResult result = RegicideBattleResolver.ApplyAction(state, action, config);
                if (!result.Success)
                {
                    report = $"Single player loop failed at step {step}: {result.Error}";
                    return false;
                }

                step++;
            }

            if (!state.IsGameOver)
            {
                report = $"Single player loop did not finish within {MaxSteps} steps.";
                return false;
            }

            report = $"Single player loop passed. steps={step}, victory={state.IsVictory}.";
            return true;
        }

        private static List<int> SelectDiscardIndices(RegicidePlayerState player, int required)
        {
            List<int> selected = new List<int>();
            if (player == null || player.Hand == null || player.Hand.Count <= 0)
            {
                return selected;
            }

            List<(int index, int value)> candidates = new List<(int index, int value)>();
            for (int i = 0; i < player.Hand.Count; i++)
            {
                candidates.Add((i, player.Hand[i].AttackValue));
            }

            candidates.Sort((a, b) =>
            {
                int byValue = b.value.CompareTo(a.value);
                return byValue != 0 ? byValue : b.index.CompareTo(a.index);
            });

            int total = 0;
            for (int i = 0; i < candidates.Count && total < required; i++)
            {
                selected.Add(candidates[i].index);
                total += candidates[i].value;
            }

            selected.Sort();
            return selected;
        }

        public static bool RunNetworkScenarioChecklist(out string report)
        {
            // This is a scenario checklist stub for 2/3/4 players, reconnect and abnormal disconnect.
            // Execution depends on actual runtime transport and should be triggered in play mode.
            report =
                "Scenario checklist:\n" +
                "1) 2-player match create/join/start/finish\n" +
                "2) 3-player match create/join/start/finish\n" +
                "3) 4-player match create/join/start/finish\n" +
                "4) one client disconnect/reconnect, verify snapshot recovery\n" +
                "5) one client abnormal quit, verify seat lifecycle cleanup";
            return true;
        }

        public static void LogAll()
        {
            bool rules = RunRuleTests(out string rulesReport);
            bool replay = RunReplayConsistencyTest(out string replayReport);
            bool single = RunSinglePlayerPlayableLoopTest(out string singleReport);
            bool net = RunNetworkScenarioChecklist(out string netReport);

            Log.Info($"[RegicideSelfTests] Rule={rules}\n{rulesReport}");
            Log.Info($"[RegicideSelfTests] Replay={replay}\n{replayReport}");
            Log.Info($"[RegicideSelfTests] Single={single}\n{singleReport}");
            Log.Info($"[RegicideSelfTests] NetworkChecklist={net}\n{netReport}");
        }
    }
}
