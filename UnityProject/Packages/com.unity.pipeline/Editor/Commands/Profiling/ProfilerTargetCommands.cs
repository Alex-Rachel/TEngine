using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Commands.Memory;
using UnityEditor;
using UnityEditorInternal;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Profiling
{
    /// <summary>
    /// Lists and selects what the Editor's single Profiler connection points at. Recording and memory
    /// snapshots both capture from that connection, and neither repoints it themselves.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class ProfilerTargetCommands
    {
        const string Group = "diagnostics";

        /// <summary>A repointed connection is a message channel, so a capture must let it establish.</summary>
        const double SwitchSettleSeconds = 1.5;

        static double s_SwitchedAt = double.NegativeInfinity;

        /// <summary>False for <see cref="SwitchSettleSeconds"/> after <see cref="ProfilerConnect"/>
        /// repoints the connection. Captures queued in that window wait for it.</summary>
        internal static bool IsConnectionSettled =>
            EditorApplication.timeSinceStartup >= s_SwitchedAt + SwitchSettleSeconds;

        [CliGroupCommand(Group, "List what the Editor's Profiler can connect to: this Editor plus any connected development Players. 'current' marks what " + CommandNames.DiagnosticsProfilerRecord + " and " + CommandNames.DiagnosticsMemorySnapshot + " capture from; change it with " + CommandNames.DiagnosticsProfilerConnect + ".", Tags = new[] { "observability/profiling" })]
        public static object ProfilerTargets()
        {
            var targets = ProfilerTarget.Enumerate();
            return new
            {
                status = "ok",
                current = targets.FirstOrDefault(t => t.IsCurrent)?.Name,
                targets = targets.Select(t => new
                {
                    guid = t.Guid,
                    name = t.Name,
                    isEditor = t.IsEditor,
                    connectable = t.Connectable,
                    current = t.IsCurrent,
                    deepProfilingSupported = t.DeepProfilingSupported
                }).ToArray()
            };
        }

        [CliGroupCommand(Group, "Point the Editor's Profiler connection at a target from " + CommandNames.DiagnosticsProfilerTargets + ", as choosing it in the Profiler window does. It stays there afterwards. Refused while a recording or memory snapshot is in progress.", Tags = new[] { "observability/profiling" })]
        public static object ProfilerConnect(
            [CliArg("target", "'editor' for this Editor, or a guid or name from " + CommandNames.DiagnosticsProfilerTargets + ".", Required = true)] string target = "")
        {
            if (string.IsNullOrWhiteSpace(target))
                return new { status = "error", message = "target is required: 'editor', or a guid or name from " + CommandNames.DiagnosticsProfilerTargets + "." };

            var busy = BusyMessage();
            if (busy != null)
                return new { status = "busy", message = busy };

            var targets = ProfilerTarget.Enumerate();
            var match = ProfilerTarget.Match(targets, target);
            if (match == null)
            {
                return new
                {
                    status = "error",
                    message = $"Unknown target '{target}'. Available targets: {string.Join(", ", targets.Select(t => t.Name))}. " +
                        "A Player must be a development build connected to this Editor."
                };
            }

            if (match.IsCurrent)
                return new { status = "ok", target = match.Name, guid = match.Guid, switched = false };

            if (!match.Connectable)
                return new { status = "error", message = $"Target '{match.Name}' is not connectable." };

            ProfilerDriver.connectedProfiler = match.Guid;
            if (ProfilerDriver.connectedProfiler != match.Guid)
                return new { status = "error", message = $"The Editor refused to connect to '{match.Name}'. Check that it is still running." };

            s_SwitchedAt = EditorApplication.timeSinceStartup;
            return new
            {
                status = "ok",
                target = match.Name,
                guid = match.Guid,
                switched = true,
                message = $"Connected. The connection takes about {SwitchSettleSeconds}s to settle; a recording or snapshot started before then waits for it."
            };
        }

        /// <summary>A recording and a snapshot each read from the connection for their whole run, so
        /// they exclude each other and a switch.</summary>
        internal static string BusyMessage()
        {
            if (ProfilerRecordCommands.IsActive)
                return "A Profiler recording is in progress. Stop it with " + CommandNames.DiagnosticsProfilerRecordStop + ", or poll " + CommandNames.DiagnosticsProfilerRecordStatus + " until it finishes.";
            if (MemorySnapshotCommands.IsActive)
                return "A memory snapshot is in progress. Poll " + CommandNames.DiagnosticsMemorySnapshotStatus + " until it finishes.";
            return null;
        }
    }
}
