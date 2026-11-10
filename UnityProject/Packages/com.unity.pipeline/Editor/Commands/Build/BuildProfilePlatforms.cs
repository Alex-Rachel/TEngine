using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Unity.Pipeline.Commands;
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Build
{
    /// <summary>
    /// Reflection shim over the Unity 6 Build Profile <em>platform</em> API
    /// (<c>UnityEditor.Build.Profile.BuildProfileModuleUtil</c>, which is internal). Backs
    /// <c>list_build_platforms</c> and the platform resolution and asset creation used by
    /// <c>create_build_profile</c> (AUTHAPI-82).
    /// <para>
    /// A Build Profile platform is <em>not</em> the same thing as a <c>BuildTarget</c>: several platforms can
    /// share one target. Meta Quest, Android XR and plain Android all report <c>BuildTarget.Android</c>, and
    /// the first two are <em>derived</em> platforms. That is why platforms are identified by id and why
    /// <see cref="TryResolve"/> prefers an exact id or display name over a target name.
    /// </para>
    /// <para>
    /// The platform id's <em>type</em> differs by version: a <c>string</c> on 6000.0, a <c>UnityEditor.GUID</c>
    /// from 6000.3. It is read from what <c>FindAllViewablePlatforms</c> returns, every member is looked up
    /// with that parameter type, and ids travel as their string form. Nothing here names
    /// <c>UnityEditor.GUID</c> at compile time.
    /// </para>
    /// <para>
    /// The cached reflection handles are resolved lazily rather than in a static initializer, so a statics
    /// cleanup on domain reload degrades to a re-resolve instead of permanently emptying the shim.
    /// </para>
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class BuildProfilePlatforms
    {
        const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static Type s_ModuleUtil;
        static Type s_TargetDiscovery;

        static Type ModuleUtil => s_ModuleUtil ??= ResolveType("UnityEditor.Build.Profile.BuildProfileModuleUtil");
        static Type TargetDiscovery => s_TargetDiscovery ??= ResolveType("UnityEditor.BuildTargetDiscovery");

        static MethodInfo s_FindAllViewablePlatforms;
        static Type s_IdType;
        static MethodInfo s_GetDisplayName;
        static MethodInfo s_IsModuleInstalled;
        static MethodInfo s_GetTargetAndSubtarget;
        static MethodInfo s_GetPlatformId;
        static MethodInfo s_IsDerivedPlatform;
        static MethodInfo s_CreateInstance;

        static MethodInfo FindAllViewablePlatforms => s_FindAllViewablePlatforms ??= ModuleUtil?.GetMethod("FindAllViewablePlatforms", AnyStatic, null, Type.EmptyTypes, null);

        /// <summary>The platform id's type: the element type of the list <c>FindAllViewablePlatforms</c> returns.</summary>
        static Type IdType => s_IdType ??= ElementType(FindAllViewablePlatforms?.ReturnType);

        static MethodInfo GetDisplayName => s_GetDisplayName ??= ById(ModuleUtil, "GetClassicPlatformDisplayName");
        static MethodInfo IsModuleInstalled => s_IsModuleInstalled ??= ById(ModuleUtil, "IsModuleInstalled");
        static MethodInfo GetTargetAndSubtarget => s_GetTargetAndSubtarget ??= ById(ModuleUtil, "GetBuildTargetAndSubtarget");
        static MethodInfo GetPlatformId => s_GetPlatformId ??= ModuleUtil?.GetMethod(
            "GetPlatformId", AnyStatic, null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null);
        static MethodInfo CreateInstance => s_CreateInstance ??= IdType == null ? null : BuildProfiles.ProfileType?.GetMethod(
            "CreateInstance", AnyStatic, null, new[] { IdType, typeof(string) }, null);

        /// <summary>
        /// Optional: 6000.0 has no such member, and derived platforms are inferred there instead (see
        /// <see cref="IsDerived"/>). Where Unity provides it, it is authoritative.
        /// </summary>
        static MethodInfo IsDerivedPlatform => s_IsDerivedPlatform ??= ById(TargetDiscovery, "BuildPlatformIsDerivedPlatform");

        static MethodInfo ById(Type owner, string name) =>
            owner == null || IdType == null ? null : owner.GetMethod(name, AnyStatic, null, new[] { IdType }, null);

        static Type ElementType(Type listType) =>
            listType != null && listType.IsGenericType ? listType.GetGenericArguments().FirstOrDefault() : null;

        /// <summary>How missing members are named: with the id type this editor uses, where known.</summary>
        static string IdName => IdType?.Name ?? "id";

        /// <summary>
        /// A platform id in the type this editor's API takes, from its string form: the string itself on
        /// 6000.0, a <c>UnityEditor.GUID</c> built through its string constructor from 6000.3.
        /// </summary>
        static object ToId(string id) => IdType == typeof(string) ? id : Activator.CreateInstance(IdType, id);

        /// <summary>
        /// The internal members this shim depends on that this editor does not have, by name. Checked as a
        /// whole rather than member by member at call time: a renamed <c>FindAllViewablePlatforms</c> would
        /// otherwise read as "no platforms", and a renamed <c>IsModuleInstalled</c> as "nothing installed" —
        /// wrong answers that look like valid data. <c>BuildPlatformIsDerivedPlatform</c> is optional and not
        /// listed.
        /// </summary>
        internal static IReadOnlyList<string> MissingMembers
        {
            get
            {
                var missing = new List<string>();
                if (ModuleUtil == null) missing.Add("BuildProfileModuleUtil");
                if (BuildProfiles.ProfileType == null) missing.Add("BuildProfile");

                if (ModuleUtil != null)
                {
                    if (FindAllViewablePlatforms == null || IdType == null) missing.Add("BuildProfileModuleUtil.FindAllViewablePlatforms()");
                    if (GetDisplayName == null) missing.Add($"BuildProfileModuleUtil.GetClassicPlatformDisplayName({IdName})");
                    if (IsModuleInstalled == null) missing.Add($"BuildProfileModuleUtil.IsModuleInstalled({IdName})");
                    if (GetTargetAndSubtarget == null) missing.Add($"BuildProfileModuleUtil.GetBuildTargetAndSubtarget({IdName})");
                    if (GetPlatformId == null) missing.Add("BuildProfileModuleUtil.GetPlatformId(BuildTarget, StandaloneBuildSubtarget)");
                }
                if (BuildProfiles.ProfileType != null && CreateInstance == null)
                    missing.Add($"BuildProfile.CreateInstance({IdName}, string)");

                return missing;
            }
        }

        /// <summary>True when this editor exposes every platform API the create/list/edit commands need.</summary>
        public static bool IsSupported => MissingMembers.Count == 0;

        /// <summary>
        /// Why the commands are unavailable, for their <c>feature_unavailable</c> error: an editor before
        /// Unity 6, or a Unity 6 editor whose internal Build Profile API has moved on from what this package
        /// supports — the second names the missing members, so it is not mistaken for the first.
        /// </summary>
        public static string UnavailableReason
        {
            get
            {
                if (ModuleUtil == null || BuildProfiles.ProfileType == null)
                    return "Build Profiles require Unity 6 (6000.0) or later";

                return "This editor's internal Build Profile API differs from what this package supports; missing: " +
                       string.Join(", ", MissingMembers);
            }
        }

        static Type ResolveType(string fullName)
        {
            var type = Type.GetType($"{fullName}, UnityEditor.CoreModule");
            if (type != null)
                return type;

            // The assembly-qualified name has moved between versions; fall back to a full scan.
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                type = asm.GetType(fullName);
                if (type != null)
                    return type;
            }
            return null;
        }

        /// <summary>Every platform the Build Profile window would offer, installed or not.</summary>
        public static List<BuildPlatformInfo> List()
        {
            var platforms = new List<BuildPlatformInfo>();
            foreach (var id in ViewablePlatformIds())
                platforms.Add(Describe(id));

            return platforms
                .OrderBy(p => p.BuildTarget, StringComparer.Ordinal)
                .ThenBy(p => p.DisplayName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Resolve a caller-supplied platform string to a platform id. Accepts, in priority order: a platform
        /// GUID, an exact display name ("Meta Quest"), or a <c>BuildTarget</c> name ("Android") — the last of
        /// which only matches a non-derived platform, so "Android" cannot silently select Meta Quest.
        /// </summary>
        /// <param name="platform">The caller's platform string.</param>
        /// <param name="subtarget">"Player" or "Server"; only disambiguates standalone desktop platforms.</param>
        /// <param name="resolved">The matched platform on success; null otherwise.</param>
        /// <param name="error">A message naming the valid platforms on failure; null on success.</param>
        public static bool TryResolve(string platform, string subtarget, out BuildPlatformInfo resolved, out string error)
        {
            resolved = null;
            error = null;

            if (string.IsNullOrWhiteSpace(platform))
            {
                error = "A 'platform' is required. Call " + CommandNames.BuildListPlatforms + " to see the available platforms.";
                return false;
            }

            var query = platform.Trim();
            var all = List();

            resolved = all.FirstOrDefault(p => string.Equals(p.Id, query, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(p => string.Equals(p.DisplayName, query, StringComparison.OrdinalIgnoreCase));

            if (resolved == null)
            {
                // Fall back to the BuildTarget name. Derived platforms (Meta Quest, Android XR) are excluded
                // so that an unqualified target name always resolves to the base platform.
                var byTarget = all
                    .Where(p => !p.IsDerived && string.Equals(p.BuildTarget, query, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (byTarget.Count > 1 && !string.IsNullOrWhiteSpace(subtarget))
                {
                    byTarget = byTarget
                        .Where(p => string.Equals(p.Subtarget, subtarget.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (byTarget.Count > 1)
                {
                    error = $"Platform '{platform}' is ambiguous — it matches {string.Join(", ", byTarget.Select(p => $"'{p.DisplayName}'"))}. " +
                            "Pass the exact display name or platform id, or set 'subtarget' to Player or Server.";
                    return false;
                }

                resolved = byTarget.FirstOrDefault();
            }

            if (resolved == null)
            {
                // Name the installed platforms — the ones a profile can actually be created for — and point
                // at showAll for the rest, rather than listing every known platform here.
                var installed = all.Where(p => p.IsInstalled).Select(p => $"'{p.DisplayName}'").ToList();
                error = $"Unknown platform '{platform}'. Installed platforms: " +
                        (installed.Count > 0 ? string.Join(", ", installed) : "none") +
                        ". Call " + CommandNames.BuildListPlatforms + " with show-all=true to see every known platform.";
                return false;
            }

            // A platform matched by id or display name already names its subtarget, so a caller-supplied
            // one that disagrees is a mistake rather than a refinement — refuse instead of silently
            // creating a profile for the other subtarget.
            if (!MatchesRequestedSubtarget(resolved, subtarget, all, out var subtargetError))
            {
                resolved = null;
                error = subtargetError;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether an explicitly requested subtarget agrees with the resolved platform. Only meaningful for
        /// standalone desktop platforms, which are the only ones that report Player/Server; everything else
        /// reports "Default" and ignores the argument.
        /// </summary>
        static bool MatchesRequestedSubtarget(BuildPlatformInfo resolved, string subtarget,
            List<BuildPlatformInfo> all, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(subtarget))
                return true;

            var requested = subtarget.Trim();
            if (string.Equals(resolved.Subtarget, requested, StringComparison.OrdinalIgnoreCase))
                return true;

            // The platform does not carry a Player/Server distinction at all — nothing to contradict.
            if (!IsStandaloneSubtarget(resolved.Subtarget))
                return true;

            if (!IsStandaloneSubtarget(requested))
            {
                error = $"Invalid subtarget '{subtarget}'. Use Player or Server.";
                return false;
            }

            var sibling = all.FirstOrDefault(p =>
                string.Equals(p.BuildTarget, resolved.BuildTarget, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Subtarget, requested, StringComparison.OrdinalIgnoreCase));

            error = $"Platform '{resolved.DisplayName}' is the {resolved.Subtarget} platform, which does not match subtarget '{subtarget}'. " +
                    (sibling != null
                        ? $"Use '{sibling.DisplayName}' instead, or drop 'subtarget'."
                        : $"Drop 'subtarget', or pick a {requested} platform from {CommandNames.BuildListPlatforms}.");
            return false;
        }

        static bool IsStandaloneSubtarget(string subtarget) =>
            string.Equals(subtarget, "Player", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subtarget, "Server", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Create the Build Profile asset at <paramref name="assetPath"/> for the given platform, via the
        /// internal <c>BuildProfile.CreateInstance(id, string)</c> overload. Throws if that overload is
        /// missing, rather than silently producing a profile with no platform set.
        /// </summary>
        public static void CreateAsset(BuildPlatformInfo platform, string assetPath)
        {
            var create = CreateInstance;
            if (create == null)
                throw new InvalidOperationException(
                    $"This editor does not expose BuildProfile.CreateInstance({IdName}, string); cannot create a Build Profile asset.");

            // Checked here rather than left to Unity: 6000.3 rejects an unknown id, but 6000.0 accepts it and
            // writes a profile for a platform that does not exist — which an overwrite would then copy onto the
            // profile it was replacing. The commands only pass ids from List(), but the shim does not rely on it.
            if (platform == null || !ViewablePlatformIds().Any(id => string.Equals(id.ToString(), platform.Id, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(
                    $"'{platform?.Id}' is not a platform this editor knows, so no Build Profile can be created for it. " +
                    "Call " + CommandNames.BuildListPlatforms + " with show-all=true for the valid platform ids.");

            try
            {
                create.Invoke(null, new[] { ToId(platform.Id), assetPath });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // Surface Unity's own exception (e.g. "A build profile must be created for a valid
                // platform.") rather than reflection's generic "thrown by the target of an invocation".
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
        }

        // ---- Reflection plumbing ---------------------------------------------------------------

        /// <summary>The platform ids, as the id objects this editor's API takes (a string or a GUID).</summary>
        static IEnumerable<object> ViewablePlatformIds()
        {
            var find = FindAllViewablePlatforms;
            if (find == null || IdType == null)
                yield break;

            if (find.Invoke(null, null) is IEnumerable ids)
            {
                foreach (var id in ids)
                {
                    if (id != null && IdType.IsInstanceOfType(id))
                        yield return id;
                }
            }
        }

        static BuildPlatformInfo Describe(object id)
        {
            var info = new BuildPlatformInfo
            {
                Id = id.ToString(),
                DisplayName = InvokeString(GetDisplayName, id) ?? id.ToString(),
                IsInstalled = InvokeBool(IsModuleInstalled, id),
                BuildTarget = string.Empty,
                Subtarget = string.Empty
            };

            // (BuildTarget, StandaloneBuildSubtarget) — the subtarget is only meaningful for standalone
            // desktop platforms; everything else reports "Default".
            object target = null, subtarget = null;
            try
            {
                var tuple = GetTargetAndSubtarget?.Invoke(null, new[] { id });
                if (tuple != null)
                {
                    var type = tuple.GetType();
                    target = type.GetField("Item1")?.GetValue(tuple);
                    subtarget = type.GetField("Item2")?.GetValue(tuple);
                    info.BuildTarget = target?.ToString() ?? string.Empty;
                    info.Subtarget = subtarget?.ToString() ?? string.Empty;
                }
            }
            catch { /* best effort — the platform is still usable by id */ }

            info.IsDerived = IsDerived(id, target, subtarget);
            return info;
        }

        /// <summary>
        /// Whether the platform derives from a base platform sharing its build target (Meta Quest from
        /// Android). Unity's own <c>BuildPlatformIsDerivedPlatform</c> decides where it exists. 6000.0 has no
        /// such member, so there a platform counts as derived when it is not the one <c>GetPlatformId</c>
        /// returns for its target: that is the base platform Unity picks for the target.
        /// </summary>
        static bool IsDerived(object id, object target, object subtarget)
        {
            if (IsDerivedPlatform != null)
                return InvokeBool(IsDerivedPlatform, id);

            if (target == null || subtarget == null)
                return false;

            try
            {
                var baseId = GetPlatformId?.Invoke(null, new[] { target, subtarget });
                return baseId != null && !string.Equals(baseId.ToString(), id.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Per-platform failures only: IsSupported has already established that each method exists, so a
        // throw here is Unity rejecting one platform, not a missing API reported as "false".
        static string InvokeString(MethodInfo method, object id)
        {
            try { return method?.Invoke(null, new object[] { id }) as string; }
            catch { return null; }
        }

        static bool InvokeBool(MethodInfo method, object id)
        {
            try { return method?.Invoke(null, new object[] { id }) is bool value && value; }
            catch { return false; }
        }
    }
}
