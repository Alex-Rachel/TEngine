using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Request model for /api/exec endpoint.
    /// Contains command name and parameters for remote command execution.
    /// </summary>
    [Serializable]
    class CommandExecutionRequest
    {
        /// <summary>
        /// The command to execute, by its full name (<c>group name</c>, or just the name for a
        /// command without a group). The one-word spelling (<c>group.name</c>) is accepted too.
        ///
        /// Optional, because <see cref="Argv"/> and <see cref="CommandLine"/> name the command in
        /// their leading tokens instead; the server assigns the resolved full name here before anything
        /// downstream reads it. Exactly one of the three forms may be present - <see cref="Validate"/> enforces
        /// that.
        /// </summary>
        [JsonProperty("command")]
        public string Command { get; set; }

        /// <summary>
        /// An unparsed command line: the server tokenizes it (see
        /// <see cref="CommandLineTokenizer"/>) and then binds it. For clients that genuinely
        /// hold raw text — MCP, a chat box, a pasted line. Mutually exclusive with
        /// <see cref="Command"/> and <see cref="Argv"/>.
        /// </summary>
        [JsonProperty("commandLine")]
        public string CommandLine { get; set; }

        /// <summary>
        /// Pre-split tokens, led by the command: <c>group name</c>, or its one-word spelling as one token. A
        /// group alone asks for its listing. Preferred over
        /// <see cref="CommandLine"/> by any caller that already has argv — on POSIX a CLI never
        /// holds the original command STRING, so making it re-quote tokens for the server to
        /// re-split would be a lossy round-trip across two independently-versioned quoting
        /// dialects. Mutually exclusive with <see cref="Command"/> and <see cref="CommandLine"/>.
        /// </summary>
        [JsonProperty("argv")]
        public List<string> Argv { get; set; }

        /// <summary>
        /// True when this request arrived in a raw form, so the reply should echo the bound
        /// parameters back. <c>[JsonIgnore]</c> is MANDATORY — this type is serialized
        /// (ExecResponseSerializer), and an internal flag must never reach the wire.
        /// </summary>
        [JsonIgnore]
        public bool IsRawForm => CommandLine != null || Argv != null;

        /// <summary>
        /// Parameters for the command execution.
        /// Contains key-value pairs matching command parameter names.
        /// </summary>
        [JsonProperty("parameters")]
        public JObject Parameters { get; set; }

        /// <summary>
        /// Optional timeout for command execution in milliseconds. When omitted, the command's own
        /// default applies (see <see cref="PipelineTimeout"/>).
        /// </summary>
        [JsonProperty("timeout")]
        public int? Timeout { get; set; }

        /// <summary>
        /// Opt into the full response envelope. When false (default) the server returns a lean,
        /// token-efficient reply — compact JSON, the envelope's own null keys omitted, and the
        /// always-on metadata (executedAt, command, executionTimeMs) dropped; the command's
        /// result payload keeps explicit nulls (see <see cref="OmitNulls"/>). Set true to get
        /// every envelope field back for debugging/correlation. (AUTHAPI-21)
        /// </summary>
        [JsonProperty("verbose")]
        public bool Verbose { get; set; }

        /// <summary>
        /// When true, drop null keys from the whole reply INCLUDING the command's result payload.
        /// Off by default: an absent payload key would otherwise be indistinguishable from a
        /// nonexistent or misspelled one. Opt in when the result schema is already known and the
        /// nulls are pure bytes — e.g. bulk list reads where a null column repeats per item.
        /// Orthogonal to <see cref="Verbose"/> (envelope metadata). (AUTHAPI-21)
        /// </summary>
        [JsonProperty("omitNulls")]
        public bool OmitNulls { get; set; }

        /// <summary>
        /// When true, run the command as a detached job (CLI-335): the response returns a job
        /// id immediately and the client collects the result later via GET /api/job?id=…,
        /// surviving its own HTTP timeout. Execution still queues through the server's
        /// one-command-at-a-time exec gate.
        /// </summary>
        [JsonProperty("job")]
        public bool Job { get; set; }

        /// <summary>
        /// Caller-generated id of the client invocation that sent this request, reported on the
        /// command-executed analytics event so it can be joined with the client's own event.
        /// </summary>
        [JsonProperty("commandId")]
        [JsonConverter(typeof(LenientAnalyticDataConverter))]
        public long? CommandId { get; set; }

        /// <summary>Who drove the client: <c>human</c>, <c>agent</c>, <c>ci</c> or <c>unknown</c>.</summary>
        [JsonProperty("clientSource")]
        [JsonConverter(typeof(LenientAnalyticDataConverter))]
        public string ClientSource { get; set; }

        /// <summary>Which AI client family drove the client, e.g. <c>claude</c>, or <c>none</c>.</summary>
        [JsonProperty("clientName")]
        [JsonConverter(typeof(LenientAnalyticDataConverter))]
        public string ClientName { get; set; }

        /// <summary>What invoked the client, such as <c>plugin</c> or <c>other</c>.</summary>
        [JsonProperty("caller")]
        [JsonConverter(typeof(LenientAnalyticDataConverter))]
        public string Caller { get; set; }

        /// <summary>The agent skill driving the client, such as <c>unity-editor</c>.</summary>
        [JsonProperty("skill")]
        [JsonConverter(typeof(LenientAnalyticDataConverter))]
        public string Skill { get; set; }

        /// <summary>
        /// Create a new command execution request.
        /// </summary>
        public CommandExecutionRequest()
        {
            Parameters = new JObject();
        }

        /// <summary>
        /// Create a command execution request with specified command and parameters.
        /// </summary>
        /// <param name="command">Name of the command to execute.</param>
        /// <param name="parameters">Parameters for the command execution.</param>
        public CommandExecutionRequest(string command, JObject parameters = null)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Parameters = parameters ?? new JObject();
        }

        /// <summary>
        /// Get a parameter value as the specified type.
        /// Returns the default value if the parameter is not found or cannot be converted.
        /// </summary>
        /// <typeparam name="T">The type to convert the parameter value to.</typeparam>
        /// <param name="name">Parameter name.</param>
        /// <param name="defaultValue">Value to return if the parameter is missing or unconvertible.</param>
        /// <returns>The parameter's value, or <paramref name="defaultValue"/>.</returns>
        public T GetParameter<T>(string name, T defaultValue = default(T))
        {
            if (Parameters == null || !Parameters.ContainsKey(name))
                return defaultValue;

            try
            {
                return Parameters[name].ToObject<T>();
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Check if a parameter exists in the request.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <returns>True if the parameter is present.</returns>
        public bool HasParameter(string name)
        {
            return Parameters != null && Parameters.ContainsKey(name);
        }

        /// <summary>
        /// Validate the request structure.
        /// </summary>
        /// <returns>An error message, or null if the request is valid.</returns>
        public string Validate()
        {
            // Exactly one of the three request forms. Counting them first keeps every
            // combination -- including all three at once -- on one message.
            var forms = 0;
            if (Command != null) forms++;
            if (CommandLine != null) forms++;
            if (Argv != null) forms++;

            if (forms > 1)
                return "Specify exactly one of 'command', 'commandLine' or 'argv'";

            if (IsRawForm)
            {
                // Rejected, not ignored: a silently-dropped payload is indistinguishable from
                // one that was never sent, which is the ambiguity the lean envelope already
                // legislates against.
                if (Parameters != null && Parameters.Count > 0)
                    return "'parameters' cannot be combined with 'commandLine' or 'argv'; the arguments belong in the command line itself";

                if (Argv != null)
                {
                    if (Argv.Count == 0)
                        return "'argv' must contain at least the command or its group";
                    for (var i = 0; i < Argv.Count; i++)
                        if (Argv[i] == null)
                            return "'argv' must not contain null elements";
                    if (string.IsNullOrWhiteSpace(Argv[0]))
                        return "'argv' must start with the command or its group";
                }
                else if (string.IsNullOrWhiteSpace(CommandLine))
                {
                    return "'commandLine' must not be empty";
                }
            }
            else if (string.IsNullOrWhiteSpace(Command))
            {
                return "Command name is required";
            }

            if (Timeout.HasValue && Timeout.Value <= 0)
                return "Timeout must be a positive number";

            return null; // No validation errors
        }
    }
}