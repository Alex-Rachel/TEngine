using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Pipeline.Editor.Commands.Scenes
{
    /// <summary>
    /// Scene-management authoring commands (CLI-193): create / open / save scenes, inspect open
    /// scenes, control the active scene, snapshot a scene's hierarchy, and manage the build scene
    /// list. They build on the same foundation as the asset commands — paths go through the
    /// <see cref="ProjectPaths"/> sandbox and results come back as the canonical
    /// <see cref="AuthoringResult"/> envelope so an agent can chain follow-up calls.
    ///
    /// WHY the play-mode guard: <see cref="EditorSceneManager"/> mutations (new/open/save/setActive)
    /// are undefined and destructive while the editor is entering or in play mode — they can discard
    /// in-flight play-mode state or leave the scene manager inconsistent. Rather than risk corrupting
    /// state, every mutating command refuses up front with a clear, *recoverable* error (an
    /// <see cref="InvalidOperationException"/>, surfaced by the server as a 400 "Command Execution
    /// Failed" without touching scene state). The caller can exit play mode and retry.
    /// </summary>
    static class SceneCommands
    {
        const string Group = "scene";
        const string EditorGroup = "editor";

        private const string SceneExtension = ".unity";

        // create_scene --template values. "empty" is the historical default (blank scene); "default"
        // seeds Unity's built-in "3D" new-scene contents (Main Camera + Directional Light).
        private const string TemplateEmpty = "empty";
        private const string TemplateDefault = "default";

        #region Create / Open / Save

        [CliGroupCommand(Group, "Create a new scene and save it to the given path under the authoring root.", Tags = new[] { "scenes" })]
        public static AuthoringResult Create(
            [CliArg("path", "Scene path relative to the authoring root (default Assets/); the Assets/ prefix and the .unity extension are optional. e.g. Scenes/Level1", Required = true)] string path,
            [CliArg("additive", "Open the new scene additively alongside currently open scenes instead of replacing them.", DefaultValue = false)] bool additive = false,
            [CliArg("template", "Initial contents: 'empty' (default) for a blank scene, or 'default' to seed a Main Camera + Directional Light matching Unity's built-in 3D template.", DefaultValue = TemplateEmpty)] string template = TemplateEmpty)
        {
            GuardNotPlaying(CommandNames.SceneCreate);

            // Validate the template up front (before touching scene state) so an unknown value fails
            // recoverably with a clear, enumerated error rather than silently doing the wrong thing.
            var sceneSetup = ResolveSceneSetup(template);

            var normalized = ResolveScenePath(path);
            EnsureParentFolder(normalized);

            // NewSceneMode.Single replaces all open scenes; Additive keeps them. NewSceneSetup.EmptyScene
            // gives a blank scene (no default camera/light) — predictable for programmatic population;
            // NewSceneSetup.DefaultGameObjects gives exactly a Main Camera (tagged MainCamera) + a
            // Directional Light, identical to Unity's built-in "3D" new-scene template.
            var setup = additive ? NewSceneMode.Additive : NewSceneMode.Single;
            var scene = EditorSceneManager.NewScene(sceneSetup, setup);

            if (!EditorSceneManager.SaveScene(scene, normalized))
                throw new InvalidOperationException($"Failed to save new scene to '{normalized}'.");

            AssetDatabase.Refresh();
            return DescribeSceneAsset(normalized);
        }

        [CliGroupCommand(Group, "Open an existing scene from the given path.", Tags = new[] { "scenes" })]
        public static AuthoringResult Open(
            [CliArg("path", "Scene path relative to the authoring root (default Assets/); the Assets/ prefix and the .unity extension are optional.", Required = true)] string path,
            [CliArg("additive", "Open additively alongside currently open scenes instead of replacing them.", DefaultValue = false)] bool additive = false)
        {
            GuardNotPlaying(CommandNames.SceneOpen);

            var normalized = ResolveScenePath(path);
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(normalized)))
                throw new InvalidOperationException($"No scene asset at '{normalized}'.");

            var mode = additive ? OpenSceneMode.Additive : OpenSceneMode.Single;
            var scene = EditorSceneManager.OpenScene(normalized, mode);
            if (!scene.IsValid())
                throw new InvalidOperationException($"Failed to open scene '{normalized}'.");

            return DescribeSceneAsset(normalized);
        }

        [CliGroupCommand(Group, "Save an open scene. Saves the active scene when no path is given.", Tags = new[] { "scenes" })]
        public static AuthoringResult Save(
            [CliArg("path", "Path of the open scene to save (authoring-root relative; Assets/ prefix and .unity optional). Omit to save the active scene.")] string path = null)
        {
            GuardNotPlaying(CommandNames.SceneSave);

            var scene = string.IsNullOrEmpty(path)
                ? EditorSceneManager.GetActiveScene()
                : FindOpenScene(ResolveScenePath(path));

            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(path)
                        ? "No valid active scene to save."
                        : $"Scene '{path}' is not open.");

            // An untitled/never-saved scene has an empty path. SaveScene(scene) with no explicit path
            // would either trigger a modal "Save Scene" dialog (hangs a headless editor) or fail
            // non-deterministically. Refuse up front with a recoverable error so the caller picks a path.
            if (string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException(
                    $"Scene '{scene.name}' has never been saved (no path). " +
                    $"Create or save it with an explicit path first ({CommandNames.SceneCreate} <path>) — no scene state was changed.");

            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Failed to save scene '{scene.name}'.");

            AssetDatabase.Refresh();
            return DescribeSceneAsset(scene.path);
        }

        [CliGroupCommand(EditorGroup, "Save all open scenes that have unsaved changes.", Tags = new[] { "scenes" })]
        public static object SaveAll()
        {
            GuardNotPlaying(CommandNames.EditorSaveAll);

            // SaveOpenScenes saves every loaded, dirty scene. A dirty untitled scene (empty path) would
            // make it pop a modal "Save Scene" dialog, which hangs a headless editor. Fail fast with a
            // recoverable error before saving anything so the caller saves that scene with an explicit path.
            var dirty = OpenScenes().Where(s => s.isDirty).ToList();
            var untitled = dirty.Where(s => string.IsNullOrEmpty(s.path)).Select(s => s.name).ToList();
            if (untitled.Count > 0)
                throw new InvalidOperationException(
                    $"Cannot save all: {untitled.Count} dirty scene(s) have never been saved ({string.Join(", ", untitled)}). " +
                    $"Save each with an explicit path first ({CommandNames.SceneCreate} <path>) — no scenes were saved.");

            // Report which scenes it touched.
            var dirtyBefore = dirty.Select(s => s.path).ToList();

            var saved = EditorSceneManager.SaveOpenScenes();
            if (!saved && dirtyBefore.Count > 0)
                throw new InvalidOperationException("Failed to save one or more open scenes.");

            AssetDatabase.Refresh();
            return new { saved, scenes = dirtyBefore };
        }

        #endregion

        #region Inspect / Active

        [CliGroupCommand(Group, "List all currently open scenes with their load/active/dirty state.", Tags = new[] { "scenes" })]
        public static object ListOpen()
        {
            // Read-only; safe in play mode, so no guard here.
            var active = SceneManager.GetActiveScene();
            var scenes = OpenScenes()
                .Select(s => new
                {
                    name = s.name,
                    path = s.path,
                    isLoaded = s.isLoaded,
                    isDirty = s.isDirty,
                    isActive = s.handle == active.handle,
                    rootCount = s.isLoaded ? s.rootCount : 0
                })
                .ToList();

            return new { count = scenes.Count, scenes };
        }

        [CliGroupCommand(Group, "Set which open scene is the active scene (new objects are created in the active scene).", Tags = new[] { "scenes" })]
        public static AuthoringResult SetActive(
            [CliArg("path", "Path of an already-open scene to make active (authoring-root relative; Assets/ prefix and .unity optional).", Required = true)] string path)
        {
            GuardNotPlaying(CommandNames.SceneSetActive);

            var normalized = ResolveScenePath(path);
            var scene = FindOpenScene(normalized);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException($"Scene '{path}' is not open; open it before making it active.");

            if (!SceneManager.SetActiveScene(scene))
                throw new InvalidOperationException($"Failed to set '{normalized}' as the active scene.");

            return DescribeSceneAsset(scene.path);
        }

        [CliGroupCommand(Group, "Return the GameObject tree of an open scene (or the active scene). Each node carries instanceId + hierarchyPath usable by GameObject commands.", Tags = new[] { "scenes" })]
        public static SceneHierarchy GetHierarchy(
            [CliArg("path", "Path of the open scene to snapshot (authoring-root relative; Assets/ prefix and .unity optional). Omit for the active scene.")] string path = null)
        {
            // Read-only; safe in play mode.
            var scene = string.IsNullOrEmpty(path)
                ? SceneManager.GetActiveScene()
                : FindOpenScene(ResolveScenePath(path));

            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(path)
                        ? "No valid active scene to read."
                        : $"Scene '{path}' is not open.");

            var active = SceneManager.GetActiveScene();
            var hierarchy = new SceneHierarchy
            {
                SceneName = scene.name,
                ScenePath = scene.path,
                IsDirty = scene.isDirty,
                IsActive = scene.handle == active.handle
            };

            foreach (var root in scene.GetRootGameObjects())
                hierarchy.Roots.Add(BuildNode(root, "/" + root.name));

            return hierarchy;
        }

        #endregion

        #region Build settings

        [CliGroupCommand(Group, "Add a scene to the Build Settings scene list (idempotent). Optionally enable it.", Tags = new[] { "scenes", "build/settings" })]
        public static object AddToBuild(
            [CliArg("path", "Scene path to add (authoring-root relative; Assets/ prefix and .unity optional).", Required = true)] string path,
            [CliArg("enabled", "Whether the scene is enabled in the build list.", DefaultValue = true)] bool enabled = true)
        {
            GuardNotPlaying(CommandNames.SceneAddToBuild);

            var normalized = ResolveScenePath(path);
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(normalized)))
                throw new InvalidOperationException($"No scene asset at '{normalized}'.");

            var scenes = EditorBuildSettings.scenes.ToList();
            var existing = scenes.FindIndex(s => PathsEqual(s.path, normalized));
            if (existing >= 0)
            {
                // Idempotent: just reconcile the enabled flag.
                if (scenes[existing].enabled != enabled)
                    scenes[existing] = new EditorBuildSettingsScene(normalized, enabled);
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(normalized, enabled));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            return new { path = normalized, enabled, buildIndex = SceneUtility.GetBuildIndexByScenePath(normalized), count = scenes.Count };
        }

        [CliGroupCommand(Group, "Remove a scene from the Build Settings scene list (idempotent).", Tags = new[] { "scenes", "build/settings" })]
        public static object RemoveFromBuild(
            [CliArg("path", "Scene path to remove (authoring-root relative; Assets/ prefix and .unity optional).", Required = true)] string path)
        {
            GuardNotPlaying(CommandNames.SceneRemoveFromBuild);

            var normalized = ResolveScenePath(path);
            var scenes = EditorBuildSettings.scenes.ToList();
            var removed = scenes.RemoveAll(s => PathsEqual(s.path, normalized));
            EditorBuildSettings.scenes = scenes.ToArray();
            return new { path = normalized, removed = removed > 0, count = scenes.Count };
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Refuse a mutating scene op while entering or in play mode. We deliberately throw a
        /// recoverable <see cref="InvalidOperationException"/> *before* touching any state, so the
        /// command surfaces as a clean failure rather than leaving the scene manager inconsistent.
        /// </summary>
        private static void GuardNotPlaying(string command)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    $"'{command}' cannot run while the editor is in (or entering) play mode. " +
                    $"Exit play mode ({CommandNames.EditorPlaymodeStop}) and retry — no scene state was changed.");
        }

        /// <summary>
        /// Map the create_scene <c>template</c> argument to a <see cref="NewSceneSetup"/>. "empty"
        /// (or a null/blank value) keeps the historical blank-scene behavior; "default" seeds Unity's
        /// built-in "3D" template (Main Camera tagged MainCamera + Directional Light). An unknown value
        /// throws a recoverable <see cref="ArgumentException"/> listing the valid options.
        /// </summary>
        private static NewSceneSetup ResolveSceneSetup(string template)
        {
            // Treat a null/blank template as the default ("empty") for parity with an omitted argument.
            var value = string.IsNullOrWhiteSpace(template) ? TemplateEmpty : template.Trim();

            if (string.Equals(value, TemplateEmpty, StringComparison.OrdinalIgnoreCase))
                return NewSceneSetup.EmptyScene;
            if (string.Equals(value, TemplateDefault, StringComparison.OrdinalIgnoreCase))
                return NewSceneSetup.DefaultGameObjects;

            throw new ArgumentException(
                $"Unknown template '{template}'. Valid values: '{TemplateEmpty}', '{TemplateDefault}'.");
        }

        /// <summary>Resolve an agent path through the sandbox and normalize it to a "*.unity" asset path.</summary>
        private static string ResolveScenePath(string path)
        {
            var normalized = ProjectPaths.Resolve(path, out var error);
            if (normalized == null)
                throw new ArgumentException(error);

            if (!normalized.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase))
                normalized += SceneExtension;

            return normalized;
        }

        /// <summary>Create the scene's parent folder chain if missing (mirrors FolderCommands).</summary>
        private static void EnsureParentFolder(string scenePath)
        {
            var parent = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
                return;

            CreateFolderRecursive(parent);
        }

        private static void CreateFolderRecursive(string assetsPath)
        {
            if (AssetDatabase.IsValidFolder(assetsPath))
                return;

            var parent = Path.GetDirectoryName(assetsPath)?.Replace('\\', '/');
            var name = Path.GetFileName(assetsPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new ArgumentException($"Invalid folder path '{assetsPath}'.");

            if (!AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Build the canonical identity envelope for a scene asset path.</summary>
        private static AuthoringResult DescribeSceneAsset(string scenePath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            var result = ObjectResolver.Describe(asset) ?? new AuthoringResult { Type = nameof(SceneAsset) };
            result.AssetPath = scenePath;
            return result;
        }

        /// <summary>Enumerate every open scene (loaded or not) by index.</summary>
        private static IEnumerable<Scene> OpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                yield return SceneManager.GetSceneAt(i);
        }

        /// <summary>Find an open scene by its (normalized) asset path. Returns an invalid Scene when not open.</summary>
        private static Scene FindOpenScene(string normalizedPath)
        {
            foreach (var scene in OpenScenes())
            {
                if (PathsEqual(scene.path, normalizedPath))
                    return scene;
            }

            return default;
        }

        private static bool PathsEqual(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static SceneHierarchyNode BuildNode(GameObject go, string hierarchyPath)
        {
            var node = new SceneHierarchyNode
            {
                Name = go.name,
                InstanceId = PipelineUtils.GetObjectId(go),
                HierarchyPath = hierarchyPath,
                ActiveSelf = go.activeSelf,
                Components = go.GetComponents<Component>()
                    // A missing/broken script serializes as a null component; skip it rather than NRE.
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToList()
            };

            var transform = go.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i).gameObject;
                node.Children.Add(BuildNode(child, hierarchyPath + "/" + child.name));
            }

            return node;
        }

        #endregion
    }
}
