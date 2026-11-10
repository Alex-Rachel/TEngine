using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Config;
using UnityEditor;
using UnityEditor.Build;

namespace Unity.Pipeline.Editor.BuildProcessors
{
    /// <summary>
    /// Reads and writes the Pipeline's scripting define symbols across the installed build
    /// targets. Defines are static per build target and cannot vary between a development and a
    /// release build of that target, so each setting has its own symbol, which the assemblies'
    /// define constraints combine with ENABLE_PROFILER. The symbols themselves and the
    /// list-parsing live in <see cref="PipelineBuildSymbols"/>, shared with the runtime gate.
    /// </summary>
    static class PipelineDefineSymbols
    {
        /// <summary>
        /// The build targets the defines have to cover: those whose platform support is installed
        /// in this editor, plus the active target. The active target's defines compile the editor
        /// and Play Mode, so they matter even in an editor that has no player support installed
        /// for it.
        /// </summary>
        public static IEnumerable<NamedBuildTarget> InstalledTargets()
        {
            var seen = new List<NamedBuildTarget>();

            try
            {
                seen.Add(ActiveTarget());
            }
            catch (ArgumentException)
            {
                // No NamedBuildTarget for the active group; the enumeration below still applies.
            }

            foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
            {
                if (IsObsolete(target))
                    continue;

                var group = BuildPipeline.GetBuildTargetGroup(target);
                if (group == BuildTargetGroup.Unknown || !BuildPipeline.IsBuildTargetSupported(group, target))
                    continue;

                NamedBuildTarget named;
                try
                {
                    named = NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!seen.Contains(named))
                    seen.Add(named);
            }

            // Server shares BuildTargetGroup.Standalone but is its own NamedBuildTarget with its
            // own define list, so enumerating BuildTarget alone never reaches it.
            if (seen.Contains(NamedBuildTarget.Standalone) && !seen.Contains(NamedBuildTarget.Server))
                seen.Add(NamedBuildTarget.Server);

            return seen;
        }

        /// <summary>
        /// Every build target a project can carry defines for, installed in this editor or not.
        /// Project settings are shared, so a change meant for every teammate has to reach targets
        /// this editor cannot build.
        /// </summary>
        public static IEnumerable<NamedBuildTarget> KnownTargets()
        {
            var known = new List<NamedBuildTarget>();

            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown || IsObsolete(group))
                    continue;

                NamedBuildTarget named;
                try
                {
                    named = NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!known.Contains(named))
                    known.Add(named);
            }

            if (!known.Contains(NamedBuildTarget.Server))
                known.Add(NamedBuildTarget.Server);

            return known;
        }

        /// <summary>
        /// Adds or removes the symbol for every installed target and returns the names of the
        /// targets that actually changed, so the caller can report what it touched.
        /// </summary>
        /// <param name="symbol">The symbol to add or remove.</param>
        /// <param name="enabled">True to define the symbol, false to undefine it.</param>
        public static string[] SetForAllTargets(string symbol, bool enabled)
        {
            return SetFor(InstalledTargets(), symbol, enabled);
        }

        /// <summary>Like <see cref="SetForAllTargets"/>, over the given targets.</summary>
        /// <param name="targets">The targets to change.</param>
        /// <param name="symbol">The symbol to add or remove.</param>
        /// <param name="enabled">True to define the symbol, false to undefine it.</param>
        internal static string[] SetFor(IEnumerable<NamedBuildTarget> targets, string symbol, bool enabled)
        {
            var changed = new List<string>();

            foreach (var target in targets)
            {
                var before = PlayerSettings.GetScriptingDefineSymbols(target);
                var after = enabled ? PipelineBuildSymbols.Add(before, symbol) : PipelineBuildSymbols.Remove(before, symbol);
                if (string.Equals(before, after, StringComparison.Ordinal))
                    continue;

                PlayerSettings.SetScriptingDefineSymbols(target, after);
                changed.Add(target.TargetName);
            }

            return changed.ToArray();
        }

        /// <summary>
        /// The installed targets whose defines disagree with the expected state, so a setting
        /// edited by hand or through a build profile does not pass unnoticed.
        /// </summary>
        /// <param name="symbol">The symbol to check.</param>
        /// <param name="expected">The state the symbol is expected to be in.</param>
        public static string[] TargetsWhereDiffers(string symbol, bool expected)
        {
            return TargetsWhereDiffers(InstalledTargets(), symbol, expected);
        }

        /// <summary>Like <see cref="TargetsWhereDiffers(string, bool)"/>, over the given targets.</summary>
        /// <param name="targets">The targets to check.</param>
        /// <param name="symbol">The symbol to check.</param>
        /// <param name="expected">The state the symbol is expected to be in.</param>
        internal static string[] TargetsWhereDiffers(IEnumerable<NamedBuildTarget> targets, string symbol, bool expected)
        {
            return targets
                .Where(t => PipelineBuildSymbols.Contains(PlayerSettings.GetScriptingDefineSymbols(t), symbol) != expected)
                .Select(t => t.TargetName)
                .ToArray();
        }

        /// <summary>The NamedBuildTarget matching the editor's active build target.</summary>
        public static NamedBuildTarget ActiveTarget()
        {
            return PipelineBuildSymbols.ActiveTarget();
        }

        private static bool IsObsolete(Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            return field == null || field.IsDefined(typeof(ObsoleteAttribute), false);
        }
    }
}
