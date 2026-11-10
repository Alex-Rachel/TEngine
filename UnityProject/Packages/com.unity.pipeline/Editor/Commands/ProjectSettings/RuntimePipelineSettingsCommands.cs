using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Config;
using Unity.Pipeline.Editor.BuildProcessors;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set the Pipeline Runtime settings (Project Settings > Pipeline > Runtime), i.e.
    /// RuntimePipelineConfig: which build types compile the Pipeline (scripting define symbols, not
    /// config fields), its port, timeouts, audit logging, auto-start, and per-frame work item
    /// budget. This is the structured equivalent of what a human does through that Project Settings page — the config has no
    /// scene GameObject or asset to script against directly (see RuntimePipelineConfig.Load/Save).
    /// </summary>
    static class RuntimePipelineSettingsCommands
    {
        const string Domain = "runtime_pipeline";
        const string Group = "settings." + Domain;

        // Must stay in sync with RuntimePipelineConfig's own enforcement: OnValidate() clamps port
        // to this same range, and maxWorkItemsPerFrame carries a matching [Range]
        // attribute — but neither is enforced on a direct field set like Apply() below performs,
        // so Set() must check them itself. The 7900-7999 "recommended" port range is deliberately
        // NOT enforced here: that's Validate()'s job as a build-time warning, not a hard rejection.
        const int MinPort = 0, MaxPort = 65535;
        const int MinMaxWorkItemsPerFrame = 1, MaxMaxWorkItemsPerFrame = 50;

        [CliGroupCommand(Group, "Read Pipeline Runtime settings (enable-in-development-builds, enable-in-release-builds, port, enable-audit-logging, auto-start, max-work-items-per-frame). Reports the settings in effect, falling back to built-in defaults when none have been authored; never creates the settings file. The two toggles are reported for the active build target.", MainThreadRequired = true, Tags = new[] { "settings/runtime_pipeline" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Change Pipeline Runtime settings. Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z. Refused while in Play Mode: the running driver already loaded its own settings, so a change would silently not apply until the next Play session or build. port and max-work-items-per-frame are rejected outright if outside the same bounds the Project Settings page enforces.", MainThreadRequired = true, Tags = new[] { "settings/runtime_pipeline" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] RuntimePipelineSettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            if (settings.HasLegacyEnableInBuilds)
            {
                return ProjectSettingsResponse.Fail(Domain,
                    "'enableInBuilds' was removed. Use 'enable-in-development-builds' and 'enable-in-release-builds' instead.");
            }

            if (EditorApplication.isPlaying)
            {
                return ProjectSettingsResponse.Fail(Domain,
                    "Refused: Pipeline Runtime settings are read-only while in Play Mode — the running " +
                    "driver already loaded its own settings, so a change here would silently not apply " +
                    "until the next Play session or build.");
            }

            var violations = new List<string>();
            if (settings.Port.HasValue && (settings.Port.Value < MinPort || settings.Port.Value > MaxPort))
                violations.Add($"port {settings.Port.Value} out of range ({MinPort}..{MaxPort}).");
            if (settings.MaxWorkItemsPerFrame.HasValue && (settings.MaxWorkItemsPerFrame.Value < MinMaxWorkItemsPerFrame || settings.MaxWorkItemsPerFrame.Value > MaxMaxWorkItemsPerFrame))
                violations.Add($"max-work-items-per-frame {settings.MaxWorkItemsPerFrame.Value} out of range ({MinMaxWorkItemsPerFrame}..{MaxMaxWorkItemsPerFrame}).");
            if (violations.Count > 0)
                return ProjectSettingsResponse.Fail(Domain, string.Join("; ", violations));

            var config = RuntimePipelineSettingsProvider.LoadOrCreateConfig();
            try
            {
                var changes = new List<string>();

                // Against every installed target, not just the active one: the settings page
                // supports targets disagreeing, so a request the active target already satisfies
                // can still have work to do elsewhere.
                var developmentTargets = TargetsToRetarget(settings.EnableInDevelopmentBuilds, PipelineBuildSymbols.Development);
                var releaseTargets = TargetsToRetarget(settings.EnableInReleaseBuilds, PipelineBuildSymbols.Release);

                if (developmentTargets.Length > 0)
                    changes.Add($"enable-in-development-builds -> {settings.EnableInDevelopmentBuilds.Value} for {string.Join(", ", developmentTargets)}");
                if (releaseTargets.Length > 0)
                    changes.Add($"enable-in-release-builds -> {settings.EnableInReleaseBuilds.Value} for {string.Join(", ", releaseTargets)}");

                var changesBeforeConfigFields = changes.Count;
                if (settings.Port.HasValue && settings.Port.Value != config.port)
                    changes.Add($"port {config.port} -> {settings.Port.Value}");
                if (settings.EnableAuditLogging.HasValue && settings.EnableAuditLogging.Value != config.enableAuditLogging)
                    changes.Add($"enable-audit-logging {config.enableAuditLogging} -> {settings.EnableAuditLogging.Value}");
                if (settings.AutoStart.HasValue && settings.AutoStart.Value != config.autoStart)
                    changes.Add($"auto-start {config.autoStart} -> {settings.AutoStart.Value}");
                if (settings.MaxWorkItemsPerFrame.HasValue && settings.MaxWorkItemsPerFrame.Value != config.maxWorkItemsPerFrame)
                    changes.Add($"max-work-items-per-frame {config.maxWorkItemsPerFrame} -> {settings.MaxWorkItemsPerFrame.Value}");

                var configChanged = changes.Count > changesBeforeConfigFields;

                if (changes.Count == 0)
                    return NoChanges();

                var planText = "Set Pipeline Runtime settings: " + string.Join("; ", changes);

                void Apply()
                {
                    if (settings.EnableInDevelopmentBuilds.HasValue)
                        PipelineDefineSymbols.SetForAllTargets(PipelineBuildSymbols.Development, settings.EnableInDevelopmentBuilds.Value);
                    if (settings.EnableInReleaseBuilds.HasValue)
                        PipelineDefineSymbols.SetForAllTargets(PipelineBuildSymbols.Release, settings.EnableInReleaseBuilds.Value);
                    if (settings.EnableInReleaseBuilds == true)
                        RuntimePipelineSettingsMigration.MarkCurrentVersion();
                    if (settings.Port.HasValue) config.port = settings.Port.Value;
                    if (settings.EnableAuditLogging.HasValue) config.enableAuditLogging = settings.EnableAuditLogging.Value;
                    if (settings.AutoStart.HasValue) config.autoStart = settings.AutoStart.Value;
                    if (settings.MaxWorkItemsPerFrame.HasValue) config.maxWorkItemsPerFrame = settings.MaxWorkItemsPerFrame.Value;

                    // Only a real field change authors the settings file. Writing it for a
                    // toggle-only call would freeze today's defaults into the project, where they
                    // would outlive any later change to the package's own defaults.
                    if (configChanged)
                        config.Save();
                }

                // Changing a define recompiles the project, so the caller has to wait for the
                // domain reload before issuing the next command.
                var requiresDomainReload = developmentTargets.Length > 0 || releaseTargets.Length > 0;

                return ProjectSettingsCommand.Apply(Domain, confirm, dryRun, settings, () => planText, Apply, Read,
                    requiresDomainReload: requiresDomainReload);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        /// <summary>
        /// The installed targets whose define does not already match the request, empty when the
        /// caller did not ask for that toggle.
        /// </summary>
        /// <param name="requested">The requested state, or null when the toggle was not supplied.</param>
        /// <param name="symbol">The symbol backing the toggle.</param>
        static string[] TargetsToRetarget(bool? requested, string symbol)
        {
            return requested.HasValue
                ? PipelineDefineSymbols.TargetsWhereDiffers(symbol, requested.Value)
                : System.Array.Empty<string>();
        }

        static ProjectSettingsResponse NoChanges()
        {
            var response = ProjectSettingsCommand.Get(Domain, Read);
            response.Message = "No changes specified; nothing to apply.";
            return response;
        }

        static Dictionary<string, object> Read()
        {
            var config = RuntimePipelineSettingsProvider.LoadOrCreateConfig();
            try
            {
                return new Dictionary<string, object>
                {
                    ["enable-in-development-builds"] = PipelineBuildSymbols.IsDefinedForActiveTarget(PipelineBuildSymbols.Development),
                    ["enable-in-release-builds"] = PipelineBuildSymbols.IsDefinedForActiveTarget(PipelineBuildSymbols.Release),
                    ["port"] = config.port,
                    ["enable-audit-logging"] = config.enableAuditLogging,
                    ["auto-start"] = config.autoStart,
                    ["max-work-items-per-frame"] = config.maxWorkItemsPerFrame
                };
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }

    /// <summary>Pipeline Runtime settings to change. Null/omitted fields are left unchanged.</summary>
    class RuntimePipelineSettingsInput : IStructuredCommandInput
    {
        [CliArg("enable-in-development-builds", "Compile the Pipeline into development builds. Writes a scripting define symbol for every installed build target, so it triggers a recompile.")]
        public bool? EnableInDevelopmentBuilds { get; set; }

        [CliArg("enable-in-release-builds", "Compile the Pipeline into release builds. SECURITY WARNING: a release build carrying the Pipeline exposes code execution over a local HTTP server.")]
        public bool? EnableInReleaseBuilds { get; set; }

        [CliArg("port", "HTTP port for the Pipeline server. 0 = auto-assign from range 7900-7999.")]
        public int? Port { get; set; }

        [CliArg("enable-audit-logging", "Log every remote request for security auditing.")]
        public bool? EnableAuditLogging { get; set; }

        [CliArg("auto-start", "Start the server automatically when the Player boots (or Play Mode is entered).")]
        public bool? AutoStart { get; set; }

        [CliArg("max-work-items-per-frame", "Maximum work items the dispatcher processes per frame.")]
        public int? MaxWorkItemsPerFrame { get; set; }

        // Private, so the schema does not advertise it; Newtonsoft still fills it.
        [JsonExtensionData]
        private IDictionary<string, JToken> m_UnrecognizedFields = null;

        internal bool HasLegacyEnableInBuilds =>
            m_UnrecognizedFields != null && m_UnrecognizedFields.ContainsKey("enableInBuilds");
    }
}
