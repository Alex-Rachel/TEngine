using System;
using System.IO;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Commands.Prefabs
{
    /// <summary>
    /// Prefab authoring commands (CLI-194). These cover the prefab lifecycle an agent needs to
    /// build content procedurally: saving a configured GameObject as a prefab asset, instantiating
    /// it into a scene, deriving a variant, applying/reverting instance overrides, unpacking, and
    /// editing prefab contents through the prefab stage.
    ///
    /// Authoring conventions (CLI-190):
    /// - Asset paths flow through <see cref="ProjectPaths.Resolve"/> so writes are sandboxed to the
    ///   authoring root and "../" traversal is rejected.
    /// - Existing objects are referenced by <see cref="ObjectRef"/> and resolved with
    ///   <see cref="ObjectResolver.TryResolve"/>; results are returned as <see cref="AuthoringResult"/>.
    /// - Scene/object mutations (instantiation, override apply/revert, unpack) are wrapped in an
    ///   <see cref="AuthoringUndoScope"/> so they revert as one Editor undo step.
    ///
    /// IMPORTANT about undo coverage: prefab ASSET writes (<see cref="PrefabUtility.SaveAsPrefabAsset"/>,
    /// variant creation, and prefab-stage save) are AssetDatabase operations and are NOT part of
    /// Unity's Undo system, so the undo scope only reverts the scene-side effects. This mirrors the
    /// note in <see cref="AuthoringUndoScope"/> and <see cref="FolderCommands"/>.
    ///
    /// Nested-prefab safety: structural edits to a prefab asset go through the prefab stage
    /// (<see cref="PrefabUtility.LoadPrefabContents"/> / <see cref="PrefabUtility.SaveAsPrefabAsset"/> /
    /// <see cref="PrefabUtility.UnloadPrefabContents"/>) rather than mutating the asset root directly,
    /// which preserves nested-prefab links instead of flattening them.
    /// </summary>
    static class PrefabCommands
    {
        const string Group = "prefab";

        /// <summary>
        /// Save a source GameObject (typically a scene object) as a prefab asset. The source becomes
        /// a connected instance of the new prefab (interactionMode: AutomatedAction).
        /// </summary>
        [CliGroupCommand(Group, "Save a GameObject as a prefab asset at a project path; the source becomes a connected instance.", Tags = new[] { "prefabs" })]
        public static AuthoringResult Create(
            [CliArg("source", "Reference to the source GameObject to save as a prefab (globalId/path/guid/instanceId/hierarchyPath).", Required = true)] ObjectRef source,
            [CliArg("path", "Prefab asset path relative to the authoring root (the Assets/ prefix is optional and the .prefab extension is added if missing). e.g. Prefabs/Enemy or Prefabs/Enemy.prefab", Required = true)] string path)
        {
            var go = ResolveGameObject(source, "source");

            var assetPath = ResolvePrefabPath(path);
            EnsureParentFolder(assetPath);

            GameObject saved;
            using (new AuthoringUndoScope("Create Prefab"))
            {
                // ConnectToInstance leaves the source as an instance of the saved prefab so the agent
                // can keep editing it; SaveAsPrefabAsset itself is an AssetDatabase op (not undoable).
                saved = PrefabUtility.SaveAsPrefabAssetAndConnect(go, assetPath, InteractionMode.AutomatedAction, out var success);
                if (!success || saved == null)
                    throw new InvalidOperationException($"Failed to save prefab at '{assetPath}'.");
            }

            AssetDatabase.SaveAssets();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var result = ObjectResolver.Describe(asset) ?? new AuthoringResult { Type = nameof(GameObject) };
            result.AssetPath = assetPath;
            return result;
        }

        /// <summary>
        /// Instantiate a prefab asset into the active (or specified) loaded scene and return the
        /// scene instance's identity.
        /// </summary>
        [CliGroupCommand(Group, "Instantiate a prefab asset into a loaded scene and return the created instance.", Tags = new[] { "prefabs" })]
        public static AuthoringResult Instantiate(
            [CliArg("prefab", "Reference to the prefab asset to instantiate (path/guid/globalId).", Required = true)] ObjectRef prefab,
            [CliArg("scene-path", "Optional path of a loaded scene to instantiate into; defaults to the active scene.")] string scenePath = null,
            [CliArg("name", "Optional name for the created instance; defaults to the prefab name.")] string name = null)
        {
            var prefabAsset = ResolvePrefabAsset(prefab, "prefab");

            var scene = ResolveTargetScene(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("No valid loaded scene to instantiate into.");

            AuthoringResult result;
            using (new AuthoringUndoScope("Instantiate Prefab"))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, scene);
                if (instance == null)
                    throw new InvalidOperationException($"Failed to instantiate prefab '{AssetDatabase.GetAssetPath(prefabAsset)}'.");

                if (!string.IsNullOrEmpty(name))
                    instance.name = name;

                Undo.RegisterCreatedObjectUndo(instance, "Instantiate Prefab");
                EditorSceneManager.MarkSceneDirty(scene);

                result = ObjectResolver.Describe(instance) ?? new AuthoringResult { Type = nameof(GameObject) };
            }

            return result;
        }

        /// <summary>
        /// Create a prefab variant asset from a base prefab. The variant inherits the base and can
        /// override it. Implemented by instantiating the base, saving the instance as a new prefab
        /// asset (which becomes a variant because its root is a base-prefab instance), then removing
        /// the temporary scene instance.
        /// </summary>
        [CliGroupCommand(Group, "Create a prefab variant asset that inherits from a base prefab.", Tags = new[] { "prefabs" })]
        public static AuthoringResult CreateVariant(
            [CliArg("base", "Reference to the base prefab asset (path/guid/globalId).", Required = true)] ObjectRef basePrefab,
            [CliArg("path", "Variant prefab asset path relative to the authoring root (.prefab added if missing).", Required = true)] string path)
        {
            var basePrefabAsset = ResolvePrefabAsset(basePrefab, "base");

            var assetPath = ResolvePrefabPath(path);
            EnsureParentFolder(assetPath);

            // Author the variant from a throwaway instance of the base (saving an instance of a prefab
            // as a new prefab yields a variant). The temporary instance is created in an isolated
            // preview scene and the instance + scene are torn down in the finally, so the user's active
            // scene is never touched, dirtied, or subjected to object lifecycle callbacks.
            var previewScene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            GameObject variant;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefabAsset, previewScene);
                if (instance == null)
                    throw new InvalidOperationException("Failed to instantiate base prefab for variant creation.");

                variant = PrefabUtility.SaveAsPrefabAsset(instance, assetPath, out var success);
                if (!success || variant == null)
                    throw new InvalidOperationException($"Failed to save prefab variant at '{assetPath}'.");
            }
            finally
            {
                if (instance != null)
                    Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }

            AssetDatabase.SaveAssets();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var result = ObjectResolver.Describe(asset) ?? new AuthoringResult { Type = nameof(GameObject) };
            result.AssetPath = assetPath;
            return result;
        }

        /// <summary>
        /// Apply a prefab instance's overrides back to its source prefab asset. By default applies
        /// the whole instance; the asset is updated on disk.
        /// </summary>
        [CliGroupCommand(Group, "Apply a prefab instance's overrides back to its source prefab asset.", Tags = new[] { "prefabs" })]
        public static AuthoringResult ApplyOverrides(
            [CliArg("instance", "Reference to a prefab instance GameObject in a scene (instanceId/hierarchyPath/globalId).", Required = true)] ObjectRef instance)
        {
            var go = ResolveGameObject(instance, "instance");
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root == null)
                throw new ArgumentException($"Object '{instance}' is not part of a prefab instance.");

            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            // Applying overrides writes back to the source prefab asset, so confine it to the
            // authoring root and reject instances whose source lives outside the sandbox.
            ConfineToAuthoringRoot(assetPath, "instance's source prefab asset");

            using (new AuthoringUndoScope("Apply Prefab Overrides"))
            {
                Undo.RegisterFullObjectHierarchyUndo(root, "Apply Prefab Overrides");
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
                EditorSceneManager.MarkSceneDirty(root.scene);
            }

            AssetDatabase.SaveAssets();

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var result = ObjectResolver.Describe(asset) ?? new AuthoringResult { Type = nameof(GameObject) };
            if (!string.IsNullOrEmpty(assetPath))
                result.AssetPath = assetPath;
            return result;
        }

        /// <summary>
        /// Revert a prefab instance's overrides, restoring it to match its source prefab asset.
        /// </summary>
        [CliGroupCommand(Group, "Revert a prefab instance's overrides so it matches its source prefab asset.", Tags = new[] { "prefabs" })]
        public static AuthoringResult RevertOverrides(
            [CliArg("instance", "Reference to a prefab instance GameObject in a scene (instanceId/hierarchyPath/globalId).", Required = true)] ObjectRef instance)
        {
            var go = ResolveGameObject(instance, "instance");
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root == null)
                throw new ArgumentException($"Object '{instance}' is not part of a prefab instance.");

            using (new AuthoringUndoScope("Revert Prefab Overrides"))
            {
                Undo.RegisterFullObjectHierarchyUndo(root, "Revert Prefab Overrides");
                PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
                EditorSceneManager.MarkSceneDirty(root.scene);
            }

            var result = ObjectResolver.Describe(root) ?? new AuthoringResult { Type = nameof(GameObject) };
            return result;
        }

        /// <summary>
        /// Unpack a prefab instance, turning it (and optionally its nested instances) back into plain
        /// GameObjects. <paramref name="completely"/> chooses between
        /// <see cref="PrefabUnpackMode.OutermostRoot"/> (one level) and
        /// <see cref="PrefabUnpackMode.Completely"/> (all nested levels).
        /// </summary>
        [CliGroupCommand(Group, "Unpack a prefab instance into plain GameObjects (outermost level or completely).", Tags = new[] { "prefabs" })]
        public static AuthoringResult Unpack(
            [CliArg("instance", "Reference to a prefab instance GameObject in a scene (instanceId/hierarchyPath/globalId).", Required = true)] ObjectRef instance,
            [CliArg("completely", "If true, unpack all nested prefab levels (Completely); if false, only the outermost level (OutermostRoot).", DefaultValue = false)] bool completely = false)
        {
            var go = ResolveGameObject(instance, "instance");
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root == null)
                throw new ArgumentException($"Object '{instance}' is not part of a prefab instance.");

            var mode = completely ? PrefabUnpackMode.Completely : PrefabUnpackMode.OutermostRoot;

            using (new AuthoringUndoScope("Unpack Prefab"))
            {
                PrefabUtility.UnpackPrefabInstance(root, mode, InteractionMode.AutomatedAction);
                EditorSceneManager.MarkSceneDirty(root.scene);
            }

            var result = ObjectResolver.Describe(root) ?? new AuthoringResult { Type = nameof(GameObject) };
            return result;
        }

        /// <summary>
        /// Edit a prefab asset's contents safely through the prefab stage. Loads the prefab into an
        /// isolated, editable copy, optionally renames a child or reparents/adds nothing structural by
        /// default, then saves and unloads. This is the nested-prefab-safe path: it never mutates the
        /// asset root in-place, so nested prefab links are preserved.
        ///
        /// The MVP supports a small, declarative set of edits expressed as arguments so an agent can
        /// drive common changes without an embedded script:
        /// - <paramref name="setActiveChild"/> + <paramref name="active"/>: toggle a child's activeSelf.
        /// - <paramref name="renameChild"/> + <paramref name="newName"/>: rename a child.
        /// A no-op call (no edit args) simply round-trips the asset through the stage, which is a useful
        /// integrity check that the open/edit/close cycle does not corrupt the asset.
        /// </summary>
        [CliGroupCommand(Group, "Open a prefab asset in an isolated prefab stage, apply a declarative edit, and save it back (nested-prefab safe).", Tags = new[] { "prefabs" })]
        public static AuthoringResult SaveContents(
            [CliArg("prefab", "Reference to the prefab asset to edit (path/guid/globalId).", Required = true)] ObjectRef prefab,
            [CliArg("rename-child", "Optional child name (relative path under the root, e.g. 'Body/Head') to rename.")] string renameChild = null,
            [CliArg("new-name", "New name for the child identified by rename-child.")] string newName = null,
            [CliArg("set-active-child", "Optional child name (relative path under the root) whose active state to set.")] string setActiveChild = null,
            [CliArg("active", "Active state to apply when set-active-child is provided.", DefaultValue = true)] bool active = true)
        {
            var prefabAsset = ResolvePrefabAsset(prefab, "prefab");
            var assetPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"'{prefab}' does not resolve to a prefab asset on disk.");
            // This command writes the prefab asset back to disk, so confine it to the authoring root.
            ConfineToAuthoringRoot(assetPath, "prefab asset");

            // LoadPrefabContents gives an isolated, fully-editable copy of the prefab (with nested
            // prefabs intact). All edits happen on this copy; SaveAsPrefabAsset writes it back without
            // flattening nested prefabs. Always unload to avoid leaking the temporary scene.
            var contentsRoot = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                if (!string.IsNullOrEmpty(renameChild))
                {
                    var child = FindChild(contentsRoot.transform, renameChild);
                    if (child == null)
                        throw new ArgumentException($"No child '{renameChild}' under prefab root '{contentsRoot.name}'.");
                    if (string.IsNullOrEmpty(newName))
                        throw new ArgumentException("new-name is required when rename-child is provided.");
                    child.name = newName;
                }

                if (!string.IsNullOrEmpty(setActiveChild))
                {
                    var child = FindChild(contentsRoot.transform, setActiveChild);
                    if (child == null)
                        throw new ArgumentException($"No child '{setActiveChild}' under prefab root '{contentsRoot.name}'.");
                    child.gameObject.SetActive(active);
                }

                PrefabUtility.SaveAsPrefabAsset(contentsRoot, assetPath, out var success);
                if (!success)
                    throw new InvalidOperationException($"Failed to save prefab contents to '{assetPath}'.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentsRoot);
            }

            AssetDatabase.SaveAssets();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var result = ObjectResolver.Describe(asset) ?? new AuthoringResult { Type = nameof(GameObject) };
            result.AssetPath = assetPath;
            return result;
        }

        #region Helpers

        /// <summary>Resolve an <see cref="ObjectRef"/> to a GameObject (a component ref maps to its GameObject).</summary>
        private static GameObject ResolveGameObject(ObjectRef handle, string argName)
        {
            if (!ObjectResolver.TryResolve(handle, out var obj, out var error))
                throw new ArgumentException($"Could not resolve {argName}: {error}");

            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (go == null)
                throw new ArgumentException($"{argName} '{handle}' does not resolve to a GameObject (got {obj.GetType().Name}).");
            return go;
        }

        /// <summary>Resolve an <see cref="ObjectRef"/> to a prefab asset GameObject on disk.</summary>
        private static GameObject ResolvePrefabAsset(ObjectRef handle, string argName)
        {
            if (!ObjectResolver.TryResolve(handle, out var obj, out var error))
                throw new ArgumentException($"Could not resolve {argName}: {error}");

            var go = obj as GameObject;
            if (go == null)
                throw new ArgumentException($"{argName} '{handle}' does not resolve to a prefab GameObject (got {obj.GetType().Name}).");

            var assetPath = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{argName} '{handle}' is not a prefab asset.");
            return go;
        }

        /// <summary>Sandbox + normalize a prefab asset path, ensuring the .prefab extension.</summary>
        private static string ResolvePrefabPath(string path)
        {
            var resolved = ProjectPaths.Resolve(path, out var error);
            if (resolved == null)
                throw new ArgumentException(error);

            if (!resolved.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                resolved += ".prefab";
            return resolved;
        }

        /// <summary>
        /// Confine an existing asset path (one discovered from a resolved object, not user-supplied)
        /// to the authoring root, rejecting writes outside the sandbox. Runs the path through
        /// <see cref="ProjectPaths.Resolve"/>, which honors explicit "Assets/..." paths as-is and
        /// errors when they escape the root. <paramref name="what"/> describes the path for the error.
        /// </summary>
        private static void ConfineToAuthoringRoot(string assetPath, string what)
        {
            if (ProjectPaths.Resolve(assetPath, out var error) == null)
                throw new ArgumentException($"The {what} ('{assetPath}') is outside the authoring root: {error}");
        }

        /// <summary>Create the parent folder chain for an asset path if it does not yet exist.</summary>
        private static void EnsureParentFolder(string assetPath)
        {
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
                return;
            CreateFolderRecursive(parent);
            AssetDatabase.Refresh();
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            var name = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new ArgumentException($"Invalid folder path '{folderPath}'.");

            if (!AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Resolve the scene to instantiate into: a named loaded scene, or the active scene.</summary>
        private static Scene ResolveTargetScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return SceneManager.GetActiveScene();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded &&
                    (string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scene.name, scenePath, StringComparison.OrdinalIgnoreCase)))
                {
                    return scene;
                }
            }

            throw new ArgumentException($"No loaded scene matching '{scenePath}'.");
        }

        /// <summary>Find a descendant by a '/'-separated relative path under a root transform.</summary>
        private static Transform FindChild(Transform root, string relativePath)
        {
            // Transform.Find already supports '/'-separated paths for descendants. Normalize Windows-style
            // '\' separators to '/' first (mirrors ProjectPaths.Resolve) so paths like 'Body\Head' work.
            return root.Find(relativePath.Replace('\\', '/').Trim('/'));
        }

        #endregion
    }
}
