using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Pipeline.Config;
using Unity.Pipeline.Editor.BuildProcessors;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Carries a project from the settings file's <c>enableInBuilds</c> flag to the scripting
    /// defines that decide whether the Pipeline is compiled into a build. A project with the flag
    /// on keeps producing working development builds. Any other project had the server off, so
    /// it loses the release symbol, which alone enables the server in release builds. Runs once:
    /// the settings file then carries <see cref="RuntimePipelineConfig.CurrentSettingsVersion"/>.
    /// </summary>
    static class RuntimePipelineSettingsMigration
    {
        private const string LegacyPattern = @"""enableInBuilds""\s*:\s*(true|false)";
        private const string LegacyPatternWithSeparators = @"\s*""enableInBuilds""\s*:\s*(true|false)\s*,?";
        private const string VersionPattern = @"""settingsVersion""\s*:\s*(\d+)";
        private const string VersionPatternWithSeparators = @"\s*""settingsVersion""\s*:\s*\d+\s*,?";
        private const string SuspendedOnLoadKey = "Unity.Pipeline.RuntimePipelineSettingsMigration.SuspendedOnLoad";

        /// <summary>
        /// Stops <see cref="MigrateOnLoad"/> from migrating. Kept in SessionState so it holds across
        /// domain reloads; the Pipeline's own test run sets it so a reload mid-run doesn't migrate
        /// the defines and settings file its tests arrange.
        /// </summary>
        internal static bool SuspendedOnLoad
        {
            get => SessionState.GetBool(SuspendedOnLoadKey, false);
            set => SessionState.SetBool(SuspendedOnLoadKey, value);
        }

        [InitializeOnLoadMethod]
        internal static void MigrateOnLoad()
        {
            if (!SuspendedOnLoad)
                Migrate();
        }

        internal static void Migrate()
        {
            Migrate(PipelineDefineSymbols.KnownTargets().ToList());
        }

        /// <summary><see cref="Migrate()"/>, carrying the defines over on the given targets only.</summary>
        /// <param name="targets">The build targets whose defines are migrated.</param>
        internal static void Migrate(IReadOnlyCollection<NamedBuildTarget> targets)
        {
            var path = RuntimePipelineConfig.GetAbsoluteSettingsPath();
            string json = null;
            if (File.Exists(path))
            {
                if (!TryRead(path, out json) || IsCurrent(json))
                    return;
            }
            else if (!targets.Any(DefinesRelease))
            {
                return;
            }

            var legacy = json == null ? Match.Empty : Regex.Match(json, LegacyPattern);
            if (legacy.Success && legacy.Groups[1].Value == "true")
                PipelineDefineSymbols.SetFor(targets, PipelineBuildSymbols.Development, true);
            else
                PipelineDefineSymbols.SetFor(targets, PipelineBuildSymbols.Release, false);

            WriteMarked(path, json);
        }

        /// <summary>
        /// Marks the settings file as current, so <see cref="Migrate"/> leaves the release toggle
        /// alone from then on. Creates a file holding only the marker when none exists.
        /// </summary>
        internal static void MarkCurrentVersion()
        {
            var path = RuntimePipelineConfig.GetAbsoluteSettingsPath();
            string json = null;
            if (File.Exists(path) && (!TryRead(path, out json) || IsCurrent(json)))
                return;

            WriteMarked(path, json);
        }

        private static bool DefinesRelease(NamedBuildTarget target)
        {
            return PipelineBuildSymbols.Contains(PlayerSettings.GetScriptingDefineSymbols(target), PipelineBuildSymbols.Release);
        }

        private static bool IsCurrent(string json)
        {
            var match = Regex.Match(json, VersionPattern);
            return match.Success && int.Parse(match.Groups[1].Value) >= RuntimePipelineConfig.CurrentSettingsVersion;
        }

        private static bool TryRead(string path, out string json)
        {
            try
            {
                json = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: could not read runtime settings at '{path}' to migrate it ({ex.Message}).");
                json = null;
                return false;
            }
        }

        private static void WriteMarked(string path, string json)
        {
            var marker = $"\"settingsVersion\": {RuntimePipelineConfig.CurrentSettingsVersion}";
            string marked;
            if (json == null)
            {
                marked = "{\n    " + marker + "\n}";
            }
            else
            {
                var stripped = Regex.Replace(json, LegacyPatternWithSeparators, string.Empty);
                stripped = Regex.Replace(stripped, VersionPatternWithSeparators, string.Empty);
                marked = new Regex(@"^\s*\{").Replace(stripped, "{\n    " + marker + ",", 1);
                // Stripping the last key, or marking an empty object, leaves a comma before the brace.
                marked = Regex.Replace(marked, @",(\s*})", "$1");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, marked);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: could not write runtime settings at '{path}' after migrating it ({ex.Message}).");
            }
        }
    }
}
