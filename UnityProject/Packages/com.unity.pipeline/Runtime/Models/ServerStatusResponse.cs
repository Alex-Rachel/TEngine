using System;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// The /api/status body for every server state ("ready", "settling", "blocked_by_dialog", ...).
    /// Optional keys are omitted rather than sent as null: this is a widely-polled endpoint, so the
    /// common shape stays unchanged.
    /// </summary>
    internal sealed class ServerStatusResponse
    {
        [JsonProperty("status")]
        public string Status;

        [JsonProperty("lastHeartbeat")]
        public DateTime? LastHeartbeat;

        [JsonProperty("capabilities")]
        public string[] Capabilities;

        /// <summary>Time.frameCount as last sampled, present only in Play mode.</summary>
        [JsonProperty("frameCount", NullValueHandling = NullValueHandling.Ignore)]
        public int? FrameCount;

        /// <summary>Whether frames advanced within the last second, present only in Play mode.</summary>
        [JsonProperty("playerLoopTicking", NullValueHandling = NullValueHandling.Ignore)]
        public bool? PlayerLoopTicking;

        /// <summary>Application.runInBackground as last sampled, present only in Play mode.</summary>
        [JsonProperty("runInBackground", NullValueHandling = NullValueHandling.Ignore)]
        public bool? RunInBackground;

        [JsonProperty("dialog", NullValueHandling = NullValueHandling.Ignore)]
        public object Dialog;
    }
}