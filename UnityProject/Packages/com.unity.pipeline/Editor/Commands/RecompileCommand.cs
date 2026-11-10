using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Console;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Forces a script recompile that works even when the editor is unfocused or minimized.
    ///
    /// Why this is non-trivial:
    ///  - Unity only runs script compilation while the editor is the active OS application. Pass
    ///    focus=true to bring it to the foreground (editor_focus) before AssetDatabase.Refresh();
    ///    it is off by default because the server keeps the editor ticking while unfocused, so
    ///    compilation still proceeds without stealing the user's foreground window.
    ///  - A successful compile triggers a domain reload, which destroys the managed AppDomain
    ///    (HTTP server, in-flight requests, statics). The triggering request therefore cannot stay
    ///    open and return when done.
    ///
    /// Pattern (mirrors the test runner): completion is reported via a status file that survives the
    /// domain reload. Call "recompile" to trigger, then poll "recompile_status" until status is
    /// "completed" or "up_to_date". The client must tolerate connection errors during the reload.
    ///
    /// The status file is not sufficient on its own: a failed editor compile suppresses the domain
    /// reload entirely, and the file lives under Temp/, which Unity deletes at every editor launch.
    /// Both commands therefore also consult the compile-failure flag Unity keeps natively.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class RecompileCommand
    {
        const string Group = "script";
        const string StatusFile = "Temp/pipeline_recompile_status.json";

        // How stale a ground-truth sample may be and still describe the present. Sampling runs on
        // the main thread, so it stops while the Editor is blocked and across a domain reload.
        const double MaxGroundTruthAgeSeconds = 2.0;

        static readonly List<string> s_Errors = new List<string>();

        // Focus action, indirected so tests can observe whether focus was performed without
        // actually stealing the OS foreground window.
        internal static Action s_FocusAction = () => FocusEditorCommand.Focus();

        // Indirected for the same reason: a test cannot make Unity's native compile-failure flag
        // true, but it can point this at a stub.
        internal static Func<bool> s_CompilationFailedProbe = () => EditorUtility.scriptCompilationFailed;

        /// <summary>
        /// The status file's contents, and the result <c>recompile_status</c> returns. Shared by the
        /// writer and both readers; the [JsonProperty] names are the wire field names.
        /// </summary>
        [Serializable]
        internal class RecompileStatusPayload
        {
            [JsonProperty("status")] public string Status;
            [JsonProperty("failed")] public bool Failed;
            [JsonProperty("errors")] public string[] Errors;

            /// <summary>
            /// Whether the project currently has editor compile errors, as Unity reports it
            /// natively. Filled when the status is read rather than written: it outlives both the
            /// status file and any domain reload, so it is the authority when the two disagree.
            /// </summary>
            [JsonProperty("compilationFailed")] public bool CompilationFailed;
        }

        static RecompileCommand()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        [CliGroupCommand(Group, "Force a script recompile (works while unfocused/minimized). Poll " + CommandNames.ScriptRecompileStatus + " for completion.", MainThreadRequired = true, Tags = new[] { "scripts/compile" })]
        public static object Recompile(
            [CliArg("focus", "If true, bring the Editor to the foreground before compiling. Off by default.")] bool focus = false)
        {
            // Unity compiles only while it is the active application. Optionally bring it to the
            // foreground first; off by default (the server keeps the editor ticking while unfocused,
            // so compilation still proceeds).
            if (focus)
                s_FocusAction();

            var previous = ReadStatusOrIdle();
            WriteStatus("triggered", false, null);

            // Trigger asset import + compilation.
            AssetDatabase.Refresh();

            if (EditorApplication.isCompiling)
            {
                // compilationStarted has written "compiling"; a domain reload will follow on success.
                return new { status = "compiling", message = "Recompilation started. Poll " + CommandNames.ScriptRecompileStatus + " until completed." };
            }

            // Nothing was imported and nothing compiled. That is only "up to date" if the code
            // actually builds: a failed editor compile leaves the errors standing and suppresses the
            // domain reload, so a repeat call with nothing changed lands here with the failure still
            // true. Reporting "up_to_date" would erase it.
            if (s_CompilationFailedProbe())
            {
                if (previous.Failed)
                    WriteStatus(previous.Status, true, previous.Errors);
                else
                    WriteStatus("completed", true, Array.Empty<string>());

                return new { status = "failed", message = "Scripts still have compile errors; nothing was recompiled. Read them with the " + CommandNames.ConsoleGet + " command." };
            }

            WriteStatus("up_to_date", false, null);
            return new { status = "up_to_date", message = "No scripts needed recompilation." };
        }

        [CliGroupCommand(Group, "Get the status of the last recompile: idle | triggered | compiling | completed | up_to_date.", MainThreadRequired = false, Tags = new[] { "scripts/compile" })]
        public static RecompileStatusPayload RecompileStatus()
        {
            var status = ReadStatusOrIdle();

            var truth = ConsoleLogCapture.GroundTruth;
            var fresh = truth != null
                && (DateTime.UtcNow - truth.SampledUtc).TotalSeconds <= MaxGroundTruthAgeSeconds;

            status.CompilationFailed = fresh && truth.CompilationFailed;

            // Only a status that carries no evidence of its own defers to the native flag. "idle" is
            // what the file reads after an editor launch, since Temp/ is deleted then, and
            // "up_to_date" means nothing compiled — in both cases errors can be standing with
            // nothing in the file to say so.
            //
            // A "completed" status is not overridden: it was written by the compile that just ran and
            // lists exactly what that compile produced. The native flag still reads true for a moment
            // after a fixing compile finishes — it clears with the domain reload that follows — so
            // trusting it there would report a build that has just been fixed as broken.
            var carriesNoEvidence = status.Status == "idle" || status.Status == "up_to_date";
            if (status.CompilationFailed && carriesNoEvidence && !status.Failed)
            {
                status.Status = "completed";
                status.Failed = true;
            }

            return status;
        }

        static void OnCompilationStarted(object _)
        {
            s_Errors.Clear();
            WriteStatus("compiling", false, null);
        }

        static void OnAssemblyCompilationFinished(string assembly, CompilerMessage[] messages)
        {
            if (messages == null) return;
            foreach (var m in messages)
                if (m.type == CompilerMessageType.Error)
                    s_Errors.Add(m.message);
        }

        static void OnCompilationFinished(object _)
        {
            // Written before the domain reload, so it persists for pollers after the reload.
            WriteStatus("completed", s_Errors.Count > 0, s_Errors.ToArray());
        }

        static RecompileStatusPayload ReadStatusOrIdle()
        {
            try
            {
                if (File.Exists(StatusFile))
                {
                    var payload = JsonConvert.DeserializeObject<RecompileStatusPayload>(File.ReadAllText(StatusFile));
                    if (payload != null)
                    {
                        payload.Errors = payload.Errors ?? Array.Empty<string>();
                        return payload;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recompile] Failed to read status file: {ex.Message}");
            }

            return new RecompileStatusPayload { Status = "idle", Errors = Array.Empty<string>() };
        }

        static void WriteStatus(string status, bool failed, string[] errors)
        {
            try
            {
                var payload = new RecompileStatusPayload
                {
                    Status = status,
                    Failed = failed,
                    Errors = errors ?? Array.Empty<string>()
                };
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(payload));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Recompile] Failed to write status file: {ex.Message}");
            }
        }
    }
}
