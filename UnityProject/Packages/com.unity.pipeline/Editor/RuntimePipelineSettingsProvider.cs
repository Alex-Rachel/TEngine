using System;
using System.Collections.Generic;
using Unity.Pipeline.Config;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Project Settings UI for the runtime Pipeline configuration (Project Settings > Pipeline >
    /// Runtime). This is the only editing surface for RuntimePipelineConfig — the config is never
    /// a saved asset, so there's nothing to click on in the Project window. This is the primary,
    /// discoverable UI for what used to require manually adding a RuntimePipelineManager
    /// component to a scene.
    /// </summary>
    static class RuntimePipelineSettingsProvider
    {
        /// <summary>
        /// Load the current settings, or a fresh in-memory default instance if none have been
        /// authored yet. Deliberately does NOT persist that default: merely viewing this page, or
        /// reading via get_runtime_pipeline_settings, must never have the side effect of writing to
        /// disk. The caller owns the returned instance and must DestroyImmediate it when done, same
        /// as RuntimePipelineConfig.Load().
        /// </summary>
        /// <summary>Settings window path of this page, shared with the Window > Pipeline menu.</summary>
        internal const string SettingsPath = "Project/Pipeline/Runtime";

        public static RuntimePipelineConfig LoadOrCreateConfig() =>
            RuntimePipelineConfig.Load() ?? ScriptableObject.CreateInstance<RuntimePipelineConfig>();

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            RuntimePipelineConfig config = null;
            UnityEditor.Editor editor = null;
            DateTime loadedWriteTimeUtc = default;

            void Reload()
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);
                if (config != null)
                    UnityEngine.Object.DestroyImmediate(config);
                config = LoadOrCreateConfig();
                editor = UnityEditor.Editor.CreateEditor(config, typeof(RuntimePipelineConfigEditor));
                // DateTime.MinValue (no settings file yet) is a stable value here, not a moving
                // target: GetSettingsFileWriteTimeUtc() keeps returning MinValue on every repaint
                // until an actual edit below calls config.Save(), so LoadOrCreateConfig() no longer
                // persisting a default does not cause a reload loop.
                loadedWriteTimeUtc = RuntimePipelineConfig.GetSettingsFileWriteTimeUtc();
            }

            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Runtime",
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
                    //    wrote to it directly — a build's "Disable Pipeline" security-dialog choice,
                    //    or a set_runtime_pipeline_settings CLI call — so our in-memory copy is stale.
                    if (config == null || RuntimePipelineConfig.GetSettingsFileWriteTimeUtc() != loadedWriteTimeUtc)
                        Reload();

                    EditorGUI.BeginChangeCheck();
                    editor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck())
                    {
                        config.Save();
                        loadedWriteTimeUtc = RuntimePipelineConfig.GetSettingsFileWriteTimeUtc();
                    }
                },
                keywords = new HashSet<string>(new[] { "Pipeline", "Runtime", "Method Reload", "Server", "CLI" })
            };
        }
    }
}
