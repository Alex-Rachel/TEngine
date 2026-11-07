using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_6000_0_OR_NEWER
using PipelinePhysicsMaterial = UnityEngine.PhysicsMaterial;
#else
using PipelinePhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

namespace Unity.Pipeline.Editor.Commands.Assets
{
    /// <summary>
    /// Asset lifecycle authoring commands (CLI-191): create / import / move / copy / rename / delete
    /// project assets. They sit on top of the CLI-190 authoring foundation, so:
    ///
    /// - every agent-supplied path is funnelled through <see cref="ProjectPaths.Resolve"/> (sandboxed
    ///   to the authoring root; rejects "../" and out-of-project writes),
    /// - results are returned as the canonical <see cref="AuthoringResult"/> envelope so an agent can
    ///   reference the asset in a follow-up call,
    /// - existing assets are addressed with an <see cref="ObjectRef"/> resolved by
    ///   <see cref="ObjectResolver"/>.
    ///
    /// NOTE: AssetDatabase create/move/copy/rename/delete are NOT part of Unity's Undo system, so
    /// <see cref="AuthoringUndoScope"/> would have no effect here — these operations are intentionally
    /// not wrapped in an undo scope, and destructive/overwriting operations instead require an explicit
    /// <c>confirm</c> argument (and support <c>dry-run</c>) so an agent cannot silently lose data.
    /// </summary>
    static class AssetCommands
    {
        const string Group = "asset";

        [CliGroupCommand(Group, "Create a new ScriptableObject (or other UnityEngine.Object) asset of the given type at a path under the authoring root.", Tags = new[] { "assets" })]
        public static AuthoringResult Create(
            [CliArg("path", "Asset path relative to the authoring root, including extension (e.g. Data/Config.asset or Materials/Wall.mat). The Assets/ prefix is optional.", Required = true)] string path,
            [CliArg("type", "Fully-qualified or short type name to instantiate (e.g. UnityEngine.Material, MyGame.GameConfig). Must derive from UnityEngine.Object and be creatable.", Required = true)] string type,
            [CliArg("shader", "Material-only (ignored otherwise): shader name to assign (e.g. Standard, \"Universal Render Pipeline/Lit\"). When omitted, defaults to \"Universal Render Pipeline/Lit\" if a Scriptable Render Pipeline is active, otherwise the built-in \"Standard\" shader (falling back to \"Standard\" if URP/Lit is unavailable).")] string shader = null,
            [CliArg("confirm", "Required (true) only when overwriting an existing asset at the path. Ignored when the path is empty.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate inputs and report what would be created without writing anything.")] bool dryRun = false)
        {
            var normalized = ProjectPaths.Resolve(path, out var error);
            if (normalized == null)
                throw new ArgumentException(error);

            if (string.IsNullOrEmpty(Path.GetExtension(normalized)))
                throw new ArgumentException($"Asset path '{normalized}' must include a file extension (e.g. .asset, .mat).");

            var resolvedType = ResolveType(type);
            if (resolvedType == null)
                throw new ArgumentException($"Could not resolve type '{type}'. Use a fully-qualified name (e.g. UnityEngine.Material).");
            if (!typeof(Object).IsAssignableFrom(resolvedType))
                throw new ArgumentException($"Type '{resolvedType.FullName}' does not derive from UnityEngine.Object.");

            // CLI-222: Unity 6 renamed PhysicMaterial -> PhysicsMaterial, but the on-disk asset
            // extension remains ".physicMaterial" in both supported editor generations.
            // AssetDatabase.CreateAsset rejects a ".physicsMaterial" file, so accept either spelling
            // from the agent and normalize to the extension Unity writes.
            if (typeof(PipelinePhysicsMaterial).IsAssignableFrom(resolvedType) &&
                normalized.EndsWith(".physicsMaterial", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - ".physicsMaterial".Length) + ".physicMaterial";
            }

            var exists = AssetDatabase.LoadMainAssetAtPath(normalized) != null;
            if (exists && !confirm)
                throw new ArgumentException($"An asset already exists at '{normalized}'. Pass confirm=true to overwrite it.");

            // CLI-221: for a Material, resolve (and thereby validate) the shader up front so dry-run
            // also fails fast on an unknown shader — dry-run is documented as "validate inputs".
            Shader materialShader = null;
            if (typeof(Material).IsAssignableFrom(resolvedType))
                materialShader = ResolveShader(shader);

            if (dryRun)
                return new AuthoringResult { AssetPath = normalized, Type = resolvedType.Name };

            EnsureParentFolder(normalized);

            Object asset;
            if (typeof(ScriptableObject).IsAssignableFrom(resolvedType))
            {
                asset = ScriptableObject.CreateInstance(resolvedType);
            }
            else if (typeof(Material).IsAssignableFrom(resolvedType))
            {
                // CLI-221: Material has no public parameterless ctor (Activator.CreateInstance produces
                // an invalid Material with a null shader), so construct it explicitly from the shader
                // resolved (and validated) above. The `shader` arg is Material-specific and ignored for
                // every other type.
                asset = new Material(materialShader);
            }
            else if (typeof(PipelinePhysicsMaterial).IsAssignableFrom(resolvedType))
            {
                // CLI-222: PhysicMaterial/PhysicsMaterial has a public parameterless ctor, but create
                // it explicitly so we can stamp the documented sensible defaults rather than relying
                // on engine defaults.
                asset = new PipelinePhysicsMaterial
                {
                    dynamicFriction = 0.6f,
                    staticFriction = 0.6f,
                    bounciness = 0f,
                };
            }
            else
            {
                // Other UnityEngine.Object subclasses with a public parameterless ctor.
                // Activator.CreateInstance throws for abstract types or types without an accessible
                // parameterless ctor — surface that as an actionable ArgumentException.
                try
                {
                    asset = Activator.CreateInstance(resolvedType) as Object;
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        $"Type '{resolvedType.FullName}' could not be instantiated (it may be abstract or lack a public parameterless constructor): {ex.Message}");
                }
            }

            if (asset == null)
                throw new ArgumentException($"Type '{resolvedType.FullName}' could not be instantiated as a creatable asset.");

            // AssetDatabase.CreateAsset does not reliably overwrite an existing asset at the path, so
            // explicitly delete it first. The confirm guard above gates that an asset already exists.
            if (exists)
                AssetDatabase.DeleteAsset(normalized);

            AssetDatabase.CreateAsset(asset, normalized);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(normalized);

            var loaded = AssetDatabase.LoadMainAssetAtPath(normalized);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult { Type = resolvedType.Name };
            result.AssetPath = normalized;
            return result;
        }

        [CliGroupCommand(Group, "Import an external file (e.g. a texture, model, audio clip) into the project by copying it to a path under the authoring root, then importing it.", Tags = new[] { "assets/import" })]
        public static AuthoringResult Import(
            [CliArg("source", "Absolute filesystem path to the external file to import.", Required = true)] string source,
            [CliArg("path", "Destination asset path relative to the authoring root, including extension. The Assets/ prefix is optional.", Required = true)] string path,
            [CliArg("confirm", "Required (true) only when overwriting an existing asset at the destination path.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate inputs and report what would be imported without writing anything.")] bool dryRun = false)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("source is required.");
            if (!File.Exists(source))
                throw new ArgumentException($"Source file '{source}' does not exist.");

            var normalized = ProjectPaths.Resolve(path, out var error);
            if (normalized == null)
                throw new ArgumentException(error);

            var exists = AssetDatabase.LoadMainAssetAtPath(normalized) != null || File.Exists(ToAbsolute(normalized));
            if (exists && !confirm)
                throw new ArgumentException($"An asset already exists at '{normalized}'. Pass confirm=true to overwrite it.");

            if (dryRun)
                return new AuthoringResult { AssetPath = normalized, Type = "ImportedAsset" };

            EnsureParentFolder(normalized);
            File.Copy(source, ToAbsolute(normalized), overwrite: true);
            AssetDatabase.ImportAsset(normalized, ImportAssetOptions.ForceUpdate);

            var loaded = AssetDatabase.LoadMainAssetAtPath(normalized);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult();
            result.AssetPath = normalized;
            return result;
        }

        [CliGroupCommand(Group, "Move (or rename via a new path) an asset to a new location under the authoring root. Preserves the asset's GUID.", Tags = new[] { "assets" })]
        public static AuthoringResult Move(
            [CliArg("asset", "Reference to the asset to move (path / guid / globalId).", Required = true)] ObjectRef asset,
            [CliArg("destination", "Destination asset path relative to the authoring root, including extension. The Assets/ prefix is optional.", Required = true)] string destination,
            [CliArg("dry-run", "If true, validate the move (via AssetDatabase.ValidateMoveAsset) without performing it.")] bool dryRun = false)
        {
            var sourcePath = ResolveAssetPath(asset);

            var dest = ProjectPaths.Resolve(destination, out var error);
            if (dest == null)
                throw new ArgumentException(error);

            // For a real move, create the destination's parent folder *before* validating: Unity's
            // ValidateMoveAsset reports failure ("move as a subdirectory") when the target folder
            // doesn't exist yet. Dry-run must not create folders, so it validates against the current
            // project state only.
            if (!dryRun)
                EnsureParentFolder(dest);

            var validation = AssetDatabase.ValidateMoveAsset(sourcePath, dest);
            if (!string.IsNullOrEmpty(validation))
                throw new ArgumentException($"Cannot move '{sourcePath}' to '{dest}': {validation}");

            if (dryRun)
                return new AuthoringResult { AssetPath = dest, Type = AssetDatabase.GetMainAssetTypeAtPath(sourcePath)?.Name };

            var moveError = AssetDatabase.MoveAsset(sourcePath, dest);
            if (!string.IsNullOrEmpty(moveError))
                throw new ArgumentException($"Failed to move '{sourcePath}' to '{dest}': {moveError}");

            var loaded = AssetDatabase.LoadMainAssetAtPath(dest);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult();
            result.AssetPath = dest;
            return result;
        }

        [CliGroupCommand(Group, "Copy an asset to a new path under the authoring root. The copy gets a fresh GUID.", Tags = new[] { "assets" })]
        public static AuthoringResult Copy(
            [CliArg("asset", "Reference to the asset to copy (path / guid / globalId).", Required = true)] ObjectRef asset,
            [CliArg("destination", "Destination asset path relative to the authoring root, including extension. The Assets/ prefix is optional.", Required = true)] string destination,
            [CliArg("confirm", "Required (true) only when overwriting an existing asset at the destination path.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate inputs and report what would be copied without writing anything.")] bool dryRun = false)
        {
            var sourcePath = ResolveAssetPath(asset);

            var dest = ProjectPaths.Resolve(destination, out var error);
            if (dest == null)
                throw new ArgumentException(error);

            var exists = AssetDatabase.LoadMainAssetAtPath(dest) != null;
            if (exists && !confirm)
                throw new ArgumentException($"An asset already exists at '{dest}'. Pass confirm=true to overwrite it.");

            if (dryRun)
                return new AuthoringResult { AssetPath = dest, Type = AssetDatabase.GetMainAssetTypeAtPath(sourcePath)?.Name };

            EnsureParentFolder(dest);

            // AssetDatabase.CopyAsset fails if the destination already exists, so delete it first when
            // the caller has confirmed an overwrite (the confirm guard above gates that).
            if (exists)
                AssetDatabase.DeleteAsset(dest);

            if (!AssetDatabase.CopyAsset(sourcePath, dest))
                throw new ArgumentException($"Failed to copy '{sourcePath}' to '{dest}'.");

            var loaded = AssetDatabase.LoadMainAssetAtPath(dest);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult();
            result.AssetPath = dest;
            return result;
        }

        [CliGroupCommand(Group, "Rename an asset in place (keeps it in the same folder, keeps its GUID).", Tags = new[] { "assets" })]
        public static AuthoringResult Rename(
            [CliArg("asset", "Reference to the asset to rename (path / guid / globalId).", Required = true)] ObjectRef asset,
            [CliArg("new-name", "New file name WITHOUT a folder path. The extension is preserved if omitted.", Required = true)] string newName,
            [CliArg("dry-run", "If true, validate the rename without performing it.")] bool dryRun = false)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("new-name is required.");
            if (newName.Contains("/") || newName.Contains("\\"))
                throw new ArgumentException("new-name must be a file name only, not a path. Use " + CommandNames.AssetMove + " to relocate an asset.");

            var sourcePath = ResolveAssetPath(asset);

            // Preserve the original extension when the agent omits one (AssetDatabase.RenameAsset expects no extension).
            var bareName = Path.GetFileNameWithoutExtension(newName);
            var extension = Path.GetExtension(sourcePath);
            var folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            var destPath = $"{folder}/{bareName}{extension}";

            if (AssetDatabase.LoadMainAssetAtPath(destPath) != null)
                throw new ArgumentException($"An asset already exists at '{destPath}'.");

            if (dryRun)
                return new AuthoringResult { AssetPath = destPath, Type = AssetDatabase.GetMainAssetTypeAtPath(sourcePath)?.Name };

            var renameError = AssetDatabase.RenameAsset(sourcePath, bareName);
            if (!string.IsNullOrEmpty(renameError))
                throw new ArgumentException($"Failed to rename '{sourcePath}': {renameError}");

            var loaded = AssetDatabase.LoadMainAssetAtPath(destPath);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult();
            result.AssetPath = destPath;
            return result;
        }

        [CliGroupCommand(Group, "Delete an asset from the project. Destructive: requires confirm=true.", Tags = new[] { "assets" })]
        public static AuthoringResult Delete(
            [CliArg("asset", "Reference to the asset to delete (path / guid / globalId).", Required = true)] ObjectRef asset,
            [CliArg("confirm", "Must be true to actually delete. Without it the command refuses (destructive guard).")] bool confirm = false,
            [CliArg("dry-run", "If true, report the asset that would be deleted without deleting it.")] bool dryRun = false)
        {
            var sourcePath = ResolveAssetPath(asset);

            // Capture the identity before deletion so the agent gets a record of what was removed.
            var loaded = AssetDatabase.LoadMainAssetAtPath(sourcePath);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult { Type = AssetDatabase.GetMainAssetTypeAtPath(sourcePath)?.Name };
            result.AssetPath = sourcePath;

            if (dryRun)
                return result;

            if (!confirm)
                throw new ArgumentException($"Refusing to delete '{sourcePath}'. Pass confirm=true to delete it (destructive, not undoable via Unity's Undo).");

            if (!AssetDatabase.DeleteAsset(sourcePath))
                throw new ArgumentException($"Failed to delete '{sourcePath}'.");

            return result;
        }

        [CliGroupCommand(Group, "Find assets by type and/or name and/or label, returning their path, GUID and type. At least one filter is required.", Tags = new[] { "assets" })]
        public static FindAssetsResult Find(
            [CliArg("type", "Type name to filter by (e.g. Material, GameObject, ScriptableObject, MyGame.GameConfig). Resolved to a System.Type and matched against each asset's actual main type.")] string type = null,
            [CliArg("name", "Name substring to filter by (AssetDatabase name filter).")] string name = null,
            [CliArg("label", "Asset label to filter by (AssetDatabase 'l:' filter).")] string label = null,
            [CliArg("search-in", "Folder to scope the search to, relative to the authoring root (default: the authoring root).")] string searchIn = null,
            [CliArg("limit", "Maximum number of results to return (default 200).")] int limit = 200)
        {
            if (string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("At least one of type, name, or label is required.");

            // The type filter is applied by post-filtering on each asset's actual loaded type rather
            // than via AssetDatabase's "t:" token: "t:" does NOT reliably match freshly-created/custom
            // ScriptableObject assets (it can return 0 even for "t:ScriptableObject"), though it works
            // for built-in types. Name/label searches do work, so build the query from those only.
            var filterParts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(name))
                filterParts.Add(name.Trim());
            if (!string.IsNullOrWhiteSpace(label))
                filterParts.Add($"l:{label.Trim()}");
            var filter = string.Join(" ", filterParts);

            // Default the search scope to the authoring root (not the whole project) so an agent only
            // ever discovers assets within its sandbox unless an explicit scope is given.
            var scopeError = (string)null;
            var scope = !string.IsNullOrWhiteSpace(searchIn)
                ? ProjectPaths.Resolve(searchIn, out scopeError)
                : ProjectPaths.AuthoringRoot;
            if (scope == null)
                throw new ArgumentException(scopeError);
            var searchFolders = new[] { scope };

            // Resolve the type filter up front so an unresolvable name fails fast with a clear message.
            Type typeFilter = null;
            if (!string.IsNullOrWhiteSpace(type))
            {
                typeFilter = ResolveTypeName(type.Trim());
                if (typeFilter == null)
                    throw new ArgumentException(
                        $"Could not resolve type '{type}'. Use a short name (e.g. Material) or a fully-qualified name (e.g. MyGame.GameConfig).");
            }

            // An empty name/label filter enumerates every asset under the search folders.
            var guids = AssetDatabase.FindAssets(filter ?? string.Empty, searchFolders);

            var result = new FindAssetsResult { Filter = filter };
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                var mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                if (typeFilter != null && (mainType == null || !typeFilter.IsAssignableFrom(mainType)))
                    continue;

                if (result.Assets.Count >= limit)
                {
                    result.Truncated = true;
                    break;
                }

                result.Assets.Add(new AuthoringResult
                {
                    AssetPath = assetPath,
                    Guid = guid,
                    Type = mainType?.Name
                });
            }

            result.Count = result.Assets.Count;
            return result;
        }

        /// <summary>
        /// Resolve a user-supplied type name (short or fully-qualified) to a <see cref="Type"/> by
        /// scanning non-dynamic loaded assemblies. Returns null when no match is found.
        /// </summary>
        private static Type ResolveTypeName(string typeName)
        {
            var direct = Type.GetType(typeName, throwOnError: false);
            if (direct != null)
                return direct;

            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                var byFullName = assembly.GetType(typeName, throwOnError: false);
                if (byFullName != null)
                    return byFullName;
            }

            return FindTypeByShortName(LoadedTypes(), typeName);
        }

        /// <summary>
        /// Every type in every loaded, non-dynamic assembly. An assembly whose type list is only
        /// partly loadable yields the types that did load (<see cref="ReflectionTypeLoadException"/>
        /// hands those back alongside nulls for the rest).
        /// </summary>
        static IEnumerable<Type> LoadedTypes()
        {
            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                foreach (var type in types)
                    yield return type;
            }
        }

        /// <summary>
        /// The candidate whose short name is <paramref name="typeName"/>, preferring one that derives
        /// from <see cref="Object"/>; the first non-Object match only when no Object-derived one
        /// exists; null when nothing matches.
        ///
        /// The preference is what keeps a collision deterministic. A short name is not unique: Unity 7
        /// ships UnityEditor.LightBaking.Material next to UnityEngine.Material, so a plain "first match
        /// wins" scan answers "Material" differently depending on whether UnityEngine.CoreModule or
        /// UnityEditor.CoreModule is enumerated first — which varies by project. Preferring the
        /// Object-derived type makes that answer UnityEngine.Material whatever the order.
        ///
        /// It is a preference and not a filter because the two callers do not require the same thing.
        /// create_asset needs an Object-derived type and rejects anything else itself, with a clearer
        /// error than "could not resolve". find_assets matches each asset's main type with
        /// IsAssignableFrom, so an interface is a legitimate filter there — find_assets(type:
        /// "IAssetMarker") returns every asset implementing it — and dropping non-Object types
        /// outright would make such a filter unresolvable by its short name.
        ///
        /// Fully-qualified names are resolved before this is reached and bypass it entirely.
        ///
        /// Order still decides between two Object-derived types sharing a short name, or between two
        /// non-Object ones when no Object-derived one exists. That ambiguity is real but has no better
        /// answer here; a fully-qualified name settles it.
        /// </summary>
        internal static Type FindTypeByShortName(IEnumerable<Type> candidates, string typeName)
        {
            Type fallback = null;
            foreach (var candidate in candidates)
            {
                // Null entries come from ReflectionTypeLoadException.Types, one per type that failed
                // to load.
                if (candidate == null || candidate.Name != typeName)
                    continue;

                if (typeof(Object).IsAssignableFrom(candidate))
                    return candidate;

                fallback ??= candidate;
            }

            return fallback;
        }

        /// <summary>
        /// Resolve an <see cref="ObjectRef"/> to a project-relative asset path, rejecting handles that
        /// do not resolve to an on-disk asset (scene objects have no asset path). The resolved path is
        /// confined to the authoring root so a GUID/globalId handle cannot make a destructive command
        /// (delete/move/copy/rename) operate on an asset outside the sandbox.
        /// </summary>
        private static string ResolveAssetPath(ObjectRef asset)
        {
            var assetPath = ObjectResolver.TryResolve(asset, out var obj, out var error)
                ? AssetDatabase.GetAssetPath(obj)
                : null;

            // Fallback: a path reference that points at an asset which exists on disk (a GUID is
            // registered) but whose object cannot be loaded — e.g. a just-moved asset whose object
            // isn't reloadable this frame, or an asset with an unresolved/broken script. We can still
            // operate on it by path, so honor the path rather than failing the whole command.
            if (string.IsNullOrEmpty(assetPath) && !string.IsNullOrEmpty(asset?.Path))
            {
                var candidate = ProjectPaths.Resolve(asset.Path, out _);
                if (candidate != null && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(candidate)))
                    assetPath = candidate;
            }

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException(error ?? $"Reference '{asset}' does not point at an on-disk asset.");

            // Confine to the authoring root. ProjectPaths.Resolve treats explicit "Assets/..." paths as
            // project-relative and rejects anything outside the configured root.
            var confined = ProjectPaths.Resolve(assetPath, out var confineError);
            if (confined == null)
                throw new ArgumentException(
                    $"Asset '{assetPath}' is outside the authoring root '{ProjectPaths.AuthoringRoot}': {confineError}");

            return confined;
        }

        /// <summary>Create the asset's parent folder chain if it does not yet exist.</summary>
        private static void EnsureParentFolder(string assetPath)
        {
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
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

        /// <summary>Convert a project-relative asset path to an absolute filesystem path.</summary>
        private static string ToAbsolute(string projectRelative)
        {
            return $"{ProjectPaths.ProjectRoot.Replace('\\', '/')}/{projectRelative}";
        }

        /// <summary>
        /// CLI-221: resolve the shader to assign to a newly-created <see cref="Material"/>.
        /// - An explicit <paramref name="shaderName"/> must resolve via <see cref="Shader.Find"/>;
        ///   a miss is a clear, actionable error (no null Material / null-ref downstream).
        /// - When omitted, default to the active render pipeline's lit shader: "Universal Render
        ///   Pipeline/Lit" when an SRP asset is active, else built-in "Standard". If that default
        ///   can't be found, fall back to "Standard"; if even that is missing, error clearly.
        /// </summary>
        private static Shader ResolveShader(string shaderName)
        {
            if (!string.IsNullOrWhiteSpace(shaderName))
            {
                var requested = Shader.Find(shaderName.Trim());
                if (requested == null)
                    throw new ArgumentException($"Shader '{shaderName}' not found.");
                return requested;
            }

            var usingSrp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            var defaultName = usingSrp ? "Universal Render Pipeline/Lit" : "Standard";

            var defaultShader = Shader.Find(defaultName);
            if (defaultShader != null)
                return defaultShader;

            // Fall back to the built-in Standard shader (always present in the Built-in RP, and a sane
            // last resort even under an SRP project that lacks the URP/Lit shader).
            var standard = Shader.Find("Standard");
            if (standard != null)
                return standard;

            throw new ArgumentException(
                $"Could not resolve a default shader ('{defaultName}' or 'Standard'). Pass an explicit shader= argument.");
        }

        /// <summary>
        /// Resolve a user-supplied type name to a <see cref="Type"/>. Tries the name as-is (covers
        /// fully-qualified names), then scans loaded assemblies for an exact full-name or short-name
        /// match (covers "Material" or a user type given without its namespace).
        /// </summary>
        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            // Trim once up front so every resolution path below (special-case + assembly/type scans)
            // sees the same normalized name — leading/trailing whitespace must not be accepted for one
            // type and rejected for another.
            typeName = typeName.Trim();

            // CLI-222: Unity 6 calls this UnityEngine.PhysicsMaterial; Unity 2022 calls it
            // UnityEngine.PhysicMaterial. Normalize both short and fully-qualified spellings to the
            // type available in the running editor so callers can use either name.
            if (typeName == "PhysicsMaterial" || typeName == "PhysicMaterial"
                || typeName == "UnityEngine.PhysicsMaterial" || typeName == "UnityEngine.PhysicMaterial")
            {
                return typeof(PipelinePhysicsMaterial);
            }

            var direct = Type.GetType(typeName, throwOnError: false);
            if (direct != null)
                return direct;

            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                var byFullName = assembly.GetType(typeName, throwOnError: false);
                if (byFullName != null)
                    return byFullName;
            }

            // Fall back to a short-name match across all loaded types, preferring
            // UnityEngine.Object-derived types — see FindTypeByShortName for why that preference makes
            // this deterministic rather than assembly-order dependent.
            return FindTypeByShortName(LoadedTypes(), typeName);
        }
    }
}
