using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Profiling
{
    /// <summary>
    /// Records Profiler frames from whatever the Editor's Profiler connection points at and saves them
    /// to a <c>.data</c> file. Start and stop run on the main thread, like the update pump, so the
    /// state below has a single writer. A domain reload ends a recording as <c>interrupted</c>.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class ProfilerRecordCommands
    {
        const string Group = "diagnostics";
        const string StatusFile = "Temp/pipeline_profiler_record_status.json";
        const string FallbackFolder = "ProfilerCaptures";
        const string CaptureExtension = ".data";

        const int DefaultFrames = 300;
        const double DefaultTimeoutSeconds = 300;
        const double StatusWriteIntervalSeconds = 0.5;

        const string InterruptedMessage =
            "A domain reload (a script change, or entering Play mode with domain reload enabled) ended the recording. Run " + CommandNames.DiagnosticsProfilerRecord + " again.";

        const string EditModeRecordingNote =
            "Recording Editor frames in Edit mode: frames only advance as the Editor ticks, so keep " + CommandNames.EditorSetAutoTick + " enabled.";

        enum State
        {
            Idle,

            // Waiting for script compilation or a fresh profiler_connect to settle.
            Pending,
            Recording
        }

        static State s_State;

        static string s_CaptureId;
        static string s_Path;
        static ProfilerTarget s_Target;
        static int s_RequestedFrames;
        static bool s_RequestedDeepProfile;
        static double s_RequestedTimeout;

        static double s_StartTime;
        static double s_LastStatusWrite;
        static int s_BaselineFrameIndex;
        static string s_RecordingNote;

        // What StartRecording changed, so Release can put it back. Only set once recording started:
        // a request that fails before then has touched nothing.
        static bool s_Started;
        static bool s_PriorEnabled;
        static bool s_PriorProfileEditor;

        static ProfilerRecordCommands()
        {
            ReconcileAfterReload();

            // A subscription left across a reload pins the old AssemblyLoadContext.
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        /// <summary>A recording is queued or running, so the live Profiler history belongs to it.</summary>
        internal static bool IsActive => s_State != State.Idle;

        [CliGroupCommand(Group, "Record Unity Profiler frames from whatever the Editor's Profiler is connected to (see " + CommandNames.DiagnosticsProfilerConnect + ") and save them to a .data file. Returns immediately; poll " + CommandNames.DiagnosticsProfilerRecordStatus + " until status is 'completed', then read the path.", Tags = new[] { "observability/profiling" })]
        public static object ProfilerRecord(
            [CliArg("frames", "Number of frames to record before stopping and saving. Use 0 to record until " + CommandNames.DiagnosticsProfilerRecordStop + ". Default 300. The Profiler keeps a bounded history (Preferences > Analysis > Profiler > Frame Count, 300 by default), so a longer recording drops its earliest frames: the status reports framesSaved alongside framesRecorded.")] int frames = DefaultFrames,
            [CliArg("output-path", "Output .data path (absolute or relative to the project root). Defaults to <Profiler capture folder>/<product-name>_<timestamp>_<captureId>.data. Always written on the Editor machine, even when recording a Player.")] string output = "",
            [CliArg("deep-profile", "Whether this is a deep-profiling recording. Must match the Profiler's Deep Profile setting, which this command does not change: turn it on or off in the Profiler window first (for the Editor that reloads scripts). A Player must be a development build with Deep Profiling Support (see " + CommandNames.DiagnosticsProfilerTargets + ").")] bool deepProfile = false,
            [CliArg("timeout-seconds", "Stop and save what was recorded after this long. Default 300.")] double timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (frames < 0)
                return new { status = "error", message = "frames must be zero (record until " + CommandNames.DiagnosticsProfilerRecordStop + ") or greater." };

            if (timeoutSeconds <= 0)
                return new { status = "error", message = "timeout-seconds must be greater than zero." };

            var busy = ProfilerTargetCommands.BusyMessage();
            if (busy != null)
                return new { status = "busy", message = busy };

            var target = ProfilerTarget.Current();
            var deepProfileError = DeepProfileError(target, deepProfile);
            if (deepProfileError != null)
                return new { status = "error", message = deepProfileError };

            s_CaptureId = Guid.NewGuid().ToString("N").Substring(0, 8);
            s_Path = CaptureOutput.ResolvePath(output, ProfilerCaptureFolder.Resolve(), FallbackFolder, s_CaptureId, CaptureExtension);
            s_Target = target;
            s_RequestedFrames = frames;
            s_RequestedDeepProfile = deepProfile;
            s_RequestedTimeout = timeoutSeconds;
            s_State = State.Pending;

            WriteStatus(new ProfilerRecordStatus
            {
                Status = "queued",
                CaptureId = s_CaptureId,
                Path = s_Path,
                Target = target.Name,
                TargetIsEditor = target.IsEditor,
                FramesRequested = frames,
                DeepProfile = deepProfile
            });
            SubscribeUpdate();

            return new
            {
                status = "queued",
                captureId = s_CaptureId,
                path = s_Path,
                target = target.Name,
                message = "Recording queued. Poll " + CommandNames.DiagnosticsProfilerRecordStatus + " until status is 'completed'."
            };
        }

        [CliGroupCommand(Group, "Stop the Profiler recording early and save the frames recorded so far. Returns the final status.", Tags = new[] { "observability/profiling" })]
        public static object ProfilerRecordStop()
        {
            switch (s_State)
            {
                case State.Pending:
                    return Fail("Stopped by " + CommandNames.DiagnosticsProfilerRecordStop + " before recording started, so no frames were captured.");
                case State.Recording:
                    return Finish(RecordedFrames(), "Stopped by " + CommandNames.DiagnosticsProfilerRecordStop + ".");
                default:
                    return new { status = "idle", message = "No Profiler recording is queued or running." };
            }
        }

        [CliGroupCommand(Group, "Get the status of the last Profiler recording: idle | queued | recording | completed | failed | interrupted.", MainThreadRequired = false, Tags = new[] { "observability/profiling" })]
        public static JToken ProfilerRecordStatus()
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
            if (s_State == State.Recording)
            {
                PollRecording();
                return;
            }

            if (s_State != State.Pending)
                return;

            if (EditorApplication.isCompiling || !ProfilerTargetCommands.IsConnectionSettled)
                return;

            try
            {
                StartRecording();
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        /// <summary>
        /// Null when the recording can be labelled with the requested mode. The Editor's Deep Profile
        /// setting only takes effect through a script reload, and a reload ends a recording, so the
        /// setting is the caller's to change beforehand rather than this command's.
        /// </summary>
        static string DeepProfileError(ProfilerTarget target, bool deepProfile)
        {
            if (deepProfile != ProfilerDriver.deepProfiling)
            {
                return deepProfile
                    ? "deep-profile=true, but the Profiler's Deep Profile setting is off. Turn on Deep Profile in the Profiler window (for the Editor this reloads scripts), then run this again."
                    : "The Profiler's Deep Profile setting is on, so this recording would be a deep-profiling one. Pass deep-profile=true, or turn Deep Profile off in the Profiler window first.";
            }

            // The Editor accepts deep profiling for a Player that cannot honour it and reads it back as
            // enabled, so an unchecked recording would be labelled deep with no deep samples.
            if (deepProfile && !target.DeepProfilingSupported)
            {
                return $"Target '{target.Name}' was not built with deep profiling support, so it would ignore " +
                    "deep profiling and return an ordinary recording. Rebuild the Player with Deep Profiling Support " +
                    "(a development build option).";
            }

            return null;
        }

        static void StartRecording()
        {
            s_Target.VerifyStillSelected();

            // Checked again: the Profiler window may have changed it since the request was accepted.
            var deepProfileError = DeepProfileError(s_Target, s_RequestedDeepProfile);
            if (deepProfileError != null)
                throw new InvalidOperationException(deepProfileError);

            CaptureOutput.EnsureDirectory(s_Path);

            s_PriorEnabled = ProfilerDriver.enabled;
            s_PriorProfileEditor = ProfilerDriver.profileEditor;
            s_Started = true;

            // Without this an Editor target only records while in Play mode.
            if (s_Target.IsEditor)
                ProfilerDriver.profileEditor = true;

            // SaveProfile writes the entire history, so start from an empty one.
            ProfilerDriver.ClearAllFrames();
            s_BaselineFrameIndex = ProfilerDriver.lastFrameIndex;
            s_StartTime = EditorApplication.timeSinceStartup;
            s_RecordingNote = s_Target.IsEditor && !EditorApplication.isPlaying ? EditModeRecordingNote : null;
            s_State = State.Recording;

            ProfilerDriver.enabled = true;
            WriteRecordingStatus(0);
        }

        static void PollRecording()
        {
            var recorded = RecordedFrames();

            try
            {
                // Every poll: the Profiler window drives the same global connection, and the counts and
                // the save read a history that would by then belong to another process.
                s_Target.VerifyStillSelected();

                if (s_RequestedFrames > 0 && recorded >= s_RequestedFrames)
                {
                    Finish(recorded, null);
                    return;
                }

                if (EditorApplication.timeSinceStartup - s_StartTime > s_RequestedTimeout)
                {
                    Finish(recorded, $"Timed out after {s_RequestedTimeout:0}s with {recorded} of {s_RequestedFrames} frames; saved what was recorded.");
                    return;
                }

                if (EditorApplication.timeSinceStartup - s_LastStatusWrite >= StatusWriteIntervalSeconds)
                    WriteRecordingStatus(recorded);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        static ProfilerRecordStatus Finish(int recorded, string message)
        {
            ProfilerDriver.enabled = false;

            // What the file will hold, which is not what the recording observed: the Profiler keeps a
            // bounded history, so a long recording has already dropped its earliest frames and
            // SaveProfile writes only what is left. Read after recording stops, so it cannot move.
            var saved = RetainedFrames();
            if (saved <= 0)
            {
                return Fail("No Profiler frames were recorded, so there is nothing to save. For an Editor target " +
                    "check that the Editor is ticking (" + CommandNames.EditorSetAutoTick + "); for a Player target check it is still connected.");
            }

            if (saved < recorded)
            {
                var dropped = $"Recorded {recorded} frames, but the Profiler history holds {saved}: " +
                    $"the earliest {recorded - saved} were dropped before the save. Raise Preferences > " +
                    "Analysis > Profiler > Frame Count to keep more, or record fewer frames.";
                message = string.IsNullOrEmpty(message) ? dropped : $"{message} {dropped}";
            }

            bool written;
            try
            {
                // Immediately before the write: SaveProfile dumps the global history, so a connection
                // moved since the last poll would put another process's frames in this file.
                s_Target.VerifyStillSelected();
#if UNITY_6000_3_OR_NEWER
                written = ProfilerDriver.SaveProfile(s_Path);
#else
                // Returns void before 6000.3. The file is overwritten either way, so removing a stale
                // one first lets its presence afterwards stand in for the result.
                if (File.Exists(s_Path))
                    File.Delete(s_Path);
                ProfilerDriver.SaveProfile(s_Path);
                written = File.Exists(s_Path);
#endif
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }

            if (!written)
                return Fail($"ProfilerDriver.SaveProfile failed for '{s_Path}'. Check the Editor log.");

            var status = BaseStatus("completed");
            status.FramesRecorded = recorded;
            status.FramesSaved = saved;
            status.ElapsedSeconds = EditorApplication.timeSinceStartup - s_StartTime;
            status.SizeBytes = CaptureOutput.SizeOf(s_Path);
            status.Message = message;
            WriteStatus(status);

            Release();
            return status;
        }

        static ProfilerRecordStatus Fail(string error)
        {
            var status = BaseStatus("failed");
            status.Error = error;
            WriteStatus(status);

            Release();
            return status;
        }

        /// <summary>Puts back the Profiler state <see cref="StartRecording"/> changed and goes idle.</summary>
        static void Release()
        {
            if (s_Started)
            {
                ProfilerDriver.enabled = s_PriorEnabled;
                if (s_Target.IsEditor)
                    ProfilerDriver.profileEditor = s_PriorProfileEditor;
                s_Started = false;
            }

            s_State = State.Idle;
            UnsubscribeUpdate();
        }

        /// <summary>
        /// Ends a queued or running recording as interrupted while the prior Profiler switches still
        /// exist: they do not survive the reload, and <see cref="ReconcileAfterReload"/> could only
        /// disable recording, not restore what the user had.
        /// </summary>
        internal static void OnBeforeReload()
        {
            if (s_State == State.Idle)
                return;

            try
            {
                var status = BaseStatus("interrupted");
                status.Message = InterruptedMessage;
                WriteStatus(status);
            }
            finally
            {
                Release();
            }
        }

        static ProfilerRecordStatus BaseStatus(string status)
        {
            return new ProfilerRecordStatus
            {
                Status = status,
                CaptureId = s_CaptureId,
                Path = s_Path,
                Target = s_Target?.Name,
                TargetIsEditor = s_Target?.IsEditor,
                FramesRequested = s_RequestedFrames,
                DeepProfile = s_RequestedDeepProfile
            };
        }

        static void WriteRecordingStatus(int recorded)
        {
            var status = BaseStatus("recording");
            status.FramesRecorded = recorded;
            // Frames still in the history, so a caller watching a long recording sees the bounded
            // history biting rather than discovering it only in the completed status.
            status.FramesSaved = RetainedFrames();
            status.ElapsedSeconds = EditorApplication.timeSinceStartup - s_StartTime;
            status.Message = s_RecordingNote;
            WriteStatus(status);
            s_LastStatusWrite = EditorApplication.timeSinceStartup;
        }

        static int RecordedFrames() => RecordedFrames(ProfilerDriver.lastFrameIndex, s_BaselineFrameIndex);

        static int RetainedFrames() =>
            RetainedFrames(ProfilerDriver.firstFrameIndex, ProfilerDriver.lastFrameIndex, s_BaselineFrameIndex);

        /// <summary>Frames recorded since <paramref name="baseline"/>, the last index before recording
        /// started. Indices only grow, so this stays correct once the history drops old frames.</summary>
        internal static int RecordedFrames(int last, int baseline) => last < 0 ? 0 : last - baseline;

        /// <summary>
        /// Frames of this recording the Profiler still holds, which is what SaveProfile writes. The
        /// history is bounded by the Profiler's frame count, so this falls behind RecordedFrames once a
        /// recording outruns it; indices at or below <paramref name="baseline"/> belong to whatever ran
        /// before.
        /// </summary>
        internal static int RetainedFrames(int first, int last, int baseline)
        {
            if (last < 0 || first < 0)
                return 0;

            return Math.Max(0, last - Math.Max(first - 1, baseline));
        }

        /// <summary>
        /// The state that would stop and save a recording does not survive a domain reload, so one
        /// that was queued or running is reported as interrupted. Recording is stopped too, because
        /// nothing is left to save it and it would otherwise run on unseen.
        /// </summary>
        static void ReconcileAfterReload()
        {
            if (!File.Exists(StatusFile))
                return;

            ProfilerRecordStatus prior;
            try
            {
                prior = JsonConvert.DeserializeObject<ProfilerRecordStatus>(File.ReadAllText(StatusFile));
            }
            catch
            {
                return;
            }

            if (prior == null || (prior.Status != "queued" && prior.Status != "recording"))
                return;

            if (prior.Status == "recording")
                ProfilerDriver.enabled = false;

            WriteStatus(new ProfilerRecordStatus
            {
                Status = "interrupted",
                CaptureId = prior.CaptureId,
                Path = prior.Path,
                Target = prior.Target,
                Message = InterruptedMessage
            });
        }

        static void WriteStatus(ProfilerRecordStatus status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProfilerRecord] Failed to write status file: {ex.Message}");
            }
        }
    }

    /// <summary>The Profiler's configured capture folder, which is internal API. Null when absent.</summary>
    static class ProfilerCaptureFolder
    {
        static PropertyInfo s_Property;
        static bool s_Resolved;

        public static string Resolve()
        {
            try
            {
                if (!s_Resolved)
                {
                    s_Resolved = true;
                    // Not a sweep of AppDomain.GetAssemblies(), which trips the UAC0005 analyzer.
                    var type = typeof(ProfilerDriver).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings");
                    s_Property = type?.GetProperty("AbsoluteProfilerCaptureStoragePath", BindingFlags.Public | BindingFlags.Static);
                }

                return s_Property?.GetValue(null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    sealed class ProfilerRecordStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("captureId", NullValueHandling = NullValueHandling.Ignore)]
        public string CaptureId { get; set; }

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path { get; set; }

        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
        public string Target { get; set; }

        [JsonProperty("targetIsEditor", NullValueHandling = NullValueHandling.Ignore)]
        public bool? TargetIsEditor { get; set; }

        [JsonProperty("framesRequested", NullValueHandling = NullValueHandling.Ignore)]
        public int? FramesRequested { get; set; }

        /// <summary>Frames the recording observed, which is what the request's frame count is
        /// measured against.</summary>
        [JsonProperty("framesRecorded", NullValueHandling = NullValueHandling.Ignore)]
        public int? FramesRecorded { get; set; }

        /// <summary>Frames in the file, fewer than framesRecorded once a recording outruns the
        /// Profiler's bounded history.</summary>
        [JsonProperty("framesSaved", NullValueHandling = NullValueHandling.Ignore)]
        public int? FramesSaved { get; set; }

        [JsonProperty("deepProfile", NullValueHandling = NullValueHandling.Ignore)]
        public bool? DeepProfile { get; set; }

        [JsonProperty("elapsedSeconds", NullValueHandling = NullValueHandling.Ignore)]
        public double? ElapsedSeconds { get; set; }

        [JsonProperty("sizeBytes", NullValueHandling = NullValueHandling.Ignore)]
        public long? SizeBytes { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }
    }
}
