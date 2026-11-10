using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Build;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set a representative slice of <see cref="PlayerSettings"/> (CLI-202): identity
    /// (company/product/version) and the two domain-reload-triggering toggles — scripting backend and
    /// API compatibility level. Scripting-backend / API-level changes are read and written against the
    /// active build target's <see cref="NamedBuildTarget"/>.
    ///
    /// Icons and splash are intentionally out of this MVP set: they take <c>Texture2D</c> assets, not
    /// scalar JSON, so they need a dedicated asset-reference convention.
    /// </summary>
    static class PlayerSettingsCommands
    {
        const string Domain = "player";
        const string Group = "settings." + Domain;

        [CliGroupCommand(Group, "Read PlayerSettings (company/product/version, scripting backend, API level).", MainThreadRequired = true, Tags = new[] { "settings/player" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Change PlayerSettings. Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z. Scripting backend / API level changes trigger a domain reload.", MainThreadRequired = true, Tags = new[] { "settings/player" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] PlayerSettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var changes = new List<string>();
            var reload = false;

            if (settings.CompanyName != null && settings.CompanyName != PlayerSettings.companyName)
                changes.Add($"company-name '{PlayerSettings.companyName}' -> '{settings.CompanyName}'");
            if (settings.ProductName != null && settings.ProductName != PlayerSettings.productName)
                changes.Add($"product-name '{PlayerSettings.productName}' -> '{settings.ProductName}'");
            if (settings.BundleVersion != null && settings.BundleVersion != PlayerSettings.bundleVersion)
                changes.Add($"bundle-version '{PlayerSettings.bundleVersion}' -> '{settings.BundleVersion}'");

            if (settings.ScriptingBackend.HasValue)
            {
                var current = PlayerSettings.GetScriptingBackend(target);
                if (settings.ScriptingBackend.Value != current)
                {
                    changes.Add($"scripting-backend {current} -> {settings.ScriptingBackend.Value} (domain reload)");
                    reload = true;
                }
            }

            if (settings.ApiCompatibilityLevel.HasValue)
            {
                var current = PlayerSettings.GetApiCompatibilityLevel(target);
                if (settings.ApiCompatibilityLevel.Value != current)
                {
                    changes.Add($"api-compatibility-level {current} -> {settings.ApiCompatibilityLevel.Value} (domain reload)");
                    reload = true;
                }
            }

            if (changes.Count == 0)
                return NoChanges();

            var planText = "Set player settings: " + string.Join("; ", changes);

            void Apply()
            {
                if (settings.CompanyName != null) PlayerSettings.companyName = settings.CompanyName;
                if (settings.ProductName != null) PlayerSettings.productName = settings.ProductName;
                if (settings.BundleVersion != null) PlayerSettings.bundleVersion = settings.BundleVersion;
                if (settings.ScriptingBackend.HasValue) PlayerSettings.SetScriptingBackend(target, settings.ScriptingBackend.Value);
                if (settings.ApiCompatibilityLevel.HasValue) PlayerSettings.SetApiCompatibilityLevel(target, settings.ApiCompatibilityLevel.Value);
                AssetDatabase.SaveAssets();
            }

            return ProjectSettingsCommand.Apply(Domain, confirm, dryRun, settings, () => planText, Apply, Read, requiresDomainReload: reload);
        }

        static ProjectSettingsResponse NoChanges()
        {
            var response = ProjectSettingsCommand.Get(Domain, Read);
            response.Message = "No changes specified; nothing to apply.";
            return response;
        }

        static Dictionary<string, object> Read()
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            return new Dictionary<string, object>
            {
                ["company-name"] = PlayerSettings.companyName,
                ["product-name"] = PlayerSettings.productName,
                ["bundle-version"] = PlayerSettings.bundleVersion,
                ["build-target"] = target.TargetName,
                ["scripting-backend"] = PlayerSettings.GetScriptingBackend(target).ToString(),
                ["api-compatibility-level"] = PlayerSettings.GetApiCompatibilityLevel(target).ToString()
            };
        }
    }

    /// <summary>Player settings to change. Null/omitted fields are left unchanged.</summary>
    class PlayerSettingsInput : IStructuredCommandInput
    {
        [CliArg("company-name", "Company name.")]
        public string CompanyName { get; set; }

        [CliArg("product-name", "Product name.")]
        public string ProductName { get; set; }

        [CliArg("bundle-version", "Bundle/application version string.")]
        public string BundleVersion { get; set; }

        [CliArg("scripting-backend", "Scripting backend (e.g. Mono2x, IL2CPP). Triggers a domain reload.")]
        public ScriptingImplementation? ScriptingBackend { get; set; }

        [CliArg("api-compatibility-level", "API compatibility level (e.g. NET_Standard_2_0, NET_Unity_4_8). Triggers a domain reload.")]
        public ApiCompatibilityLevel? ApiCompatibilityLevel { get; set; }
    }
}
