using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

// Every occlusion API this file drives -- StaticOcclusionCulling -- is [Obsolete] from 6000.7
// (superseded by GPU Occlusion Culling, which has no equivalent bake-and-poll API yet). It all
// still functions, and this type exists for no other purpose, so the deprecation warning is
// suppressed for the whole type rather than at each of its fourteen call sites.
#pragma warning disable CS0618

namespace Unity.Pipeline.Editor.Commands.Baking
{
    /// <summary>
    /// CLI-215 Group C — Occlusion Culling bake commands. Trigger an async occlusion bake of the open
    /// scene(s) via <see cref="StaticOcclusionCulling.GenerateInBackground"/>, poll its status, cancel
    /// it, and clear the baked occlusion data.
    ///
    /// Async pattern mirrors <see cref="BuildCommand"/> / <see cref="LightingBakeCommands"/>: trigger
    /// returns immediately; completion is detected by <see cref="StaticOcclusionCulling.isRunning"/>
    /// flipping false (polled in <see cref="EditorApplication.update"/>); in-flight state is held in
    /// static fields and reconciled against a Temp/ status file across domain reloads.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class OcclusionBakeCommands
    {
        const string Group = "occlusion";
        const string StatusFile = "Temp/pipeline_occlusion_bake_status.json";

        // Sentinel for "argument omitted" on the float bake parameters. float.NaN is not a
        // compile-time constant (so it can't be a default parameter value), but float.MinValue is, and
        // it is not a plausible real value for any of these positive bake parameters.
        const float Unset = float.MinValue;

        static readonly object s_Lock = new object();
        static bool s_Tracking;
        static string s_BakeId;
        static DateTime s_StartedUtc;

        static OcclusionBakeCommands()
        {
            OnReload();
            EditorApplication.update += OnUpdate;
        }

        #region bake_occlusion_culling

        [CliGroupCommand(Group, "Trigger an async occlusion-culling bake of the open scene(s) via StaticOcclusionCulling.GenerateInBackground(). Returns immediately; poll " + CommandNames.OcclusionBakeStatus + " until completed.", Tags = new[] { "baking/occlusion" })]
        public static object Bake(
            [CliArg("smallest-occluder", "Smallest object that will occlude others (meters). Defaults to Unity's current value.")] float smallestOccluder = Unset,
            [CliArg("smallest-hole", "Smallest gap geometry can have that the view can see through (meters). Defaults to Unity's current value.")] float smallestHole = Unset,
            [CliArg("backface-threshold", "Backface threshold (1-100); lower trims more backfaces. Defaults to Unity's current value.")] float backfaceThreshold = Unset,
            [CliArg("confirm", "Accepted for parity (a bake overwrites existing occlusion data); not required.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate there is an open scene and report the parameters that would be used without baking.")] bool dryRun = false)
        {
            if (!BakeSceneGuard.HasBakeableScene())
                return BakeSceneGuard.NoSceneResult();

            OnUpdate(); // reconcile any stale tracking flag first.

            if (StaticOcclusionCulling.isRunning)
                return new { code = "bake_in_progress", message = "An occlusion bake is already running. Poll " + CommandNames.OcclusionBakeStatus + "." };

            // Validate explicitly provided values before resolving. An omitted arg is the Unset
            // sentinel (float.MinValue); NaN is never <= Unset, so a NaN arg is treated as provided
            // and rejected here. smallest-occluder / smallest-hole must be positive; backface-threshold
            // is documented as 1-100.
            if (!IsOmitted(smallestOccluder) && !(smallestOccluder > 0f))
                throw new ArgumentException($"smallest-occluder must be greater than 0 (got {smallestOccluder}).");
            if (!IsOmitted(smallestHole) && !(smallestHole > 0f))
                throw new ArgumentException($"smallest-hole must be greater than 0 (got {smallestHole}).");
            if (!IsOmitted(backfaceThreshold) && !(backfaceThreshold >= 1f && backfaceThreshold <= 100f))
                throw new ArgumentException($"backface-threshold must be between 1 and 100 (got {backfaceThreshold}).");

            // Resolve effective parameters: an omitted arg (the Unset sentinel) keeps Unity's current value.
            var occluder = IsOmitted(smallestOccluder) ? StaticOcclusionCulling.smallestOccluder : smallestOccluder;
            var hole = IsOmitted(smallestHole) ? StaticOcclusionCulling.smallestHole : smallestHole;
            var backface = IsOmitted(backfaceThreshold) ? StaticOcclusionCulling.backfaceThreshold : backfaceThreshold;

            if (dryRun)
            {
                return new
                {
                    status = "dry_run",
                    wouldBake = true,
                    parameters = new { smallestOccluder = occluder, smallestHole = hole, backfaceThreshold = backface }
                };
            }

            // Apply the (possibly overridden) bake parameters before generating.
            StaticOcclusionCulling.smallestOccluder = occluder;
            StaticOcclusionCulling.smallestHole = hole;
            StaticOcclusionCulling.backfaceThreshold = backface;

            var bakeId = Guid.NewGuid().ToString("N");
            lock (s_Lock)
            {
                s_Tracking = true;
                s_BakeId = bakeId;
                s_StartedUtc = DateTime.UtcNow;
            }

            WriteStatus(new OcclusionBakeStatus
            {
                Status = "baking",
                BakeId = bakeId,
                StartedUtcTicks = s_StartedUtc.Ticks
            });

            StaticOcclusionCulling.GenerateInBackground();
            return new { status = "baking", bakeId };
        }

        [CliGroupCommand(Group, "Get the status of the last occlusion bake: idle | baking | completed.", MainThreadRequired = false, Tags = new[] { "baking/occlusion" })]
        public static JToken BakeStatus()
        {
            return StatusFileReader.ReadOrIdle(StatusFile);
        }

        [CliGroupCommand(Group, "Cancel an in-progress occlusion bake (StaticOcclusionCulling.Cancel()).", Tags = new[] { "baking/occlusion" })]
        public static object CancelBake()
        {
            var wasRunning = StaticOcclusionCulling.isRunning;
            if (wasRunning)
                StaticOcclusionCulling.Cancel();

            lock (s_Lock)
            {
                if (s_Tracking)
                {
                    var ms = (int)Math.Max(0, (DateTime.UtcNow - s_StartedUtc).TotalMilliseconds);
                    WriteStatus(new OcclusionBakeStatus
                    {
                        Status = "completed",
                        Result = "cancelled",
                        BakeId = s_BakeId,
                        BakeTimeMs = ms
                    });
                    s_Tracking = false;
                }
            }

            return new { cancelled = wasRunning };
        }

        [CliGroupCommand(Group, "Clear baked occlusion-culling data for the open scene(s). Destructive: requires confirm=true.", Tags = new[] { "baking/occlusion" })]
        public static object ClearBake(
            [CliArg("confirm", "Must be true to actually clear (destructive, not undoable via Unity's Undo).")] bool confirm = false,
            [CliArg("dry-run", "If true, report what would be cleared without clearing.")] bool dryRun = false)
        {
            if (!confirm)
                throw new ArgumentException("Refusing to clear occlusion culling data. Pass confirm=true (destructive, not undoable via Unity's Undo).");

            if (dryRun)
                return new { status = "dry_run", wouldClear = true };

            StaticOcclusionCulling.Clear();
            return new { cleared = true };
        }

        // True when the caller omitted a float bake arg (left it at the Unset sentinel). NaN is never
        // <= Unset, so NaN counts as provided and is caught by the range validation above.
        static bool IsOmitted(float value) => value <= Unset;

        #endregion

        #region completion / reload bookkeeping

        static void OnUpdate()
        {
            lock (s_Lock)
            {
                if (s_Tracking && !StaticOcclusionCulling.isRunning)
                    Finish("succeeded");
            }
        }

        // Must be called under s_Lock.
        static void Finish(string result)
        {
            var ms = (int)Math.Max(0, (DateTime.UtcNow - s_StartedUtc).TotalMilliseconds);
            WriteStatus(new OcclusionBakeStatus
            {
                Status = "completed",
                Result = result,
                BakeId = s_BakeId,
                BakeTimeMs = ms,
                Stats = result == "succeeded"
                    ? new OcclusionBakeStats { UmbraDataSizeBytes = StaticOcclusionCulling.umbraDataSize }
                    : null
            });
            s_Tracking = false;
        }

        static void OnReload()
        {
            if (!File.Exists(StatusFile))
                return;

            OcclusionBakeStatus prior;
            try
            {
                prior = JsonConvert.DeserializeObject<OcclusionBakeStatus>(File.ReadAllText(StatusFile));
            }
            catch
            {
                return;
            }

            if (prior == null || prior.Status != "baking")
                return;

            lock (s_Lock)
            {
                s_BakeId = prior.BakeId;
                s_StartedUtc = prior.StartedUtcTicks > 0 ? new DateTime(prior.StartedUtcTicks, DateTimeKind.Utc) : DateTime.UtcNow;
                s_Tracking = true;
                if (!StaticOcclusionCulling.isRunning)
                    Finish("succeeded");
            }
        }

        #endregion

        static void WriteStatus(OcclusionBakeStatus status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OcclusionBake] Failed to write status file: {ex.Message}");
            }
        }
    }

    /// <summary>Status/result payload for <c>bake_occlusion_culling</c> / <c>occlusion_bake_status</c>.</summary>
    [Serializable]
    class OcclusionBakeStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("bakeId")]
        public string BakeId { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public string Result { get; set; }

        [JsonProperty("bakeTimeMs")]
        public int BakeTimeMs { get; set; }

        [JsonProperty("stats", NullValueHandling = NullValueHandling.Ignore)]
        public OcclusionBakeStats Stats { get; set; }

        [JsonProperty("startedUtcTicks")]
        public long StartedUtcTicks { get; set; }
    }

    /// <summary>Post-bake occlusion statistics (umbra data size in bytes).</summary>
    [Serializable]
    class OcclusionBakeStats
    {
        [JsonProperty("umbraDataSizeBytes")]
        public long UmbraDataSizeBytes { get; set; }
    }
}

#pragma warning restore CS0618
