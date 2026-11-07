using System.Collections.Generic;
using Unity.Pipeline.Config;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Creates the runtime Pipeline driver automatically when a Player boots (or Play Mode is
    /// entered in the Editor) — no scene setup required. Reads RuntimePipelineConfig (JSON-backed
    /// in the Editor, a build-baked Resources asset in a Player) and the build-baked
    /// RuntimePipelineBuildInfo from Resources; does nothing if no config exists or the config
    /// has the server disabled.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class RuntimePipelineBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeBootstrap()
        {
            Bootstrap();
        }

        /// <summary>
        /// The driver created by <see cref="Bootstrap"/>, or null if it has not run yet (or created
        /// nothing because there was no usable config). Reachable for tests and manual control.
        /// </summary>
        public static RuntimePipelineDriver Instance { get; internal set; }

        /// <summary>
        /// Core bootstrap logic, split out from the attributed entry point so tests can invoke it
        /// directly instead of waiting for a real domain/Player boot. Returns the created driver,
        /// or null if the server should not start (no config asset, or disabled). Idempotent: a
        /// second call returns the existing <see cref="Instance"/> rather than creating a duplicate.
        /// </summary>
        /// <returns>The created (or already-existing) driver, or null if the server should not start.</returns>
        public static RuntimePipelineDriver Bootstrap()
        {
#if UNITY_EDITOR
            // Play Mode stands in for a development build, so it follows the same setting: the
            // server runs only when this project would compile the Pipeline into a dev build.
            return Bootstrap(Config.PipelineBuildSymbols.IsDefinedForActiveTarget(Config.PipelineBuildSymbols.Development));
#else
            return Bootstrap(true);
#endif
        }

        /// <summary>
        /// <see cref="Bootstrap()"/> with the development-build setting given rather than read
        /// from the active build target's defines. Only the Editor consults it: a Player that runs
        /// this code was built with the Pipeline.
        /// </summary>
        /// <param name="pipelineInDevelopmentBuilds">Whether the project compiles the Pipeline into development builds.</param>
        /// <returns>The created (or already-existing) driver, or null if the server should not start.</returns>
        internal static RuntimePipelineDriver Bootstrap(bool pipelineInDevelopmentBuilds)
        {
            if (Instance != null)
                return Instance;

#if UNITY_EDITOR
            if (!pipelineInDevelopmentBuilds)
            {
                // The runtime server is a player-build feature, but in-editor method reload still
                // needs the [MethodReload] targets registered for this Play session — the driver
                // would do it in Awake; with no driver, do it here so a save can bind.
                RuntimePipelineDriver.RegisterDiscoveredMethodReloadMethods();
                return null;
            }
#endif

            // Reaching here means the Pipeline was compiled into this build, so it runs. An absent
            // settings file simply means nothing was customized; the defaults apply.
            var config = RuntimePipelineConfig.Load() ?? ScriptableObject.CreateInstance<RuntimePipelineConfig>();

            var buildInfo = RuntimePipelineBuildInfo.Load();
            IReadOnlyList<string> roots = buildInfo != null
                ? (IReadOnlyList<string>)buildInfo.allowedReloadRoots
                : System.Array.Empty<string>();
            // Empty when there's no build info (e.g. play mode never built): the driver then rejects
            // every push, which is the correct strict default outside a real signed build.
            string pushPublicKey = buildInfo != null ? buildInfo.pushPublicKey : "";

            var go = new GameObject("Pipeline Runtime Driver") { hideFlags = HideFlags.DontSave };
            var driver = go.AddComponent<RuntimePipelineDriver>();
            driver.Initialize(config, roots, pushPublicKey);
            Instance = driver;
            return driver;
        }
    }
}
