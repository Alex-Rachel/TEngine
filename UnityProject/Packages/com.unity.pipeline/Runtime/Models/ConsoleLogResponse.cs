using System;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Response model for the <c>console</c> command: a page of captured console entries plus
    /// the cursor needed to fetch the next page.
    /// </summary>
    [Serializable]
    public class ConsoleLogResponse
    {
        /// <summary>
        /// The matching entries, oldest first, after applying the level filter, <c>since</c> cursor,
        /// and <c>tail</c> limit.
        /// </summary>
        [JsonProperty("entries")]
        public ConsoleLogEntry[] Entries { get; set; }

        /// <summary>
        /// The highest <see cref="ConsoleLogEntry.Seq"/> currently held in the buffer (regardless of
        /// the level filter). A "--follow" client passes this back as <c>since</c> on the next poll
        /// so it resumes exactly where it left off, even if the only new entries were filtered out.
        /// Only meaningful paired with <see cref="Session"/>: sequence numbers restart from zero
        /// with the capture session, so a cursor from an earlier session names different entries.
        /// </summary>
        [JsonProperty("cursor")]
        public long Cursor { get; set; }

        /// <summary>
        /// Identifies the capture session <see cref="Cursor"/> belongs to. A "--follow" client
        /// passes this back as <c>since-session</c> alongside <c>since</c>; a cursor from a
        /// different session is refused rather than silently misapplied.
        /// </summary>
        [JsonProperty("session")]
        public string Session { get; set; }

        /// <summary>
        /// Number of entries returned in <see cref="Entries"/>.
        /// </summary>
        [JsonProperty("returned")]
        public int Returned { get; set; }

        /// <summary>
        /// True when the requested <c>since</c> cursor pointed at entries that have already been
        /// evicted from the bounded buffer, meaning the consumer missed some entries. Lets a
        /// "--follow" client warn about a gap instead of silently skipping output. Also true
        /// whenever <see cref="Reset"/> is.
        /// </summary>
        [JsonProperty("dropped")]
        public bool Dropped { get; set; }

        /// <summary>
        /// True when the requested cursor could not be honored — it came from another session, or
        /// it was past the current counter — so the cursor was ignored and the tail returned
        /// instead. A "--follow" client should adopt <see cref="Cursor"/> and <see cref="Session"/>
        /// and treat the returned entries as a fresh start.
        /// </summary>
        [JsonProperty("reset")]
        public bool Reset { get; set; }

        /// <summary>
        /// How many entries of each severity the buffer currently holds, before the level filter
        /// and <c>tail</c> limit. Compare with <see cref="ConsoleGroundTruth.ConsoleErrors"/> and
        /// friends to see whether the buffer is missing entries the Editor console still shows.
        /// </summary>
        [JsonProperty("counts")]
        public ConsoleLevelCounts Counts { get; set; }

        /// <summary>
        /// Editor ground truth about the console at the time it was last sampled. Null in a player
        /// build, and before the first sample after an Editor load.
        /// </summary>
        [JsonProperty("groundTruth")]
        public ConsoleGroundTruth GroundTruth { get; set; }
    }

    /// <summary>
    /// Entry counts per severity retained in the buffer, before the level filter and <c>tail</c>
    /// limit are applied. Compare with the Editor console's own totals in
    /// <see cref="ConsoleGroundTruth"/> to see whether the buffer is missing entries the console
    /// still shows.
    /// </summary>
    [Serializable]
    public class ConsoleLevelCounts
    {
        /// <summary>
        /// Retained entries at error severity. Unity's Error, Exception and Assert log types all
        /// count here.
        /// </summary>
        [JsonProperty("error")]
        public int Error { get; set; }

        /// <summary>Retained entries at warning severity.</summary>
        [JsonProperty("warn")]
        public int Warn { get; set; }

        /// <summary>Retained entries at informational severity.</summary>
        [JsonProperty("log")]
        public int Log { get; set; }
    }
}
