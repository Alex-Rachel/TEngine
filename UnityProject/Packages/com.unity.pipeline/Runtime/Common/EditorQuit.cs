#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Quits the Editor without ever opening a save prompt. Unsaved scenes and an unsaved prefab stage
    /// make the quit fail unless the caller chooses to save or discard them. The exit runs a moment
    /// after the call returns, so the command's reply reaches the client before the process ends.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EditorQuit
    {
        const double k_ExitDelaySeconds = 0.2;

        static readonly PropertyInfo s_HasUnsavedChanges =
            typeof(PrefabStage).GetProperty("hasUnsavedChanges", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>Performs the exit. Tests replace it so a scheduled exit is recorded, not performed.</summary>
        internal static Action<int> ExitAction = EditorApplication.Exit;

        /// <summary>Saves the open scenes. Tests replace it to simulate a failed save.</summary>
        internal static Func<bool> SaveOpenScenesAction = EditorSceneManager.SaveOpenScenes;

        static EditorQuitResult s_Pending;
        static bool s_PendingSave;
        static bool s_PendingDiscard;
        static double s_ExitAt;

        internal static bool IsExitScheduled => s_Pending != null;

        internal static EditorQuitResult Quit(bool save, bool discard, int exitCode)
        {
            if (s_Pending != null)
                return s_Pending;

            if (save && discard)
                throw new ArgumentException("Pass --save or --discard, not both.");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (save)
                    throw new InvalidOperationException(
                        $"Cannot save scenes in Play Mode. Stop Play Mode ({CommandNames.EditorPlaymodeStop}) and retry — nothing was changed.");
                if (!discard)
                    throw new InvalidOperationException(
                        $"The Editor is in Play Mode. Stop Play Mode ({CommandNames.EditorPlaymodeStop}) first, " +
                        $"or run '{CommandNames.EditorQuit} --discard' to quit anyway — nothing was changed.");
            }

            var (dirtyScenes, dirtyStage, unsaved) = CollectUnsaved();
            var result = new EditorQuitResult { Quitting = true, ExitCode = exitCode };

            if (unsaved.Count > 0)
            {
                if (save)
                {
                    Save(dirtyScenes, dirtyStage);
                    result.Saved = unsaved;
                }
                else if (discard)
                {
                    result.Discarded = unsaved;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Cannot quit: there are unsaved changes in {string.Join(", ", unsaved)}. " +
                        $"Run '{CommandNames.EditorQuit} --save' to save them or '{CommandNames.EditorQuit} --discard' " +
                        "to drop them — nothing was changed.");
                }
            }

            ScheduleExit(result, save, discard);
            return result;
        }

        internal static void CancelScheduledExit()
        {
            EditorApplication.update -= Tick;
            s_Pending = null;
        }

        static void Save(List<Scene> dirtyScenes, PrefabStage dirtyStage)
        {
            // A dirty untitled scene would make SaveOpenScenes open a modal "Save Scene" dialog.
            var untitled = dirtyScenes.Where(s => string.IsNullOrEmpty(s.path)).Select(Describe).ToList();
            if (untitled.Count > 0)
                throw new InvalidOperationException(
                    $"Cannot save: {untitled.Count} dirty scene(s) have never been saved ({string.Join(", ", untitled)}). " +
                    "Save each to a path first (File > Save As, or EditorSceneManager.SaveScene(scene, path) through script eval), " +
                    $"or run '{CommandNames.EditorQuit} --discard' — nothing was saved.");

            if (dirtyScenes.Count > 0 && !SaveOpenScenesAction())
                throw new InvalidOperationException("Failed to save one or more open scenes. The Editor was not quit.");

            if (dirtyStage != null)
            {
                PrefabUtility.SaveAsPrefabAsset(dirtyStage.prefabContentsRoot, dirtyStage.assetPath, out var saved);
                if (!saved)
                    throw new InvalidOperationException($"Failed to save prefab '{dirtyStage.assetPath}'. The Editor was not quit.");
                dirtyStage.ClearDirtiness();
            }
        }

        static (List<Scene> dirtyScenes, PrefabStage dirtyStage, List<string> unsaved) CollectUnsaved()
        {
            var dirtyScenes = OpenScenes().Where(s => s.isDirty).ToList();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var dirtyStage = stage != null && HasUnsavedChanges(stage) ? stage : null;

            var unsaved = dirtyScenes.Select(Describe).ToList();
            if (dirtyStage != null)
                unsaved.Add(dirtyStage.assetPath);
            return (dirtyScenes, dirtyStage, unsaved);
        }

        static void ScheduleExit(EditorQuitResult result, bool save, bool discard)
        {
            s_Pending = result;
            s_PendingSave = save;
            s_PendingDiscard = discard;
            s_ExitAt = EditorApplication.timeSinceStartup + k_ExitDelaySeconds;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < s_ExitAt)
                return;

            var exitCode = s_Pending.ExitCode;
            var save = s_PendingSave;
            var discard = s_PendingDiscard;
            CancelScheduledExit();

            // Another command can change a scene during the delay; never let the exit discard that silently.
            if (!discard)
            {
                var (dirtyScenes, dirtyStage, unsaved) = CollectUnsaved();
                if (unsaved.Count > 0)
                {
                    if (!save)
                    {
                        Debug.LogError($"Pipeline: the quit was cancelled because {string.Join(", ", unsaved)} changed after it was accepted. " +
                            $"Run '{CommandNames.EditorQuit}' again with --save or --discard.");
                        return;
                    }

                    try
                    {
                        Save(dirtyScenes, dirtyStage);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Debug.LogError($"Pipeline: the quit was cancelled. {ex.Message}");
                        return;
                    }
                }
            }

            ExitAction(exitCode);
        }

        // PrefabStage.hasUnsavedChanges is internal; scene.isDirty can be set by merely opening the stage.
        static bool HasUnsavedChanges(PrefabStage stage) =>
            s_HasUnsavedChanges != null ? (bool)s_HasUnsavedChanges.GetValue(stage) : stage.scene.isDirty;

        static string Describe(Scene scene)
        {
            if (!string.IsNullOrEmpty(scene.path))
                return scene.path;
            return string.IsNullOrEmpty(scene.name) ? "an untitled scene" : $"untitled scene '{scene.name}'";
        }

        static IEnumerable<Scene> OpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                yield return SceneManager.GetSceneAt(i);
        }
    }

    /// <summary>The outcome of a quit request: the exit code and the unsaved items that were saved or discarded.</summary>
    sealed class EditorQuitResult
    {
        [JsonProperty("quitting")]
        public bool Quitting { get; set; }

        [JsonProperty("exitCode")]
        public int ExitCode { get; set; }

        [JsonProperty("saved")]
        public List<string> Saved { get; set; } = new List<string>();

        [JsonProperty("discarded")]
        public List<string> Discarded { get; set; } = new List<string>();
    }
}
#endif
