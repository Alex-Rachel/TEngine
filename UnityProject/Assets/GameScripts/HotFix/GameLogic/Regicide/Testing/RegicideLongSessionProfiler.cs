using System;
using System.Diagnostics;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameProto.Regicide;
using TEngine;

namespace GameLogic.Regicide
{
    public static class RegicideLongSessionProfiler
    {
        public static async UniTask<RegicideLongSessionMetrics> RunAsync(int maxActions = 500)
        {
            Stopwatch watch = Stopwatch.StartNew();
            long startGc = GC.GetTotalMemory(false);

            RegicideRuntimeConfig config = new RegicideRuntimeConfig
            {
                EnableRegicide = true,
                Environment = RegicideClientEnvironment.LocalSinglePlayer,
                RuleId = 1,
                RandomSeed = 20260318,
                PlayerId = "Profiler",
            };

            GameModule.RegicideBattle.Setup(config);
            bool started = GameModule.RegicideBattle.TryStartLocalSinglePlayer("LOCAL-PROFILER");
            if (!started)
            {
                return new RegicideLongSessionMetrics();
            }

            float peakFrameMs = 0;
            for (int i = 0; i < maxActions; i++)
            {
                RegicideBattleState state = GameModule.RegicideBattle.State;
                if (state == null || state.IsGameOver)
                {
                    break;
                }

                Stopwatch frameWatch = Stopwatch.StartNew();
                bool success;
                RegicidePlayerState player = state.Players[state.CurrentPlayerIndex];
                if (state.IsAwaitingDiscard)
                {
                    success = await GameModule.RegicideBattle.DiscardForDamageAsync(
                        SelectDiscardIndices(player, state.PendingDiscardRequiredValue));
                }
                else if (player.Hand.Count > 0)
                {
                    success = await GameModule.RegicideBattle.PlayCardAsync(0);
                }
                else
                {
                    success = await GameModule.RegicideBattle.PassAsync();
                }

                frameWatch.Stop();
                if (!success)
                {
                    break;
                }

                float frameMs = (float)frameWatch.Elapsed.TotalMilliseconds;
                if (frameMs > peakFrameMs)
                {
                    peakFrameMs = frameMs;
                }
            }

            watch.Stop();
            long endGc = GC.GetTotalMemory(false);

            RegicideLongSessionMetrics metrics = GameModule.RegicideNetwork.GetMetricsSnapshot();
            metrics.SessionTicks = watch.ElapsedMilliseconds;
            metrics.PeakFrameMillis = peakFrameMs;
            metrics.GcAllocatedBytesDelta = endGc - startGc;

            Log.Info($"[RegicideLongSessionProfiler] elapsed={metrics.SessionTicks}ms, peakFrame={metrics.PeakFrameMillis:F3}ms, gcDelta={metrics.GcAllocatedBytesDelta}");
            return metrics;
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
    }
}
