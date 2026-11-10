using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Build
{
    /// <summary>
    /// Triggers a Player build and reports the full <see cref="BuildReport"/> (CLI-204). Fills out the
    /// original <c>build</c>/<c>build_status</c> stub (CLI-127) with the complete param contract and the
    /// structured report shape so build failures can be diagnosed without the Editor UI.
    ///
    /// Why this is async (trigger-then-poll, mirroring <see cref="RecompileCommand"/> /
    /// <see cref="PackageManager.PackageManagerCommand"/>): <see cref="BuildPipeline.BuildPlayer(BuildPlayerOptions)"/>
    /// runs synchronously and freezes the main thread for the whole build (seconds to minutes). If
    /// <c>build</c> ran it inline it would block the server's (serial) request loop and the call could
    /// never return. Instead <c>build</c> validates + queues and returns <c>queued</c> immediately; an
    /// <see cref="EditorApplication.update"/> hook runs the build on the next tick. Poll <c>build_status</c>
    /// until <c>status == "completed"</c>.
    ///
    /// <c>build_status</c> is deliberately <c>MainThreadRequired = false</c>: while the build holds the
    /// main thread, any main-thread command (e.g. editor_status) would hang, but build_status only reads
    /// a Temp status file off-thread, so polling keeps working throughout the build. The status file also
    /// survives the domain reload a build can incur, so the last report is retained until the next build.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class BuildCommand
    {
        const string Group = "build";
        const string StatusFile = "Temp/pipeline_build_status.json";

        // BuildOptions that callers may pass by name (the supported set from the ticket). Parsed
        // case-insensitively; anything outside this set is a validation error rather than silently
        // tolerated, so a typo surfaces instead of changing the build shape.
        static readonly Dictionary<string, BuildOptions> s_SupportedOptions =
            new Dictionary<string, BuildOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["Development"] = BuildOptions.Development,
                ["AllowDebugging"] = BuildOptions.AllowDebugging,
                ["ConnectWithProfiler"] = BuildOptions.ConnectWithProfiler,
                // EnableHeadlessMode is obsolete in Unity 6 (superseded by StandaloneBuildSubtarget.Server)
                // but still functions and is part of this command's documented option set (CLI-204), so we
                // keep accepting it and suppress only the deprecation warning here.
#pragma warning disable 618
                ["EnableHeadlessMode"] = BuildOptions.EnableHeadlessMode,
#pragma warning restore 618
                ["SymlinkSources"] = BuildOptions.SymlinkSources,
                ["BuildAdditionalStreamedScenes"] = BuildOptions.BuildAdditionalStreamedScenes,
                ["CleanBuildCache"] = BuildOptions.CleanBuildCache,
                ["DetailedBuildReport"] = BuildOptions.DetailedBuildReport,
            };

        static readonly object s_Lock = new object();
        static bool s_Pending;
        static bool s_Building;

        // Queued request, captured under s_Lock by build() and consumed by the update pump.
        static BuildTarget s_PendingTarget;
        static string s_PendingOutput;
        static BuildOptions s_PendingOptions;

        /// <summary>
        /// BuildOptions of the scripted build currently inside BuildPipeline.BuildPlayer, or null
        /// when no `build` command is running. Build processors read it because a scripted build
        /// with BuildOptions.Development is a development build even when the Build Settings
        /// toggle (EditorUserBuildSettings.development) is off.
        /// </summary>
        internal static BuildOptions? ActiveScriptedBuildOptions { get; private set; }
        static string[] s_PendingScenes;
        static string s_PendingProfile;
        static string s_PendingBuildId;

        static BuildCommand()
        {
            EditorApplication.update += OnUpdate;
        }

        [CliGroupCommand(Group,
            "Trigger an async Player build and report the full BuildReport. Returns immediately (queued); " +
            "poll " + CommandNames.BuildStatus + " until status is 'completed'. DetailedBuildReport is included by default unless " +
            "'options' is supplied. Use dry-run to validate without building.",
            MainThreadRequired = false,
            Tags = new[] { "build" })]
        public static object Run(
            [CliArg("target", "BuildTarget name (e.g. StandaloneWindows64). Defaults to the active target. Must be installed.")] string target = "",
            [CliArg("output-path", "Output path (absolute, or relative to the project root). Defaults to the last/auto path.")] string outputPath = "",
            [CliArg("profile-name", "Build Profile name to activate before building (Unity 6 only; ignored otherwise).")] string profileName = "",
            [CliArg("options", "BuildOptions names. Omit to get just DetailedBuildReport; supplying any disables that default.")] string[] options = null,
            [CliArg("scenes", "Scene asset paths to build (e.g. Assets/Scenes/Main.unity). Defaults to EditorBuildSettings.")] string[] scenes = null,
            [CliArg("confirm", "Acknowledge and run the build; without it the call is refused. Use dry-run to validate only.")] bool confirm = false,
            [CliArg("dry-run", "Validate target/output-path/scenes without building.")] bool dryRun = false,
            [CliArg("timeout", "Seconds to wait for the build to be validated and queued (the build itself is polled via " + CommandNames.BuildStatus + ").")] double timeoutInSeconds = 60)
        {
            // Validation + queueing both touch Unity APIs, so run them on the main thread. When invoked
            // off-thread (the normal HTTP path) this marshals through the server dispatcher; in-process
            // (tests, no server) it runs inline.
            return RunOnMain<object>(() => ValidateAndQueue(target, outputPath, profileName, options, scenes, confirm, dryRun), PipelineTimeout.FromSeconds(timeoutInSeconds));
        }

        static object ValidateAndQueue(string targetArg, string outputArg, string profileName,
            string[] optionArgs, string[] sceneArgs, bool confirm, bool dryRun)
        {
            lock (s_Lock)
            {
                if (!dryRun && (s_Building || s_Pending))
                    return new { status = "busy", success = false, message = "A build is already queued or in progress. Poll " + CommandNames.BuildStatus + "." };
            }

            var errors = new List<object>();

            // ---- target ----
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (!string.IsNullOrWhiteSpace(targetArg))
            {
                if (!TryParseTarget(targetArg, out target))
                    errors.Add(Invalid("target", $"Unknown BuildTarget '{targetArg}'. Use a name from {CommandNames.BuildTargetList}."));
                else if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                    errors.Add(Invalid("target", $"Build target '{target}' is not installed (isInstalled=false in {CommandNames.BuildTargetList})."));
            }

            // ---- options ----
            var options = BuildOptions.None;
            if (optionArgs != null && optionArgs.Length > 0)
            {
                foreach (var name in optionArgs)
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (s_SupportedOptions.TryGetValue(name.Trim(), out var opt))
                        options |= opt;
                    else
                        errors.Add(Invalid("options", $"Unsupported BuildOptions value '{name}'."));
                }
            }
            else
            {
                // Strongly recommended: without it BuildReport.packedAssets is empty.
                options = BuildOptions.DetailedBuildReport;
            }

            // ---- scenes ----
            var scenes = (sceneArgs != null && sceneArgs.Length > 0)
                ? sceneArgs
                : EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

            if (sceneArgs != null)
            {
                foreach (var scene in sceneArgs)
                {
                    if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(scene)))
                        errors.Add(Invalid("scenes", $"No scene asset at '{scene}'."));
                }
            }

            // ---- output path ----
            // Non-mutating writability check: resolve the path and confirm an existing ancestor
            // directory exists (so the build's output folder can be created). BuildPipeline.BuildPlayer
            // creates the leaf directory itself, so we never create anything here — a dry run stays pure.
            string outputPath = null;
            try
            {
                outputPath = ResolveOutput(outputArg, target);
                if (!HasExistingAncestor(outputPath))
                    errors.Add(Invalid("output-path", $"No existing parent directory for '{outputPath}'."));
            }
            catch (Exception ex)
            {
                errors.Add(Invalid("output-path", $"Output path is invalid: {ex.Message}"));
            }

            if (dryRun)
            {
                return new
                {
                    status = "dry_run",
                    valid = errors.Count == 0,
                    validationErrors = errors
                };
            }

            if (errors.Count > 0)
            {
                // Fail immediately — do not queue (acceptance criteria 5).
                return new { status = "error", success = false, validationErrors = errors, message = "Build configuration is invalid; nothing was queued." };
            }

            if (!confirm)
                return new
                {
                    status = "error",
                    success = false,
                    message = "Refused: this triggers a player build. Pass confirm=true to build, or dry-run=true to validate without building."
                };

            var buildId = NewBuildId();

            lock (s_Lock)
            {
                s_PendingTarget = target;
                s_PendingOutput = outputPath;
                s_PendingOptions = options;
                s_PendingScenes = scenes;
                s_PendingProfile = profileName;
                s_PendingBuildId = buildId;
                s_Pending = true;
            }

            WriteStatusJson(new { status = "queued", buildId, message = "Build queued. Poll " + CommandNames.BuildStatus + " until status is 'completed'." });

            return new { status = "queued", buildId };
        }

        [CliGroupCommand(Group,
            "Status of the current/most recent build: idle | queued | building | completed, with the full " +
            "BuildReport (files, packedAssets, buildSteps, errors, warnings) once completed. Retained until the next build.",
            MainThreadRequired = false,
            Tags = new[] { "build" })]
        public static JToken Status()
        {
            var status = StatusFileReader.ReadOrIdle(StatusFile);

            // While building, recompute elapsedMs at read time so polling shows live progress. A file
            // that is not a JSON object cannot carry a status, so it passes through untouched.
            var doc = status as JObject;
            if (doc != null && (string)doc["status"] == "building")
            {
                long elapsed = 0;
                var startedStr = (string)doc["buildStartedAt"];
                if (!string.IsNullOrEmpty(startedStr) &&
                    DateTime.TryParse(startedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var started))
                {
                    elapsed = (long)Math.Max(0, (DateTime.UtcNow - started.ToUniversalTime()).TotalMilliseconds);
                }

                return new JObject
                {
                    ["status"] = "building",
                    ["buildId"] = (string)doc["buildId"],
                    ["elapsedMs"] = elapsed
                };
            }

            return status;
        }

        /// <summary>
        /// Main-thread pump: picks up a queued request and runs the (blocking) build. Guarded so a build
        /// runs at most once at a time.
        /// </summary>
        static void OnUpdate()
        {
            BuildTarget target;
            string output;
            BuildOptions options;
            string[] scenes;
            string profile;
            string buildId;

            lock (s_Lock)
            {
                if (!s_Pending || s_Building)
                    return;
                s_Pending = false;
                s_Building = true;
                target = s_PendingTarget;
                output = s_PendingOutput;
                options = s_PendingOptions;
                scenes = s_PendingScenes;
                profile = s_PendingProfile;
                buildId = s_PendingBuildId;
            }

            try
            {
                RunBuild(buildId, target, output, options, scenes, profile);
            }
            catch (Exception ex)
            {
                WriteStatusJson(new
                {
                    status = "completed",
                    buildId,
                    result = "Failed",
                    totalSizeBytes = 0,
                    errors = new[] { new { message = $"Build failed to start: {ex.Message}" } },
                    warnings = Array.Empty<object>()
                });
            }
            finally
            {
                lock (s_Lock) { s_Building = false; }
            }
        }

        static void RunBuild(string buildId, BuildTarget target, string outputPath, BuildOptions options,
            string[] scenes, string profileName)
        {
            var startedAt = DateTime.UtcNow;
            WriteStatusJson(new
            {
                status = "building",
                buildId,
                buildStartedAt = startedAt.ToString("o"),
                platform = target.ToString(),
                outputPath
            });

            // Best-effort Build Profile activation (Unity 6 only). Never fails the build.
            if (!string.IsNullOrWhiteSpace(profileName))
                TryActivateBuildProfile(profileName);

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = options
            };

            ActiveScriptedBuildOptions = options;
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(buildOptions); }
            finally { ActiveScriptedBuildOptions = null; }
            WriteStatusJson(Project(buildId, report));
        }

        // ---- BuildReport projection ------------------------------------------------------------

        static BuildReportResult Project(string buildId, BuildReport report)
        {
            var summary = report.summary;
            var success = summary.result == BuildResult.Succeeded;
            var started = summary.buildStartedAt;
            var ended = started + summary.totalTime;

            var result = new BuildReportResult
            {
                Status = "completed",
                BuildId = buildId,
                Result = summary.result.ToString(),
                Platform = summary.platform.ToString(),
                OutputPath = summary.outputPath,
                TotalSizeBytes = (long)summary.totalSize,
                BuildTimeMs = (long)summary.totalTime.TotalMilliseconds,
                BuildStartedAt = started.ToUniversalTime().ToString("o"),
                BuildEndedAt = ended.ToUniversalTime().ToString("o"),
                TotalWarnings = summary.totalWarnings,
                TotalErrors = summary.totalErrors,
                BuildSteps = ProjectSteps(report, out var errors, out var warnings),
                Errors = errors,
                Warnings = warnings
            };

            // Files / packed assets are only meaningful on success (and packedAssets needs DetailedBuildReport).
            if (success)
            {
                result.Files = ProjectFiles(report);
                result.PackedAssets = ProjectPackedAssets(report);
            }

            return result;
        }

        static List<BuildFileEntry> ProjectFiles(BuildReport report)
        {
            var files = new List<BuildFileEntry>();
            try
            {
                foreach (var f in report.GetFiles())
                    files.Add(new BuildFileEntry { Path = f.path, Role = f.role, SizeBytes = (long)f.size });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[pipeline] build file projection failed: {ex.Message}");
            }
            return files;
        }

        static List<PackedAssetEntry> ProjectPackedAssets(BuildReport report)
        {
            var packed = new List<PackedAssetEntry>();
            if (report.packedAssets == null)
                return packed;

            foreach (var bundle in report.packedAssets)
            {
                var entry = new PackedAssetEntry
                {
                    BundlePath = bundle.shortPath,
                    OverheadBytes = (long)bundle.overhead,
                    Contents = new List<PackedAssetContent>()
                };

                if (bundle.contents != null)
                {
                    foreach (var c in bundle.contents)
                    {
                        entry.Contents.Add(new PackedAssetContent
                        {
                            AssetPath = c.sourceAssetPath,
                            Type = c.type != null ? c.type.Name : null,
                            Guid = c.sourceAssetGUID.ToString(),
                            SizeBytes = (long)c.packedSize
                        });
                    }
                }

                packed.Add(entry);
            }
            return packed;
        }

        static List<BuildStepEntry> ProjectSteps(BuildReport report,
            out List<BuildIssue> errors, out List<BuildIssue> warnings)
        {
            errors = new List<BuildIssue>();
            warnings = new List<BuildIssue>();

            var steps = new List<BuildStepEntry>();
            if (report.steps == null)
                return steps;

            foreach (var step in report.steps)
            {
                var entry = new BuildStepEntry
                {
                    Name = step.name,
                    DurationMs = (long)step.duration.TotalMilliseconds,
                    Depth = (int)step.depth,
                    Messages = new List<BuildStepMessageEntry>()
                };

                if (step.messages != null)
                {
                    foreach (var m in step.messages)
                    {
                        entry.Messages.Add(new BuildStepMessageEntry { Type = MapLogType(m.type), Content = m.content });

                        if (m.type == LogType.Error || m.type == LogType.Assert || m.type == LogType.Exception)
                            errors.Add(BuildIssue.From(m.content));
                        else if (m.type == LogType.Warning)
                            warnings.Add(BuildIssue.From(m.content));
                    }
                }

                steps.Add(entry);
            }
            return steps;
        }

        static string MapLogType(LogType type)
        {
            switch (type)
            {
                case LogType.Warning: return "Warning";
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception: return "Error";
                default: return "Log";
            }
        }

        // ---- Helpers ---------------------------------------------------------------------------

        /// <summary>Case-insensitive parse of a <see cref="BuildTarget"/> enum name.</summary>
        static bool TryParseTarget(string name, out BuildTarget target)
        {
            try
            {
                target = (BuildTarget)Enum.Parse(typeof(BuildTarget), name.Trim(), ignoreCase: true);
                return Enum.IsDefined(typeof(BuildTarget), target);
            }
            catch
            {
                target = EditorUserBuildSettings.activeBuildTarget;
                return false;
            }
        }

        static object Invalid(string field, string message) => new { field, message };

        static string NewBuildId() => "build_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        static string ResolveOutput(string output, BuildTarget target)
        {
            var projectRoot = ProjectPaths.ProjectRoot;

            if (!string.IsNullOrWhiteSpace(output))
                return ProjectPaths.ResolveFile(output);

            var product = string.IsNullOrEmpty(PlayerSettings.productName) ? "Player" : PlayerSettings.productName;
            return Path.Combine(projectRoot, "Builds", target.ToString(), product + ExtensionFor(target));
        }

        /// <summary>True if some ancestor directory of <paramref name="path"/> already exists — a cheap,
        /// non-mutating check that the build output folder is creatable.</summary>
        static bool HasExistingAncestor(string path)
        {
            var dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(dir))
                    return true;
                dir = Path.GetDirectoryName(dir);
            }
            return false;
        }

        static string ExtensionFor(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                case BuildTarget.Android:
                    return ".apk";
                default:
                    return string.Empty; // Linux standalone, dedicated server, etc.
            }
        }

        /// <summary>
        /// Best-effort: activate a Build Profile by name via reflection so the assembly still compiles on
        /// Unity versions without the Build Profile API. Any failure is logged and ignored — the build
        /// proceeds with the current configuration.
        /// </summary>
        static void TryActivateBuildProfile(string profileName)
        {
            try
            {
                var info = BuildProfiles.FindByName(profileName);
                if (info == null)
                {
                    Debug.LogWarning($"[pipeline] {CommandNames.BuildRun}: profile '{profileName}' not found; building with current settings.");
                    return;
                }
                if (!BuildProfiles.TrySetActive(info))
                    Debug.LogWarning($"[pipeline] {CommandNames.BuildRun}: could not activate profile '{profileName}'; building with current settings.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[pipeline] {CommandNames.BuildRun}: profile activation failed ({ex.Message}); building with current settings.");
            }
        }

        /// <summary>
        /// Run <paramref name="fn"/> on the Unity main thread. Off-thread (the HTTP path) it marshals
        /// through the live server's dispatcher; on the main thread (a direct in-process/test call, or no
        /// server running) it runs inline. Mirrors <see cref="PackageManager.PackageManagerCommand"/>.
        /// </summary>
        static T RunOnMain<T>(Func<T> fn, TimeSpan timeout)
        {
            var dispatcher = PipelineServerStartup.Server?.Dispatcher;
            if (dispatcher != null && dispatcher.IsInitialized && !dispatcher.IsMainThread())
                return dispatcher.Invoke(fn, PipelineTimeout.StopBefore(timeout));
            return fn();
        }

        static void WriteStatusJson(object status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Build] Failed to write status file: {ex.Message}");
            }
        }
    }
}
