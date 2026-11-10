using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

// CLI-215 deliberately targets the built-in legacy UnityEditor.AI.NavMeshBuilder (see class doc). It is
// [Obsolete] in Unity 6000.x (the modern path is the com.unity.ai.navigation package), but is the
// intentional v1 baker here, so suppress CS0618 file-wide rather than migrate.
#pragma warning disable CS0618

namespace Unity.Pipeline.Editor.Commands.Baking
{
    /// <summary>
    /// CLI-215 Group B — NavMesh bake commands, targeting the <b>built-in legacy</b> baker
    /// <see cref="UnityEditor.AI.NavMeshBuilder"/> (always available; bakes a single scene NavMesh from
    /// the Navigation-static geometry of the open scene). Trigger an async build, poll its status,
    /// cancel it, clear the baked NavMesh, and read/configure the default agent's bake settings.
    ///
    /// The newer AI Navigation package (<c>com.unity.ai.navigation</c>, <c>NavMeshSurface</c>
    /// components) is OUT for v1 — we do NOT add an asmdef reference to it. <c>bake_navmesh_surfaces</c>
    /// is a guarded stub that probes for the package by reflection and returns
    /// <c>{ code: "package_not_found" }</c> when it is absent (full support is a follow-up).
    ///
    /// Async pattern mirrors <see cref="BuildCommand"/> / <see cref="LightingBakeCommands"/>: trigger
    /// returns immediately, completion is detected by <see cref="NavMeshBuilder.isRunning"/> flipping
    /// false (polled in <see cref="EditorApplication.update"/>), and in-flight state is held in static
    /// fields reconciled against a Temp/ status file across domain reloads.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class NavMeshBakeCommands
    {
        const string Group = "navmesh";
        const string StatusFile = "Temp/pipeline_navmesh_bake_status.json";

        // The serialized NavMesh bake-settings live on the Navigation window's settings object, keyed by
        // these property names (stable across 2019+ / Unity 6).
        const string PropAgentRadius = "m_BuildSettings.agentRadius";
        const string PropAgentHeight = "m_BuildSettings.agentHeight";
        const string PropAgentSlope = "m_BuildSettings.agentSlope";
        const string PropAgentClimb = "m_BuildSettings.agentClimb";
        const string PropMinRegionArea = "m_BuildSettings.minRegionArea";
        const string PropManualCellSize = "m_BuildSettings.manualCellSize";
        const string PropCellSize = "m_BuildSettings.cellSize";

        static readonly object s_Lock = new object();
        static bool s_Tracking;
        static string s_BakeId;
        static DateTime s_StartedUtc;

        static NavMeshBakeCommands()
        {
            OnReload();
            EditorApplication.update += OnUpdate;
        }

        #region bake_navmesh

        [CliGroupCommand(Group, "Trigger an async legacy NavMesh bake of the open scene(s) via UnityEditor.AI.NavMeshBuilder. Returns immediately; poll " + CommandNames.NavmeshBakeStatus + " until completed.", Tags = new[] { "baking/navmesh" })]
        public static object Bake(
            [CliArg("confirm", "Accepted for parity (a bake overwrites the existing NavMesh); not required.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate there is an open scene and return current NavMesh settings without baking.")] bool dryRun = false)
        {
            if (!BakeSceneGuard.HasBakeableScene())
                return BakeSceneGuard.NoSceneResult();

            OnUpdate(); // reconcile a possibly-stale tracking flag first.

            if (NavMeshBuilder.isRunning)
                return new { code = "bake_in_progress", message = "A NavMesh bake is already running. Poll " + CommandNames.NavmeshBakeStatus + "." };

            if (dryRun)
                return new { status = "dry_run", wouldBake = true, settings = ReadNavMeshSettings() };

            var bakeId = Guid.NewGuid().ToString("N");
            lock (s_Lock)
            {
                s_Tracking = true;
                s_BakeId = bakeId;
                s_StartedUtc = DateTime.UtcNow;
            }

            WriteStatus(new NavMeshBakeStatus
            {
                Status = "baking",
                BakeId = bakeId,
                StartedUtcTicks = s_StartedUtc.Ticks
            });

            NavMeshBuilder.BuildNavMeshAsync();
            return new { status = "baking", bakeId };
        }

        [CliGroupCommand(Group, "Get the status of the last NavMesh bake: idle | baking | completed.", MainThreadRequired = false, Tags = new[] { "baking/navmesh" })]
        public static JToken BakeStatus()
        {
            return StatusFileReader.ReadOrIdle(StatusFile);
        }

        [CliGroupCommand(Group, "Cancel an in-progress NavMesh bake (NavMeshBuilder.Cancel()).", Tags = new[] { "baking/navmesh" })]
        public static object CancelBake()
        {
            var wasRunning = NavMeshBuilder.isRunning;
            if (wasRunning)
                NavMeshBuilder.Cancel();

            lock (s_Lock)
            {
                if (s_Tracking)
                {
                    var ms = (int)Math.Max(0, (DateTime.UtcNow - s_StartedUtc).TotalMilliseconds);
                    WriteStatus(new NavMeshBakeStatus
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

        [CliGroupCommand(Group, "Clear the baked NavMesh for the open scene(s). Destructive: requires confirm=true.", Tags = new[] { "baking/navmesh" })]
        public static object ClearBake(
            [CliArg("confirm", "Must be true to actually clear (destructive, not undoable via Unity's Undo).")] bool confirm = false,
            [CliArg("dry-run", "If true, report what would be cleared without clearing.")] bool dryRun = false)
        {
            if (!confirm)
                throw new ArgumentException("Refusing to clear the NavMesh. Pass confirm=true (destructive, not undoable via Unity's Undo).");

            if (dryRun)
                return new { status = "dry_run", wouldClear = true };

            NavMeshBuilder.ClearAllNavMeshes();
            return new { cleared = true };
        }

        #endregion

        #region settings

        [CliGroupCommand(Group, "Read the default agent's legacy NavMesh bake settings (agentRadius/Height/Slope/Climb, minRegionArea, voxelSize).", Tags = new[] { "baking/navmesh" })]
        public static NavMeshSettingsResult GetSettings()
        {
            return ReadNavMeshSettings();
        }

        [CliGroupCommand(Group, "Apply a subset of legacy NavMesh bake settings to the default agent. Returns { applied[], unknown[] }.", Tags = new[] { "baking/navmesh" })]
        public static object SetSettings(
            [CliArg("settings", "JSON object with a subset of NavMesh fields to set (same names as " + CommandNames.NavmeshGetSettings + ").", Required = true)] JObject settings,
            [CliArg("dry-run", "If true, validate the keys and report applied/unknown without changing anything.")] bool dryRun = false)
        {
            if (settings == null)
                throw new ArgumentException("settings is required (a JSON object of NavMesh fields to set).");

            // NavMeshBuilder.navMeshSettingsObject is a UnityEngine.Object (the Navigation window's
            // settings holder); wrap it in a SerializedObject to read/write the m_BuildSettings fields.
            var settingsObject = NavMeshBuilder.navMeshSettingsObject;
            if (settingsObject == null)
                return new { code = "settings_unavailable", message = "Could not access the legacy NavMesh settings object." };

            var so = new SerializedObject(settingsObject);
            so.Update();

            var applied = new List<string>();
            var unknown = new List<string>();

            foreach (var prop in settings.Properties())
            {
                if (TryApply(so, prop.Name, prop.Value, dryRun))
                    applied.Add(prop.Name);
                else
                    unknown.Add(prop.Name);
            }

            if (!dryRun && applied.Count > 0)
                so.ApplyModifiedPropertiesWithoutUndo();

            return new { applied = applied.ToArray(), unknown = unknown.ToArray(), dryRun };
        }

        /// <summary>
        /// Guarded stub for the AI Navigation package (<c>com.unity.ai.navigation</c>) NavMeshSurface
        /// bake path. We never reference that assembly, so probe for the type by reflection; absent → the
        /// documented <c>package_not_found</c> result. Full multi-surface baking is a follow-up.
        /// </summary>
        [CliGroupCommand(Group, "Bake NavMeshSurface components (AI Navigation package). v1 stub: returns package_not_found when the package is absent.", Tags = new[] { "baking/navmesh" })]
        public static object BakeSurfaces()
        {
            var surfaceType = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation")
                ?? Type.GetType("UnityEngine.AI.NavMeshSurface, Unity.AI.Navigation");

            if (surfaceType == null)
                return new
                {
                    code = "package_not_found",
                    message = "The AI Navigation package (com.unity.ai.navigation) is not installed. " +
                              "NavMeshSurface-based baking is out of scope for v1; use " + CommandNames.NavmeshBake + " for the built-in baker."
                };

            return new
            {
                code = "not_implemented",
                message = "AI Navigation package detected, but NavMeshSurface baking is not implemented in v1 (follow-up). Use " + CommandNames.NavmeshBake + "."
            };
        }

        #endregion

        #region completion / reload bookkeeping

        static void OnUpdate()
        {
            lock (s_Lock)
            {
                if (s_Tracking && !NavMeshBuilder.isRunning)
                    Finish("succeeded");
            }
        }

        // Must be called under s_Lock.
        static void Finish(string result)
        {
            var ms = (int)Math.Max(0, (DateTime.UtcNow - s_StartedUtc).TotalMilliseconds);
            WriteStatus(new NavMeshBakeStatus
            {
                Status = "completed",
                Result = result,
                BakeId = s_BakeId,
                BakeTimeMs = ms
            });
            s_Tracking = false;
        }

        static void OnReload()
        {
            if (!File.Exists(StatusFile))
                return;

            NavMeshBakeStatus prior;
            try
            {
                prior = JsonConvert.DeserializeObject<NavMeshBakeStatus>(File.ReadAllText(StatusFile));
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
                if (!NavMeshBuilder.isRunning)
                    Finish("succeeded");
            }
        }

        #endregion

        #region settings read/write helpers

        static NavMeshSettingsResult ReadNavMeshSettings()
        {
            var settingsObject = NavMeshBuilder.navMeshSettingsObject;
            if (settingsObject == null)
                return new NavMeshSettingsResult { Available = false };

            var so = new SerializedObject(settingsObject);
            so.Update();
            var manual = FindBool(so, PropManualCellSize, false);
            return new NavMeshSettingsResult
            {
                Available = true,
                AgentRadius = FindFloat(so, PropAgentRadius, 0.5f),
                AgentHeight = FindFloat(so, PropAgentHeight, 2f),
                AgentSlope = FindFloat(so, PropAgentSlope, 45f),
                AgentClimb = FindFloat(so, PropAgentClimb, 0.4f),
                MinRegionArea = FindFloat(so, PropMinRegionArea, 2f),
                VoxelSize = FindFloat(so, PropCellSize, 0f),
                ManualVoxelSize = manual
            };
        }

        static bool TryApply(SerializedObject so, string key, JToken value, bool dryRun)
        {
            switch (key)
            {
                case "agentRadius":
                    return SetFloat(so, PropAgentRadius, value, dryRun);
                case "agentHeight":
                    return SetFloat(so, PropAgentHeight, value, dryRun);
                case "agentSlope":
                    return SetFloat(so, PropAgentSlope, value, dryRun);
                case "agentClimb":
                    return SetFloat(so, PropAgentClimb, value, dryRun);
                case "minRegionArea":
                    return SetFloat(so, PropMinRegionArea, value, dryRun);
                case "voxelSize":
                    // Setting an explicit voxel size implies manual override on.
                    {
                        var ok = SetFloat(so, PropCellSize, value, dryRun);
                        if (ok && !dryRun)
                        {
                            var manual = so.FindProperty(PropManualCellSize);
                            if (manual != null) manual.boolValue = true;
                        }
                        return ok;
                    }
                case "manualVoxelSize":
                    return SetBool(so, PropManualCellSize, value, dryRun);
                default:
                    return false;
            }
        }

        static bool SetFloat(SerializedObject so, string propPath, JToken value, bool dryRun)
        {
            var prop = so.FindProperty(propPath);
            if (prop == null)
                return false;
            var f = value.Value<float>();
            if (!dryRun) prop.floatValue = f;
            return true;
        }

        static bool SetBool(SerializedObject so, string propPath, JToken value, bool dryRun)
        {
            var prop = so.FindProperty(propPath);
            if (prop == null)
                return false;
            var b = value.Value<bool>();
            if (!dryRun) prop.boolValue = b;
            return true;
        }

        static float FindFloat(SerializedObject so, string propPath, float fallback)
        {
            var prop = so.FindProperty(propPath);
            return prop != null ? prop.floatValue : fallback;
        }

        static bool FindBool(SerializedObject so, string propPath, bool fallback)
        {
            var prop = so.FindProperty(propPath);
            return prop != null ? prop.boolValue : fallback;
        }

        #endregion

        static void WriteStatus(NavMeshBakeStatus status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NavMeshBake] Failed to write status file: {ex.Message}");
            }
        }
    }

    /// <summary>Status/result payload for <c>bake_navmesh</c> / <c>navmesh_bake_status</c>.</summary>
    [Serializable]
    class NavMeshBakeStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("bakeId")]
        public string BakeId { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public string Result { get; set; }

        [JsonProperty("bakeTimeMs")]
        public int BakeTimeMs { get; set; }

        [JsonProperty("startedUtcTicks")]
        public long StartedUtcTicks { get; set; }
    }

    /// <summary>Result of <c>get_navmesh_settings</c> (default-agent legacy bake settings).</summary>
    [Serializable]
    class NavMeshSettingsResult
    {
        [JsonProperty("available")]
        public bool Available { get; set; }

        [JsonProperty("agentRadius")]
        public float AgentRadius { get; set; }

        [JsonProperty("agentHeight")]
        public float AgentHeight { get; set; }

        [JsonProperty("agentSlope")]
        public float AgentSlope { get; set; }

        [JsonProperty("agentClimb")]
        public float AgentClimb { get; set; }

        [JsonProperty("minRegionArea")]
        public float MinRegionArea { get; set; }

        [JsonProperty("voxelSize")]
        public float VoxelSize { get; set; }

        [JsonProperty("manualVoxelSize")]
        public bool ManualVoxelSize { get; set; }
    }
}
