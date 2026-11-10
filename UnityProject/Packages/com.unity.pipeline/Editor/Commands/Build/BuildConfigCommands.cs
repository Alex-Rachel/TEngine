using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Build
{
    /// <summary>
    /// Read/inspect build configuration for agents (CLI-204): enumerate targets, read/write the mutable
    /// <c>EditorUserBuildSettings</c> fields, and list Build Profile assets. Scene-list management and
    /// target switching are intentionally elsewhere (<c>add_scene_to_build</c>/<c>remove_scene_from_build</c>
    /// from CLI-189, and <c>switch_build_target</c>).
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class BuildConfigCommands
    {
        const string Group = "build";
        const string TargetGroup = "build.target";

        [CliGroupCommand(TargetGroup,
            "List the known BuildTarget values with their group and whether build support is installed.",
            MainThreadRequired = true,
            Tags = new[] { "build/targets" })]
        public static object List()
        {
            var targets = new List<BuildTargetInfo>();
            var seen = new HashSet<string>();

            foreach (BuildTarget value in Enum.GetValues(typeof(BuildTarget)))
            {
                // Exclude NoTarget / internal sentinels (negative) and deprecated/obsolete values.
                if ((int)value <= 0)
                    continue;

                var name = value.ToString();
                if (!seen.Add(name))
                    continue;

                var field = typeof(BuildTarget).GetField(name);
                if (field != null && field.GetCustomAttribute<ObsoleteAttribute>() != null)
                    continue;

                BuildTargetGroup group;
                try { group = BuildPipeline.GetBuildTargetGroup(value); }
                catch { continue; }

                bool installed;
                try { installed = BuildPipeline.IsBuildTargetSupported(group, value); }
                catch { installed = false; }

                targets.Add(new BuildTargetInfo
                {
                    Name = name,
                    DisplayName = DisplayNameFor(value),
                    TargetGroup = group.ToString(),
                    IsInstalled = installed
                });
            }

            return targets.OrderBy(t => t.TargetGroup).ThenBy(t => t.Name).ToList();
        }

        [CliGroupCommand(Group,
            "Read the current build configuration from EditorUserBuildSettings / EditorBuildSettings.",
            MainThreadRequired = true,
            Tags = new[] { "build/settings" })]
        public static object GetSettings()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);

            return new BuildSettingsResult
            {
                ActiveBuildTarget = target.ToString(),
                ActiveBuildTargetGroup = group.ToString(),
                DevelopmentBuild = EditorUserBuildSettings.development,
                AllowDebugging = EditorUserBuildSettings.allowDebugging,
                ConnectWithProfiler = EditorUserBuildSettings.connectProfiler,
                BuildScriptsOnly = EditorUserBuildSettings.buildScriptsOnly,
                SymlinkSources = EditorUserBuildSettings.symlinkSources,
                Il2CppCodeGeneration = ReadIl2CppCodeGeneration(group),
                Scenes = EditorBuildSettings.scenes.Select(s => new BuildSceneEntry
                {
                    Path = s.path,
                    Guid = s.guid.ToString(),
                    Enabled = s.enabled
                }).ToList()
            };
        }

        [CliGroupCommand(Group,
            "Set mutable EditorUserBuildSettings fields. Does NOT manage scenes (use " + CommandNames.SceneAddToBuild + " / " +
            CommandNames.SceneRemoveFromBuild + ") or switch target (use " + CommandNames.BuildTargetSwitch + "). Use dry-run to preview.",
            MainThreadRequired = true,
            Tags = new[] { "build/settings" })]
        public static object SetSettings(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] SetBuildSettingsInput settings = null,
            [CliArg("confirm", "Apply the changes. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return SetBuildSettingsResult.Fail("No 'settings' object provided.");

            if (!dryRun && !confirm)
                return SetBuildSettingsResult.Fail(
                    "Refused: this operation mutates the project. Re-run with confirm=true to apply, or dry-run=true to preview the change.");
            var result = new SetBuildSettingsResult { DryRun = dryRun, Success = true };
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

            // For each supplied field: change it (recording in Applied) only if it differs from the
            // current value; otherwise record it in Skipped as a no-op.
            Plan(result, "development-build", settings.DevelopmentBuild, EditorUserBuildSettings.development,
                v => EditorUserBuildSettings.development = v, dryRun);
            Plan(result, "allow-debugging", settings.AllowDebugging, EditorUserBuildSettings.allowDebugging,
                v => EditorUserBuildSettings.allowDebugging = v, dryRun);
            Plan(result, "connect-with-profiler", settings.ConnectWithProfiler, EditorUserBuildSettings.connectProfiler,
                v => EditorUserBuildSettings.connectProfiler = v, dryRun);
            Plan(result, "build-scripts-only", settings.BuildScriptsOnly, EditorUserBuildSettings.buildScriptsOnly,
                v => EditorUserBuildSettings.buildScriptsOnly = v, dryRun);
            Plan(result, "symlink-sources", settings.SymlinkSources, EditorUserBuildSettings.symlinkSources,
                v => EditorUserBuildSettings.symlinkSources = v, dryRun);

            if (!string.IsNullOrWhiteSpace(settings.Il2CppCodeGeneration))
            {
                if (!TryParseIl2Cpp(settings.Il2CppCodeGeneration, out var requested))
                    return SetBuildSettingsResult.Fail($"Invalid il2cpp-code-generation '{settings.Il2CppCodeGeneration}'. Use OptimizeSpeed | OptimizeSize.");

                var current = ReadIl2CppCodeGeneration(group);
                if (!string.Equals(current, requested.ToString(), StringComparison.Ordinal))
                {
                    if (!dryRun)
                        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.FromBuildTargetGroup(group), requested);
                    result.Applied["il2cpp-code-generation"] = requested.ToString();
                }
                else
                {
                    result.Skipped["il2cpp-code-generation"] = current;
                }
            }

            result.Message = dryRun
                ? $"Dry run — {result.Applied.Count} field(s) would change, {result.Skipped.Count} unchanged."
                : $"Applied {result.Applied.Count} field(s); {result.Skipped.Count} unchanged.";
            return result;
        }

        [CliGroupCommand(Group,
            "List Build Profile assets in the project (Unity 6 only). Returns feature_unavailable on earlier versions.",
            MainThreadRequired = true,
            Tags = new[] { "build/settings" })]
        public static object ListProfiles()
        {
            if (!BuildProfiles.IsSupported)
                return new { error = "Build Profiles require Unity 6 (6000.0) or later", code = "feature_unavailable" };

            return BuildProfiles.List();
        }

        // ---- Helpers ---------------------------------------------------------------------------

        static void Plan(SetBuildSettingsResult result, string name, bool? requested, bool current,
            Action<bool> set, bool dryRun)
        {
            if (!requested.HasValue)
                return;

            if (requested.Value != current)
            {
                if (!dryRun)
                    set(requested.Value);
                result.Applied[name] = requested.Value;
            }
            else
            {
                result.Skipped[name] = current;
            }
        }

        static string ReadIl2CppCodeGeneration(BuildTargetGroup group)
        {
            try
            {
                return PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.FromBuildTargetGroup(group)).ToString();
            }
            catch
            {
                return Il2CppCodeGeneration.OptimizeSpeed.ToString();
            }
        }

        static bool TryParseIl2Cpp(string value, out Il2CppCodeGeneration parsed)
        {
            return Enum.TryParse(value.Trim(), ignoreCase: true, out parsed)
                && Enum.IsDefined(typeof(Il2CppCodeGeneration), parsed);
        }

        /// <summary>Friendly label for common targets; falls back to the enum name.</summary>
        static string DisplayNameFor(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows: return "Windows (32-bit)";
                case BuildTarget.StandaloneWindows64: return "Windows 64-bit";
                case BuildTarget.StandaloneOSX: return "macOS";
                case BuildTarget.StandaloneLinux64: return "Linux 64-bit";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                case BuildTarget.tvOS: return "tvOS";
                case BuildTarget.WebGL: return "WebGL";
                case BuildTarget.WSAPlayer: return "Universal Windows Platform";
                case BuildTarget.PS4: return "PlayStation 4";
                case BuildTarget.PS5: return "PlayStation 5";
                case BuildTarget.XboxOne: return "Xbox One";
                case BuildTarget.Switch: return "Nintendo Switch";
                default: return target.ToString();
            }
        }
    }

    /// <summary>
    /// Reflection shim over the Unity 6 Build Profile API (<c>UnityEditor.Build.Profile.BuildProfile</c>),
    /// so this assembly compiles and runs on older editors without the type. Used by
    /// <c>list_build_profiles</c> and by <c>build</c>'s optional <c>profileName</c> activation.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class BuildProfiles
    {
        const string TypeName = "UnityEditor.Build.Profile.BuildProfile, UnityEditor.CoreModule";

        static readonly Type s_Type = ResolveType();

        public static bool IsSupported => s_Type != null;

        /// <summary>The resolved <c>BuildProfile</c> type, for shims that need its internal members.</summary>
        public static Type ProfileType => s_Type;

        static Type ResolveType()
        {
            var t = Type.GetType(TypeName);
            if (t != null)
                return t;

            // Fall back to a full-assembly scan — the assembly-qualified name can vary across versions.
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                t = asm.GetType("UnityEditor.Build.Profile.BuildProfile");
                if (t != null)
                    return t;
            }
            return null;
        }

        public static List<BuildProfileInfo> List()
        {
            var list = new List<BuildProfileInfo>();
            if (s_Type == null)
                return list;

            var active = GetActive();
            foreach (var guid in AssetDatabase.FindAssets("t:BuildProfile"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, s_Type);
                if (asset == null)
                    continue;

                list.Add(new BuildProfileInfo
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Guid = guid,
                    Platform = ReadPlatform(asset),
                    IsActive = active != null && ReferenceEquals(active, asset)
                });
            }
            return list;
        }

        public static BuildProfileInfo FindByName(string name)
        {
            return List().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The profile stored at a project-relative asset path, or null. Used after creation, where the path
        /// is known exactly and the name may collide with an existing profile.
        /// </summary>
        public static BuildProfileInfo FindByPath(string assetPath)
        {
            if (s_Type == null || string.IsNullOrEmpty(assetPath))
                return null;

            var asset = AssetDatabase.LoadAssetAtPath(assetPath, s_Type);
            if (asset == null)
                return null;

            var active = GetActive();
            return new BuildProfileInfo
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                Guid = AssetDatabase.AssetPathToGUID(assetPath),
                Platform = ReadPlatform(asset),
                IsActive = active != null && ReferenceEquals(active, asset)
            };
        }

        /// <summary>Activate the named profile via the API's static setter; returns false if unavailable.</summary>
        public static bool TrySetActive(BuildProfileInfo info)
        {
            if (s_Type == null || info == null)
                return false;

            var path = AssetDatabase.GUIDToAssetPath(info.Guid);
            var asset = AssetDatabase.LoadAssetAtPath(path, s_Type);
            if (asset == null)
                return false;

            try
            {
                // Unity 6 exposes a static settable 'activeProfile' (and/or a SetActiveBuildProfile method).
                var prop = s_Type.GetProperty("activeProfile", BindingFlags.Public | BindingFlags.Static);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(null, asset);
                    return true;
                }

                var method = s_Type.GetMethod("SetActiveBuildProfile", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    method.Invoke(null, new[] { asset });
                    return true;
                }
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // Rethrow Unity's own exception (e.g. activation refused in batch mode) instead of
                // reflection's generic "thrown by the target of an invocation", for every caller.
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }

            return false;
        }

        static UnityEngine.Object GetActive()
        {
            try
            {
                var prop = s_Type.GetProperty("activeProfile", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                    return prop.GetValue(null) as UnityEngine.Object;

                var method = s_Type.GetMethod("GetActiveBuildProfile", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                    return method.Invoke(null, null) as UnityEngine.Object;
            }
            catch { /* best effort */ }
            return null;
        }

        /// <summary>
        /// The platform GUID a profile asset targets (its serialized <c>m_PlatformId</c>), or null when
        /// unreadable. Finer-grained than <see cref="ReadPlatform"/>: Meta Quest and Android share a build
        /// target but not a platform id.
        /// </summary>
        public static string ReadPlatformId(UnityEngine.Object asset)
        {
            try
            {
                var field = s_Type?.GetField("m_PlatformId", BindingFlags.NonPublic | BindingFlags.Instance);
                var value = field?.GetValue(asset)?.ToString();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch { return null; }
        }

        static string ReadPlatform(UnityEngine.Object asset)
        {
            try
            {
                // Public property first, then the serialized backing field.
                var prop = s_Type.GetProperty("buildTarget", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                    return prop.GetValue(asset)?.ToString() ?? string.Empty;

                var field = s_Type.GetField("m_BuildTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    return field.GetValue(asset)?.ToString() ?? string.Empty;
            }
            catch { /* best effort */ }
            return string.Empty;
        }
    }
}
