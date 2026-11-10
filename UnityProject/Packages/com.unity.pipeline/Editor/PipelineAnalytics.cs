using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Telemetry;
using UnityEditor;
using UnityEngine;
using UnityEngine.Analytics;
#if !UNITY_2023_2_OR_NEWER
using IAnalytic = Unity.Pipeline.Editor.PipelineAnalytics.ILegacyAnalytic;
#endif
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Reports pipeline usage to Unity's editor analytics: one session-start event on the first
    /// command a client executes, one event per command executed after that, and one session-stop
    /// event when the Editor quits.
    ///
    /// Everything here is main-thread only — SendAnalytic, SessionState and the editor state the
    /// events describe all require it. Commands finish on the background HTTP thread, so
    /// <see cref="EditorPipelineServer"/> posts each one to the server dispatcher, which runs it
    /// here on the next main-thread pump.
    ///
    /// A session spans a Unity PROCESS, not a domain: the latch lives in SessionState, which
    /// survives domain reloads and dies with the editor, so a recompile does not start a new
    /// session.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class PipelineAnalytics
    {
        private const string VendorKey = "unity.pipeline";

        private const string SessionStartedEventName = "Pipeline_SessionStarted";
        private const string CommandExecutedEventName = "Pipeline_CommandExecuted";
        private const string SessionStoppedEventName = "Pipeline_SessionStopped";

        // A session emits at most one start and one stop; commands are the volume, and an
        // agent-driven session issues them continuously.
        private const int SessionEventsPerHour = 10;
        private const int CommandEventsPerHour = 1000;
        private const int MaxElements = 1000;

        private const string SessionStartedKey = "Unity.Pipeline.Analytics.SessionStarted";
        private const string SessionStartTicksKey = "Unity.Pipeline.Analytics.SessionStartTicks";

        /// <summary>
        /// The tag every code-evaluation command carries (eval, eval_file, run_script). Matching the
        /// tag rather than a name list covers a new eval command the moment it is tagged, and the
        /// suite already enforces that shipped commands carry well-formed tags.
        /// </summary>
        private const string EvalTag = "scripts/eval";

        /// <summary>
        /// Reported in place of a project-declared command's real name. A command name is text the
        /// project's own authors wrote, so it can name an unannounced feature or an internal system;
        /// none of that belongs in an event leaving the machine. Its tags are withheld for the same
        /// reason — nothing constrains a project to the documented taxonomy, so a tag can carry a
        /// project-chosen word exactly as a name can. Counting how much traffic is project-declared
        /// is the useful signal, and isUserDefinedCommand already carries it.
        /// </summary>
        private const string RedactedCommandName = "<customUserCommand>";

        /// <summary>
        /// The send call, indirected so tests can capture events instead of shipping them.
        /// </summary>
#if UNITY_2023_2_OR_NEWER
        internal static Action<IAnalytic> s_Send = analytic => EditorAnalytics.SendAnalytic(analytic);
#else
        internal static Action<IAnalytic> s_Send = SendLegacy;

        internal interface ILegacyAnalytic
        {
            public interface IData { }

            bool TryGatherData(out IData data, out Exception error);
        }

        private static readonly HashSet<string> s_RegisteredLegacyEvents = new HashSet<string>();

        private static void SendLegacy(IAnalytic analytic)
        {
            if (!EditorAnalytics.enabled || !analytic.TryGatherData(out var data, out _))
                return;

            string eventName;
            int eventsPerHour;
            switch (analytic)
            {
                case SessionStartedAnalytic _:
                    eventName = SessionStartedEventName;
                    eventsPerHour = SessionEventsPerHour;
                    break;
                case CommandExecutedAnalytic _:
                    eventName = CommandExecutedEventName;
                    eventsPerHour = CommandEventsPerHour;
                    break;
                case SessionStoppedAnalytic _:
                    eventName = SessionStoppedEventName;
                    eventsPerHour = SessionEventsPerHour;
                    break;
                default:
                    return;
            }

            if (!s_RegisteredLegacyEvents.Contains(eventName))
            {
                if (EditorAnalytics.RegisterEventWithLimit(eventName, eventsPerHour, MaxElements, VendorKey) != AnalyticsResult.Ok)
                    return;
                s_RegisteredLegacyEvents.Add(eventName);
            }

            EditorAnalytics.SendEventWithLimit(eventName, data);
        }
#endif

        private const int RecentEventCapacity = 5;
        private static readonly Queue<SentEvent> s_RecentEvents = new Queue<SentEvent>();

        /// <summary>
        /// Whether sent events are kept for <see cref="RecentEvents"/>. Off until a client starts
        /// recording; a domain reload turns it off again.
        /// </summary>
        internal static bool IsRecording { get; set; }

        /// <summary>
        /// Whether evals record what <c>Pipeline_CommandExecuted</c> reports about them: while the
        /// user's Editor analytics are on, or while recording.
        /// </summary>
        internal static bool CollectsEvalDetails() => EditorAnalytics.enabled || IsRecording;

        [InitializeOnLoadMethod]
        static void CollectEvalDetailsWithAnalytics() => EvalAnalyticsDetails.IsCollecting = CollectsEvalDetails;

        /// <summary>
        /// The last events the reporter handed to Unity's analytics while recording, newest last,
        /// whether or not the user's analytics settings then let them through. Held in memory, so a
        /// domain reload empties it.
        /// </summary>
        internal static IReadOnlyList<SentEvent> RecentEvents
        {
            get
            {
                lock (s_RecentEvents)
                    return s_RecentEvents.ToArray();
            }
        }

        /// <summary>One event the reporter sent: its name, when, and its payload.</summary>
        internal sealed class SentEvent
        {
            public SentEvent(string eventName, IAnalytic.IData data)
            {
                EventName = eventName;
                Time = DateTime.UtcNow.ToString("o");
                Data = data;
            }

            [JsonProperty("eventName")]
            public string EventName { get; }

            [JsonProperty("time")]
            public string Time { get; }

            [JsonProperty("data")]
            public IAnalytic.IData Data { get; }
        }

        internal static void ClearRecentEvents()
        {
            lock (s_RecentEvents)
                s_RecentEvents.Clear();
        }

        internal static void Send(string eventName, IAnalytic analytic)
        {
            try
            {
                if (IsRecording && analytic.TryGatherData(out var data, out _))
                {
                    lock (s_RecentEvents)
                    {
                        s_RecentEvents.Enqueue(new SentEvent(eventName, data));
                        while (s_RecentEvents.Count > RecentEventCapacity)
                            s_RecentEvents.Dequeue();
                    }
                }
            }
            finally
            {
                s_Send(analytic);
            }
        }

        /// <summary>
        /// The session latch as stored in SessionState: whether the start event has been sent this
        /// Unity process, and the UTC tick count it was sent at. Exposed so a fixture driving the
        /// reporter can save and restore the live latch.
        /// </summary>
        internal static (bool started, long startTicks) SessionLatch
        {
            get
            {
                var started = SessionState.GetBool(SessionStartedKey, false);
                long.TryParse(SessionState.GetString(SessionStartTicksKey, "0"), out var ticks);
                return (started, ticks);
            }
            set
            {
                SessionState.SetBool(SessionStartedKey, value.started);
                SessionState.SetString(SessionStartTicksKey, value.startTicks.ToString());
            }
        }

        /// <summary>
        /// Report one executed command, opening the session first if this is the first one. An
        /// info with no command is a request that was rejected before executing anything, which is
        /// not a command execution and is skipped. Main thread.
        /// </summary>
        internal static void RecordCommandExecuted(in CommandExecutionInfo info)
        {
            if (info.Command == null)
                return;

            try
            {
                EnsureSessionStarted();

                var command = info.Command;
                var userDefined = IsUserDefinedCommand(command.Package);
                var isEval = IsEvalCommand(command.Tags);
                var evalDetails = isEval ? info.EvalDetails : null;
                Send(CommandExecutedEventName, new CommandExecutedAnalytic(new CommandExecutedData
                {
                    // Only a command that shipped with the package describes itself; a
                    // project-declared one reports the placeholder and no tags.
                    commandName = userDefined ? RedactedCommandName : command.FullName,
                    commandTags = userDefined ? Array.Empty<string>() : ToArray(command.Tags),
                    commandSuccess = info.Success,
                    commandDuration = info.DurationMs,
                    isUserDefinedCommand = userDefined,
                    isEval = isEval,
                    isEvalWithExistingCommandAvailable = EvalCoversAnExistingCommand(command, info.Parameters),
                    commandId = info.CommandId,
                    clientSource = info.ClientSource ?? "",
                    clientName = info.ClientName ?? "",
                    caller = info.Caller ?? "",
                    skill = info.Skill ?? "",
                    failureKind = (isEval ? info.FailureKind : null) ?? "",
                    unityAPIUsed = ToArray(evalDetails?.UnityApisUsed),
                    compileErrorCodes = ToArray(evalDetails?.CompileErrorCodes),
                    exceptionType = evalDetails?.ExceptionType ?? "",
                }));
            }
            catch (Exception ex)
            {
                // Reporting must never break request handling.
                Debug.LogWarning($"Pipeline analytics failed to report a command: {ex.Message}");
            }
        }

        /// <summary>
        /// Send the session-stop event if this process ever opened a session, and close the latch so
        /// a second call is a no-op. Main thread.
        /// </summary>
        internal static void SendSessionStoppedIfStarted()
        {
            try
            {
                var (started, startTicks) = SessionLatch;
                if (!started)
                    return;

                SessionLatch = (false, 0);

                var elapsedMs = startTicks > 0
                    ? Math.Max(0, (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond)
                    : 0;

                Send(SessionStoppedEventName, new SessionStoppedAnalytic(new SessionStoppedData
                {
                    sessionDuration = elapsedMs,
                }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline analytics failed to report the session end: {ex.Message}");
            }
        }

        /// <summary>
        /// Send the session-start event once per Unity process, describing how this editor and its
        /// server were launched. The latch is set before sending, so a throwing send cannot make a
        /// later command try again.
        /// </summary>
        private static void EnsureSessionStarted()
        {
            if (SessionLatch.started)
                return;

            SessionLatch = (true, DateTime.UtcNow.Ticks);

            var server = PipelineServerStartup.Server;
            Send(SessionStartedEventName, new SessionStartedAnalytic(new SessionStartedData
            {
                batchmode = Application.isBatchMode,
                automated = EditorProcessInfo.IsAutomated(Environment.GetCommandLineArgs()),
                // Both fall back to what the server starts with when no settings are authored:
                // browser clients refused, port auto-assigned from the range.
                browserAllowed = server?.AllowSandboxedBrowserClients ?? false,
                dynamicPortAssignation = server?.PortAutoAssigned ?? true,
                hotReloadWatcherEnabled = EditorMethodReloadWatcher.IsWatching,
            }));
        }

        /// <summary>
        /// Whether a command came from the project rather than the package, judged by its declaring
        /// assembly. An unresolved assembly is not evidence of a project command, so it counts as
        /// shipped.
        /// </summary>
        internal static bool IsUserDefinedCommand(string package)
        {
            return !string.IsNullOrEmpty(package) && !CommandRegistry.IsPackageAssembly(package);
        }

        /// <summary>
        /// Whether the snippet this eval ran touches an API that an existing command already covers
        /// — the "you did not need eval for this" signal.
        ///
        /// Same judgement the report_evals command makes, on one invocation instead of the whole log:
        /// fingerprint the body, look each fingerprint up in the curated
        /// <see cref="EvalCoverageMap"/>, and count it only if the command it names is actually in the
        /// live catalog. The map is a hint layer, so a false here means "no mapped-and-present
        /// command", not "no command could ever cover this".
        ///
        /// Answerable only where the body is reachable from the parameters: <c>eval</c> passes it
        /// inline and <c>eval_file</c> names a file to read. <c>run_script</c> runs a project file
        /// with a named entry point rather than a snippet, and the fingerprinter parses script-kind
        /// bodies, so it is not fingerprinted and reports false.
        /// </summary>
        internal static bool EvalCoversAnExistingCommand(CommandInfo command, IReadOnlyDictionary<string, object> parameters)
        {
            if (parameters == null || !IsEvalCommand(command.Tags))
                return false;

            var code = ReadEvalBody(command.FullName, parameters);
            if (string.IsNullOrWhiteSpace(code))
                return false;

            var fingerprints = EvalUsageFingerprinter.Analyze(code).Fingerprints;
            if (fingerprints == null || fingerprints.Count == 0)
                return false;

            var available = new HashSet<string>(CommandRegistry.DiscoverCommands().Select(c => c.FullName));
            foreach (var fingerprint in fingerprints)
            {
                if (EvalCoverageMap.Default.TryGetValue(fingerprint, out var mapped) && available.Contains(mapped))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The evaluated body, from the parameters the command was invoked with. Returns null for a
        /// command whose body is not reachable that way.
        /// </summary>
        private static string ReadEvalBody(string commandName, IReadOnlyDictionary<string, object> parameters)
        {
            switch (commandName)
            {
                case CommandNames.ScriptEval:
                    var code = parameters.TryGetValue("code", out var value) ? value?.ToString() : null;
                    return string.IsNullOrEmpty(code) ? ReadEvalFile(parameters) : code;

                case CommandNames.ScriptEvalFile:
                    return ReadEvalFile(parameters);

                default:
                    return null;
            }
        }

        /// <summary>
        /// The file an eval named in its <c>file</c> parameter, resolved like the command resolves
        /// it. The command read this file moments ago; re-reading it costs one small read and keeps
        /// a file eval from reporting a permanent false that would be indistinguishable from a
        /// genuine "nothing covers this".
        /// </summary>
        private static string ReadEvalFile(IReadOnlyDictionary<string, object> parameters)
        {
            var file = parameters.TryGetValue("file", out var value) ? value?.ToString() : null;
            if (string.IsNullOrEmpty(file))
                return null;

            var path = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(Authoring.ProjectPaths.ProjectRoot, file);
            if (!System.IO.File.Exists(path))
                return null;
            try
            {
                return System.IO.File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsEvalCommand(IReadOnlyList<string> tags)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                if (tags[i] == EvalTag)
                    return true;
            }

            return false;
        }

        private static string[] ToArray(IReadOnlyList<string> tags)
        {
            if (tags == null)
                return Array.Empty<string>();

            var array = new string[tags.Count];
            for (var i = 0; i < tags.Count; i++)
                array[i] = tags[i];
            return array;
        }

        // The payload field names below ARE the registered event schema. Renaming one silently stops
        // that column being populated, so they keep the schema casing rather than C# casing.

        [Serializable]
        internal struct SessionStartedData : IAnalytic.IData
        {
            public bool batchmode;
            public bool automated;
            public bool browserAllowed;
            public bool dynamicPortAssignation;
            // Keeps the registered analytics schema's field name from before the method-reload rename.
            public bool hotReloadWatcherEnabled;
        }

        [Serializable]
        internal struct CommandExecutedData : IAnalytic.IData
        {
            public string commandName;
            public string[] commandTags;
            public bool commandSuccess;
            public long commandDuration;
            public bool isUserDefinedCommand;
            public bool isEval;
            public bool isEvalWithExistingCommandAvailable;
            public long commandId;
            public string clientSource;
            public string clientName;
            public string caller;
            public string skill;
            public string failureKind;
            public string[] unityAPIUsed;
            public string[] compileErrorCodes;
            public string exceptionType;
        }

        [Serializable]
        internal struct SessionStoppedData : IAnalytic.IData
        {
            public long sessionDuration;
        }

#if UNITY_2023_2_OR_NEWER
        [AnalyticInfo(eventName: SessionStartedEventName, vendorKey: VendorKey,
            maxEventsPerHour: SessionEventsPerHour, maxNumberOfElements: MaxElements)]
#endif
        internal class SessionStartedAnalytic : IAnalytic
        {
            private readonly SessionStartedData m_Data;

            public SessionStartedAnalytic(SessionStartedData data) => m_Data = data;

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                data = m_Data;
                error = null;
                return true;
            }
        }

#if UNITY_2023_2_OR_NEWER
        [AnalyticInfo(eventName: CommandExecutedEventName, vendorKey: VendorKey,
            maxEventsPerHour: CommandEventsPerHour, maxNumberOfElements: MaxElements)]
#endif
        internal class CommandExecutedAnalytic : IAnalytic
        {
            private readonly CommandExecutedData m_Data;

            public CommandExecutedAnalytic(CommandExecutedData data) => m_Data = data;

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                data = m_Data;
                error = null;
                return true;
            }
        }

#if UNITY_2023_2_OR_NEWER
        [AnalyticInfo(eventName: SessionStoppedEventName, vendorKey: VendorKey,
            maxEventsPerHour: SessionEventsPerHour, maxNumberOfElements: MaxElements)]
#endif
        internal class SessionStoppedAnalytic : IAnalytic
        {
            private readonly SessionStoppedData m_Data;

            public SessionStoppedAnalytic(SessionStoppedData data) => m_Data = data;

            public bool TryGatherData(out IAnalytic.IData data, out Exception error)
            {
                data = m_Data;
                error = null;
                return true;
            }
        }
    }
}
