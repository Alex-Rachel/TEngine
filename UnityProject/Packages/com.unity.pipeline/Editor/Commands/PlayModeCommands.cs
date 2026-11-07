using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Commands for controlling Unity Editor play mode state.
    /// Enables automation workflows to control Editor play/stop/pause from CLI tools.
    /// </summary>
    static class PlayModeCommands
    {
        const string Group = "editor.playmode";

        // TODO Pipeline: should all commands have name with namespace: editor.play build.generate instead of using _

        [CliGroupCommand(Group, "Enter Unity Editor play mode", Tags = new[] { "editor/playmode" })]
        public static string Start()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Editor is already in play mode");
                return "Already in play mode";
            }

            EditorApplication.isPlaying = true;
            return "Entered play mode";
        }

        /// <summary>
        /// Override the project's Run In Background Player Setting so the Play-mode player loop
        /// keeps running while the Editor is unfocused (CLI-1432). Unlike <c>editor.playmode start</c>, which
        /// used to do this implicitly and silently for every Play session, this records an explicit
        /// override that lasts until reverted or until the Editor quits: <c>enabled: true</c>
        /// remembers the project's own value (unless an override is already active) and turns Run
        /// In Background on; <c>enabled: false</c> stops
        /// the override and restores the project's own value, unless the "Ensure editor runs in
        /// background in PlayMode" project setting is still forcing it on right now. A build always
        /// uses the project's own value regardless of either — see
        /// <see cref="BuildProcessors.PipelineRuntimeBuildProcessor"/>.
        /// </summary>
        [CliGroupCommand(Group,
            "Override the project's Run In Background Player Setting, recording enough state to restore it later and to keep it out of builds",
            Tags = new[] { "editor/playmode" })]
        public static string SetRunInBackground(
            [CliArg("enabled", "Turn the override on (true, default) or stop it (false) and restore the project's own value, unless the \"Ensure editor runs in background in PlayMode\" setting is still forcing it on right now")] bool enabled = true)
        {
            var cfg = EditorPipelineConfig.Load() ?? ScriptableObject.CreateInstance<EditorPipelineConfig>();
            try
            {
                // First settle any change the user made themselves (they win and the stored override
                // ends), so this explicit request is applied on top of it instead of being swallowed
                // by it: asking again is how the override comes back.
                PipelineServerStartup.ReconcileRunInBackground(cfg, EditorApplication.isPlaying, out _);
                cfg.SetRunInBackgroundCommandOverride(enabled);
                PipelineServerStartup.ReconcileRunInBackground(cfg, EditorApplication.isPlaying, out var message);
                // Always persisted, regardless of whether the override's net effect changed: the
                // command's own record field (RunInBackgroundCommandOverride) always needs saving,
                // even when e.g. the Play-mode setting was already forcing the same result.
                cfg.Save();
                return message;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
            }
        }

        [CliGroupCommand(Group, "Exit Unity Editor play mode", Tags = new[] { "editor/playmode" })]
        public static string Stop()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Editor is already in edit mode");
                return "Already in edit mode";
            }

            EditorApplication.isPlaying = false;
            return "Exited play mode";
        }

        [CliGroupCommand(Group, "Toggle pause state of Unity Editor play mode", Tags = new[] { "editor/playmode" })]
        public static string Pause()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Cannot pause - Editor is not in play mode");
                return "Cannot pause - not in play mode";
            }

            EditorApplication.isPaused = !EditorApplication.isPaused;
            string state = EditorApplication.isPaused ? "paused" : "unpaused";
            return $"Play mode {state}";
        }
    }
}