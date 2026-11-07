using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set the project Audio settings (CLI-202). The AudioManager has no scripting API, so this
    /// reads/writes its serialized properties (global volume, rolloff scale, doppler factor) via
    /// <see cref="ProjectSettingsAsset"/>. Properties absent in a given Unity version are skipped.
    /// </summary>
    static class AudioSettingsCommands
    {
        const string Domain = "audio";
        const string Group = "settings." + Domain;
        const string AssetPath = "ProjectSettings/AudioManager.asset";

        // Serialized property name -> response/plan label.
        const string VolumeProp = "m_Volume";
        const string RolloffProp = "Rolloff Scale";
        const string DopplerProp = "Doppler Factor";

        [CliGroupCommand(Group, "Read project Audio settings (volume, rolloff scale, doppler factor).", MainThreadRequired = true, Tags = new[] { "settings/audio" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Change project Audio settings. Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z.", MainThreadRequired = true, Tags = new[] { "settings/audio" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] AudioSettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            var so = ProjectSettingsAsset.Load(AssetPath);
            var changes = new List<string>();
            AddFloatChange(so, VolumeProp, "volume", settings.Volume, changes);
            AddFloatChange(so, RolloffProp, "rolloff-scale", settings.RolloffScale, changes);
            AddFloatChange(so, DopplerProp, "doppler-factor", settings.DopplerFactor, changes);

            if (changes.Count == 0)
                return NoChanges();

            var planText = "Set audio settings: " + string.Join("; ", changes);

            void Apply()
            {
                var obj = ProjectSettingsAsset.Load(AssetPath);
                SetFloat(obj, VolumeProp, settings.Volume);
                SetFloat(obj, RolloffProp, settings.RolloffScale);
                SetFloat(obj, DopplerProp, settings.DopplerFactor);
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

        static Dictionary<string, object> Read()
        {
            var so = ProjectSettingsAsset.Load(AssetPath);
            var values = new Dictionary<string, object>();
            AddFloat(so, VolumeProp, "volume", values);
            AddFloat(so, RolloffProp, "rolloff-scale", values);
            AddFloat(so, DopplerProp, "doppler-factor", values);
            return values;
        }

        static void AddFloat(SerializedObject so, string prop, string label, Dictionary<string, object> values)
        {
            var p = so.FindProperty(prop);
            if (p != null)
                values[label] = p.floatValue;
        }

        static void AddFloatChange(SerializedObject so, string prop, string label, float? newValue, List<string> changes)
        {
            if (!newValue.HasValue)
                return;
            var p = so.FindProperty(prop);
            if (p == null)
                return; // not present in this Unity version
            if (!Mathf.Approximately(p.floatValue, newValue.Value))
                changes.Add($"{label} {p.floatValue} -> {newValue.Value}");
        }

        static void SetFloat(SerializedObject so, string prop, float? value)
        {
            if (!value.HasValue)
                return;
            var p = so.FindProperty(prop);
            if (p != null)
                p.floatValue = value.Value;
        }
    }

    /// <summary>Audio settings to change. Null/omitted fields are left unchanged.</summary>
    class AudioSettingsInput : IStructuredCommandInput
    {
        [CliArg("volume", "Global audio volume (0..1).")]
        public float? Volume { get; set; }

        [CliArg("rolloff-scale", "Global rolloff scale.")]
        public float? RolloffScale { get; set; }

        [CliArg("doppler-factor", "Global doppler factor.")]
        public float? DopplerFactor { get; set; }
    }
}
