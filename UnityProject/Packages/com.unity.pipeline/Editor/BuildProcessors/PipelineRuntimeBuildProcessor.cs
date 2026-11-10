using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Pipeline.Config;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Unity.Pipeline.Editor.BuildProcessors
{
    /// <summary>
    /// Build processor for runtime Pipeline support. Four responsibilities:
    ///  - Validates the RuntimePipelineConfig settings (JSON-authored, read via
    ///    RuntimePipelineConfig.Load()) before allowing builds with runtime Pipeline enabled.
    ///  - Bakes the current settings into a transient RuntimePipelineConfig Resources asset so a
    ///    Player build can find them (the authored copy stays in ProjectSettings/, never here).
    ///  - Bakes the project's method reload scope (Assets + loaded package locations) into a
    ///    generated RuntimePipelineBuildInfo asset. A running Player cannot resolve the project
    ///    layout, so the absolute roots it is allowed to method reload from must be captured at build
    ///    time.
    ///  - Keeps an editor-only Run In Background override (CLI-1432) out of builds: puts the project's
    ///    own value back before the build (OnPreprocessBuild) and re-applies the override afterwards
    ///    (OnPostprocessBuild). See <see cref="RestoreRunInBackgroundForBuild"/>.
    ///  Both generated assets are purged at the very start of every build (before either is
    ///  possibly rewritten) and deleted again after a successful build, so machine-specific/
    ///  duplicated data never lingers in the project — including a stale, possibly-enabled asset
    ///  left behind by a build that was interrupted before OnPostprocessBuild could run (Unity
    ///  never invokes it for a failed or cancelled build).
    /// </summary>
#if UNITY_6000_3_OR_NEWER
    class PipelineRuntimeBuildProcessor : IPreprocessBuildWithContext, IPostprocessBuildWithReport
#else
    class PipelineRuntimeBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
#endif
    {
        /// <summary>Callback ordering relative to other build processors (0 = default).</summary>
        public int callbackOrder => 0;

        private const string ConfigAssetPath = "Assets/Settings/Pipeline/Resources/RuntimePipelineConfig.asset";
        private const string BuildInfoAssetPath = "Assets/Settings/Pipeline/Resources/RuntimePipelineBuildInfo.asset";


        /// <summary>Verify bundled DLL integrity and validate runtime pipeline configuration before a build.</summary>
#if UNITY_6000_3_OR_NEWER
        /// <param name="ctx">The build callback context.</param>
        public void OnPreprocessBuild(BuildCallbackContext ctx)
#else
        /// <param name="report">The build report.</param>
        public void OnPreprocessBuild(BuildReport report)
#endif
        {
#if UNITY_6000_3_OR_NEWER
            var report = ctx?.Report;
#endif
            // First, before any exit path below: the project's own Run In Background value must be
            // back in place before Unity reads Player Settings for the build, whatever else happens.
            RestoreRunInBackgroundForBuild();

            var buildOptions = report?.summary.options;
            var namedTarget = report == null
                ? PipelineBuildSymbols.ActiveTarget()
                : TargetFor(report.summary);

            Preprocess(namedTarget, buildOptions);
        }

        /// <summary>
        /// The define list the build described by <paramref name="summary"/> compiles against. Only a
        /// Standalone build has a subtarget; reading it for any other platform throws.
        /// </summary>
        /// <param name="summary">The build's summary.</param>
        internal static NamedBuildTarget TargetFor(BuildSummary summary)
        {
            var subtarget = summary.platformGroup == BuildTargetGroup.Standalone
                ? summary.GetSubtarget<StandaloneBuildSubtarget>()
                : StandaloneBuildSubtarget.Player;
            return PipelineBuildSymbols.TargetFor(summary.platformGroup, subtarget);
        }

        /// <summary>
        /// Purges the generated assets, then bakes them again when this build contains the Pipeline.
        /// </summary>
        /// <param name="namedTarget">The define list this build compiles against.</param>
        /// <param name="buildOptions">The options this build runs with, null when they are unknown.</param>
        internal void Preprocess(NamedBuildTarget namedTarget, BuildOptions? buildOptions)
        {
            // Purge any leftovers from a previous, interrupted build before anything else in this
            // method — every exit path below (no config, disabled, not a development build,
            // validation failure) must start from a clean slate. OnPostprocessBuild is not invoked
            // for a failed/cancelled build, so it can never be relied on for this;
            // this purge is the only mechanism that actually guarantees it. Without it, a stale
            // *enabled* asset left by an earlier interrupted build would still get packaged into a
            // build that is supposed to have Pipeline disabled.
            DeleteGeneratedAssetsIfPresent();

            // Integrity gate: fail the build if a bundled Roslyn DLL was swapped or modified.
            VerifyBundledChecksums();

            // A build that will not contain the Pipeline assemblies cannot read the generated
            // Resources assets, so baking them would ship dead, machine-specific data.
            if (!PipelineIsInBuild(namedTarget, buildOptions))
            {
                Debug.Log("Pipeline: this build does not include the Pipeline (Project Settings > " +
                    "Pipeline > Runtime), so its assemblies and settings are excluded from it.");
                return;
            }

            // An absent settings file means nothing was customized, not that the Pipeline is off:
            // whether it is in the build is decided by the scripting defines above.
            var config = RuntimePipelineConfig.Load() ?? ScriptableObject.CreateInstance<RuntimePipelineConfig>();

            try
            {
                var validationResult = config.Validate();
                if (!validationResult.IsValid)
                {
                    throw new BuildFailedException($"Pipeline: Runtime configuration validation failed: {validationResult.Message}");
                }

                if (validationResult.Level == "warning")
                {
                    Debug.LogWarning($"Pipeline: Runtime configuration warning: {validationResult.Message}");
                }

                // Push signing: ensure the project key exists and bake its public half into the
                // build info, so the player rejects any method-reload push not signed by the matching
                // private key. Log the fingerprint so a later key mismatch is diagnosable from the
                // build log alone.
                var privateKey = PushSigningKey.LoadOrCreate(PushSigningKey.DefaultDirectory, out var keyCreated);
                var pushPublicKey = Unity.Pipeline.MethodReload.PushEnvelope.DerivePublicKey(privateKey);
                Debug.Log($"Pipeline: push-signing key {(keyCreated ? "GENERATED" : "loaded")} " +
                          $"(fingerprint {Unity.Pipeline.MethodReload.PushEnvelope.Fingerprint(pushPublicKey)}, " +
                          $"Library/Pipeline/{PushSigningKey.KeyFileName}). This build only accepts pushes signed with it.");

                WriteConfigAsset(config);
                WriteBuildInfoAsset(pushPublicKey);

                Debug.Log("Pipeline: Runtime server ENABLED in build.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        /// <summary>
        /// Whether this build will contain the Pipeline runtime assemblies, mirroring the define
        /// constraint they carry: a build that defines <c>UNITY_INCLUDE_INSTRUMENTATION</c> or
        /// <c>ENABLE_PROFILER</c> needs <see cref="PipelineBuildSymbols.Development"/>, any other
        /// build needs <see cref="PipelineBuildSymbols.Release"/>.
        /// </summary>
        /// <param name="namedTarget">The define list this build compiles against.</param>
        /// <param name="buildOptions">The options this build runs with, null when they are unknown.</param>
        private static bool PipelineIsInBuild(NamedBuildTarget namedTarget, BuildOptions? buildOptions)
        {
            var symbol = InstrumentedOrProfiledBuild(namedTarget, buildOptions)
                ? PipelineBuildSymbols.Development
                : PipelineBuildSymbols.Release;

            return PipelineBuildSymbols.Contains(PlayerSettings.GetScriptingDefineSymbols(namedTarget), symbol);
        }

        /// <summary>
        /// Whether <c>UNITY_INCLUDE_INSTRUMENTATION</c> or <c>ENABLE_PROFILER</c> will be defined
        /// for this build, which is what the assemblies' define constraints pair each settings
        /// toggle with. Both symbols cover the same managed-code variants from 6000.6; below that
        /// only ENABLE_PROFILER exists, and it tracks the Development Build setting.
        /// </summary>
        /// <param name="namedTarget">The target this build is for.</param>
        /// <param name="buildOptions">The options this build runs with, null when they are unknown.</param>
        internal static bool InstrumentedOrProfiledBuild(NamedBuildTarget namedTarget, BuildOptions? buildOptions)
        {
#if !UNITY_7000_0_OR_NEWER // ENABLE_PROFILER is ONLY defined for Instrumented managed code variant in Unity 7+ and is no longer tied to development builds
            // Authoritative: BuildPipeline.BuildPlayer makes a development player from this flag
            // whoever set it, including a caller outside this package with the Build Settings
            // toggle off.
            if (buildOptions is { } options)
                return (options & BuildOptions.Development) != 0 || InstrumentedVariant(namedTarget);

            if (EditorUserBuildSettings.development)
                return true;

            if (Commands.Build.BuildCommand.ActiveScriptedBuildOptions is { } scriptedOptions
                && (scriptedOptions & BuildOptions.Development) != 0)
                return true;
#endif // UNITY_7000_0_OR_NEWER

            return InstrumentedVariant(namedTarget);
        }

        /// <summary>
        /// Whether the target's managed-code variant carries instrumentation, which defines the
        /// same symbols a development build does regardless of the Development Build setting.
        /// </summary>
        /// <param name="namedTarget">The target this build is for.</param>
        private static bool InstrumentedVariant(NamedBuildTarget namedTarget)
        {
#if UNITY_6000_6_OR_NEWER
            // From 6000.6 the Debug, Checked and Instrumented variants each define both
            // UNITY_INCLUDE_INSTRUMENTATION and ENABLE_PROFILER. Release defines neither, and is
            // the default, so this only admits a project that opted into one of the other three.
            return PlayerSettings.GetManagedCodeVariant(namedTarget) <= ManagedCodeVariant.Instrumented;
#else
            return false;
#endif
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            ReapplyRunInBackgroundAfterBuild();
            DeleteGeneratedAssetsIfPresent();
        }

        /// <summary>
        /// Ensures a build is never polluted by an editor-only Run In Background override (CLI-1432).
        ///
        /// <see cref="EditorPipelineConfig"/> can record an active override on
        /// <see cref="Application.runInBackground"/> (from <c>editor.playmode set_run_in_background</c>, or
        /// from the "Ensure editor runs in background in PlayMode" setting while in Play mode). That
        /// override exists purely to keep an unfocused Editor's player loop ticking; it must never
        /// change what a Player build ships with, since Application.runInBackground in the Editor is
        /// literally the project's own Player Settings value, baked into the build.
        ///
        /// Pre-build: <see cref="PipelineServerStartup.RestoreProjectRunInBackground"/> puts the
        /// project's own recorded value back before Unity reads Player Settings for the build,
        /// without ending the override. Post-build: <see cref="ReapplyRunInBackgroundAfterBuild"/>
        /// re-applies the override, if the record still calls for it.
        ///
        /// Unity does not invoke <see cref="IPostprocessBuildWithReport.OnPostprocessBuild"/> for a
        /// build that fails or is cancelled, so a build interrupted between these two steps would
        /// otherwise leave the live value stuck at the project's own setting with the record still
        /// saying "overridden". The session flag tells <see cref="PipelineServerStartup.ReconcileRunInBackground(bool)"/>
        /// (which also runs on every Play mode entry and Editor/domain-reload load) that a false live value
        /// is the build's doing rather than the user's, so it re-applies the override.
        ///
        /// Internal and side-effect-scoped so tests can call it directly, without a real
        /// <see cref="BuildReport"/>.
        /// </summary>
        internal static void RestoreRunInBackgroundForBuild() => PipelineServerStartup.RestoreProjectRunInBackground();

        /// <summary>
        /// Re-apply the Run In Background override, if the record still calls for it, once the
        /// build is done, then clear the temporarily-restored session flag. Not in Play mode at this
        /// point, since a build cannot run while playing. The reconcile runs BEFORE the flag is
        /// cleared: it is the flag that tells it the false live value was the build's doing.
        /// </summary>
        internal static void ReapplyRunInBackgroundAfterBuild()
        {
            PipelineServerStartup.ReconcileRunInBackground(playing: false);
            SessionState.SetBool(PipelineServerStartup.RunInBackgroundTemporarilyRestoredKey, false);
        }

        /// <summary>
        /// Absolute roots considered in-scope for runtime method reload: the project's Assets folder
        /// plus the resolved location of every package loaded into the project (a local package may
        /// live anywhere on disk, not only under Packages).
        /// </summary>
        /// <returns>Absolute paths of the project's Assets folder and every loaded package.</returns>
        public static List<string> CollectProjectRoots()
        {
            var roots = new List<string> { Path.GetFullPath(Application.dataPath) };

            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (!string.IsNullOrEmpty(package.resolvedPath))
                {
                    roots.Add(Path.GetFullPath(package.resolvedPath));
                }
            }

            return roots;
        }

        private static void WriteConfigAsset(RuntimePipelineConfig sourceConfig)
        {
            var folder = Path.GetDirectoryName(ConfigAssetPath).Replace('\\', '/');
            CreateFolderRecursive(folder);

            // No DeleteGeneratedAssetIfPresent() call here: OnPreprocessBuild already purged both
            // paths before any of this method's caller ran, so this path is guaranteed clear.

            var baked = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            baked.port = sourceConfig.port;
            baked.enableAuditLogging = sourceConfig.enableAuditLogging;
            baked.autoStart = sourceConfig.autoStart;
            baked.maxWorkItemsPerFrame = sourceConfig.maxWorkItemsPerFrame;
            AssetDatabase.CreateAsset(baked, ConfigAssetPath);
            AssetDatabase.SaveAssets();
        }

        private static void WriteBuildInfoAsset(string pushPublicKey)
        {
            var folder = Path.GetDirectoryName(BuildInfoAssetPath).Replace('\\', '/');
            CreateFolderRecursive(folder);

            // No DeleteGeneratedAssetIfPresent() call here: OnPreprocessBuild already purged both
            // paths before any of this method's caller ran, so this path is guaranteed clear.

            var buildInfo = ScriptableObject.CreateInstance<RuntimePipelineBuildInfo>();
            buildInfo.allowedReloadRoots = CollectProjectRoots();
            buildInfo.pushPublicKey = pushPublicKey ?? "";
            AssetDatabase.CreateAsset(buildInfo, BuildInfoAssetPath);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Delete both transient build-only assets if either is present, however they got there —
        /// baked by a previous build (normal cleanup) or left behind by one that never reached
        /// OnPostprocessBuild (crash, force-quit, cancelled build).
        /// </summary>
        private static void DeleteGeneratedAssetsIfPresent()
        {
            DeleteGeneratedAssetIfPresent(ConfigAssetPath);
            DeleteGeneratedAssetIfPresent(BuildInfoAssetPath);
        }

        /// <summary>
        /// Untyped load + a raw file-existence fallback, deliberately not a typed
        /// AssetDatabase.LoadAssetAtPath&lt;T&gt;: a typed load returns null — leaving a stale file
        /// untouched on disk, inside Resources, where a Player build would still package it — for
        /// any asset whose script reference broke after a package upgrade, or that was only
        /// partially written by the very crash that left it behind. Both are exactly the kind of
        /// leftover this cleanup exists to catch.
        /// </summary>
        private static void DeleteGeneratedAssetIfPresent(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null || File.Exists(assetPath))
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static void CreateFolderRecursive(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // Relative location of the bundled Roslyn DLLs + their integrity manifest within the package.
        private const string CodeAnalysisRelDir = "Runtime/Plugins/CodeAnalysis";
        private const string ChecksumsFileName = "CHECKSUMS";

        /// <summary>
        /// Verify the bundled Roslyn DLLs against the committed CHECKSUMS manifest, locating them
        /// relative to this package on disk. Throws <see cref="BuildFailedException"/> on any
        /// mismatch so a tampered/swapped DLL cannot be built into a player.
        /// </summary>
        public static void VerifyBundledChecksums()
        {
            if (!TryGetBundledCodeAnalysisPaths(out var codeAnalysisDir, out var checksumsPath))
            {
                throw new BuildFailedException(
                    "Pipeline: could not locate the com.unity.pipeline package on disk to verify " +
                    "bundled Roslyn DLL integrity. Aborting build.");
            }

            var error = VerifyChecksums(codeAnalysisDir, checksumsPath);
            if (error != null)
            {
                throw new BuildFailedException($"Pipeline: bundled Roslyn DLL integrity check failed. {error}");
            }
        }

        /// <summary>
        /// Resolve the bundled Roslyn DLL folder and its CHECKSUMS manifest from this package's
        /// location on disk. Returns false when the package cannot be located, in which case both
        /// out parameters are null.
        /// </summary>
        /// <param name="codeAnalysisDir">Directory holding the bundled Roslyn DLLs.</param>
        /// <param name="checksumsPath">Path to the CHECKSUMS manifest inside that directory.</param>
        /// <returns>True when the package was located and both paths were resolved.</returns>
        public static bool TryGetBundledCodeAnalysisPaths(out string codeAnalysisDir, out string checksumsPath)
        {
            codeAnalysisDir = null;
            checksumsPath = null;

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(PipelineRuntimeBuildProcessor).Assembly);

            if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath))
                return false;

            codeAnalysisDir = Path.Combine(packageInfo.resolvedPath, CodeAnalysisRelDir);
            checksumsPath = Path.Combine(codeAnalysisDir, ChecksumsFileName);
            return true;
        }

        /// <summary>
        /// Core, side-effect-free integrity check (so it is directly unit-testable). Returns null
        /// when every DLL listed in <paramref name="checksumsPath"/> exists under
        /// <paramref name="codeAnalysisDir"/> with a matching SHA-256 and no unlisted DLL is present;
        /// otherwise returns a human-readable error describing the first problem found.
        /// </summary>
        /// <param name="codeAnalysisDir">Directory containing the bundled Roslyn DLLs.</param>
        /// <param name="checksumsPath">Path to the CHECKSUMS manifest.</param>
        /// <returns>Null if every DLL matches; otherwise a human-readable description of the first problem found.</returns>
        public static string VerifyChecksums(string codeAnalysisDir, string checksumsPath)
        {
            if (!Directory.Exists(codeAnalysisDir))
                return $"DLL directory not found: {codeAnalysisDir}";
            if (!File.Exists(checksumsPath))
                return $"CHECKSUMS manifest not found: {checksumsPath}";

            var expected = ParseChecksums(checksumsPath);
            if (expected.Count == 0)
                return $"CHECKSUMS manifest has no entries: {checksumsPath}";

            // Every listed DLL must exist and match.
            foreach (var entry in expected)
            {
                var dllPath = Path.Combine(codeAnalysisDir, entry.Key);
                if (!File.Exists(dllPath))
                    return $"listed DLL is missing: {entry.Key}";

                var actual = ComputeSha256(dllPath);
                if (!string.Equals(actual, entry.Value, StringComparison.OrdinalIgnoreCase))
                    return $"hash mismatch for {entry.Key} (expected {entry.Value}, got {actual})";
            }

            // No unlisted DLL may sit alongside them (guards against an injected extra assembly).
            foreach (var dllPath in Directory.GetFiles(codeAnalysisDir, "*.dll"))
            {
                var name = Path.GetFileName(dllPath);
                if (!expected.ContainsKey(name))
                    return $"unexpected DLL not listed in CHECKSUMS: {name}";
            }

            return null;
        }

        /// <summary>SHA-256 of a file as a lowercase hex string.</summary>
        /// <param name="filePath">The file to hash.</param>
        /// <returns>The lowercase hex-encoded SHA-256 hash.</returns>
        public static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // Parse "<sha256>  <filename>  # comment" lines, skipping blanks and '#' comment lines.
        private static Dictionary<string, string> ParseChecksums(string checksumsPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in File.ReadAllLines(checksumsPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2)
                    continue;

                result[tokens[1]] = tokens[0];
            }

            return result;
        }
    }
}
