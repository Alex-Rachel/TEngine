using System;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.MethodReload
{
    /// <summary>
    /// Thread-safe snapshot of the most recent method-reload activity, for on-screen display by
    /// <see cref="MethodReloadStatusOverlay"/>. Fed from two sides: the PlayerConnection receiver
    /// (compile-started / compile-failed notices arrive on a background thread) and the apply path
    /// in RuntimePipelineDriver (main thread). Timestamps use <see cref="Environment.TickCount"/>
    /// because Unity's Time API is main-thread-only and reporters run off it.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class MethodReloadActivity
    {
        public enum Phase
        {
            /// <summary>No reload in flight; nothing to show beyond the override count.</summary>
            Idle = 0,
            /// <summary>The editor announced a compile for this player (ReloadPendingMsg).</summary>
            Compiling = 1,
            /// <summary>A pushed override was applied; message is the apply summary.</summary>
            Applied = 2,
            /// <summary>Editor compile failed or on-device apply failed; message says why.</summary>
            Failed = 3,
        }

        private static readonly object s_Lock = new object();
        private static Phase s_Phase = Phase.Idle;
        private static string s_Message = "";
        private static int s_PhaseTick = Environment.TickCount;

        public static void ReportCompileStarted(string name) => Set(Phase.Compiling, name);
        public static void ReportApplied(string summary) => Set(Phase.Applied, summary);
        public static void ReportFailed(string summary) => Set(Phase.Failed, summary);

        private static void Set(Phase phase, string message)
        {
            lock (s_Lock)
            {
                s_Phase = phase;
                s_Message = message ?? "";
                s_PhaseTick = Environment.TickCount;
            }
        }

        /// <summary>Current phase, its message, and milliseconds since the phase changed.</summary>
        public static Phase Snapshot(out string message, out int ageMs)
        {
            lock (s_Lock)
            {
                message = s_Message;
                ageMs = Environment.TickCount - s_PhaseTick;
                return s_Phase;
            }
        }

        private static long s_OverrideCalls;

        /// <summary>Called by the dispatch path once per overridden-method invocation. The running
        /// total lets the overlay show a call rate — "N overrides · 0 calls/s" exposes the
        /// bound-but-inert case (override registered but the woven prologue never dispatching),
        /// which no log line catches.</summary>
        public static void CountOverrideCall() => System.Threading.Interlocked.Increment(ref s_OverrideCalls);

        /// <summary>Total overridden-method invocations this session.</summary>
        public static long OverrideCallCount => System.Threading.Interlocked.Read(ref s_OverrideCalls);
    }
}
