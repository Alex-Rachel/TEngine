using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set <see cref="QualitySettings"/> (CLI-202): the active quality level plus a couple of
    /// representative per-level toggles (vSync, anti-aliasing). Level changes apply expensive changes
    /// so the switch takes full effect.
    /// </summary>
    static class QualitySettingsCommands
    {
        const string Domain = "quality";
        const string Group = "settings." + Domain;

        [CliGroupCommand(Group, "Read QualitySettings (current level, level names, vSync, anti-aliasing).", MainThreadRequired = true, Tags = new[] { "settings/quality" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Change QualitySettings. Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z.", MainThreadRequired = true, Tags = new[] { "settings/quality" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] QualitySettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            var names = QualitySettings.names;
            var changes = new List<string>();

            if (settings.Level.HasValue)
            {
                if (settings.Level.Value < 0 || settings.Level.Value >= names.Length)
                    return ProjectSettingsResponse.Fail(Domain, $"level {settings.Level.Value} out of range (0..{names.Length - 1}).");
                if (settings.Level.Value != QualitySettings.GetQualityLevel())
                    changes.Add($"level {QualitySettings.GetQualityLevel()} -> {settings.Level.Value}");
            }

            if (settings.VSyncCount.HasValue && settings.VSyncCount.Value != QualitySettings.vSyncCount)
                changes.Add($"vsync-count {QualitySettings.vSyncCount} -> {settings.VSyncCount.Value}");
            if (settings.AntiAliasing.HasValue && settings.AntiAliasing.Value != QualitySettings.antiAliasing)
                changes.Add($"anti-aliasing {QualitySettings.antiAliasing} -> {settings.AntiAliasing.Value}");

            if (changes.Count == 0)
                return NoChanges();

            var planText = "Set quality settings: " + string.Join("; ", changes);

            void Apply()
            {
                if (settings.Level.HasValue) QualitySettings.SetQualityLevel(settings.Level.Value, true);
                if (settings.VSyncCount.HasValue) QualitySettings.vSyncCount = settings.VSyncCount.Value;
                if (settings.AntiAliasing.HasValue) QualitySettings.antiAliasing = settings.AntiAliasing.Value;
                AssetDatabase.SaveAssets();
            }

            return ProjectSettingsCommand.Apply(Domain, confirm, dryRun, settings, () => planText, Apply, Read);
        }

        static ProjectSettingsResponse NoChanges()
        {
            var response = ProjectSettingsCommand.Get(Domain, Read);
            response.Message = "No changes specified; nothing to apply.";
            return response;
        }

        static Dictionary<string, object> Read()
        {
            var names = QualitySettings.names;
            var level = QualitySettings.GetQualityLevel();
            return new Dictionary<string, object>
            {
                ["level"] = level,
                ["level-name"] = (level >= 0 && level < names.Length) ? names[level] : null,
                ["level-names"] = names,
                ["vsync-count"] = QualitySettings.vSyncCount,
                ["anti-aliasing"] = QualitySettings.antiAliasing
            };
        }
    }

    /// <summary>Quality settings to change. Null/omitted fields are left unchanged.</summary>
    class QualitySettingsInput : IStructuredCommandInput
    {
        [CliArg("level", "Quality level index (see level-names from " + CommandNames.SettingsQualityGet + ").")]
        public int? Level { get; set; }

        [CliArg("vsync-count", "VSync count (0 = off, 1, 2).")]
        public int? VSyncCount { get; set; }

        [CliArg("anti-aliasing", "MSAA sample count (0, 2, 4, 8).")]
        public int? AntiAliasing { get; set; }
    }
}
