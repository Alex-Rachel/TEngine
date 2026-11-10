using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Commands.Profiling;
using Unity.Profiling.Memory;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Memory
{
    /// <summary>
    /// Captures a memory snapshot (<c>.snap</c>) of whatever the Editor's Profiler connection points
    /// at: native forwards the request to a connected Player, so one TakeSnapshot call covers either
    /// process. Start runs on the main thread, like the update pump and the completion callback.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class MemorySnapshotCommands
    {
        const string Group = "diagnostics";
        const string StatusFile = "Temp/pipeline_memory_snapshot_status.json";
        const string FallbackFolder = "MemoryCaptures";
        const string SnapshotExtension = ".snap";

        const double DefaultTimeoutSeconds = 600;

        enum State
        {
            Idle,

            // Waiting for script compilation or a fresh profiler_connect to settle.
            Pending,
            Capturing
        }

        static State s_State;

        static string s_SnapshotId;
        static string s_Path;
        static ProfilerTarget s_Target;
        static CaptureFlags s_Flags;
        static bool s_RequestedCollectGarbage;
        static double s_RequestedTimeout;

        static double s_CaptureStartTime;
        static bool s_TimedOutReported;

        static MemorySnapshotCommands()
        {
            // Native tears the operation down and this state is gone, so a dangling snapshot is only
            // reportable as "interrupted".
            ReconcileAfterReload();

            // A subscription left across a reload pins the old AssemblyLoadContext.
            AssemblyReloadEvents.beforeAssemblyReload -= UnsubscribeUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += UnsubscribeUpdate;
        }

        /// <summary>A snapshot is queued or capturing, including one past its timeout that native may
        /// still be running.</summary>
        internal static bool IsActive => s_State != State.Idle;

        [CliGroupCommand(Group, "Capture a memory snapshot (.snap) of whatever the Editor's Profiler is connected to (see " + CommandNames.DiagnosticsProfilerConnect + "). Returns immediately; poll " + CommandNames.DiagnosticsMemorySnapshotStatus + " until status is 'completed', then read the path.", Tags = new[] { "observability/memory" })]
        public static object MemorySnapshot(
            [CliArg("flags", "Comma-separated capture flags (ManagedObjects,NativeObjects,NativeAllocations,NativeAllocationSites,NativeStackTraces). Default: the Memory Profiler package's configured flags, else ManagedObjects,NativeObjects. NativeStackTraces captures nothing unless the target Editor/Player was launched with -enable-memoryprofiler-callstacks.")] string flags = "",
            [CliArg("output-path", "Output .snap path (absolute or relative to the project root). Defaults to <snapshot folder>/<product-name>_<timestamp>_<snapshotId>.snap. Always written on the Editor machine, even for a Player.")] string output = "",
            [CliArg("collect-garbage", "Run GC.Collect before capturing, so that entirely empty managed heap pages get unmapped. Editor only: ignored for a Player, where it would collect the Editor's heap instead.")] bool collectGarbage = true,
            [CliArg("timeout-seconds", "Give up waiting for the capture after this long. Default 600. Raise it for slow remote connections.")] double timeoutSeconds = DefaultTimeoutSeconds)
        {
            var requested = ParseFlagNames(flags);
            var validationError = ValidateFlags(requested, Enum.GetNames(typeof(CaptureFlags)));
            if (validationError != null)
                return new { status = "error", message = validationError };

            if (timeoutSeconds <= 0)
                return new { status = "error", message = "timeout-seconds must be greater than zero." };

            var busy = ProfilerTargetCommands.BusyMessage();
            if (busy != null)
                return new { status = "busy", message = busy };

            s_SnapshotId = Guid.NewGuid().ToString("N").Substring(0, 8);
            s_Path = CaptureOutput.ResolvePath(output, ResolveSnapshotFolder(), FallbackFolder, s_SnapshotId, SnapshotExtension);
            s_Target = ProfilerTarget.Current();
            s_Flags = ResolveFlags(requested);
            s_RequestedCollectGarbage = collectGarbage;
            s_RequestedTimeout = timeoutSeconds;
            s_TimedOutReported = false;
            s_State = State.Pending;

            WriteStatus(BaseStatus("queued"));
            SubscribeUpdate();

            return new
            {
                status = "queued",
                snapshotId = s_SnapshotId,
                path = s_Path,
                target = s_Target.Name,
                message = "Capture queued. Poll " + CommandNames.DiagnosticsMemorySnapshotStatus + " until status is 'completed'."
            };
        }

        [CliGroupCommand(Group, "Get the status of the last memory snapshot: idle | queued | capturing | completed | failed | interrupted.", MainThreadRequired = false, Tags = new[] { "observability/memory" })]
        public static JToken MemorySnapshotStatus()
        {
            return StatusFileReader.ReadOrIdle(StatusFile);
        }

        static void SubscribeUpdate()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        static void UnsubscribeUpdate() => EditorApplication.update -= OnUpdate;

        static void OnUpdate()
        {
            if (s_State == State.Capturing)
            {
                CheckTimeout();
                return;
            }

            if (s_State != State.Pending)
                return;

            // While scripts compile, TakeSnapshot logs an error and returns without ever invoking the
            // callback, which would leave this waiting forever.
            if (EditorApplication.isCompiling || !ProfilerTargetCommands.IsConnectionSettled)
                return;

            try
            {
                StartCapture();
            }
            catch (Exception ex)
            {
                var status = BaseStatus("failed");
                status.Error = ex.Message;
                WriteStatus(status);
                Release();
            }
        }

        static void StartCapture()
        {
            s_Target.VerifyStillSelected();
            CaptureOutput.EnsureDirectory(s_Path);

            // GC.Collect only touches this process's heap, so for a Player it would imply a collection
            // that never reached the captured heap.
            var collect = s_RequestedCollectGarbage && s_Target.IsEditor;
            if (collect)
                CollectGarbage();

            s_State = State.Capturing;
            s_CaptureStartTime = EditorApplication.timeSinceStartup;

            var status = BaseStatus("capturing");
            if (s_RequestedCollectGarbage && !collect)
                status.Message = "collect-garbage ignored: it only affects the Editor's own heap, and this is a Player capture.";
            WriteStatus(status);

            MemoryProfiler.TakeSnapshot(s_Path, OnSnapshotFinished, s_Flags);
        }

        static void OnSnapshotFinished(string path, bool success)
        {
            try
            {
                var status = BaseStatus(success ? "completed" : "failed");
                status.Path = path;
                if (success)
                    status.SizeBytes = CaptureOutput.SizeOf(path);
                else
                    status.Error = "Unity reported the memory snapshot capture as failed. Check the Editor log.";
                WriteStatus(status);
            }
            finally
            {
                Release();
            }
        }

        static void Release()
        {
            s_State = State.Idle;
            UnsubscribeUpdate();
        }

        /// <summary>Leaves the snapshot active on purpose. Native still holds the operation and would
        /// ignore a further TakeSnapshot without invoking its callback.</summary>
        static void CheckTimeout()
        {
            if (s_TimedOutReported)
                return;
            if (EditorApplication.timeSinceStartup - s_CaptureStartTime <= s_RequestedTimeout)
                return;

            s_TimedOutReported = true;
            var status = BaseStatus("failed");
            status.Error = $"Capture did not complete within {s_RequestedTimeout:0}s. The underlying operation may still " +
                "be in progress, so further snapshots, Profiler recordings and connection changes are refused until it " +
                "finishes or the domain reloads. Raise timeout-seconds for slow remote connections.";
            WriteStatus(status);
        }

        static MemorySnapshotStatus BaseStatus(string status)
        {
            return new MemorySnapshotStatus
            {
                Status = status,
                SnapshotId = s_SnapshotId,
                Path = s_Path,
                Flags = s_Flags.ToString(),
                Target = s_Target?.Name,
                TargetIsEditor = s_Target?.IsEditor
            };
        }

        /// <summary>Six passes, waiting on finalizers between them, so emptied heap pages get unmapped.
        /// Mirrors the Memory Profiler window.</summary>
        static void CollectGarbage()
        {
            var before = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
            for (var i = 0; i < 6; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() < before)
                    break;
            }
        }

        internal static string ValidateFlags(IReadOnlyList<string> requested, IReadOnlyCollection<string> validNames)
        {
            if (requested == null || requested.Count == 0)
                return null;

            var unknown = requested
                .Where(r => !validNames.Any(v => string.Equals(v, r, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (unknown.Count == 0)
                return null;

            return $"Unknown capture flag{(unknown.Count == 1 ? "" : "s")}: {string.Join(", ", unknown)}. " +
                $"Valid flags: {string.Join(", ", validNames)}.";
        }

        static List<string> ParseFlagNames(string flags)
        {
            if (string.IsNullOrWhiteSpace(flags))
                return new List<string>();
            return flags.Split(',')
                .Select(f => f.Trim())
                .Where(f => f.Length > 0)
                .ToList();
        }

        static CaptureFlags ResolveFlags(IReadOnlyList<string> requested)
        {
            if (requested.Count > 0)
            {
                CaptureFlags result = 0;
                foreach (var name in requested)
                    result |= (CaptureFlags)Enum.Parse(typeof(CaptureFlags), name, ignoreCase: true);
                return result;
            }

            if (TryResolveSettings(out var settings) && settings.TryGetConfiguredFlags(out var configured))
                return configured;

            return CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects;
        }

        static string ResolveSnapshotFolder()
        {
            return TryResolveSettings(out var settings) && settings.TryGetStorageFolder(out var folder) ? folder : null;
        }

        static void ReconcileAfterReload()
        {
            if (!File.Exists(StatusFile))
                return;

            MemorySnapshotStatus prior;
            try
            {
                prior = JsonConvert.DeserializeObject<MemorySnapshotStatus>(File.ReadAllText(StatusFile));
            }
            catch
            {
                return;
            }

            if (prior != null && (prior.Status == "capturing" || prior.Status == "queued"))
                WriteStatus(new MemorySnapshotStatus { Status = "interrupted", SnapshotId = prior.SnapshotId, Path = prior.Path });
        }

        static void WriteStatus(MemorySnapshotStatus status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MemorySnapshot] Failed to write status file: {ex.Message}");
            }
        }

        #region memory profiler package settings (reflection)

        static ResolvedMemoryProfilerSettings s_Resolved;
        static bool s_ResolveAttempted;

        static bool TryResolveSettings(out ResolvedMemoryProfilerSettings settings)
        {
            if (!s_ResolveAttempted)
            {
                s_ResolveAttempted = true;
                // Not a sweep of AppDomain.GetAssemblies(), which trips the UAC0005 analyzer.
                var settingsType = Type.GetType(
                    "Unity.MemoryProfiler.Editor.MemoryProfilerSettings, Unity.MemoryProfiler.Editor");
                if (settingsType != null)
                {
                    try
                    {
                        s_Resolved = new ResolvedMemoryProfilerSettings(settingsType);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            "[MemorySnapshot] Memory Profiler package is installed but its settings API could not be " +
                            $"resolved (version mismatch?); using defaults. {ex.Message}");
                    }
                }
            }

            settings = s_Resolved;
            return settings != null;
        }

        #endregion
    }

    sealed class ResolvedMemoryProfilerSettings
    {
        readonly PropertyInfo m_AbsoluteStoragePath;
        readonly PropertyInfo m_CaptureFlags;

        public ResolvedMemoryProfilerSettings(Type settingsType)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            m_AbsoluteStoragePath = Require(
                settingsType.GetProperty("AbsoluteMemorySnapshotStoragePath", flags),
                "MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath");
            m_CaptureFlags = Require(
                settingsType.GetProperty("MemoryProfilerCaptureFlags", flags),
                "MemoryProfilerSettings.MemoryProfilerCaptureFlags");
        }

        public bool TryGetStorageFolder(out string folder)
        {
            folder = m_AbsoluteStoragePath.GetValue(null) as string;
            return !string.IsNullOrEmpty(folder);
        }

        public bool TryGetConfiguredFlags(out CaptureFlags flags)
        {
            flags = 0;
            if (!(m_CaptureFlags.GetValue(null) is CaptureFlags value) || value == 0)
                return false;
            flags = value;
            return true;
        }

        static T Require<T>(T member, string name) where T : class
        {
            if (member == null)
                throw new MissingMemberException($"Memory Profiler member not found: {name}");
            return member;
        }
    }

    sealed class MemorySnapshotStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("snapshotId", NullValueHandling = NullValueHandling.Ignore)]
        public string SnapshotId { get; set; }

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path { get; set; }

        [JsonProperty("flags", NullValueHandling = NullValueHandling.Ignore)]
        public string Flags { get; set; }

        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
        public string Target { get; set; }

        [JsonProperty("targetIsEditor", NullValueHandling = NullValueHandling.Ignore)]
        public bool? TargetIsEditor { get; set; }

        [JsonProperty("sizeBytes", NullValueHandling = NullValueHandling.Ignore)]
        public long? SizeBytes { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }
    }
}
