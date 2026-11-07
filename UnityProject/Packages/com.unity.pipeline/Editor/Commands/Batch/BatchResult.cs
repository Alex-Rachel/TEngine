using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.Pipeline.Editor.Commands.Batch
{
    /// <summary>
    /// Result envelope for the <c>batch</c> command (AUTHAPI-27). Carries a per-operation result in
    /// input order, how many ops were applied, whether a transactional rollback occurred, and the
    /// Editor Undo group the whole batch collapsed into.
    /// </summary>
    sealed class BatchResult
    {
        /// <summary>Per-operation results, in the same order as the input operations.</summary>
        [JsonProperty("results")]
        public List<BatchOperationResult> Results { get; set; } = new List<BatchOperationResult>();

        /// <summary>Number of operations that executed successfully (before any rollback).</summary>
        [JsonProperty("applied")]
        public int Applied { get; set; }

        /// <summary>True when a transactional rollback undid every applied operation.</summary>
        [JsonProperty("reverted")]
        public bool Reverted { get; set; }

        /// <summary>
        /// Whether the batch ran transactionally (all ops in one Undo group, revert-on-failure).
        /// Forced false when <c>on-error=continue</c>.
        /// </summary>
        [JsonProperty("transactional")]
        public bool Transactional { get; set; }

        /// <summary>The effective error policy: <c>abort</c> or <c>continue</c>.</summary>
        [JsonProperty("onError")]
        public string OnError { get; set; }

        /// <summary>
        /// Batch-level abort reason for stops not attributable to a single op's failure:
        /// "batch timeout about to run out" (the cooperative <c>timeout</c> was about to run out between ops)
        /// or "batch canceled" (cooperative job cancellation). Null otherwise.
        /// </summary>
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        /// <summary>The Editor Undo group index the whole batch collapsed into (execution only).</summary>
        [JsonProperty("undoGroup", NullValueHandling = NullValueHandling.Ignore)]
        public int? UndoGroup { get; set; }

        /// <summary>True when this was a dry run (validation only, zero mutation).</summary>
        [JsonProperty("dryRun", NullValueHandling = NullValueHandling.Ignore)]
        public bool? DryRun { get; set; }

        /// <summary>Dry run only: whether every operation validated without error.</summary>
        [JsonProperty("valid", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Valid { get; set; }
    }

    /// <summary>Result of a single operation within a <see cref="BatchResult"/>.</summary>
    sealed class BatchOperationResult
    {
        /// <summary>The operation's id, echoed back when supplied.</summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        /// <summary>The command that was (or would be) run.</summary>
        [JsonProperty("command")]
        public string Command { get; set; }

        /// <summary>
        /// Whether the operation succeeded. On a dry run this is "would run" (validation passed).
        /// </summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>
        /// The command's result, shaped identically to running it standalone (optionally projected
        /// via <c>result-fields</c>, or replaced by a truncation marker for oversized results).
        /// </summary>
        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        /// <summary>Error message when the operation failed (or failed dry-run validation).</summary>
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        /// <summary>True for operations not attempted because an earlier op aborted the batch.</summary>
        [JsonProperty("skipped", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Skipped { get; set; }

        /// <summary>True when the result was replaced by a truncation marker (payload economy).</summary>
        [JsonProperty("resultTruncated", NullValueHandling = NullValueHandling.Ignore)]
        public bool? ResultTruncated { get; set; }

        /// <summary>
        /// Whether a transactional rollback can revert this operation's mutations. False for commands
        /// that mutate state outside the Editor Undo system (asset/file/settings writes) — those only
        /// run with <c>transactional=false</c>. Unset when the command never resolved.
        /// </summary>
        [JsonProperty("revertible", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Revertible { get; set; }
    }
}
