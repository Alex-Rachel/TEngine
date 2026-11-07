using System;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Animation
{
    /// <summary>
    /// Presence guard for the optional <c>com.unity.timeline</c> package (CLI-214, Group C).
    ///
    /// The Editor assembly deliberately does NOT reference <c>Unity.Timeline</c> (an asmdef reference
    /// would break compilation in any project that doesn't have the package installed). Instead, the
    /// Timeline commands reach the package via reflection, and this guard reports whether the package's
    /// core type is loadable — the optional-package guard pattern for commands backed by a package that
    /// may not be present.
    ///
    /// The guard caches its result for the lifetime of the domain; a package add/remove triggers a
    /// domain reload, which resets the cache.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class TimelineGuard
    {
        /// <summary>The package id, surfaced in the not-installed error message.</summary>
        public const string PackageId = "com.unity.timeline";

        /// <summary>Assembly-qualified name of the package's root asset type.</summary>
        public const string TimelineAssetTypeName = "UnityEngine.Timeline.TimelineAsset, Unity.Timeline";

        private static bool? s_Installed;

        /// <summary>
        /// True when the Timeline package is installed (its <c>TimelineAsset</c> type resolves).
        /// </summary>
        public static bool IsInstalled()
        {
            if (s_Installed.HasValue)
                return s_Installed.Value;

            s_Installed = ResolveTimelineAssetType() != null;
            return s_Installed.Value;
        }

        /// <summary>
        /// The <c>UnityEngine.Timeline.TimelineAsset</c> <see cref="Type"/>, or null if the package is
        /// absent. Callers use this as the entry point for further reflection.
        /// </summary>
        public static Type ResolveTimelineAssetType() => Type.GetType(TimelineAssetTypeName, throwOnError: false);

        /// <summary>
        /// The structured "package not installed" error (HTTP 200, no exception) that every Timeline
        /// command returns when <see cref="IsInstalled"/> is false.
        /// </summary>
        public static ErrorResult NotInstalledError() =>
            ErrorResult.PackageStyle("package_not_found", $"{PackageId} is not installed");
    }
}
