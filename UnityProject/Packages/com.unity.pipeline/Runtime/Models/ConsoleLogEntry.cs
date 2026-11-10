using System;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// A single captured Unity console entry, as returned by the <c>console</c> command.
    /// </summary>
    [Serializable]
    public class ConsoleLogEntry
    {
        /// <summary>
        /// Monotonic sequence number assigned when the entry was captured. Acts as the cursor for
        /// incremental ("--follow") retrieval: pass the highest seq seen back as the <c>since</c>
        /// parameter to get only newer entries. Never reused, and increasing for the life of the
        /// buffer (including across domain reloads, since the buffer is persisted).
        /// </summary>
        [JsonProperty("seq")]
        public long Seq { get; set; }

        /// <summary>
        /// When the entry was captured (UTC).
        /// </summary>
        [JsonProperty("timestampUtc")]
        public DateTime TimestampUtc { get; set; }

        /// <summary>
        /// Normalized severity: "log", "warn", or "error". Unity's Error/Exception/Assert log types
        /// all map to "error"; Warning maps to "warn"; Log maps to "log".
        /// </summary>
        [JsonProperty("level")]
        public string Level { get; set; }

        /// <summary>
        /// Unity's own <see cref="UnityEngine.LogType"/> name for the entry ("Log", "Warning",
        /// "Error", "Assert", "Exception"), keeping the distinction that <see cref="Level"/>
        /// collapses.
        /// </summary>
        [JsonProperty("logType")]
        public string LogType { get; set; }

        /// <summary>
        /// The log message text.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// The stack trace associated with the entry, if any. Empty for most plain logs.
        /// </summary>
        [JsonProperty("stackTrace")]
        public string StackTrace { get; set; }

        /// <summary>
        /// True when the entry was read from the Editor console's own store rather than captured
        /// live. Its <see cref="TimestampUtc"/> is when it was read, not when it was logged, and
        /// its position relative to live-captured entries reflects the read, not the original
        /// chronology.
        /// </summary>
        [JsonProperty("seeded")]
        public bool Seeded { get; set; }
    }
}
