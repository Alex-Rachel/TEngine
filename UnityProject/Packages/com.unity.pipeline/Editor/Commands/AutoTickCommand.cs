using System;
using System.Reflection;
using Unity.Pipeline.Commands;
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Opt-in mode that keeps the Unity Editor ticking at full rate even when its window is
    /// unfocused. Unity throttles EditorApplication.update when the editor is in the background,
    /// which can stall long-running work (HTTP commands, async test runs) driven from the CLI.
    ///
    /// When enabled, this forces EditorApplication.SignalTick() on every update, which schedules
    /// the next tick immediately and keeps the loop spinning. It is off by default and changes
    /// nothing in normal package behavior. Cross-platform: no native/OS calls.
    ///
    /// Note: the update-loop subscription is static and does not survive a domain reload, but the
    /// enabled/interval choice is persisted to SessionState and restored by
    /// <see cref="RestoreFromSession"/> (called from <c>EditorPipelineStartup</c> on every server
    /// (re)start, including after a recompile). Forcing full-rate ticks uses CPU like a focused editor.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class AutoTickCommand
    {
        const string Group = "editor";

        // Default interval when nothing else applies (fresh editor session, no prior SetAutoTick call).
        internal const int DefaultIntervalMs = 16;

        // SessionState keys: session-scoped editor state, survives domain reload, dies with the editor.
        private const string EnabledKey = "Unity.Pipeline.AutoTick.Enabled";
        private const string IntervalKey = "Unity.Pipeline.AutoTick.IntervalMs";

        private static bool s_Enabled;
        private static Action s_SignalTick;
        private static EditorApplication.CallbackFunction s_Pump;
        private static readonly System.Diagnostics.Stopwatch s_Stopwatch = new System.Diagnostics.Stopwatch();
        private static long s_IntervalMs;

        internal static bool IsEnabled => s_Enabled;
        internal static long CurrentIntervalMs => s_IntervalMs;

        [CliGroupCommand(Group, "Keep the editor ticking while unfocused by forcing EditorApplication.SignalTick at a throttled rate", MainThreadRequired = true, Tags = new[] { "editor" })]
        public static string SetAutoTick(
            [CliArg("enabled", "Turn auto-tick mode on (true) or off (false)")] bool enabled = true,
            [CliArg("interval-ms", "Minimum milliseconds between forced ticks. 0 = every update (max rate, pegs a CPU core). Default 16 (~60Hz).")] int intervalMs = DefaultIntervalMs,
            [CliArg("persist", "Persist this choice to SessionState so it survives a domain reload. Set false for a one-off/expensive setting (e.g. interval-ms=0) that should revert to the last persisted choice (or the default) after the next recompile instead of sticking for the rest of the session.")] bool persist = true)
        {
            var result = ApplyAutoTick(enabled, intervalMs);
            if (!persist)
                return result.StartsWith("Error:") ? result : result + " (session-only, won't survive a domain reload)";

            if (result.StartsWith("Error:"))
                return result;

            SessionState.SetBool(EnabledKey, s_Enabled);
            SessionState.SetInt(IntervalKey, (int)s_IntervalMs);
            return result;
        }

        /// <summary>
        /// Restore auto-tick to whatever the user last explicitly set via <see cref="SetAutoTick"/>
        /// this editor session (surviving the domain reload that just wiped the statics). If nothing
        /// was ever explicitly set this session, apply <paramref name="defaultEnabled"/> at
        /// <see cref="DefaultIntervalMs"/> instead — this is the only case where the watchdog's
        /// enabled setting decides auto-tick's state. Call on the main thread (touches SessionState).
        /// </summary>
        internal static void RestoreFromSession(bool defaultEnabled)
        {
            var storedInterval = SessionState.GetInt(IntervalKey, -1);
            if (storedInterval < 0)
            {
                ApplyAutoTick(defaultEnabled, DefaultIntervalMs);
                return;
            }

            var storedEnabled = SessionState.GetBool(EnabledKey, defaultEnabled);
            ApplyAutoTick(storedEnabled, storedInterval);
        }

        /// <summary>
        /// Test-only: drop the in-memory state (statics + update-loop subscription) but keep
        /// SessionState, to simulate a domain reload (statics cleared, SessionState survives).
        /// </summary>
        internal static void ResetForTests()
        {
            if (s_Pump != null)
            {
                EditorApplication.update -= s_Pump;
                s_Pump = null;
            }
            s_Stopwatch.Stop();
            s_Enabled = false;
            s_IntervalMs = 0;
        }

        /// <summary>
        /// Test-only: erase the persisted SessionState copy (unlike <see cref="ResetForTests"/>, which
        /// deliberately leaves it intact). Use for test isolation, not to simulate a reload.
        /// </summary>
        internal static void EraseSessionForTests()
        {
            SessionState.EraseInt(IntervalKey);
            SessionState.EraseBool(EnabledKey);
        }

        /// <summary>
        /// Test-only: read the persisted SessionState copy without touching the in-memory statics.
        /// Returns false if nothing has ever been persisted this session.
        /// </summary>
        internal static bool TryGetPersistedSessionForTests(out bool enabled, out long intervalMs)
        {
            var storedInterval = SessionState.GetInt(IntervalKey, -1);
            if (storedInterval < 0)
            {
                enabled = false;
                intervalMs = 0;
                return false;
            }

            enabled = SessionState.GetBool(EnabledKey, false);
            intervalMs = storedInterval;
            return true;
        }

        /// <summary>
        /// Test-only: write the persisted SessionState copy directly, bypassing ApplyAutoTick.
        /// </summary>
        internal static void SetPersistedSessionForTests(bool enabled, long intervalMs)
        {
            SessionState.SetBool(EnabledKey, enabled);
            SessionState.SetInt(IntervalKey, (int)intervalMs);
        }

        private static string ApplyAutoTick(bool enable, int intervalMs)
        {
            if (enable && s_Enabled)
            {
                s_IntervalMs = Math.Max(0, intervalMs);
                return $"Auto-tick already enabled (interval updated to {s_IntervalMs}ms)";
            }
            if (!enable && !s_Enabled)
                return "Auto-tick already disabled";

            if (enable)
            {
                if (s_SignalTick == null)
                {
                    var method = typeof(EditorApplication).GetMethod(
                        "SignalTick",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                    if (method == null)
                        return "Error: EditorApplication.SignalTick not found (internal API may have changed)";

                    try
                    {
                        s_SignalTick = (Action)Delegate.CreateDelegate(typeof(Action), method);
                    }
                    catch (Exception ex)
                    {
                        return $"Error: could not bind EditorApplication.SignalTick: {ex.Message}";
                    }
                }

                s_IntervalMs = Math.Max(0, intervalMs);
                s_Stopwatch.Restart();
                s_Pump = () =>
                {
                    if (s_IntervalMs <= 0 || s_Stopwatch.ElapsedMilliseconds >= s_IntervalMs)
                    {
                        s_Stopwatch.Restart();
                        s_SignalTick();
                    }
                };
                EditorApplication.update += s_Pump;
                s_Enabled = true;
                return $"Auto-tick enabled (interval {s_IntervalMs}ms)";
            }

            if (s_Pump != null)
            {
                EditorApplication.update -= s_Pump;
                s_Pump = null;
            }
            s_Stopwatch.Stop();
            s_Enabled = false;
            return "Auto-tick disabled";
        }
    }
}
