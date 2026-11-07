using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline.Editor.Commands.Build
{
    /// <summary>
    /// One entry of <c>list_build_targets</c> (CLI-204): a <c>BuildTarget</c> enum value with the
    /// information an agent needs to choose a valid target without guessing — its group and whether the
    /// platform's build support is actually installed in this editor.
    /// </summary>
    [Serializable]
    class BuildTargetInfo
    {
        /// <summary><c>BuildTarget</c> enum name, e.g. "StandaloneWindows64".</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>Friendly label, e.g. "Windows 64-bit".</summary>
        [JsonProperty("displayName")] public string DisplayName { get; set; }

        /// <summary><c>BuildTargetGroup</c> enum name, e.g. "Standalone".</summary>
        [JsonProperty("targetGroup")] public string TargetGroup { get; set; }

        /// <summary><c>BuildPipeline.IsBuildTargetSupported(group, target)</c> — module installed and usable.</summary>
        [JsonProperty("isInstalled")] public bool IsInstalled { get; set; }
    }

    /// <summary>One scene in the Build Settings scene list, projected for <c>get_build_settings</c>.</summary>
    [Serializable]
    class BuildSceneEntry
    {
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("guid")] public string Guid { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
    }

    /// <summary>
    /// Result of <c>get_build_settings</c> (CLI-204): the current build configuration read from
    /// <c>EditorUserBuildSettings</c> / <c>EditorBuildSettings</c> plus the active target's IL2CPP code
    /// generation mode. Scene-list management lives in <c>add_scene_to_build</c> /
    /// <c>remove_scene_from_build</c> (CLI-189); this only reports the list.
    /// </summary>
    [Serializable]
    class BuildSettingsResult
    {
        [JsonProperty("active-build-target")] public string ActiveBuildTarget { get; set; }
        [JsonProperty("active-build-target-group")] public string ActiveBuildTargetGroup { get; set; }
        [JsonProperty("development-build")] public bool DevelopmentBuild { get; set; }
        [JsonProperty("allow-debugging")] public bool AllowDebugging { get; set; }
        [JsonProperty("connect-with-profiler")] public bool ConnectWithProfiler { get; set; }
        [JsonProperty("build-scripts-only")] public bool BuildScriptsOnly { get; set; }
        [JsonProperty("symlink-sources")] public bool SymlinkSources { get; set; }

        /// <summary>"OptimizeSpeed" | "OptimizeSize" for the active build target.</summary>
        [JsonProperty("il2cpp-code-generation")] public string Il2CppCodeGeneration { get; set; }

        [JsonProperty("scenes")] public List<BuildSceneEntry> Scenes { get; set; }
    }

    /// <summary>
    /// Mutable build-settings fields for <c>set_build_settings</c> (CLI-204). Every field is nullable;
    /// only the ones the caller supplies are changed. Note that <see cref="AllowDebugging"/> and
    /// <see cref="ConnectWithProfiler"/> only take effect when <see cref="DevelopmentBuild"/> is also on.
    /// </summary>
    class SetBuildSettingsInput : IStructuredCommandInput
    {
        [CliArg("development-build", "Build a Development Player (enables the debugger/profiler).")]
        public bool? DevelopmentBuild { get; set; }

        [CliArg("allow-debugging", "Allow script debugging (only effective with development-build=true).")]
        public bool? AllowDebugging { get; set; }

        [CliArg("connect-with-profiler", "Auto-connect the Profiler (only effective with development-build=true).")]
        public bool? ConnectWithProfiler { get; set; }

        [CliArg("build-scripts-only", "Build only the scripts (skip data) for faster iteration.")]
        public bool? BuildScriptsOnly { get; set; }

        [CliArg("symlink-sources", "Symlink runtime/plugin sources instead of copying (where supported).")]
        public bool? SymlinkSources { get; set; }

        [CliArg("il2cpp-code-generation", "IL2CPP code generation for the active target: OptimizeSpeed | OptimizeSize.")]
        public string Il2CppCodeGeneration { get; set; }
    }

    /// <summary>
    /// Result of <c>set_build_settings</c> (CLI-204): the fields that were actually changed
    /// (<see cref="Applied"/>) versus the supplied fields that already had the requested value
    /// (<see cref="Skipped"/>). A dry run reports the same <see cref="Applied"/> set it <em>would</em>
    /// write, without changing anything.
    /// </summary>
    [Serializable]
    class SetBuildSettingsResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("dryRun")] public bool DryRun { get; set; }

        /// <summary>Field name → new value, for each field that changed (or would change on a dry run).</summary>
        [JsonProperty("applied")] public Dictionary<string, object> Applied { get; set; } = new Dictionary<string, object>();

        /// <summary>Field name → current value, for supplied fields that already matched (no-ops).</summary>
        [JsonProperty("skipped")] public Dictionary<string, object> Skipped { get; set; } = new Dictionary<string, object>();

        [JsonProperty("message")] public string Message { get; set; }

        public static SetBuildSettingsResult Fail(string message) =>
            new SetBuildSettingsResult { Success = false, Message = message };
    }

    /// <summary>One Build Profile asset, projected for <c>list_build_profiles</c> (Unity 6 only).</summary>
    [Serializable]
    class BuildProfileInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("guid")] public string Guid { get; set; }

        /// <summary>The profile's build target name (best-effort; may be empty if unreadable).</summary>
        [JsonProperty("platform")] public string Platform { get; set; }

        [JsonProperty("isActive")] public bool IsActive { get; set; }
    }

    /// <summary>
    /// One platform a Build Profile can target, projected for <c>list_build_platforms</c> (AUTHAPI-82).
    /// Platforms are not one-to-one with <c>BuildTarget</c>: Meta Quest, Android XR and Android all report
    /// <c>BuildTarget.Android</c>, with the first two flagged <see cref="IsDerived"/>. Pass <see cref="Id"/>
    /// (or an exact <see cref="DisplayName"/>) to <c>create_build_profile</c> to select one unambiguously.
    /// </summary>
    [Serializable]
    class BuildPlatformInfo
    {
        /// <summary>Display name as the Build Profile window shows it, e.g. "Meta Quest".</summary>
        [JsonProperty("displayName")] public string DisplayName { get; set; }

        /// <summary>Platform GUID, e.g. "80657fe557de4d17822398b3a01b8c9e" for Meta Quest.</summary>
        [JsonProperty("id")] public string Id { get; set; }

        /// <summary>The underlying <c>BuildTarget</c> name (best-effort; may be empty if unreadable).</summary>
        [JsonProperty("buildTarget")] public string BuildTarget { get; set; }

        /// <summary>"Player" | "Server" for standalone desktop platforms; "Default" elsewhere.</summary>
        [JsonProperty("subtarget")] public string Subtarget { get; set; }

        /// <summary>Whether the platform's build support module is installed in this editor.</summary>
        [JsonProperty("isInstalled")] public bool IsInstalled { get; set; }

        /// <summary>Whether this platform is derived from a base platform (e.g. Meta Quest from Android).</summary>
        [JsonProperty("isDerived")] public bool IsDerived { get; set; }
    }

    /// <summary>
    /// Result of <c>create_build_profile</c> (AUTHAPI-82). A dry run reports the <see cref="AssetPath"/> and
    /// <see cref="Platform"/> it validated without writing anything, so <see cref="Profile"/> is only present
    /// once the asset exists.
    /// </summary>
    [Serializable]
    class CreateBuildProfileResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("dryRun")] public bool DryRun { get; set; }

        /// <summary>Project-relative path the profile was (or would be) created at.</summary>
        [JsonProperty("assetPath")] public string AssetPath { get; set; }

        /// <summary>The resolved target platform.</summary>
        [JsonProperty("platform")] public BuildPlatformInfo Platform { get; set; }

        /// <summary>The created profile asset; omitted on a dry run.</summary>
        [JsonProperty("profile", NullValueHandling = NullValueHandling.Ignore)] public BuildProfileInfo Profile { get; set; }

        /// <summary>Whether the new profile was made the active Build Profile.</summary>
        [JsonProperty("activated")] public bool Activated { get; set; }

        [JsonProperty("message")] public string Message { get; set; }
    }

    /// <summary>
    /// Result of <c>edit_build_profile</c> (AUTHAPI-82), shaped like <see cref="SetBuildSettingsResult"/>:
    /// the settings that changed (<see cref="Applied"/>, new values) versus the supplied settings already at
    /// the requested value (<see cref="Skipped"/>, current values). A dry run reports the same
    /// <see cref="Applied"/> set it <em>would</em> write, without changing anything.
    /// </summary>
    [Serializable]
    class EditBuildProfileResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("dryRun")] public bool DryRun { get; set; }

        /// <summary>Project-relative path of the edited profile.</summary>
        [JsonProperty("assetPath")] public string AssetPath { get; set; }

        /// <summary>The profile after the edit (before it, on a dry run).</summary>
        [JsonProperty("profile")] public BuildProfileInfo Profile { get; set; }

        /// <summary>Setting name → new value, for each setting that changed (or would change on a dry run).</summary>
        [JsonProperty("applied")] public Dictionary<string, object> Applied { get; set; } = new Dictionary<string, object>();

        /// <summary>Setting name → current value, for supplied settings that already matched (no-ops).</summary>
        [JsonProperty("skipped")] public Dictionary<string, object> Skipped { get; set; } = new Dictionary<string, object>();

        [JsonProperty("message")] public string Message { get; set; }
    }

    /// <summary>One output file produced by the build (a row of <c>BuildReport.GetFiles()</c>).</summary>
    [Serializable]
    class BuildFileEntry
    {
        [JsonProperty("path")] public string Path { get; set; }

        /// <summary><c>BuildFile.role</c> verbatim, e.g. "MainData", "AssetBundle", "BootConfig".</summary>
        [JsonProperty("role")] public string Role { get; set; }

        [JsonProperty("sizeBytes")] public long SizeBytes { get; set; }
    }

    /// <summary>One asset packed into a build bundle/file (a row of a <c>PackedAssets</c> entry).</summary>
    [Serializable]
    class PackedAssetContent
    {
        [JsonProperty("assetPath")] public string AssetPath { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("guid")] public string Guid { get; set; }
        [JsonProperty("sizeBytes")] public long SizeBytes { get; set; }
    }

    /// <summary>A packed bundle/file with its per-asset size breakdown (requires DetailedBuildReport).</summary>
    [Serializable]
    class PackedAssetEntry
    {
        [JsonProperty("bundlePath")] public string BundlePath { get; set; }
        [JsonProperty("overheadBytes")] public long OverheadBytes { get; set; }
        [JsonProperty("contents")] public List<PackedAssetContent> Contents { get; set; }
    }

    /// <summary>One message emitted during a build step.</summary>
    [Serializable]
    class BuildStepMessageEntry
    {
        /// <summary>"Log" | "Warning" | "Error" (mapped from <c>LogType</c>).</summary>
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("content")] public string Content { get; set; }
    }

    /// <summary>One step of the build (a row of <c>BuildReport.steps</c>).</summary>
    [Serializable]
    class BuildStepEntry
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("durationMs")] public long DurationMs { get; set; }
        [JsonProperty("depth")] public int Depth { get; set; }
        [JsonProperty("messages")] public List<BuildStepMessageEntry> Messages { get; set; }
    }

    /// <summary>A build error/warning surfaced in <c>build_status</c>, with best-effort source file.</summary>
    [Serializable]
    class BuildIssue
    {
        [JsonProperty("message")] public string Message { get; set; }

        /// <summary>Source file when the message carries a "File.cs(line,col): ..." prefix; omitted otherwise.</summary>
        [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore)] public string File { get; set; }

        /// <summary>Build a <see cref="BuildIssue"/>, reusing <see cref="BuildMessage.Parse"/> for file extraction.</summary>
        public static BuildIssue From(string content)
        {
            var parsed = BuildMessage.Parse("info", content);
            return new BuildIssue
            {
                Message = parsed.Message,
                File = string.IsNullOrEmpty(parsed.File) ? null : parsed.File
            };
        }
    }

    /// <summary>
    /// Full <c>build_status</c> result for a finished build (CLI-204) — a JSON-friendly projection of
    /// <c>UnityEditor.Build.Reporting.BuildReport</c>. Optional collections are omitted when null
    /// (<see cref="NullValueHandling"/>), so a failed build can carry errors/steps without empty
    /// file/asset arrays. Transient states (queued/building/idle/dry-run) are reported as small,
    /// purpose-built payloads, not this type.
    /// </summary>
    [Serializable]
    class BuildReportResult
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("buildId")] public string BuildId { get; set; }

        /// <summary>"Succeeded" | "Failed" | "Cancelled" | "Unknown".</summary>
        [JsonProperty("result")] public string Result { get; set; }

        [JsonProperty("platform")] public string Platform { get; set; }
        [JsonProperty("outputPath")] public string OutputPath { get; set; }
        [JsonProperty("totalSizeBytes")] public long TotalSizeBytes { get; set; }
        [JsonProperty("buildTimeMs")] public long BuildTimeMs { get; set; }

        [JsonProperty("buildStartedAt", NullValueHandling = NullValueHandling.Ignore)] public string BuildStartedAt { get; set; }
        [JsonProperty("buildEndedAt", NullValueHandling = NullValueHandling.Ignore)] public string BuildEndedAt { get; set; }

        [JsonProperty("totalWarnings")] public int TotalWarnings { get; set; }
        [JsonProperty("totalErrors")] public int TotalErrors { get; set; }

        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)] public List<BuildFileEntry> Files { get; set; }
        [JsonProperty("packedAssets", NullValueHandling = NullValueHandling.Ignore)] public List<PackedAssetEntry> PackedAssets { get; set; }
        [JsonProperty("buildSteps", NullValueHandling = NullValueHandling.Ignore)] public List<BuildStepEntry> BuildSteps { get; set; }

        [JsonProperty("errors")] public List<BuildIssue> Errors { get; set; } = new List<BuildIssue>();
        [JsonProperty("warnings")] public List<BuildIssue> Warnings { get; set; } = new List<BuildIssue>();
    }

    /// <summary>
    /// A single build error/warning with best-effort file/line parsed from the message content.
    /// Retained from the original <c>build</c> stub (CLI-127): it is the shared parser used to extract a
    /// source location from a raw build-step message and is exercised directly by the test suite.
    /// </summary>
    [Serializable]
    class BuildMessage
    {
        [JsonProperty("severity")] public string Severity { get; set; }
        [JsonProperty("file")] public string File { get; set; }
        [JsonProperty("line")] public int Line { get; set; }
        [JsonProperty("message")] public string Message { get; set; }

        // Matches the standard compiler/build message prefix "Path/To/File.cs(line,col): rest".
        static readonly Regex s_LocationPattern = new Regex(@"^(?<file>.*?)\((?<line>\d+),\d+\):\s*(?<rest>.*)$", RegexOptions.Singleline);

        /// <summary>
        /// Build a <see cref="BuildMessage"/> from a raw build-step message, extracting file/line when
        /// the content carries the standard "File.cs(line,col): ..." prefix; otherwise the whole content
        /// is kept as the message with no file/line.
        /// </summary>
        public static BuildMessage Parse(string severity, string content)
        {
            var msg = new BuildMessage { Severity = severity, Message = content ?? string.Empty };

            if (!string.IsNullOrEmpty(content))
            {
                var match = s_LocationPattern.Match(content);
                if (match.Success)
                {
                    msg.File = match.Groups["file"].Value.Trim();
                    if (int.TryParse(match.Groups["line"].Value, out var line))
                        msg.Line = line;
                    msg.Message = match.Groups["rest"].Value.Trim();
                }
            }

            return msg;
        }
    }
}
