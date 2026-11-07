using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set the project Time settings (CLI-202): fixed timestep, maximum allowed timestep, and the
    /// time scale.
    /// </summary>
    static class TimeSettingsCommands
    {
        const string Domain = "time";
        const string Group = "settings." + Domain;

        [CliGroupCommand(Group, "Read Time settings (fixed-delta-time, maximum-delta-time, time-scale).", MainThreadRequired = true, Tags = new[] { "settings/time" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Change Time settings. Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z.", MainThreadRequired = true, Tags = new[] { "settings/time" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] TimeSettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            var changes = new List<string>();

            if (settings.FixedDeltaTime.HasValue && !Mathf.Approximately(settings.FixedDeltaTime.Value, Time.fixedDeltaTime))
                changes.Add($"fixed-delta-time {Time.fixedDeltaTime} -> {settings.FixedDeltaTime.Value}");
            if (settings.MaximumDeltaTime.HasValue && !Mathf.Approximately(settings.MaximumDeltaTime.Value, Time.maximumDeltaTime))
                changes.Add($"maximum-delta-time {Time.maximumDeltaTime} -> {settings.MaximumDeltaTime.Value}");
            if (settings.TimeScale.HasValue && !Mathf.Approximately(settings.TimeScale.Value, Time.timeScale))
                changes.Add($"time-scale {Time.timeScale} -> {settings.TimeScale.Value}");

            if (changes.Count == 0)
                return NoChanges();

            var planText = "Set time settings: " + string.Join("; ", changes);

            void Apply()
            {
                if (settings.FixedDeltaTime.HasValue) Time.fixedDeltaTime = settings.FixedDeltaTime.Value;
                if (settings.MaximumDeltaTime.HasValue) Time.maximumDeltaTime = settings.MaximumDeltaTime.Value;
                if (settings.TimeScale.HasValue) Time.timeScale = settings.TimeScale.Value;
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

        static Dictionary<string, object> Read() => new Dictionary<string, object>
        {
            ["fixed-delta-time"] = Time.fixedDeltaTime,
            ["maximum-delta-time"] = Time.maximumDeltaTime,
            ["time-scale"] = Time.timeScale
        };
    }

    /// <summary>Time settings to change. Null/omitted fields are left unchanged.</summary>
    class TimeSettingsInput : IStructuredCommandInput
    {
        [CliArg("fixed-delta-time", "Fixed timestep in seconds (e.g. 0.02).")]
        public float? FixedDeltaTime { get; set; }

        [CliArg("maximum-delta-time", "Maximum allowed timestep in seconds.")]
        public float? MaximumDeltaTime { get; set; }

        [CliArg("time-scale", "Time scale (1 = real-time).")]
        public float? TimeScale { get; set; }
    }
}
