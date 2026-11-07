using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Telemetry;

namespace Unity.Pipeline.Runtime.Commands
{
    /// <summary>
    /// Read-back command for the local eval-usage telemetry (AUTHAPI-29). Aggregates
    /// <c>Library/Pipeline/eval-usage.jsonl</c> — merged with the rotated <c>eval-usage.old.jsonl</c>,
    /// so the report sees the full retained history (~2× the size cap) — into a ranked report: API
    /// fingerprint frequency, one-liner vs multi-statement split, error rate. It cross-references the
    /// fingerprints against the live command catalog to flag patterns already coverable by an existing
    /// command and the top uncovered patterns. This is the artifact that steers eval-displacement
    /// backlog triage (AUTHAPI-24). Read-only and off the main thread, so it stays servable even while
    /// the Editor is settling. The ranked lists are capped by <c>top</c>; the report's
    /// <c>distinctFingerprints</c> total makes any truncation visible.
    /// </summary>
    static class EvalTelemetryCommand
    {
        const string Group = "diagnostics";

        [CliGroupCommand(Group,
            "Aggregate local eval-usage telemetry into a ranked report: API fingerprint frequency, one-liner percentage, error rate, and command-coverage suggestions.",
            MainThreadRequired = false,
            Tags = new[] { "observability/eval_usage" })]
        public static EvalUsageReport ReportEvals(
            [CliArg("top", "Maximum entries in each ranked list (API fingerprints, and uncovered patterns suggested as commands). Accepted range: 0 or greater; negative values fall back to the default (50).")] int top = 50)
        {
            // Clamp, don't reject: a negative cap has no meaning, so fall back to the default rather
            // than fail the command or return an unbounded report.
            if (top < 0)
                top = EvalUsageAggregator.DefaultFingerprintLimit;

            var records = EvalUsageTelemetry.ReadRecords(EvalUsageTelemetry.ResolveDirectory());

            // Cross-reference against the live catalog. Discovery is already warm (the server queried
            // it to route this very request), so this reads the cache and touches no main-thread API.
            var availableCommands = new HashSet<string>(CommandRegistry.DiscoverCommands().Select(c => c.FullName));

            return EvalUsageAggregator.Build(records, availableCommands, topUncoveredLimit: top, fingerprintLimit: top);
        }
    }
}
