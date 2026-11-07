using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Build
{
    /// <summary>
    /// Build Profile authoring for agents (AUTHAPI-82): discover the platforms a profile can target, create
    /// a profile for one, and edit an existing profile's platform-neutral settings. Listing profiles and
    /// activating one for a build stay with <c>list_build_profiles</c> and <c>build --profile-name</c>, and
    /// switching the build target stays with <c>switch_build_target</c>.
    /// <para>
    /// Everything here goes through the reflection shims <see cref="BuildProfiles"/> and
    /// <see cref="BuildProfilePlatforms"/>, since most of the Build Profile API is internal.
    /// </para>
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class BuildProfileCommands
    {
        const string Group = "build";

        /// <summary>The Undo step name shown in the Edit menu for an edit of <paramref name="profileName"/>.</summary>
        static string EditUndoName(string profileName) => $"Edit Build Profile '{profileName}'";

        [CliGroupCommand(Group,
            "List the platforms a Build Profile can target, with their platform id (Unity 6 only). Lists only " +
            "platforms whose build support is installed; pass show-all=true for every known platform.",
            MainThreadRequired = true,
            Tags = new[] { "build/settings" })]
        public static object ListPlatforms(
            [CliArg("show-all", "Include platforms whose build support module is not installed.")] bool showAll = false)
        {
            if (!BuildProfilePlatforms.IsSupported)
                return new { error = BuildProfilePlatforms.UnavailableReason, code = "feature_unavailable" };

            var platforms = BuildProfilePlatforms.List();
            return showAll ? platforms : platforms.Where(p => p.IsInstalled).ToList();
        }

        [CliGroupCommand(Group,
            "Create a Build Profile asset for a platform (Unity 6 only). Call " + CommandNames.BuildListPlatforms + " first to " +
            "get a platform id. Returns feature_unavailable on earlier versions.",
            MainThreadRequired = true,
            Tags = new[] { "build/settings" })]
        public static object CreateProfile(
            [CliArg("name", "Profile name (e.g. \"Meta Quest\"). A Build Profile is named after its file, so this is also the file name and the name " + CommandNames.BuildRun + " --profile-name matches.", Required = true)] string name = "",
            [CliArg("platform", "Platform id, display name (\"Meta Quest\") or BuildTarget name (\"Android\"). See " + CommandNames.BuildListPlatforms + ".", Required = true)] string platform = "",
            [CliArg("subtarget", "Standalone only: Player | Server. Disambiguates desktop platforms; ignored elsewhere.")] string subtarget = "",
            [CliArg("folder", "Folder to create the profile in. Defaults to Assets/Settings/Build Profiles.")] string folder = "",
            [CliArg("activate", "Make the new profile the active Build Profile after creating it.")] bool activate = false,
            [CliArg("confirm", "Required (true) only when overwriting an existing profile of the same name in the folder.")] bool confirm = false,
            [CliArg("dry-run", "Validate the name, platform and folder and report what would be created, without writing anything.")] bool dryRun = false)
        {
            if (!BuildProfiles.IsSupported || !BuildProfilePlatforms.IsSupported)
                return new { error = BuildProfilePlatforms.UnavailableReason, code = "feature_unavailable" };

            var profileName = ValidateProfileName(name);

            if (!BuildProfilePlatforms.TryResolve(platform, subtarget, out var resolved, out var platformError))
                throw new ArgumentException(platformError);

            if (!resolved.IsInstalled)
                throw new ArgumentException(
                    $"Build support for '{resolved.DisplayName}' is not installed in this editor, so the profile would be unusable. " +
                    "Install the platform module via the Unity Hub, or pick an installed platform from " + CommandNames.BuildListPlatforms + ".");

            var assetPath = ResolveProfilePath(folder, profileName);

            // Occupancy, not loadability: a file Unity cannot load (corrupt, or on disk but not imported yet)
            // still occupies the path, and BuildProfile.CreateInstance would auto-rename around it to
            // "<name> 1.asset", leaving a stray sibling and a profile at the wrong path.
            var exists = IsPathOccupied(assetPath);
            if (exists && !confirm)
                throw new ArgumentException($"Something already exists at '{assetPath}'. Pass confirm=true to overwrite it.");

            // Overwriting copies onto the existing asset, which only works profile-to-profile. Checked here
            // rather than at write time so that dry-run reports it too.
            if (exists && AssetDatabase.LoadAssetAtPath(assetPath, BuildProfiles.ProfileType) == null)
            {
                var mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                throw new ArgumentException(mainType != null
                    ? $"The asset at '{assetPath}' is a {mainType.Name}, not a Build Profile, so it cannot be overwritten with one. Choose another name or folder."
                    : $"'{assetPath}' exists but Unity cannot load it as a Build Profile (it may be corrupt, or not imported yet), " +
                      "so it cannot be overwritten. Remove or repair it, or choose another name or folder.");
            }

            RefuseDuplicateName(profileName, assetPath);

            if (exists)
                RefusePlatformChangeOfActiveProfile(assetPath, resolved);

            if (activate)
                RefuseActivationThatSwitchesTarget(resolved);

            if (dryRun)
            {
                return new CreateBuildProfileResult
                {
                    Success = true,
                    DryRun = true,
                    AssetPath = assetPath,
                    Platform = resolved,
                    Message = $"Dry run — would create a '{resolved.DisplayName}' Build Profile at '{assetPath}'" +
                              (exists ? " (overwriting the existing asset)." : ".")
                };
            }

            EnsureParentFolder(assetPath);

            if (exists)
                ReplaceProfileAsset(resolved, assetPath);
            else
                BuildProfilePlatforms.CreateAsset(resolved, assetPath);
            AssetDatabase.ImportAsset(assetPath);

            var created = BuildProfiles.FindByPath(assetPath);
            if (created == null)
                throw new InvalidOperationException($"Build Profile creation reported success but no asset was found at '{assetPath}'.");

            // The profile exists from here on, so an activation failure must not surface as a failed
            // command — the caller would retry and hit "already exists". Report it as activated=false.
            var activated = false;
            string activationError = null;
            if (activate)
            {
                try
                {
                    activated = BuildProfiles.TrySetActive(created);
                }
                catch (Exception e)
                {
                    activationError = e.Message;
                }
                created.IsActive = activated;
            }

            var createdMessage = $"Created the '{resolved.DisplayName}' Build Profile at '{assetPath}'.";
            return new CreateBuildProfileResult
            {
                Success = true,
                AssetPath = assetPath,
                Platform = resolved,
                Profile = created,
                Activated = activated,
                Message = !activate ? createdMessage
                    : activated ? createdMessage + " It is now active."
                    : createdMessage + " It could not be activated" + (activationError != null ? $": {activationError}" : ".")
            };
        }

        [CliGroupCommand(Group,
            "Edit an existing Build Profile's scene list and scripting defines (Unity 6 only). Every setting is " +
            "optional; only the ones supplied change. The platform cannot be changed here: recreate the profile " +
            "with " + CommandNames.BuildCreateProfile + " and confirm=true. Returns feature_unavailable on earlier versions.",
            MainThreadRequired = true,
            Tags = new[] { "build/settings" })]
        public static object SetProfile(
            [CliArg("name", "Name of the profile to edit (see " + CommandNames.BuildListProfiles + ").", Required = true)] string name = "",
            [CliArg("folder", "Folder holding the profile. Only needed when several profiles share the name.")] string folder = "",
            [CliArg("override-global-scenes", "Build with the profile's own scene list instead of the global Build Settings list. Omit to leave unchanged.")] bool? overrideGlobalScenes = null,
            [CliArg("scenes", "Scene asset paths in build order, replacing the profile's scene list; all are enabled. An empty list clears it. Omit to leave unchanged.")] string[] scenes = null,
            [CliArg("scripting-defines", "Scripting define symbols replacing the profile's own. An empty list clears them. Omit to leave unchanged.")] string[] scriptingDefines = null,
            [CliArg("confirm", "Accepted for parity; not required, since the edit is undoable.")] bool confirm = false,
            [CliArg("dry-run", "Validate the changes and report what would change, without writing anything.")] bool dryRun = false)
        {
            if (!BuildProfiles.IsSupported)
                return new { error = "Build Profiles require Unity 6 (6000.0) or later", code = "feature_unavailable" };

            if (overrideGlobalScenes == null && scenes == null && scriptingDefines == null)
                throw new ArgumentException("Nothing to change. Pass at least one of override-global-scenes, scenes or scripting-defines.");

            var assetPath = ResolveExistingProfile(name, folder);
            var profile = AssetDatabase.LoadAssetAtPath(assetPath, BuildProfiles.ProfileType);

            // Validate every supplied value before anything is written, so a dry run reports all problems
            // and one bad value never leaves the profile half-edited.
            var requestedScenes = scenes != null ? ResolveScenes(scenes) : null;
            var requestedDefines = scriptingDefines != null ? ValidateScriptingDefines(scriptingDefines) : null;

            // Looked up only for the settings actually being edited, so an editor missing one property still
            // allows edits to the others. The override flag is also read to explain an unused scene list.
            var overrideProperty = overrideGlobalScenes.HasValue || requestedScenes != null ? ProfileProperty("overrideGlobalScenes") : null;
            var scenesProperty = requestedScenes != null ? ProfileProperty("scenes") : null;
            var definesProperty = requestedDefines != null ? ProfileProperty("scriptingDefines") : null;

            var result = new EditBuildProfileResult { DryRun = dryRun, AssetPath = assetPath };
            var writes = new List<Action>();

            // Captured before any write: on a dry run nothing changes, and on a real run the requested value
            // wins, so "requested ?? current" is the override the profile ends up with in both cases.
            bool? effectiveOverride = overrideProperty != null
                ? overrideGlobalScenes ?? (bool)overrideProperty.GetValue(profile)
                : (bool?)null;

            if (overrideGlobalScenes.HasValue)
            {
                var current = (bool)overrideProperty.GetValue(profile);
                if (current != overrideGlobalScenes.Value)
                {
                    result.Applied["override-global-scenes"] = overrideGlobalScenes.Value;
                    writes.Add(() => overrideProperty.SetValue(profile, overrideGlobalScenes.Value));
                }
                else
                {
                    result.Skipped["override-global-scenes"] = current;
                }
            }

            if (requestedScenes != null)
            {
                var current = (EditorBuildSettingsScene[])scenesProperty.GetValue(profile) ?? Array.Empty<EditorBuildSettingsScene>();
                if (!SameScenes(current, requestedScenes))
                {
                    result.Applied["scenes"] = requestedScenes.Select(s => s.path).ToArray();
                    writes.Add(() => scenesProperty.SetValue(profile, requestedScenes));
                }
                else
                {
                    result.Skipped["scenes"] = current.Select(s => s.path).ToArray();
                }
            }

            if (requestedDefines != null)
            {
                var current = (string[])definesProperty.GetValue(profile) ?? Array.Empty<string>();
                if (!current.SequenceEqual(requestedDefines, StringComparer.Ordinal))
                {
                    result.Applied["scripting-defines"] = requestedDefines;
                    writes.Add(() => definesProperty.SetValue(profile, requestedDefines));
                }
                else
                {
                    result.Skipped["scripting-defines"] = current;
                }
            }

            if (!dryRun && writes.Count > 0)
            {
                // Asset edits through setters are covered by Undo.RecordObject, unlike AssetDatabase
                // operations, so the edit reverts as one step.
                var undoName = EditUndoName(System.IO.Path.GetFileNameWithoutExtension(assetPath));
                using (new AuthoringUndoScope(undoName))
                {
                    Undo.RecordObject(profile, undoName);
                    BuildProfileUndoPersistence.RecordEdit(Undo.GetCurrentGroup(), AssetDatabase.AssetPathToGUID(assetPath));
                    foreach (var write in writes)
                        write();

                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
            }

            result.Success = true;
            result.Profile = BuildProfiles.FindByPath(assetPath);
            result.Message = (dryRun
                    ? $"Dry run — {result.Applied.Count} setting(s) would change, {result.Skipped.Count} unchanged."
                    : $"Applied {result.Applied.Count} setting(s); {result.Skipped.Count} unchanged.")
                + EditNotes(effectiveOverride, requestedScenes != null, result);
            return result;
        }

        // ---- Helpers ---------------------------------------------------------------------------

        /// <summary>
        /// Whether anything occupies a project-relative path: an asset the AssetDatabase knows, or a file or
        /// folder on disk it does not (yet). Either one makes <c>CreateInstance</c> pick another name.
        /// </summary>
        static bool IsPathOccupied(string assetPath)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets)))
                return true;

            var onDisk = System.IO.Path.Combine(ProjectPaths.ProjectRoot, assetPath);
            return System.IO.File.Exists(onDisk) || System.IO.Directory.Exists(onDisk);
        }

        /// <summary>
        /// The path of the existing profile <c>edit_build_profile</c> targets: by name within
        /// <paramref name="folder"/> when given, otherwise by name across the project, refusing a name that
        /// matches several profiles rather than editing an arbitrary one.
        /// <para>
        /// The profile is what the command writes, so it must lie under the authoring root however it was
        /// found. The <paramref name="folder"/> route is confined by <c>ProjectPaths.Resolve</c>; the by-name
        /// route searches the whole project, so a match outside the root is refused explicitly — otherwise an
        /// agent that narrowed its sandbox could still rewrite the project's shared profiles.
        /// </para>
        /// </summary>
        static string ResolveExistingProfile(string name, string folder)
        {
            var profileName = ValidateProfileName(name);

            if (!string.IsNullOrWhiteSpace(folder))
            {
                var path = ResolveProfilePath(folder, profileName);
                if (AssetDatabase.LoadAssetAtPath(path, BuildProfiles.ProfileType) == null)
                    throw new ArgumentException($"No Build Profile named '{profileName}' at '{path}'. Call {CommandNames.BuildListProfiles} to see the existing profiles.");
                return path;
            }

            var matches = BuildProfiles.List()
                .Where(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase))
                .Select(p => AssetDatabase.GUIDToAssetPath(p.Guid))
                .ToList();

            if (matches.Count == 0)
                throw new ArgumentException($"No Build Profile named '{profileName}'. Call {CommandNames.BuildListProfiles} to see the existing profiles.");

            if (matches.Count > 1)
                throw new ArgumentException(
                    $"Several Build Profiles are named '{profileName}': {string.Join(", ", matches.Select(p => $"'{p}'"))}. " +
                    "Pass 'folder' to choose one.");

            if (!ProjectPaths.IsUnderAuthoringRoot(matches[0]))
                throw new ArgumentException(
                    $"The Build Profile '{profileName}' is at '{matches[0]}', which is outside the authoring root " +
                    $"'{ProjectPaths.AuthoringRoot}', so it cannot be edited. Widen the root with {CommandNames.AuthoringSetRoot} to edit it.");

            return matches[0];
        }

        /// <summary>
        /// A public instance property of <c>BuildProfile</c>, looked up by name like the rest of the shim so
        /// this assembly does not bind to members an older 6000.x editor might not have.
        /// </summary>
        static PropertyInfo ProfileProperty(string name)
        {
            var property = BuildProfiles.ProfileType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
                throw new InvalidOperationException($"This editor's BuildProfile has no settable '{name}' property, so it cannot be edited here.");
            return property;
        }

        /// <summary>
        /// The requested scene list as build entries, all enabled. A scene in the list is a reference — the
        /// scene file is never written — so paths resolve project-wide like <c>build --scenes</c>, not
        /// confined to the authoring root, which sandboxes writes: a package scene or one outside a narrowed
        /// root is fine. Bare paths still resolve against the root, and the ".unity" extension is optional.
        /// Every rejected path is named at once, each with its own reason.
        /// </summary>
        static EditorBuildSettingsScene[] ResolveScenes(string[] scenes)
        {
            var entries = new List<EditorBuildSettingsScene>();
            var rejected = new List<string>();

            foreach (var raw in scenes)
            {
                var path = raw?.Trim();
                if (string.IsNullOrEmpty(path))
                {
                    rejected.Add("An empty path.");
                    continue;
                }

                // A suffix check, not GetExtension: a dotted name such as "Level.v2" has an extension
                // (".v2") yet still needs ".unity". Matches SceneCommands.ResolveScenePath.
                if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    path += ".unity";

                var normalized = ProjectPaths.ResolveReference(path, out var pathError);
                if (normalized == null)
                {
                    rejected.Add(pathError);
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(normalized) == null)
                {
                    rejected.Add($"No scene asset at '{normalized}'.");
                    continue;
                }

                entries.Add(new EditorBuildSettingsScene(normalized, true));
            }

            if (rejected.Count > 0)
                throw new ArgumentException($"Invalid scenes: {string.Join(" ", rejected.Select(r => r.TrimEnd('.') + "."))}");

            return entries.ToArray();
        }

        /// <summary>A C# preprocessor symbol: a letter or underscore, then letters, digits or underscores.</summary>
        static readonly System.Text.RegularExpressions.Regex s_DefineSymbol =
            new System.Text.RegularExpressions.Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>
        /// The trimmed define symbols, refused rather than rewritten when one is not a valid preprocessor
        /// symbol: an invalid define would otherwise surface as a compiler error on the next recompile.
        /// </summary>
        static string[] ValidateScriptingDefines(string[] defines)
        {
            var trimmed = defines.Select(d => d?.Trim() ?? string.Empty).ToArray();
            var invalid = trimmed.Where(d => !s_DefineSymbol.IsMatch(d)).ToList();

            if (invalid.Count > 0)
                throw new ArgumentException(
                    $"Invalid scripting define(s): {string.Join(", ", invalid.Select(d => $"'{d}'"))}. A define must start with a letter " +
                    "or underscore and contain only letters, digits and underscores.");

            return trimmed;
        }

        /// <summary>
        /// Whether two scene lists name the same scenes, in order, with the same enabled state. Scenes are
        /// compared by asset GUID rather than by path, so the answer never depends on how the filesystem treats
        /// case: on Windows two spellings of one path are the same scene, which a case-sensitive path comparison
        /// would call two. (The opposite case, <c>Level.unity</c> and <c>level.unity</c> as two scenes, cannot
        /// arise: the AssetDatabase refuses to import a path differing from an existing asset's only in case.)
        /// Through <c>AssetDatabase.AssetPathToGUID</c>, a string, rather than <c>EditorBuildSettingsScene.guid</c>,
        /// so nothing here names <c>UnityEditor.GUID</c> at compile time (see <see cref="BuildProfilePlatforms"/>).
        /// </summary>
        static bool SameScenes(EditorBuildSettingsScene[] current, EditorBuildSettingsScene[] requested)
        {
            if (current.Length != requested.Length)
                return false;

            for (var i = 0; i < current.Length; i++)
            {
                if (current[i].enabled != requested[i].enabled || !SameScene(current[i].path, requested[i].path))
                    return false;
            }
            return true;
        }

        static bool SameScene(string currentPath, string requestedPath)
        {
            var currentGuid = AssetDatabase.AssetPathToGUID(currentPath, AssetPathToGUIDOptions.OnlyExistingAssets);
            var requestedGuid = AssetDatabase.AssetPathToGUID(requestedPath, AssetPathToGUIDOptions.OnlyExistingAssets);

            // A scene the list still names but that no longer exists has no GUID; compare such entries by their
            // exact path, so the entry is replaced unless the caller asked for precisely the same stale path.
            if (string.IsNullOrEmpty(currentGuid) || string.IsNullOrEmpty(requestedGuid))
                return string.Equals(currentPath, requestedPath, StringComparison.Ordinal);

            return string.Equals(currentGuid, requestedGuid, StringComparison.Ordinal);
        }

        /// <summary>
        /// Consequences of the edit the caller would not otherwise see: a scene list that builds will ignore,
        /// and defines that change the active profile's script compilation.
        /// </summary>
        static string EditNotes(bool? effectiveOverride, bool scenesRequested, EditBuildProfileResult result)
        {
            var notes = string.Empty;

            if (scenesRequested && effectiveOverride == false)
                notes += " The profile's scene list is only used for builds when override-global-scenes is true, and it is false, " +
                         "so builds still use the global Build Settings list.";

            if (result.Applied.ContainsKey("scripting-defines") && result.Profile != null && result.Profile.IsActive)
                notes += " It is the active profile, so the new defines change script compilation; check " + CommandNames.ScriptRecompileStatus + " " +
                         "before relying on them.";

            return notes;
        }

        /// <summary>
        /// Refuse a profile whose name another profile elsewhere in the project already uses.
        /// <c>build --profile-name</c> resolves by name alone (case-insensitively, first match in
        /// <c>AssetDatabase.FindAssets</c> order), so a second profile of the same name would make it pick
        /// one arbitrarily. The same path is not a duplicate — that is the confirm-gated overwrite.
        /// </summary>
        static void RefuseDuplicateName(string profileName, string assetPath)
        {
            // The overwrite target is the asset at assetPath, compared by GUID rather than by path: paths
            // differing only in case ("Assets/Foo" vs "Assets/foo") are one file on Windows and two on a
            // case-sensitive filesystem, and a case-insensitive path comparison would wave the second through.
            var targetGuid = AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets);
            var clash = BuildProfiles.List().FirstOrDefault(p =>
                string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(p.Guid, targetGuid, StringComparison.Ordinal));

            if (clash != null)
                throw new ArgumentException(
                    $"A Build Profile named '{clash.Name}' already exists at '{AssetDatabase.GUIDToAssetPath(clash.Guid)}'. " +
                    CommandNames.BuildRun + " --profile-name matches by name, so a second one would be ambiguous. Choose another name, or pass " +
                    "that profile's folder with confirm=true to overwrite it.");
        }

        /// <summary>
        /// Refuse an overwrite that would change the platform of the active Build Profile. The overwrite
        /// copies onto the asset in place, which bypasses the build-target switch Unity performs when a
        /// profile of another platform is activated — leaving the active profile disagreeing with the
        /// editor's active build target.
        /// </summary>
        static void RefusePlatformChangeOfActiveProfile(string assetPath, BuildPlatformInfo resolved)
        {
            var existing = BuildProfiles.FindByPath(assetPath);
            if (existing == null || !existing.IsActive)
                return;

            var asset = AssetDatabase.LoadAssetAtPath(assetPath, BuildProfiles.ProfileType);
            var existingId = BuildProfiles.ReadPlatformId(asset);

            // Compare platform ids where readable: Meta Quest and Android share a build target, and
            // switching between them is still a platform change. Fall back to the build target.
            var samePlatform = existingId != null
                ? string.Equals(existingId, resolved.Id, StringComparison.OrdinalIgnoreCase)
                : string.Equals(existing.Platform, resolved.BuildTarget, StringComparison.OrdinalIgnoreCase);

            if (!samePlatform)
                throw new ArgumentException(
                    $"'{existing.Name}' is the active Build Profile. Overwriting it with '{resolved.DisplayName}' would change its " +
                    $"platform from {existing.Platform} without the build-target switch Unity performs on activation. " +
                    "Activate another profile first, or create this one under a new name.");
        }

        /// <summary>
        /// Refuse <c>activate=true</c> when the profile's platform is not the editor's active build target.
        /// <c>BuildProfile.SetActiveBuildProfile</c> performs the build-target switch itself in that case —
        /// a full reimport and domain reload that can take minutes, and that Unity disallows in batch mode.
        /// <c>switch_build_target</c> exists to run exactly that operation safely (confirm-gated, queued,
        /// with a status that survives the reload), so it is not re-created as a side effect of a create.
        /// Same-target activation is allowed, including a derived platform (Meta Quest on an Android project),
        /// which does not change the build target.
        /// </summary>
        static void RefuseActivationThatSwitchesTarget(BuildPlatformInfo resolved)
        {
            var activeTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
            var targetDiffers = !string.Equals(resolved.BuildTarget, activeTarget, StringComparison.OrdinalIgnoreCase);

            // Player and Server are separate standalone subtargets; switching between them is a switch too.
            var activeSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget.ToString();
            var subtargetDiffers = !targetDiffers
                && (resolved.Subtarget == "Player" || resolved.Subtarget == "Server")
                && !string.Equals(resolved.Subtarget, activeSubtarget, StringComparison.OrdinalIgnoreCase);

            if (!targetDiffers && !subtargetDiffers)
                return;

            // switch_build_target takes a target but no subtarget, so it only helps with a target change.
            if (targetDiffers)
                throw new ArgumentException(
                    $"activate=true would switch the build target from {activeTarget} to {resolved.BuildTarget}, a full reimport and " +
                    $"domain reload. Run {CommandNames.BuildTargetSwitch} with target={resolved.BuildTarget} and confirm=true first, wait for " +
                    CommandNames.BuildTargetSwitchStatus + " to report completion, then create the profile with activate=true. " +
                    "Or omit activate to create it without switching.");

            throw new ArgumentException(
                $"activate=true would switch the standalone subtarget from {activeSubtarget} to {resolved.Subtarget}, a full reimport " +
                "and domain reload. Omit activate to create the profile without switching, and switch the subtarget from the " +
                "Build Profiles window.");
        }

        /// <summary>
        /// Replace the contents of the Build Profile at <paramref name="assetPath"/>, for the
        /// <c>confirm=true</c> overwrite path.
        /// <para>
        /// <c>BuildProfile.CreateInstance</c> cannot target an existing path — it auto-renames to
        /// "&lt;name&gt; 1.asset" and leaves the original alone — so the replacement is built at a staging
        /// path first. It is then copied <em>onto</em> the existing asset rather than swapped in as a file:
        /// the destination is never deleted, so a failure at any step leaves the original intact, and it
        /// keeps its GUID, so references to it (including the active-profile setting) stay valid.
        /// </para>
        /// <para>
        /// The staging name is short and fixed-length rather than derived from the profile's, so an
        /// overwrite never needs a longer file name than creating the same profile fresh would.
        /// </para>
        /// Internal rather than private so tests can drive its failure path directly.
        /// </summary>
        internal static void ReplaceProfileAsset(BuildPlatformInfo platform, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath(assetPath, BuildProfiles.ProfileType);
            if (existing == null)
                throw new InvalidOperationException($"No Build Profile to overwrite at '{assetPath}'.");

            var directory = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            var staging = $"{directory}/{StagingPrefix}{Guid.NewGuid().ToString("N").Substring(0, 8)}.asset";

            try
            {
                BuildProfilePlatforms.CreateAsset(platform, staging);

                var replacement = AssetDatabase.LoadAssetAtPath(staging, BuildProfiles.ProfileType);
                if (replacement == null)
                    throw new InvalidOperationException($"Could not create the replacement Build Profile for '{assetPath}'.");

                // CopySerialized carries the staging asset's name across with everything else; restore the
                // destination's so the main object keeps matching its file name.
                var existingName = existing.name;
                EditorUtility.CopySerialized(replacement, existing);
                existing.name = existingName;

                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
            }
            finally
            {
                // The staging asset is scaffolding on every path, success included.
                if (AssetDatabase.LoadMainAssetAtPath(staging) != null)
                    AssetDatabase.DeleteAsset(staging);
            }
        }

        /// <summary>
        /// Folder separators, refused in a profile name on every host: <c>ProjectPaths.Resolve</c> turns both
        /// into '/', so either would move the profile into a subfolder instead of naming it. Not left to
        /// <c>Path.GetInvalidFileNameChars()</c>, which omits the backslash on macOS and Linux.
        /// </summary>
        static readonly char[] s_PathSeparators = { '/', '\\' };

        /// <summary>File-name prefix of the temporary asset an overwrite builds; see <see cref="ReplaceProfileAsset"/>.</summary>
        internal const string StagingPrefix = "_pipeline_replace_";

        /// <summary>
        /// The trimmed profile name, refused rather than rewritten when this command could not use it: a Build
        /// Profile is named after its file, and a silently altered name is one that <c>build --profile-name</c>
        /// would then fail to find.
        /// <para>
        /// Deliberately limited to what breaks the command itself on this host — a separator that would change
        /// the path, a character this host cannot put in a file name, a name Unity would not import. Whether
        /// the name also works on the team's other operating systems (Windows reserved names such as
        /// <c>CON</c>, characters only Windows refuses) is the caller's choice, as it is in the Build
        /// Profiles window; Windows refuses such a name loudly when it gets there.
        /// </para>
        /// </summary>
        static string ValidateProfileName(string name)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new ArgumentException("A profile 'name' is required.");

            if (trimmed.IndexOfAny(s_PathSeparators) >= 0)
                throw new ArgumentException(
                    $"Profile name '{name}' cannot be used as a file name: it contains a folder separator ('/' or '\\'), which " +
                    "would put the profile in a subfolder. Use 'folder' to choose where it goes.");

            var invalid = trimmed.Where(c => System.IO.Path.GetInvalidFileNameChars().Contains(c)).Distinct().ToList();
            if (invalid.Count > 0)
                throw new ArgumentException(
                    $"Profile name '{name}' cannot be used as a file name on this system: it contains " +
                    string.Join(" ", invalid.Select(c => char.IsControl(c) ? $"U+{(int)c:X4}" : $"'{c}'")) + ".");

            // A valid file name the AssetDatabase would never import: it ignores dotfiles, so the profile
            // could be written and then never found.
            if (trimmed.StartsWith(".", StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Profile name '{name}' starts with a dot, and Unity ignores files whose names start with one, so the profile would never be imported.");

            return trimmed;
        }

        /// <summary>
        /// "&lt;folder&gt;/&lt;name&gt;.asset", project-relative. <paramref name="folder"/> defaults to
        /// "Assets/Settings/Build Profiles". The file name always comes from the profile name, so a folder
        /// ending in ".asset" — a full file path passed by habit — is refused rather than guessed at. Other
        /// dotted folder names ("Profiles.v2") are legitimate and accepted.
        /// </summary>
        static string ResolveProfilePath(string folder, string profileName)
        {
            if (string.IsNullOrWhiteSpace(folder))
                folder = "Settings/Build Profiles";

            folder = folder.Trim().TrimEnd('/', '\\');
            if (folder.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"'folder' must be a folder, but '{folder}' is a file path. The profile's file name comes from 'name'.");

            var normalized = ProjectPaths.Resolve($"{folder}/{profileName}.asset", out var error);
            if (normalized == null)
                throw new ArgumentException(error);

            // Unity skips folders starting with '.' or ending with '~', so nothing inside one is imported.
            // Checked on the resolved, project-relative folder only: an absolute path's segments above the
            // project (a project under ~/.projects/, say) are not imported folders and must not count.
            var hidden = (System.IO.Path.GetDirectoryName(normalized) ?? string.Empty).Replace('\\', '/').Split('/')
                .FirstOrDefault(segment => segment != "." &&
                    (segment.StartsWith(".", StringComparison.Ordinal) || segment.EndsWith("~", StringComparison.Ordinal)));
            if (hidden != null)
                throw new ArgumentException(
                    $"'folder' contains '{hidden}', a folder Unity does not import (it starts with '.' or ends with '~'), so the profile would never be found.");

            return normalized;
        }

        static void EnsureParentFolder(string assetPath)
        {
            var parent = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
                return;

            CreateFolderRecursive(parent);
        }

        static void CreateFolderRecursive(string assetsPath)
        {
            if (AssetDatabase.IsValidFolder(assetsPath))
                return;

            var parent = System.IO.Path.GetDirectoryName(assetsPath)?.Replace('\\', '/');
            var name = System.IO.Path.GetFileName(assetsPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new ArgumentException($"Invalid folder path '{assetsPath}'.");

            CreateFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    /// <summary>
    /// Keeps a Build Profile's file in step with Undo and Redo of an <c>edit_build_profile</c> edit
    /// (AUTHAPI-82). The command saves its edit straight away, but Undo only restores the profile in
    /// memory, which would leave the file holding the change the user just undid until the project is
    /// next saved — visible to a command-line build or a teammate's pull.
    /// <para>
    /// Each edit records its Undo group and the profile it touched. After an undo or redo of group G, the
    /// recorded profiles in groups G and later that are dirty are saved. "G and later" is what makes a
    /// <c>batch</c> work: it collapses its operations' groups into its own first group, and the collapsed
    /// step carries a single name, so one undo can revert several profiles that no step name could list.
    /// Later groups were undone before G could be, and saved then, so only a profile this undo or redo
    /// changed is dirty; other profiles with unsaved changes of their own are never written.
    /// </para>
    /// <para>
    /// Only each profile's latest edit group is kept. A profile qualifies for group G exactly when it has
    /// some edit in G or later, which is exactly when its latest edit is in G or later — so the latest
    /// answers every query the full history would. That bounds the record by the number of profiles rather
    /// than the number of edits, with nothing to evict: dropping entries by count could forget an edit
    /// still on the undo stack.
    /// </para>
    /// <para>
    /// The record lives in <see cref="SessionState"/>, which, like the undo stack, survives a domain reload
    /// and ends with the Editor session.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class BuildProfileUndoPersistence
    {
        const string SessionKey = "Unity.Pipeline.BuildProfileEditUndoGroups";

        static BuildProfileUndoPersistence() => Undo.undoRedoEvent += OnUndoRedo;

        /// <summary>Record that <paramref name="profileGuid"/> was edited in Undo group <paramref name="undoGroup"/>.</summary>
        internal static void RecordEdit(int undoGroup, string profileGuid)
        {
            if (string.IsNullOrEmpty(profileGuid))
                return;

            var latest = LatestGroups();
            // Max rather than overwrite: groups only grow within a session, and if they ever did not, a
            // higher group can only widen the set of profiles saved — never drop one still on the stack.
            latest[profileGuid] = latest.TryGetValue(profileGuid, out var existing) ? Math.Max(existing, undoGroup) : undoGroup;

            SessionState.SetString(SessionKey, string.Join("\n", latest.Select(r => $"{r.Value}:{r.Key}")));
        }

        /// <summary>The profiles edited in Undo group <paramref name="undoGroup"/> or later.</summary>
        internal static IEnumerable<string> ProfilesFromGroup(int undoGroup) =>
            LatestGroups().Where(r => r.Value >= undoGroup).Select(r => r.Key);

        /// <summary>How many profiles the record holds; for tests.</summary>
        internal static int RecordCount => LatestGroups().Count;

        /// <summary>Forget every record; for tests.</summary>
        internal static void Clear() => SessionState.EraseString(SessionKey);

        /// <summary>Profile GUID → the latest Undo group it was edited in.</summary>
        static Dictionary<string, int> LatestGroups()
        {
            var latest = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in SessionState.GetString(SessionKey, string.Empty).Split('\n'))
            {
                var colon = line.IndexOf(':');
                if (colon > 0 && int.TryParse(line.Substring(0, colon), out var group))
                {
                    var guid = line.Substring(colon + 1);
                    latest[guid] = latest.TryGetValue(guid, out var seen) ? Math.Max(seen, group) : group;
                }
            }
            return latest;
        }

        static void OnUndoRedo(in UndoRedoInfo info)
        {
            if (!BuildProfiles.IsSupported)
                return;

            foreach (var guid in ProfilesFromGroup(info.undoGroup))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath(path, BuildProfiles.ProfileType);
                if (profile != null && EditorUtility.IsDirty(profile))
                    AssetDatabase.SaveAssetIfDirty(profile);
            }
        }
    }
}
