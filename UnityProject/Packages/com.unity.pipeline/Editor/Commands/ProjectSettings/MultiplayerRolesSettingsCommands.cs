#if UNITY_6000_6_OR_NEWER
using System.Collections.Generic;
using Unity.Multiplayer.Editor;
using Unity.Pipeline.Commands;
using UnityEditor;
using ServerStripping = Unity.Multiplayer.Editor.EditorMultiplayerRolesManager.AutomaticSelection.Server;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set the project's Multiplayer Roles settings: whether roles are enabled, safety checks, and
    /// the server's automatic stripping of audio, rendering and UI components. The Editor's role and
    /// per-object, build and component-type roles are in the <c>multiplayer.role</c> group. Compiled in
    /// on 6000.6+, where <c>EditorMultiplayerRolesManager</c> is public, like that group.
    /// </summary>
    static class MultiplayerRolesSettingsCommands
    {
        const string Domain = "multiplayer_roles";
        const string Group = "settings." + Domain;

        [CliGroupCommand(Group, "Read Multiplayer Roles settings (enabled, safety-checks, strip-audio, strip-rendering, strip-ui).", MainThreadRequired = true, Tags = new[] { "settings/multiplayer_roles", "multiplayer/roles" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Change Multiplayer Roles settings: enable roles, safety checks, and the server's automatic stripping of audio, rendering and UI components (Edit Mode only). Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z.", MainThreadRequired = true, Tags = new[] { "settings/multiplayer_roles", "multiplayer/roles" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] MultiplayerRolesSettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            var changes = new List<string>();
            Describe(changes, "enabled", EditorMultiplayerRolesManager.EnableMultiplayerRoles, settings.Enabled);
            Describe(changes, "safety-checks", EditorMultiplayerRolesManager.EnableSafetyChecks, settings.SafetyChecks);
            Describe(changes, "strip-audio", ServerStripping.StripAudioComponents, settings.StripAudio);
            Describe(changes, "strip-rendering", ServerStripping.StripRenderingComponents, settings.StripRendering);
            Describe(changes, "strip-ui", ServerStripping.StripUIComponents, settings.StripUI);

            if (changes.Count == 0)
                return NoChanges();

            // Roles are applied when the Editor enters Play Mode, so a change made in Play Mode would
            // not take effect until the next entry; refuse it rather than appear to succeed.
            if (confirm && !dryRun && EditorApplication.isPlayingOrWillChangePlaymode)
                return ProjectSettingsResponse.Fail(Domain,
                    $"Multiplayer Roles settings cannot change while the Editor is in (or entering) Play Mode, where roles have already been applied. " +
                    $"Exit Play Mode ({CommandNames.EditorPlaymodeStop}) and retry. Nothing was changed.");

            var planText = "Set Multiplayer Roles settings: " + string.Join("; ", changes);

            void Apply()
            {
                if (settings.Enabled.HasValue) EditorMultiplayerRolesManager.EnableMultiplayerRoles = settings.Enabled.Value;
                if (settings.SafetyChecks.HasValue) EditorMultiplayerRolesManager.EnableSafetyChecks = settings.SafetyChecks.Value;
                if (settings.StripAudio.HasValue) ServerStripping.StripAudioComponents = settings.StripAudio.Value;
                if (settings.StripRendering.HasValue) ServerStripping.StripRenderingComponents = settings.StripRendering.Value;
                if (settings.StripUI.HasValue) ServerStripping.StripUIComponents = settings.StripUI.Value;
            }

            return ProjectSettingsCommand.Apply(Domain, confirm, dryRun, settings, () => planText, Apply, Read);
        }

        static void Describe(List<string> changes, string name, bool current, bool? requested)
        {
            if (requested.HasValue && requested.Value != current)
                changes.Add($"{name} {current} -> {requested.Value}");
        }

        static ProjectSettingsResponse NoChanges()
        {
            var response = ProjectSettingsCommand.Get(Domain, Read);
            response.Message = "No changes specified; nothing to apply.";
            return response;
        }

        static Dictionary<string, object> Read()
        {
            return new Dictionary<string, object>
            {
                ["enabled"] = EditorMultiplayerRolesManager.EnableMultiplayerRoles,
                ["safety-checks"] = EditorMultiplayerRolesManager.EnableSafetyChecks,
                ["strip-audio"] = ServerStripping.StripAudioComponents,
                ["strip-rendering"] = ServerStripping.StripRenderingComponents,
                ["strip-ui"] = ServerStripping.StripUIComponents
            };
        }
    }

    /// <summary>Multiplayer Roles settings to change. Null/omitted fields are left unchanged.</summary>
    class MultiplayerRolesSettingsInput : IStructuredCommandInput
    {
        [CliArg("enabled", "Enable Multiplayer Roles for the project. Nothing is stripped for any role until they are enabled.")]
        public bool? Enabled { get; set; }

        [CliArg("safety-checks", "Warn on entering Play Mode or building about stripped objects that other objects still reference.")]
        public bool? SafetyChecks { get; set; }

        [CliArg("strip-audio", "Strip common audio components from the ServerStripping.")]
        public bool? StripAudio { get; set; }

        [CliArg("strip-rendering", "Strip common rendering components from the ServerStripping.")]
        public bool? StripRendering { get; set; }

        [CliArg("strip-ui", "Strip common UI components from the ServerStripping.")]
        public bool? StripUI { get; set; }
    }
}
#endif
