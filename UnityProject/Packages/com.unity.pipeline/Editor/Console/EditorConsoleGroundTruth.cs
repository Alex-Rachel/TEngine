using System;
using System.Collections.Generic;
#if !UNITY_6000_0_OR_NEWER
using System.Reflection;
#endif
using Unity.Pipeline.Console;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Console
{
    /// <summary>
    /// Samples what the Editor knows about the console — the compile-failure flag and the console's
    /// own entry counts — and publishes it for the <c>console</c> and <c>recompile_status</c>
    /// commands. Those commands are deliberately not main-thread commands, because clients poll
    /// them while the Editor is blocked compiling or importing, so they cannot read this state
    /// themselves; sampling it here and handing over an immutable snapshot is what lets them report
    /// it at all.
    ///
    /// The same pass does two more jobs that need the same main-thread tick:
    ///  - Backfills the buffer from the console's own store once per session, so sticky compile
    ///    errors logged before capture started are visible (see <see cref="EditorConsoleEntries"/>).
    ///  - Watches for the console gaining entries while the buffer's cursor stands still, which is
    ///    the only observable symptom of the log-callback subscription having been dropped, and
    ///    re-arms capture when it sees it.
    ///
    /// Sampling runs on <see cref="EditorApplication.update"/> rather than
    /// <c>ConsoleWindowUtility.consoleLogsChanged</c>, because that event is raised from
    /// ConsoleWindow.LogChanged, which returns early when no Console window exists — precisely the
    /// case this targets. On Unity 6 the event is subscribed only to shorten the wait when a window
    /// is open; earlier versions use the update sampling alone.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EditorConsoleGroundTruth
    {
        const string SeededKey = "pipeline.console.seeded";
        const double SampleIntervalSeconds = 0.25;
        const double ReseedCooldownSeconds = 5.0;

        static double s_NextSampleTime;
        static double s_NextReseedTime;
        static int s_LastConsoleTotal = -1;
        static long s_LastBufferSeq = -1;
        static bool s_GapPending;
        static long s_GapSeq;
        static bool s_Seeded;
#if !UNITY_6000_0_OR_NEWER
        static readonly MethodInfo k_GetCountsByType;
        static bool s_CountsDisabled;
#endif

        static EditorConsoleGroundTruth()
        {
            s_Seeded = SessionState.GetBool(SeededKey, false);

            EditorApplication.update -= Sample;
            EditorApplication.update += Sample;

#if UNITY_6000_0_OR_NEWER
            ConsoleWindowUtility.consoleLogsChanged -= OnConsoleLogsChanged;
            ConsoleWindowUtility.consoleLogsChanged += OnConsoleLogsChanged;
#else
            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var byRefInt = typeof(int).MakeByRefType();
            k_GetCountsByType = Type.GetType("UnityEditor.LogEntries,UnityEditor")?.GetMethod(
                "GetCountsByType", statics, null, new[] { byRefInt, byRefInt, byRefInt }, null);
#endif

            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        static void Sample()
        {
            if (EditorApplication.timeSinceStartup < s_NextSampleTime)
                return;
            s_NextSampleTime = EditorApplication.timeSinceStartup + SampleIntervalSeconds;

            // Seed before publishing, so the first snapshot a client sees already reports whether
            // the backfill happened.
            if (!s_Seeded)
                TrySeed();

            if (!TryGetConsoleLogCounts(out var errors, out var warnings, out var logs))
            {
                ConsoleLogCapture.GroundTruth = null;
                return;
            }
            var total = errors + warnings + logs;
            var seq = ConsoleLogCapture.Buffer.LastSeq;

            // The console gained entries while the buffer's cursor stood still. Unity unregisters
            // the native log callback on domain unload and on play-mode exit, while the managed
            // event keeps its subscriber list, so capture can be dead with nothing to test directly.
            //
            // A gap is confirmed by the cursor still sitting where it was one sample later, not by
            // the console count growing again: a dead subscription that missed a single burst — a
            // failed compile, say — would otherwise be spotted once and then forgotten. Waiting for
            // the second sample keeps a console entry that legitimately never reaches the managed
            // callback from re-arming on its own. A Clear lowers the counts, so it can never be
            // mistaken for a gap.
            if (s_GapPending && seq == s_GapSeq)
            {
                if (EditorApplication.timeSinceStartup >= s_NextReseedTime)
                {
                    ConsoleLogCapture.EnsureCapturing();
                    Reseed();
                    seq = ConsoleLogCapture.Buffer.LastSeq;
                    s_NextReseedTime = EditorApplication.timeSinceStartup + ReseedCooldownSeconds;
                    s_GapPending = false;
                }
            }
            else if (s_LastConsoleTotal >= 0 && total > s_LastConsoleTotal && seq == s_LastBufferSeq)
            {
                s_GapSeq = seq;
                s_GapPending = true;
            }
            else
            {
                // The buffer caught up on its own.
                s_GapPending = false;
            }

            s_LastConsoleTotal = total;
            s_LastBufferSeq = seq;

            ConsoleLogCapture.GroundTruth = new ConsoleGroundTruth
            {
                SampledUtc = DateTime.UtcNow,
                CompilationFailed = EditorUtility.scriptCompilationFailed,
                Compiling = EditorApplication.isCompiling,
                ConsoleErrors = errors,
                ConsoleWarnings = warnings,
                ConsoleLogs = logs,
                Seeded = s_Seeded
            };
        }

#if UNITY_6000_0_OR_NEWER
        // Only shortens the wait for the next sample; never a correctness dependency, since it is
        // not raised while the Console window is closed.
        static void OnConsoleLogsChanged() => s_NextSampleTime = 0;
#endif

        static bool TryGetConsoleLogCounts(out int errors, out int warnings, out int logs)
        {
#if UNITY_6000_0_OR_NEWER
            ConsoleWindowUtility.GetConsoleLogCounts(out errors, out warnings, out logs);
            return true;
#else
            errors = warnings = logs = 0;
            if (s_CountsDisabled || k_GetCountsByType == null)
                return false;

            try
            {
                var counts = new object[] { 0, 0, 0 };
                k_GetCountsByType.Invoke(null, counts);
                errors = (int)counts[0];
                warnings = (int)counts[1];
                logs = (int)counts[2];
                return true;
            }
            catch (Exception ex)
            {
                s_CountsDisabled = true;
                Debug.LogWarning($"[Pipeline] Cannot read the Editor console counts; console ground truth is unavailable: {ex.Message}");
                return false;
            }
#endif
        }

        static void OnAfterAssemblyReload()
        {
            ConsoleLogCapture.EnsureCapturing();

            // The snapshot describes the domain that just went away, and on 6000.5+ it survives the
            // reload with the rest of the statics. Drop it so nothing reads it as current until the
            // next sample replaces it.
            ConsoleLogCapture.GroundTruth = null;

            // Let the next sample establish a fresh baseline instead of comparing against counts
            // and a cursor from before the reload, which would look like a gap.
            s_LastConsoleTotal = -1;
            s_LastBufferSeq = -1;
            s_GapPending = false;
            s_GapSeq = 0;
        }

        static void TrySeed()
        {
            if (!Reseed())
                return;

            s_Seeded = true;
            SessionState.SetBool(SeededKey, true);
        }

        /// <summary>
        /// Copy console entries the buffer does not already hold into it. Entries are matched on type
        /// and message, the only fields both sides record identically. A log type never contains "|",
        /// so the first one always terminates it and the key is unambiguous.
        ///
        /// Matching counts occurrences rather than presence, because the console holds repeated
        /// messages by design. Treating a key as seen-or-not lets a row the buffer already has cancel
        /// out an identical row it missed, and that row is then never recovered.
        /// </summary>
        static bool Reseed()
        {
            var entries = new List<EditorConsoleEntries.Entry>();
            if (!EditorConsoleEntries.TryReadRecent(ConsoleLogBuffer.Capacity, entries))
                return false;

            var held = new Dictionary<string, int>();
            foreach (var entry in ConsoleLogCapture.Buffer.Query(-1, 0, ConsoleLogBuffer.SeverityLog).Entries)
            {
                var key = Key(entry.LogType, entry.Message);
                held.TryGetValue(key, out var count);
                held[key] = count + 1;
            }

            var now = DateTime.UtcNow;
            foreach (var entry in entries)
            {
                var key = Key(entry.Type.ToString(), entry.Message);

                // Consume one occurrence the buffer already holds; whatever the console has beyond
                // that is what capture missed.
                if (held.TryGetValue(key, out var remaining) && remaining > 0)
                {
                    held[key] = remaining - 1;
                    continue;
                }

                ConsoleLogCapture.Buffer.Add(entry.Type, entry.Message, entry.StackTrace, now, seeded: true);
            }

            return true;
        }

        static string Key(string logType, string message) => logType + "|" + message;

        /// <summary>Sample immediately, ignoring the throttle. For tests.</summary>
        internal static void SampleForTests()
        {
            s_NextSampleTime = 0;
            s_NextReseedTime = 0;
            Sample();
        }

        /// <summary>Forget the baselines and the seeded flag, so the next sample starts over. For tests.</summary>
        internal static void ResetForTests()
        {
            s_NextSampleTime = 0;
            s_NextReseedTime = 0;
            s_LastConsoleTotal = -1;
            s_LastBufferSeq = -1;
            s_GapPending = false;
            s_GapSeq = 0;
            s_Seeded = false;
            SessionState.EraseBool(SeededKey);
        }
    }
}
