using System.Collections.Generic;
using UnityEngine;

namespace Unity.Pipeline.Config
{
    /// <summary>
    /// Build-only, generated companion to <see cref="RuntimePipelineConfig"/>: holds the absolute,
    /// machine-specific method-reload roots baked in by PipelineRuntimeBuildProcessor. Created fresh
    /// before each build and deleted immediately after — never checked into source control, since
    /// its contents are meaningless outside the machine that produced them. Absent outside of a
    /// real build (e.g. Editor Play Mode without ever having built) is not an error;
    /// RuntimePipelineBootstrap simply has no roots to allow in that case.
    /// </summary>
    public class RuntimePipelineBuildInfo : ScriptableObject
    {
        /// <summary>Resource name this asset must be saved under inside a Resources folder for <see cref="Load"/> to find it.</summary>
        public const string ResourceName = "RuntimePipelineBuildInfo";

        /// <summary>Absolute paths of the project's Assets folder and every loaded package, baked in at build time.</summary>
        public List<string> allowedReloadRoots = new List<string>();

        /// <summary>Public half of the project's push-signing key (from Library/Pipeline/signing.key).
        /// A player build rejects every method-reload push not signed by the matching private key; empty
        /// means reject all pushes (strict policy — there is deliberately no unsigned fallback).</summary>
        public string pushPublicKey = "";

        /// <summary>Load the asset from Resources, or null if this is not a real build.</summary>
        /// <returns>The build-time-baked info, or null if this is not a real build.</returns>
        public static RuntimePipelineBuildInfo Load() => Resources.Load<RuntimePipelineBuildInfo>(ResourceName);
    }
}
