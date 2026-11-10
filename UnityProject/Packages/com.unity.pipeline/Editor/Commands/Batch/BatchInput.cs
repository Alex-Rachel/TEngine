using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline.Editor.Commands.Batch
{
    /// <summary>
    /// A single operation in a <c>batch</c> request (AUTHAPI-27): the name of a registered command
    /// plus the parameters to invoke it with, and an optional <see cref="Id"/> so later operations
    /// can reference this one's result.
    ///
    /// String values anywhere in <see cref="Params"/> may reference an earlier operation's result
    /// with <c>"$&lt;id-or-index&gt;.&lt;jsonPath&gt;"</c> (e.g. <c>"$0.instanceId"</c> or
    /// <c>"$createHead.components[0].instanceId"</c>); <c>"$$"</c> escapes a literal <c>'$'</c>.
    /// References are backward-only. Implements <see cref="IStructuredCommandInput"/> so the batch
    /// command advertises a nested object schema (via <c>GET /api/commands</c>) rather than a string.
    /// </summary>
    sealed class BatchOperationInput : IStructuredCommandInput
    {
        /// <summary>
        /// Optional operation id used to reference this operation's result from a later op
        /// (<c>"$&lt;id&gt;.&lt;field&gt;"</c>). Must be unique within the batch when supplied, and
        /// must match <c>^[A-Za-z_][A-Za-z0-9_-]*$</c> — purely numeric ids would be ambiguous with
        /// 0-based index selectors, and <c>'.'</c> collides with the reference path separator.
        /// </summary>
        [CliArg("id", "Optional id for referencing this op's result from a later op (\"$<id>.<field>\"). Unique within the batch; must match ^[A-Za-z_][A-Za-z0-9_-]*$ (not purely numeric, no dots).")]
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// The registered command to run (any command except the excluded list), written as
        /// <c>group name</c> (<c>gameobject create</c>). A single token is read as the one-word spelling of a
        /// full name, the same way a raw request does.
        /// </summary>
        [CliArg("command", "The command to run, written as \"<group> <name>\" (e.g. \"gameobject create\").", Required = true)]
        [JsonProperty("command")]
        public string Command { get; set; }

        /// <summary>
        /// Parameters for the command, same shape as <c>/api/exec</c> <c>parameters</c>. Any string
        /// value may reference a prior op's result (<c>"$&lt;id-or-index&gt;.&lt;jsonPath&gt;"</c>).
        /// </summary>
        [CliArg("params", "Parameters for the command. String values may reference a prior op's result: \"$<id-or-index>.<jsonPath>\".")]
        [JsonProperty("params")]
        public JObject Params { get; set; }
    }
}
