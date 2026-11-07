using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set the legacy Input Manager (CLI-202). The InputManager has no scripting API, so this
    /// reads/writes <c>ProjectSettings/InputManager.asset</c> via serialized properties:
    /// <c>get</c> lists the configured axis names; <c>set</c> tunes a named axis's
    /// sensitivity/gravity/dead values.
    ///
    /// "Input (legacy / Input System where feasible)" per the ticket: the legacy InputManager is
    /// always present, so it is the portable target. Projects on the new Input System package store
    /// their config in a separate input-actions asset, which would be a follow-up.
    /// </summary>
    static class InputSettingsCommands
    {
        const string Domain = "input";
        const string Group = "settings." + Domain;
        const string AssetPath = "ProjectSettings/InputManager.asset";

        [CliGroupCommand(Group, "Read the legacy Input Manager axes (names and count).", MainThreadRequired = true, Tags = new[] { "settings/input" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Tune a legacy Input Manager axis (sensitivity/gravity/dead) by name. Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z.", MainThreadRequired = true, Tags = new[] { "settings/input" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Axis change. 'axis' selects the axis by name; omitted numeric fields are left unchanged.")] InputAxisInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");
            if (string.IsNullOrEmpty(settings.Axis))
                return ProjectSettingsResponse.Fail(Domain, "'axis' is required.");

            var so = ProjectSettingsAsset.Load(AssetPath);
            var axes = so.FindProperty("m_Axes");
            var index = FindAxisIndex(axes, settings.Axis);
            if (index < 0)
                return ProjectSettingsResponse.Fail(Domain, $"No axis named '{settings.Axis}'.");

            var axis = axes.GetArrayElementAtIndex(index);
            var changes = new List<string>();
            AddFloatChange(axis, "sensitivity", settings.Sensitivity, changes);
            AddFloatChange(axis, "gravity", settings.Gravity, changes);
            AddFloatChange(axis, "dead", settings.Dead, changes);

            if (changes.Count == 0)
                return NoChanges();

            var planText = $"Set input axis '{settings.Axis}': " + string.Join("; ", changes);

            void Apply()
            {
                var obj = ProjectSettingsAsset.Load(AssetPath);
                var arr = obj.FindProperty("m_Axes");
                var idx = FindAxisIndex(arr, settings.Axis);
                if (idx < 0)
                    return;

                var ax = arr.GetArrayElementAtIndex(idx);
                SetFloat(ax, "sensitivity", settings.Sensitivity);
                SetFloat(ax, "gravity", settings.Gravity);
                SetFloat(ax, "dead", settings.Dead);
                obj.ApplyModifiedProperties();
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

        static int FindAxisIndex(SerializedProperty axes, string name)
        {
            if (axes == null)
                return -1;
            for (var i = 0; i < axes.arraySize; i++)
            {
                var nameProp = axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name");
                if (nameProp != null && nameProp.stringValue == name)
                    return i;
            }
            return -1;
        }

        static void AddFloatChange(SerializedProperty axis, string prop, float? newValue, List<string> changes)
        {
            if (!newValue.HasValue)
                return;
            var p = axis.FindPropertyRelative(prop);
            if (p == null)
                return;
            if (!Mathf.Approximately(p.floatValue, newValue.Value))
                changes.Add($"{prop} {p.floatValue} -> {newValue.Value}");
        }

        static void SetFloat(SerializedProperty axis, string prop, float? value)
        {
            if (!value.HasValue)
                return;
            var p = axis.FindPropertyRelative(prop);
            if (p != null)
                p.floatValue = value.Value;
        }

        static Dictionary<string, object> Read()
        {
            var so = ProjectSettingsAsset.Load(AssetPath);
            var axes = so.FindProperty("m_Axes");
            var names = new List<string>();
            if (axes != null)
            {
                for (var i = 0; i < axes.arraySize; i++)
                {
                    var nameProp = axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name");
                    names.Add(nameProp != null ? nameProp.stringValue : null);
                }
            }

            return new Dictionary<string, object>
            {
                ["axis-count"] = names.Count,
                ["axes"] = names
            };
        }
    }

    /// <summary>Selects a legacy input axis by name and the numeric fields to change (omitted = unchanged).</summary>
    class InputAxisInput : IStructuredCommandInput
    {
        [CliArg("axis", "Name of the axis to modify (e.g. 'Horizontal').", Required = true)]
        public string Axis { get; set; }

        [CliArg("sensitivity", "New sensitivity.")]
        public float? Sensitivity { get; set; }

        [CliArg("gravity", "New gravity.")]
        public float? Gravity { get; set; }

        [CliArg("dead", "New dead-zone size.")]
        public float? Dead { get; set; }
    }
}
