using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Project Settings UI for the editor Pipeline server (Project Settings > Pipeline > Editor),
    /// sibling of the runtime server's page. This is the only editing surface for
    /// <see cref="EditorPipelineConfig"/> — the config is never a saved asset, so there is nothing
    /// to click on in the Project window, and nothing of the package's ends up in the user's build.
    /// </summary>
    static class EditorPipelineSettingsProvider
    {
        /// <summary>
        /// This page's path in the Project Settings tree. Shared so callers that navigate the user
        /// here — <see cref="EditorPipelineConfigEditor"/>'s leftover-asset notice — cannot drift
        /// from where the page is actually registered.
        /// </summary>
        internal const string SettingsPath = "Project/Pipeline/Editor";

        /// <summary>
        /// Load the authored settings, or a fresh in-memory default instance if none have been
        /// written yet. Deliberately does NOT persist that default: merely viewing this page must
        /// never have the side effect of writing to disk. The caller owns the returned instance and
        /// must DestroyImmediate it when done, same as <see cref="EditorPipelineConfig.Load"/>.
        /// </summary>
        /// <returns>The authored settings, or an unsaved instance carrying the defaults.</returns>
        public static EditorPipelineConfig LoadOrCreateConfig() =>
            EditorPipelineConfig.Load() ?? ScriptableObject.CreateInstance<EditorPipelineConfig>();

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            EditorPipelineConfig config = null;
            UnityEditor.Editor editor = null;
            DateTime loadedWriteTimeUtc = default;

            void Reload()
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);
                if (config != null)
                    UnityEngine.Object.DestroyImmediate(config);
                config = LoadOrCreateConfig();
                editor = UnityEditor.Editor.CreateEditor(config, typeof(EditorPipelineConfigEditor));
                // DateTime.MinValue (no settings file yet) is a stable value here, not a moving
                // target: GetSettingsFileWriteTimeUtc() keeps returning MinValue on every repaint
                // until an actual edit below calls config.Save(), so LoadOrCreateConfig() not
                // persisting a default does not cause a reload loop.
                loadedWriteTimeUtc = EditorPipelineConfig.GetSettingsFileWriteTimeUtc();
            }

            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Editor",
                activateHandler = (_, __) => Reload(),
                deactivateHandler = () =>
                {
                    if (editor != null)
                        UnityEngine.Object.DestroyImmediate(editor);
                    if (config != null)
                        UnityEngine.Object.DestroyImmediate(config);
                    editor = null;
                    config = null;
                },
                guiHandler = _ =>
                {
                    // Reload whenever our cached state can no longer be trusted:
                    //  - config == null: entering/exiting Play Mode destroyed it out from under the
                    //    cached Editor without deactivateHandler running first (Unity purges loose,
                    //    non-persisted ScriptableObjects around the Play Mode transition). Unity's
                    //    Object == overload treats a destroyed native object as null even though the
                    //    C# reference is not.
                    //  - the file's write-time moved since we last loaded/saved it: something else
                    //    wrote to it directly — a hand edit, a second editor sharing the project, or
                    //    the leftover-asset notice's copy button.
                    if (config == null)
                    {
                        Reload();
                    }
                    else if (EditorPipelineConfig.GetSettingsFileWriteTimeUtc() != loadedWriteTimeUtc)
                    {
                        Reload();
                        // Nothing pushed these to the running server the way an interactive edit
                        // does, so without this the page would show the new values while the server
                        // kept the old ones. Only on an external change: pushing on every reload
                        // would also fire on activate, re-enabling a watchdog that
                        // PipelineWatchdogTestGuard had deliberately turned off for a test run.
                        PipelineServerStartup.ApplyLiveSettings(config);
                    }

                    // Compare serialized state rather than using EditorGUI.BeginChangeCheck: this
                    // inspector's Start/Stop/Restart buttons also set GUI.changed, so a change check
                    // would author a settings file out of a button press that edited nothing.
                    var before = JsonUtility.ToJson(config);
                    editor.OnInspectorGUI();
                    if (JsonUtility.ToJson(config) != before)
                    {
                        config.Save();
                        loadedWriteTimeUtc = EditorPipelineConfig.GetSettingsFileWriteTimeUtc();

                        // Reconcile Application.runInBackground against the edit just saved
                        // (CLI-1432) — most relevantly, toggling "Ensure editor runs in background
                        // in PlayMode" while already in Play mode. Reconciles IN PLACE against this
                        // page's own `config` instance (never a second, independently-loaded one,
                        // which would race the external-change detection above), and refreshes
                        // loadedWriteTimeUtc again when it saves further — otherwise the next
                        // repaint would see the file's write time move past what this page recorded
                        // and mistake its own write for an external one, reloading and discarding
                        // whatever the user is mid-editing.
                        if (PipelineServerStartup.ReconcileRunInBackground(config, EditorApplication.isPlaying, out _))
                        {
                            config.Save();
                            loadedWriteTimeUtc = EditorPipelineConfig.GetSettingsFileWriteTimeUtc();
                        }
                    }
                },
                keywords = new HashSet<string>(new[]
                {
                    "Pipeline", "Editor", "Server", "Port", "Watchdog", "Method Reload", "CLI", "Telemetry",
                    "Run In Background", "Play Mode"
                })
            };
        }
    }
}
