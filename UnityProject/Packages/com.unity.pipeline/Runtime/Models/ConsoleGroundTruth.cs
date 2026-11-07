using System;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Editor ground truth about the console, sampled on the main thread and served alongside
    /// buffered entries so a client can tell whether the buffer agrees with the Editor console
    /// without a second round trip. Null when served from a player build, which has no Editor
    /// console.
    /// </summary>
    [Serializable]
    public class ConsoleGroundTruth
    {
        /// <summary>When the sample was taken (UTC).</summary>
        [JsonProperty("sampledUtc")]
        public DateTime SampledUtc { get; set; }

        /// <summary>
        /// How long ago the sample was taken. Sampling runs on the main thread, so it stops while
        /// the Editor is blocked compiling or importing; a large age means the rest of this object
        /// describes an older state of the console.
        /// </summary>
        [JsonProperty("ageMs")]
        public long AgeMs { get; set; }

        /// <summary>
        /// True while the project has editor compile errors. Read from
        /// <c>EditorUtility.scriptCompilationFailed</c>, which is native state independent of the
        /// log callback and of any domain reload, so it stays true for as long as the errors do.
        /// </summary>
        [JsonProperty("compilationFailed")]
        public bool CompilationFailed { get; set; }

        /// <summary>True while script compilation is in progress.</summary>
        [JsonProperty("compiling")]
        public bool Compiling { get; set; }

        /// <summary>
        /// Errors the Editor console holds. This is a console-lifetime total, so comparing it with
        /// <see cref="ConsoleLevelCounts.Error"/> shows whether the buffer is missing entries the
        /// console still displays.
        /// </summary>
        [JsonProperty("consoleErrors")]
        public int ConsoleErrors { get; set; }

        /// <summary>Warnings the Editor console holds.</summary>
        [JsonProperty("consoleWarnings")]
        public int ConsoleWarnings { get; set; }

        /// <summary>Informational entries the Editor console holds.</summary>
        [JsonProperty("consoleLogs")]
        public int ConsoleLogs { get; set; }

        /// <summary>
        /// True once the buffer has been backfilled from the Editor console's own store. False
        /// means that backfill is unavailable on this Editor version, which is a benign explanation
        /// for the buffer holding fewer entries than <see cref="ConsoleErrors"/> and friends report.
        /// </summary>
        [JsonProperty("seeded")]
        public bool Seeded { get; set; }
    }
}
