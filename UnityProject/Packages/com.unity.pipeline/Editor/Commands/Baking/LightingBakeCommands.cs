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

namespace Unity.Pipeline.Editor.Commands.Baking
{
    /// <summary>
    /// CLI-215 Group A — Lightmapping bake commands. Trigger an async lightmap bake of the open
    /// scene(s), poll its status, cancel it, clear baked data, and read/configure the active
    /// <see cref="LightingSettings"/>.
    ///
    /// Why async (trigger-then-poll, mirroring <see cref="BuildCommand"/>): a full lightmap bake runs
    /// for seconds-to-minutes. <see cref="Lightmapping.BakeAsync"/> kicks the bake off and returns
    /// immediately; <c>bake_lighting</c> therefore returns <c>{ status: "baking", bakeId }</c> right
    /// away and the caller polls <c>lighting_bake_status</c> until it reports <c>completed</c>.
    ///
    /// In-flight state is held in static fields and is domain-reload aware: a bake can survive (or
    /// resume across) a domain reload, so on reload <see cref="OnReload"/> reconciles our recorded
    /// state against the live <see cref="Lightmapping.isRunning"/> flag and a small status file under
    /// Temp/ (which persists across the reload, mirroring RecompileCommand). Completion is detected via
    /// the <see cref="Lightmapping.bakeCompleted"/> callback and, defensively, by polling
    /// <see cref="Lightmapping.isRunning"/> flipping false in <see cref="EditorApplication.update"/>.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class LightingBakeCommands
    {
        const string Group = "lighting";
        const string StatusFile = "Temp/pipeline_lighting_bake_status.json";

        static readonly object s_Lock = new object();

        // True between trigger and the completion callback / isRunning flipping false.
        static bool s_Tracking;
        static string s_BakeId;
        static DateTime s_StartedUtc;

        static LightingBakeCommands()
        {
            // Reconcile any in-flight bake recorded before a domain reload, then keep watching.
            OnReload();
            Lightmapping.bakeCompleted += OnBakeCompleted;
            EditorApplication.update += OnUpdate;
        }

        #region bake_lighting

        [CliGroupCommand(Group, "Trigger an async lightmap bake of the open scene(s) via Lightmapping.BakeAsync(). Returns immediately; poll " + CommandNames.LightingBakeStatus + " until completed.", Tags = new[] { "baking/lighting" })]
        public static object Bake(
            [CliArg("confirm", "Recommended (true): a bake overwrites existing lightmap data. Accepted for parity; not required.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate there is an open bakeable scene and return the current lighting settings without baking.")] bool dryRun = false)
        {
            if (!BakeSceneGuard.HasBakeableScene())
                return BakeSceneGuard.NoSceneResult();

            // Reconcile before the in-progress check so a stale flag from a finished/cancelled bake
            // doesn't wrongly report bake_in_progress.
            OnUpdate();

            if (Lightmapping.isRunning)
                return new { code = "bake_in_progress", message = "A lighting bake is already running. Poll " + CommandNames.LightingBakeStatus + "." };

            if (dryRun)
                return new { status = "dry_run", wouldBake = true, settings = ReadLightingSettings() };

            var bakeId = Guid.NewGuid().ToString("N");
            lock (s_Lock)
            {
                s_Tracking = true;
                s_BakeId = bakeId;
                s_StartedUtc = DateTime.UtcNow;
            }

            WriteStatus(new LightingBakeStatus
            {
                Status = "baking",
                BakeId = bakeId,
                IsBaking = true,
                StartedUtcTicks = s_StartedUtc.Ticks
            });

            // BakeAsync returns false if the bake could not be started (e.g. another job in flight).
            if (!Lightmapping.BakeAsync())
            {
                lock (s_Lock) { s_Tracking = false; }
                WriteStatus(new LightingBakeStatus { Status = "idle" });
                return new { code = "bake_in_progress", message = "Lightmapping.BakeAsync() refused to start (a bake may already be running)." };
            }

            return new { status = "baking", bakeId };
        }

        [CliGroupCommand(Group, "Get the status of the last lighting bake: idle | baking | completed.", MainThreadRequired = false, Tags = new[] { "baking/lighting" })]
        public static JToken BakeStatus()
        {
            return StatusFileReader.ReadOrIdle(StatusFile);
        }

        [CliGroupCommand(Group, "Cancel an in-progress lighting bake (Lightmapping.Cancel()).", Tags = new[] { "baking/lighting" })]
        public static object CancelBake()
        {
            var wasRunning = Lightmapping.isRunning;
            if (wasRunning)
                Lightmapping.Cancel();

            lock (s_Lock)
            {
                if (s_Tracking)
                {
                    var ms = (int)Math.Max(0, (DateTime.UtcNow - s_StartedUtc).TotalMilliseconds);
                    WriteStatus(new LightingBakeStatus
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

        [CliGroupCommand(Group, "Clear baked lightmap data for the open scene(s). Destructive: requires confirm=true.", Tags = new[] { "baking/lighting" })]
        public static object ClearBake(
            [CliArg("confirm", "Must be true to actually clear (destructive, not undoable via Unity's Undo).")] bool confirm = false,
            [CliArg("include-disk-cache", "If true, also clear the GI disk cache (Lightmapping.ClearDiskCache()).")] bool includeDiskCache = false,
            [CliArg("dry-run", "If true, report what would be cleared without clearing.")] bool dryRun = false)
        {
            if (!confirm)
                throw new ArgumentException("Refusing to clear baked lighting. Pass confirm=true (destructive, not undoable via Unity's Undo).");

            if (dryRun)
                return new { status = "dry_run", wouldClear = true, includeDiskCache };

            Lightmapping.Clear();
            if (includeDiskCache)
                Lightmapping.ClearDiskCache();

            return new { cleared = true, includeDiskCache };
        }

        #endregion

        #region settings

        [CliGroupCommand(Group, "Read the active LightingSettings (lightmapper, bounces, resolution, directional mode, AO, etc.).", Tags = new[] { "baking/lighting" })]
        public static LightingSettingsResult GetSettings()
        {
            return ReadLightingSettings();
        }

        [CliGroupCommand(Group, "Apply a subset of lighting settings to the active LightingSettings. Returns { applied[], unknown[] }.", Tags = new[] { "baking/lighting" })]
        public static object SetSettings(
            [CliArg("settings", "JSON object with a subset of lighting fields to set (same names/enums as " + CommandNames.LightingGetSettings + ").", Required = true)] JObject settings,
            [CliArg("dry-run", "If true, validate the keys and report applied/unknown without changing anything.")] bool dryRun = false)
        {
            if (settings == null)
                throw new ArgumentException("settings is required (a JSON object of lighting fields to set).");

            // In dry-run we must not create or assign a LightingSettings asset (no writes). When no
            // settings exist yet, validate the requested keys against a transient throwaway instance
            // that is never assigned, so applied/unknown is still reported without persisting anything.
            var ls = ActiveLightingSettings(allowCreate: !dryRun, out var source);
            LightingSettings transient = null;
            if (ls == null)
            {
                transient = new LightingSettings { name = "PipelineLightingSettings (dry-run)" };
                ls = transient;
                source = "none";
            }

            var applied = new System.Collections.Generic.List<string>();
            var unknown = new System.Collections.Generic.List<string>();

            foreach (var prop in settings.Properties())
            {
                if (TryApply(ls, prop.Name, prop.Value, dryRun))
                    applied.Add(prop.Name);
                else
                    unknown.Add(prop.Name);
            }

            if (!dryRun && applied.Count > 0)
                EditorUtility.SetDirty(ls);

            // The transient dry-run instance is never assigned anywhere; destroy it so it doesn't leak.
            if (transient != null)
                UnityEngine.Object.DestroyImmediate(transient);

            return new
            {
                applied = applied.ToArray(),
                unknown = unknown.ToArray(),
                settingsSource = source,
                dryRun
            };
        }

        #endregion

        #region completion / reload bookkeeping

        static void OnBakeCompleted()
        {
            lock (s_Lock)
            {
                if (!s_Tracking)
                    return;
                Finish("succeeded");
            }
        }

        /// <summary>
        /// Defensive completion detector: if we are tracking a bake but Lightmapping reports it is no
        /// longer running and the completion callback didn't fire (e.g. a cancel, or a bake that ended
        /// during a frame we didn't get the callback), finalize the status here.
        /// </summary>
        static void OnUpdate()
        {
            lock (s_Lock)
            {
                if (s_Tracking && !Lightmapping.isRunning)
                    Finish("succeeded");
            }
        }

        // Must be called under s_Lock.
        static void Finish(string result)
        {
            var ms = (int)Math.Max(0, (DateTime.UtcNow - s_StartedUtc).TotalMilliseconds);
            var status = new LightingBakeStatus
            {
                Status = "completed",
                Result = result,
                BakeId = s_BakeId,
                BakeTimeMs = ms,
                Stats = result == "succeeded" ? CollectStats() : null
            };
            WriteStatus(status);
            s_Tracking = false;
        }

        /// <summary>
        /// Reconcile recorded in-flight state after a domain reload. The status file under Temp/ survives
        /// the reload; if it said "baking" but Lightmapping is no longer running, the bake finished while
        /// the domain was being reloaded, so mark it completed. If it is still running, resume tracking.
        /// </summary>
        static void OnReload()
        {
            if (!File.Exists(StatusFile))
                return;

            LightingBakeStatus prior;
            try
            {
                prior = JsonConvert.DeserializeObject<LightingBakeStatus>(File.ReadAllText(StatusFile));
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

                if (Lightmapping.isRunning)
                {
                    s_Tracking = true; // resume; OnUpdate/OnBakeCompleted will finalize.
                }
                else
                {
                    s_Tracking = true;
                    Finish("succeeded"); // ended during the reload window.
                }
            }
        }

        #endregion

        #region settings read/write helpers

        /// <summary>
        /// The active <see cref="LightingSettings"/> for the open scene. When the scene has no explicit
        /// LightingSettings asset assigned, Unity falls back to a hidden per-scene default that
        /// <see cref="Lightmapping.GetLightingSettingsForScene"/> / <see cref="Lightmapping.lightingSettings"/>
        /// still exposes; we read/write that. <paramref name="source"/> records which we used.
        ///
        /// When none is available and <paramref name="allowCreate"/> is true, a new settings asset is
        /// created and assigned so a real (non-dry-run) set has a target. When <paramref name="allowCreate"/>
        /// is false (dry-run), this returns <c>null</c> rather than creating/assigning anything, so the
        /// dry-run "no writes" contract holds.
        /// </summary>
        static LightingSettings ActiveLightingSettings(bool allowCreate, out string source)
        {
            // Lightmapping.lightingSettings returns the active scene's settings (assigned asset or the
            // scene's embedded/default settings). It throws if there is genuinely none; guard for that.
            try
            {
                var ls = Lightmapping.lightingSettings;
                if (ls != null)
                {
                    source = AssetDatabase.Contains(ls) ? "asset" : "scene-default";
                    return ls;
                }
            }
            catch
            {
                // fall through to (optionally) creating one
            }

            if (!allowCreate)
            {
                source = "none";
                return null;
            }

            // No settings available: create and assign one so set_lighting_settings has a target.
            var created = new LightingSettings { name = "PipelineLightingSettings" };
            Lightmapping.lightingSettings = created;
            source = "created";
            return created;
        }

        static LightingSettingsResult ReadLightingSettings()
        {
            LightingSettings ls;
            try
            {
                ls = Lightmapping.lightingSettings;
            }
            catch
            {
                ls = null;
            }

            if (ls == null)
                return new LightingSettingsResult { Available = false };

            return new LightingSettingsResult
            {
                Available = true,
                // Obsolete from 7000.0, but still this asset's own lightmapper; suppress only the warning.
#pragma warning disable 618
                Lightmapper = ls.lightmapper.ToString(),
#pragma warning restore 618
                BakedGI = ls.bakedGI,
                // LightingSettings.realtimeGI is obsolete from 6000.7 (Enlighten realtime GI removal, no
                // replacement API yet) but still functions; suppress only the deprecation warning here.
#pragma warning disable 618
                RealtimeGI = ls.realtimeGI,
#pragma warning restore 618
                DirectSampleCount = ls.directSampleCount,
                IndirectSampleCount = ls.indirectSampleCount,
                EnvironmentSampleCount = ls.environmentSampleCount,
                Bounces = ls.maxBounces,
                LightmapResolution = ls.lightmapResolution,
                LightmapPadding = ls.lightmapPadding,
                MaxLightmapSize = ls.lightmapMaxSize,
                LightmapCompression = ls.lightmapCompression.ToString(),
                // Unity 6000 serialises directionalityMode as a flags enum whose .ToString() can
                // produce compound strings (e.g. "Single, Dual") for values that span multiple bits.
                // Map to the two-value contract the spec guarantees: 0 → NonDirectional, else → Directional.
                DirectionalMode = (int)ls.directionalityMode == 0 ? "NonDirectional" : "Directional",
                Ao = ls.ao,
                AoMaxDistance = ls.aoMaxDistance,
                FilteringMode = ls.filteringMode.ToString()
            };
        }

        /// <summary>
        /// Apply one settings key/value to the LightingSettings. Returns false for unrecognized keys so
        /// the caller can report them as <c>unknown[]</c>. Enum-valued fields accept their string names
        /// (case-insensitive). In dry-run mode we still validate (parse) the value but do not assign.
        /// </summary>
        static bool TryApply(LightingSettings ls, string key, JToken value, bool dryRun)
        {
            switch (key)
            {
                case "lightmapper":
                    // Obsolete from 7000.0; see the read path above for why this still writes the
                    // settings asset's own field.
#pragma warning disable 618
                    return SetEnum<LightingSettings.Lightmapper>(value, dryRun, v => ls.lightmapper = v);
#pragma warning restore 618
                case "bakedGI":
                    if (!dryRun) ls.bakedGI = value.Value<bool>();
                    return true;
                case "realtimeGI":
#pragma warning disable 618
                    if (!dryRun) ls.realtimeGI = value.Value<bool>();
#pragma warning restore 618
                    return true;
                case "directSampleCount":
                    if (!dryRun) ls.directSampleCount = value.Value<int>();
                    return true;
                case "indirectSampleCount":
                    if (!dryRun) ls.indirectSampleCount = value.Value<int>();
                    return true;
                case "environmentSampleCount":
                    if (!dryRun) ls.environmentSampleCount = value.Value<int>();
                    return true;
                case "bounces":
                    if (!dryRun) ls.maxBounces = value.Value<int>();
                    return true;
                case "lightmapResolution":
                    if (!dryRun) ls.lightmapResolution = value.Value<float>();
                    return true;
                case "lightmapPadding":
                    if (!dryRun) ls.lightmapPadding = value.Value<int>();
                    return true;
                case "maxLightmapSize":
                    if (!dryRun) ls.lightmapMaxSize = value.Value<int>();
                    return true;
                case "lightmapCompression":
                    return SetEnum<LightmapCompression>(value, dryRun, v => ls.lightmapCompression = v);
                case "directionalMode":
                    // Accept the two spec names ("NonDirectional", "Directional") and also Unity's
                    // internal name ("CombinedDirectional") for back-compat. Integer values pass through
                    // as-is so callers can target any raw enum slot Unity exposes.
                    if (value.Type == JTokenType.Integer)
                    {
                        if (!dryRun) ls.directionalityMode = (LightmapsMode)value.Value<int>();
                        return true;
                    }
                    var modeStr = value.Value<string>() ?? string.Empty;
                    if (modeStr.Equals("NonDirectional", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!dryRun) ls.directionalityMode = (LightmapsMode)0;
                        return true;
                    }
                    if (modeStr.Equals("Directional", StringComparison.OrdinalIgnoreCase) ||
                        modeStr.Equals("CombinedDirectional", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!dryRun) ls.directionalityMode = (LightmapsMode)1;
                        return true;
                    }
                    throw new ArgumentException(
                        $"Value '{modeStr}' is not valid for directionalMode. Valid: NonDirectional, Directional.");
                case "ao":
                    if (!dryRun) ls.ao = value.Value<bool>();
                    return true;
                case "aoMaxDistance":
                    if (!dryRun) ls.aoMaxDistance = value.Value<float>();
                    return true;
                case "filteringMode":
                    return SetEnum<LightingSettings.FilterMode>(value, dryRun, v => ls.filteringMode = v);
                default:
                    return false;
            }
        }

        static bool SetEnum<TEnum>(JToken value, bool dryRun, Action<TEnum> assign) where TEnum : struct
        {
            // Accept either the enum's string name (case-insensitive) or its integer value.
            if (value.Type == JTokenType.Integer)
            {
                var iv = value.Value<int>();
                if (!Enum.IsDefined(typeof(TEnum), iv))
                    throw new ArgumentException($"Value '{iv}' is not valid for {typeof(TEnum).Name}.");
                if (!dryRun) assign((TEnum)Enum.ToObject(typeof(TEnum), iv));
                return true;
            }

            var name = value.Value<string>();
            if (!Enum.TryParse<TEnum>(name, ignoreCase: true, out var parsed))
                throw new ArgumentException(
                    $"Value '{name}' is not valid for {typeof(TEnum).Name}. Valid: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}.");
            if (!dryRun) assign(parsed);
            return true;
        }

        #endregion

        #region stats

        /// <summary>
        /// Collect post-bake lightmap statistics from <see cref="LightmapSettings.lightmaps"/>: the
        /// number of baked lightmaps, their total texture size in bytes (color maps), the atlas size,
        /// the active lightmap resolution, and the baked light-probe count.
        /// </summary>
        static LightingBakeStats CollectStats()
        {
            var stats = new LightingBakeStats();

            var maps = LightmapSettings.lightmaps;
            if (maps != null)
            {
                stats.LightmapCount = maps.Length;
                long total = 0;
                int atlas = 0;
                foreach (var data in maps)
                {
                    var tex = data?.lightmapColor;
                    if (tex == null)
                        continue;
                    atlas = Mathf.Max(atlas, Mathf.Max(tex.width, tex.height));
                    total += EstimateTextureBytes(tex);
                }
                stats.TotalLightmapSizeBytes = total;
                stats.AtlasSize = atlas;
            }

            try
            {
                stats.LightmapResolution = Lightmapping.lightingSettings != null
                    ? Lightmapping.lightingSettings.lightmapResolution
                    : 0f;
            }
            catch
            {
                stats.LightmapResolution = 0f;
            }

            var probes = LightmapSettings.lightProbes;
            stats.BakedProbeCount = probes != null ? probes.count : 0;

            return stats;
        }

        /// <summary>Rough byte estimate for a baked lightmap texture (4 bytes/pixel; good enough for a size signal).</summary>
        static long EstimateTextureBytes(Texture tex)
        {
            return (long)tex.width * tex.height * 4;
        }

        #endregion

        static void WriteStatus(LightingBakeStatus status)
        {
            try
            {
                File.WriteAllText(StatusFile, JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LightingBake] Failed to write status file: {ex.Message}");
            }
        }
    }

    /// <summary>Status/result payload for <c>bake_lighting</c> / <c>lighting_bake_status</c>.</summary>
    [Serializable]
    class LightingBakeStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("bakeId")]
        public string BakeId { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public string Result { get; set; }

        [JsonProperty("isBaking")]
        public bool IsBaking { get; set; }

        [JsonProperty("bakeTimeMs")]
        public int BakeTimeMs { get; set; }

        [JsonProperty("stats", NullValueHandling = NullValueHandling.Ignore)]
        public LightingBakeStats Stats { get; set; }

        // Internal bookkeeping so a bake can be reconciled across a domain reload. Not part of the
        // documented schema, but harmless to expose.
        [JsonProperty("startedUtcTicks")]
        public long StartedUtcTicks { get; set; }
    }

    /// <summary>Post-bake lightmap statistics (CLI-215 acceptance: lightmapCount &gt;= 1, bakeTimeMs &gt; 0).</summary>
    [Serializable]
    class LightingBakeStats
    {
        [JsonProperty("lightmapCount")]
        public int LightmapCount { get; set; }

        [JsonProperty("totalLightmapSizeBytes")]
        public long TotalLightmapSizeBytes { get; set; }

        [JsonProperty("atlasSize")]
        public int AtlasSize { get; set; }

        [JsonProperty("lightmapResolution")]
        public float LightmapResolution { get; set; }

        [JsonProperty("bakedProbeCount")]
        public int BakedProbeCount { get; set; }
    }

    /// <summary>Result of <c>get_lighting_settings</c> (and the dry-run payload of bake_lighting).</summary>
    [Serializable]
    class LightingSettingsResult
    {
        /// <summary>False when no LightingSettings could be read for the active scene.</summary>
        [JsonProperty("available")]
        public bool Available { get; set; }

        [JsonProperty("lightmapper", NullValueHandling = NullValueHandling.Ignore)]
        public string Lightmapper { get; set; }

        [JsonProperty("bakedGI")]
        public bool BakedGI { get; set; }

        [JsonProperty("realtimeGI")]
        public bool RealtimeGI { get; set; }

        [JsonProperty("directSampleCount")]
        public int DirectSampleCount { get; set; }

        [JsonProperty("indirectSampleCount")]
        public int IndirectSampleCount { get; set; }

        [JsonProperty("environmentSampleCount")]
        public int EnvironmentSampleCount { get; set; }

        [JsonProperty("bounces")]
        public int Bounces { get; set; }

        [JsonProperty("lightmapResolution")]
        public float LightmapResolution { get; set; }

        [JsonProperty("lightmapPadding")]
        public int LightmapPadding { get; set; }

        [JsonProperty("maxLightmapSize")]
        public int MaxLightmapSize { get; set; }

        [JsonProperty("lightmapCompression", NullValueHandling = NullValueHandling.Ignore)]
        public string LightmapCompression { get; set; }

        [JsonProperty("directionalMode", NullValueHandling = NullValueHandling.Ignore)]
        public string DirectionalMode { get; set; }

        [JsonProperty("ao")]
        public bool Ao { get; set; }

        [JsonProperty("aoMaxDistance")]
        public float AoMaxDistance { get; set; }

        [JsonProperty("filteringMode", NullValueHandling = NullValueHandling.Ignore)]
        public string FilteringMode { get; set; }
    }
}
