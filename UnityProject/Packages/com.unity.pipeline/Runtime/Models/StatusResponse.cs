using System;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Response model for /api/status endpoint.
    /// Provides Editor state information for CLI tools and automation scripts.
    /// </summary>
    [Serializable]
    public class StatusResponse
    {
        /// <summary>
        /// Current Editor status: "ready", "compiling", "busy"
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// Whether Unity is currently compiling scripts
        /// </summary>
        [JsonProperty("compiling")]
        public bool Compiling { get; set; }

        /// <summary>
        /// Whether a domain reload is in progress
        /// </summary>
        [JsonProperty("domainReloadInProgress")]
        public bool DomainReloadInProgress { get; set; }

        /// <summary>
        /// How many times scripts have loaded in this Editor session: 1 for the first load, plus
        /// one per script reload since; resets when the Editor restarts. A change between two
        /// replies means scripts reloaded in between.
        /// </summary>
        [JsonProperty("codeLoadCount")]
        public int CodeLoadCount { get; set; }

        /// <summary>
        /// The Editor's Auto Refresh preference: "enabled", "disabled", or
        /// "enabled_outside_play_mode". When it is not "enabled", a new or changed script is not
        /// imported or compiled until the Asset Database refreshes.
        /// </summary>
        [JsonProperty("autoRefresh")]
        public string AutoRefresh { get; set; }

        /// <summary>
        /// Current play mode state: "stopped", "playing", "paused"
        /// </summary>
        [JsonProperty("playMode")]
        public string PlayMode { get; set; }

        /// <summary>
        /// <c>Time.frameCount</c> as last sampled, present only in Play mode. Two reads within one
        /// frame can match on a healthy loop, so read <see cref="PlayerLoopTicking"/> for the verdict,
        /// or compare reads at least a second apart.
        /// </summary>
        [JsonProperty("frameCount", NullValueHandling = NullValueHandling.Ignore)]
        public int? FrameCount { get; set; }

        /// <summary>
        /// Whether the frame count advanced within the last second, present only in Play mode.
        /// False while paused, and while the player loop is frozen in an unfocused Editor.
        /// </summary>
        [JsonProperty("playerLoopTicking", NullValueHandling = NullValueHandling.Ignore)]
        public bool? PlayerLoopTicking { get; set; }

        /// <summary>
        /// Whether Application.runInBackground is on, i.e. the Editor keeps running the player loop
        /// while unfocused. Always present in editor status. This is the setting;
        /// <see cref="PlayerLoopTicking"/> is whether frames are actually advancing, which also covers
        /// pause and a stalled loop.
        /// </summary>
        [JsonProperty("runInBackground")]
        public bool RunInBackground { get; set; }

        /// <summary>
        /// When this response was produced (UTC). Not the descriptor's <c>lastHeartbeat</c>, which is
        /// what clients use to judge staleness.
        /// </summary>
        [JsonProperty("lastHeartbeat")]
        public DateTime LastHeartbeat { get; set; }

        /// <summary>
        /// Full path to the Unity project
        /// </summary>
        [JsonProperty("projectPath")]
        public string ProjectPath { get; set; }

        /// <summary>
        /// Unity Editor version string
        /// </summary>
        [JsonProperty("unityVersion")]
        public string UnityVersion { get; set; }

        /// <summary>
        /// The dialog currently blocking the main thread, present only when
        /// <see cref="Status"/> is "blocked_by_dialog".
        /// </summary>
        [JsonProperty("dialog", NullValueHandling = NullValueHandling.Ignore)]
        public object Dialog { get; set; }

        /// <summary>
        /// Multiplayer Play Mode role of the responding instance: "main" or "clone".
        /// </summary>
        [JsonProperty("role")]
        public string Role { get; set; }

        /// <summary>
        /// Human-readable name of the responding instance (e.g. "Main Editor", "Player 2").
        /// </summary>
        [JsonProperty("instanceName")]
        public string InstanceName { get; set; }
    }
}