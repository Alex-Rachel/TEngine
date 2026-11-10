#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
#endif
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Pipeline.Config
{
    /// <summary>
    /// The scripting define symbols that decide whether the Pipeline is compiled into a player,
    /// and exact-match helpers for the semicolon-separated lists they live in. Defined here rather
    /// than in the Editor assembly so the runtime gate and the settings UI cannot drift apart, and
    /// so they can be checked against the assemblies' define constraints.
    /// </summary>
    static class PipelineBuildSymbols
    {
        /// <summary>Compiles the Pipeline into builds that define ENABLE_PROFILER or UNITY_INCLUDE_INSTRUMENTATION.</summary>
        internal const string Development = "ENABLE_RUNTIME_PIPELINE_DEV";

        /// <summary>Compiles the Pipeline into builds that define neither ENABLE_PROFILER nor UNITY_INCLUDE_INSTRUMENTATION.</summary>
        internal const string Release = "ENABLE_RUNTIME_PIPELINE";

        /// <summary>
        /// Whether a semicolon-separated define list contains the symbol. Matching is exact:
        /// <see cref="Release"/> is a prefix of <see cref="Development"/>, so a substring test
        /// would confuse the two.
        /// </summary>
        /// <param name="defines">Semicolon-separated symbols, as PlayerSettings stores them.</param>
        /// <param name="symbol">The symbol to look for.</param>
        internal static bool Contains(string defines, string symbol)
        {
            return Split(defines).Contains(symbol, StringComparer.Ordinal);
        }

        /// <summary>Returns the define list with the symbol present, unchanged if it already is.</summary>
        /// <param name="defines">Semicolon-separated symbols.</param>
        /// <param name="symbol">The symbol to add.</param>
        internal static string Add(string defines, string symbol)
        {
            if (Contains(defines, symbol))
                return defines;

            return string.IsNullOrEmpty(defines) ? symbol : defines + ";" + symbol;
        }

        /// <summary>Returns the define list with the symbol absent, dropping empty entries.</summary>
        /// <param name="defines">Semicolon-separated symbols.</param>
        /// <param name="symbol">The symbol to remove.</param>
        internal static string Remove(string defines, string symbol)
        {
            if (!Contains(defines, symbol))
                return defines;

            return string.Join(";", Split(defines).Where(d => !string.Equals(d, symbol, StringComparison.Ordinal)));
        }

        private static IEnumerable<string> Split(string defines)
        {
            if (string.IsNullOrEmpty(defines))
                return Enumerable.Empty<string>();

            return defines.Split(';').Select(d => d.Trim()).Where(d => d.Length > 0);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Whether the symbol is defined for the target the editor is currently set to. Play Mode
        /// stands in for a development build, so the runtime gate reads the same setting a build
        /// of the active target would compile against.
        /// </summary>
        /// <param name="symbol">The symbol to look for.</param>
        internal static bool IsDefinedForActiveTarget(string symbol)
        {
            return Contains(PlayerSettings.GetScriptingDefineSymbols(ActiveTarget()), symbol);
        }

        /// <summary>The define list the editor's active build target compiles against.</summary>
        internal static NamedBuildTarget ActiveTarget()
        {
            return TargetFor(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget),
                EditorUserBuildSettings.standaloneBuildSubtarget);
        }

        /// <summary>
        /// The define list a build compiles against. A Dedicated Server build shares
        /// <see cref="BuildTargetGroup.Standalone"/> but has its own list.
        /// </summary>
        /// <param name="group">The build's target group.</param>
        /// <param name="subtarget">The build's standalone subtarget; ignored for other groups.</param>
        internal static NamedBuildTarget TargetFor(BuildTargetGroup group, StandaloneBuildSubtarget subtarget)
        {
            return group == BuildTargetGroup.Standalone && subtarget == StandaloneBuildSubtarget.Server
                ? NamedBuildTarget.Server
                : NamedBuildTarget.FromBuildTargetGroup(group);
        }
#endif
    }
}
