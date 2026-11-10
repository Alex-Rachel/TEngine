using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Telemetry
{
    /// <summary>A ranked API fingerprint with its occurrence count across all recorded evals.</summary>
    [Serializable]
    class FingerprintCount
    {
        [JsonProperty("member")]
        public string Member { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    /// <summary>An eval pattern already served by an existing catalog command — a "stop using eval" hit.</summary>
    [Serializable]
    class CoverageHit
    {
        [JsonProperty("member")]
        public string Member { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>The existing command that already covers this pattern.</summary>
        [JsonProperty("command")]
        public string Command { get; set; }
    }

    /// <summary>A frequently-eval'd pattern with no covering command — a candidate for a new typed command.</summary>
    [Serializable]
    class CoverageGap
    {
        [JsonProperty("member")]
        public string Member { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>A proposed command name from the coverage map, when one exists but is not yet built.</summary>
        [JsonProperty("suggestedCommand", NullValueHandling = NullValueHandling.Ignore)]
        public string SuggestedCommand { get; set; }
    }

    /// <summary>
    /// Coverage cross-reference between recorded eval fingerprints and the live command catalog:
    /// which patterns are already coverable (habit/eval-avoidable) and which high-frequency patterns
    /// are still uncovered (the next commands to build — feeds AUTHAPI-17 / AUTHAPI-24).
    /// </summary>
    [Serializable]
    class EvalCoverageSuggestions
    {
        [JsonProperty("covered")]
        public List<CoverageHit> Covered { get; set; } = new List<CoverageHit>();

        [JsonProperty("topUncovered")]
        public List<CoverageGap> TopUncovered { get; set; } = new List<CoverageGap>();
    }

    /// <summary>
    /// Aggregated eval-usage report: ranked API fingerprints, one-liner vs multi-statement split,
    /// error rate, and coverage suggestions. This is the artifact that turns eval-displacement backlog
    /// triage into a lookup (AUTHAPI-24 / AUTHAPI-29).
    /// </summary>
    [Serializable]
    class EvalUsageReport
    {
        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }

        [JsonProperty("successCount")]
        public int SuccessCount { get; set; }

        [JsonProperty("errorCount")]
        public int ErrorCount { get; set; }

        /// <summary>Fraction of recorded evals that failed, 0..1.</summary>
        [JsonProperty("errorRate")]
        public double ErrorRate { get; set; }

        [JsonProperty("singleExpressionCount")]
        public int SingleExpressionCount { get; set; }

        [JsonProperty("statementsCount")]
        public int StatementsCount { get; set; }

        /// <summary>Percentage of evals classified as one-liners (single-expression), 0..100.</summary>
        [JsonProperty("oneLinerPercentage")]
        public double OneLinerPercentage { get; set; }

        /// <summary>
        /// Total number of DISTINCT fingerprints observed across all records, before the ranked
        /// <see cref="Fingerprints"/> list is capped — so truncation is visible
        /// (<c>distinctFingerprints &gt; fingerprints.Count</c> means the tail was cut).
        /// </summary>
        [JsonProperty("distinctFingerprints")]
        public int DistinctFingerprints { get; set; }

        /// <summary>
        /// API fingerprints ranked by descending occurrence count. Capped (see
        /// <see cref="EvalUsageAggregator.Build"/>); <see cref="DistinctFingerprints"/> carries the
        /// uncapped total.
        /// </summary>
        [JsonProperty("fingerprints")]
        public List<FingerprintCount> Fingerprints { get; set; } = new List<FingerprintCount>();

        [JsonProperty("coverageSuggestions")]
        public EvalCoverageSuggestions CoverageSuggestions { get; set; } = new EvalCoverageSuggestions();
    }

    /// <summary>
    /// Curated, heuristic map from an API fingerprint to the command name that would cover it. Truth
    /// about whether that command actually exists comes from the live catalog at report time, so a
    /// mapped-but-absent command surfaces as a concrete gap ("build <c>refresh_assets</c>"). Kept
    /// deliberately small and extensible — this is a hint layer, not an exhaustive registry.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EvalCoverageMap
    {
        public static readonly IReadOnlyDictionary<string, string> Default = new Dictionary<string, string>
        {
            // The habit case: a command already exists, so these should stop using eval.
            { "PlayerSettings.SetScriptingBackend", CommandNames.SettingsPlayerSet },
            { "PlayerSettings.GetScriptingBackend", CommandNames.SettingsPlayerGet },
            // Known gaps from the WizardDuelVR evidence: no command yet (feeds AUTHAPI-17).
            // NOTE: save_all is NOT a cover for AssetDatabase.SaveAssets — it saves open scenes
            // (EditorSceneManager.SaveOpenScenes + AssetDatabase.Refresh) and never persists dirty
            // non-scene assets; steering agents off that eval to save_all would silently drop those
            // writes. Mapped to a proposed save_assets command so the report surfaces it as a gap.
            { "AssetDatabase.SaveAssets", "save_assets" },
            { "AssetDatabase.Refresh", "refresh_assets" },
        };
    }

    /// <summary>
    /// Builds an <see cref="EvalUsageReport"/> from recorded eval telemetry, cross-referencing the API
    /// fingerprints against the set of available command names.
    /// </summary>
    static class EvalUsageAggregator
    {
        /// <summary>Default cap on the suggested-gaps (<c>topUncovered</c>) list.</summary>
        public const int DefaultTopUncoveredLimit = 20;

        /// <summary>Default cap on the ranked <see cref="EvalUsageReport.Fingerprints"/> list.</summary>
        public const int DefaultFingerprintLimit = 50;

        /// <summary>
        /// Aggregate <paramref name="records"/> into a report. <paramref name="availableCommands"/> is
        /// the set of command names in the live catalog; <paramref name="coverageMap"/> defaults to
        /// <see cref="EvalCoverageMap.Default"/>. <paramref name="topUncoveredLimit"/> caps the
        /// suggested-gaps list and <paramref name="fingerprintLimit"/> caps the ranked fingerprint
        /// list (<see cref="EvalUsageReport.DistinctFingerprints"/> makes any truncation visible;
        /// coverage is computed from the FULL ranking, so the fingerprint cap never skews it).
        /// Accepted range for both limits: 0 or greater — negative values fall back to the defaults
        /// (0 legitimately empties the list).
        /// </summary>
        public static EvalUsageReport Build(
            IReadOnlyList<EvalUsageRecord> records,
            ISet<string> availableCommands,
            IReadOnlyDictionary<string, string> coverageMap = null,
            int topUncoveredLimit = DefaultTopUncoveredLimit,
            int fingerprintLimit = DefaultFingerprintLimit)
        {
            records = records ?? new List<EvalUsageRecord>();
            availableCommands = availableCommands ?? new HashSet<string>();
            coverageMap = coverageMap ?? EvalCoverageMap.Default;
            if (topUncoveredLimit < 0)
                topUncoveredLimit = DefaultTopUncoveredLimit;
            if (fingerprintLimit < 0)
                fingerprintLimit = DefaultFingerprintLimit;

            var report = new EvalUsageReport { TotalCount = records.Count };

            var counts = new Dictionary<string, int>();
            foreach (var record in records)
            {
                if (record.Success)
                    report.SuccessCount++;
                else
                    report.ErrorCount++;

                if (record.Classification == EvalClassification.SingleExpression)
                    report.SingleExpressionCount++;
                else
                    report.StatementsCount++;

                if (record.Fingerprints == null)
                    continue;
                foreach (var fingerprint in record.Fingerprints)
                {
                    if (string.IsNullOrEmpty(fingerprint))
                        continue;
                    counts.TryGetValue(fingerprint, out var current);
                    counts[fingerprint] = current + 1;
                }
            }

            if (report.TotalCount > 0)
            {
                report.ErrorRate = (double)report.ErrorCount / report.TotalCount;
                report.OneLinerPercentage = 100.0 * report.SingleExpressionCount / report.TotalCount;
            }

            // Deterministic ranking: count desc, then member name asc as a tiebreak.
            var ranked = counts
                .Select(kv => new FingerprintCount { Member = kv.Key, Count = kv.Value })
                .OrderByDescending(f => f.Count)
                .ThenBy(f => f.Member, StringComparer.Ordinal)
                .ToList();

            // Cap the reported ranking (an accumulated log can hold thousands of distinct
            // fingerprints); DistinctFingerprints preserves the uncapped total so truncation shows.
            report.DistinctFingerprints = ranked.Count;
            report.Fingerprints = ranked.Count > fingerprintLimit ? ranked.Take(fingerprintLimit).ToList() : ranked;

            // Coverage cross-references the FULL ranking, so a tight fingerprint cap cannot hide a
            // covered pattern or demote an uncovered one; topUncovered has its own cap.
            report.CoverageSuggestions = BuildCoverage(ranked, availableCommands, coverageMap, topUncoveredLimit);
            return report;
        }

        private static EvalCoverageSuggestions BuildCoverage(
            List<FingerprintCount> ranked,
            ISet<string> availableCommands,
            IReadOnlyDictionary<string, string> coverageMap,
            int topUncoveredLimit)
        {
            var suggestions = new EvalCoverageSuggestions();

            foreach (var entry in ranked)
            {
                coverageMap.TryGetValue(entry.Member, out var mappedCommand);
                var covered = mappedCommand != null && availableCommands.Contains(mappedCommand);

                if (covered)
                {
                    suggestions.Covered.Add(new CoverageHit
                    {
                        Member = entry.Member,
                        Count = entry.Count,
                        Command = mappedCommand
                    });
                }
                else
                {
                    suggestions.TopUncovered.Add(new CoverageGap
                    {
                        Member = entry.Member,
                        Count = entry.Count,
                        // A mapped-but-absent command is a concrete proposal; no mapping leaves it null.
                        SuggestedCommand = mappedCommand
                    });
                }
            }

            // Negative limits were normalised to the default in Build, so a plain cap suffices.
            if (suggestions.TopUncovered.Count > topUncoveredLimit)
                suggestions.TopUncovered = suggestions.TopUncovered.Take(topUncoveredLimit).ToList();

            return suggestions;
        }
    }
}
