using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Response model for /api/exec endpoint.
    /// Contains execution result and metadata for remote command execution.
    /// </summary>
    [Serializable]
    public class CommandExecutionResponse : BaseResponse
    {
        /// <summary>
        /// Whether the command executed successfully.
        /// </summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>
        /// The command that was executed, by its full name (<c>editor status</c>), as a user types
        /// it. On the /api/exec wire it is sent in verbose mode, and always when
        /// <see cref="EchoCommand"/> is set (replies to raw command-line requests).
        /// </summary>
        [JsonProperty("command")]
        public string Command { get; set; }

        /// <summary>
        /// Whether <see cref="Command"/> is serialized even in lean mode. Set on every reply to a raw
        /// (<c>argv</c> / <c>commandLine</c>) request, which names the command by position and so
        /// has no other way to tell the caller which command it resolved to. Never serialized itself;
        /// structured requests leave it false and keep the lean envelope.
        /// </summary>
        [JsonIgnore]
        internal bool EchoCommand { get; set; }

        /// <summary>
        /// The HTTP status a failed response is answered with when the command returns it: the
        /// envelope then fails too, with this response as its result. Null keeps the default, where
        /// a returned failure is the result of a successful envelope.
        /// </summary>
        [JsonIgnore]
        internal int? FailureStatusCode { get; set; }

        /// <summary>
        /// Whether <see cref="Result"/> is serialized although <see cref="Success"/> is false: set on
        /// an envelope answering a <see cref="FailureStatusCode"/> failure, whose result is the
        /// command's payload.
        /// </summary>
        [JsonIgnore]
        internal bool KeepsResultOnFailure { get; set; }


        /// <summary>
        /// Result returned by the command (if any).
        /// </summary>
        [JsonProperty("result")]
        public object Result { get; set; }

        /// <summary>
        /// Execution duration in milliseconds.
        /// </summary>
        /// <remarks>
        /// Like <c>command</c> and <c>executedAt</c>, emitted on the /api/exec wire only in verbose
        /// mode — the gating lives in <c>ExecResponseSerializer</c>, not on this model. (AUTHAPI-21)
        /// </remarks>
        [JsonProperty("executionTimeMs")]
        public long? ExecutionTimeMs { get; set; }

        /// <summary>
        /// Coarse machine-readable server state marker. Set to "busy" when the command was not
        /// executed because the server's host cannot service it yet (e.g. the Editor is still
        /// settling after a cold start). Omitted from the JSON on ordinary success/failure
        /// responses, so existing envelopes are unchanged.
        /// </summary>
        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public string Status { get; set; }

        /// <summary>
        /// True when the failure is transient and the same request is expected to succeed after a
        /// short wait (poll /api/status until it reports "ready"). Omitted from the JSON on
        /// ordinary success/failure responses.
        /// </summary>
        [JsonProperty("retryable", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Retryable { get; set; }

        /// <summary>
        /// Stable, machine-readable failure discriminator (e.g. <c>INVALID_COMMAND_ARGS</c>, <c>TIMEOUT</c>).
        ///
        /// HTTP status cannot serve as the discriminator here: most /api/exec failures are 400, so
        /// status alone cannot separate "your arguments are wrong" from "the command threw".
        /// Spelled to match the <c>unity</c> CLI's own InvalidArgsCode literal, so neither side
        /// needs a translation table.
        /// </summary>
        [JsonProperty("errorCode", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorCode { get; set; }

        /// <summary>
        /// Every argument-binding defect found, one entry each — the binder accumulates rather
        /// than bailing, so a user fixing a command line sees all of its problems at once.
        /// Machine-readable rather than prose, because the <c>unity</c> CLI renders and localizes
        /// these itself; <see cref="BaseResponse.ErrorDetails"/> carries the English fallback for
        /// clients that do not.
        /// </summary>
        [JsonProperty("argProblems", NullValueHandling = NullValueHandling.Ignore)]
        public List<ArgProblem> ArgProblems { get; set; }

        /// <summary>
        /// The resolved command's catalog entry, shaped like a /api/commands <c>commands[]</c>
        /// element. Sent with an argument error so a client can render usage without a separate
        /// schema request. Free to produce - the command is already resolved at that point.
        /// </summary>
        [JsonProperty("commandSchema", NullValueHandling = NullValueHandling.Ignore)]
        public JObject CommandSchema { get; set; }

        /// <summary>
        /// What the server bound from a raw command line. Set ONLY for argv/commandLine requests,
        /// leaving the structured path's envelope unchanged. A client that does not bind locally
        /// has no other way to report what the command actually received.
        /// </summary>
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public JObject BoundParameters { get; set; }

        /// <summary>
        /// The group this reply lists, when a raw request named a group and nothing after it
        /// (<c>unity command animation</c>): <see cref="Result"/> is then that group's listing, not a
        /// command's result. Absent on every other reply, so a client can render a listing without
        /// guessing from the result's shape.
        /// </summary>
        [JsonProperty("listedGroup", NullValueHandling = NullValueHandling.Ignore)]
        public string ListedGroup { get; set; }

        /// <summary>
        /// Create the argument-binding failure response (sent with HTTP 400): the command was
        /// resolved but its arguments could not be bound, so nothing executed.
        /// </summary>
        internal static CommandExecutionResponse CmdInvalidArgs(string cmd, string details,
            List<ArgProblem> problems, JObject commandSchema)
        {
            return new CommandExecutionResponse
            {
                Command = cmd,
                Success = false,
                ExecutedAt = DateTime.UtcNow,
                Error = "Invalid Command Arguments",
                ErrorDetails = details,
                ErrorCode = "INVALID_COMMAND_ARGS",
                ArgProblems = problems,
                CommandSchema = commandSchema
            };
        }

        /// <summary>
        /// Create the timeout response (sent with HTTP 504): the command did not finish, or did not
        /// start, within its timeout.
        /// </summary>
        internal static CommandExecutionResponse CmdTimeout(string cmd, string details)
        {
            return new CommandExecutionResponse
            {
                Command = cmd,
                Success = false,
                ExecutedAt = DateTime.UtcNow,
                Error = "Request Timed Out",
                ErrorDetails = details,
                ErrorCode = "TIMEOUT",
                Retryable = false
            };
        }

        /// <summary>
        /// The specific cause of a "busy" response, matching /api/status's own "status" values for
        /// the same conditions ("settling", "blocked_by_dialog") — a caller can branch on this
        /// directly instead of inferring the cause from whether "dialogs" happens to be populated.
        /// Omitted from the JSON on ordinary success/failure responses.
        /// </summary>
        [JsonProperty("busyReason", NullValueHandling = NullValueHandling.Ignore)]
        public string BusyReason { get; set; }

        /// <summary>
        /// No longer set: the server stopped reporting the dialogs that opened while a command ran.
        /// Kept, always null and so omitted from the JSON, so the type's public members don't change.
        /// </summary>
        [Obsolete("No longer set; poll GET /api/dialog for the dialogs open now.")]
        [JsonProperty("dialogsDuringExecution", NullValueHandling = NullValueHandling.Ignore)]
        public List<object> DialogsDuringExecution { get; set; }

        /// <summary>
        /// Currently-open dialog(s) (see EditorDialogEvents), attached to a busy response when a
        /// MainThreadRequired command was rejected because a modal dialog is blocking the main
        /// thread. Omitted otherwise.
        /// </summary>
        [JsonProperty("dialogs", NullValueHandling = NullValueHandling.Ignore)]
        public List<object> Dialogs { get; set; }

        /// <summary>
        /// Create a successful execution response.
        /// </summary>
        /// <param name="command">Name of the executed command.</param>
        /// <param name="result">The command's result payload.</param>
        /// <param name="executionTimeMs">How long the command took to execute, in milliseconds.</param>
        /// <returns>A successful response wrapping <paramref name="result"/>.</returns>
        public static CommandExecutionResponse CmdSuccess(string command, object result = null, long? executionTimeMs = null)
        {
            return new CommandExecutionResponse
            {
                Success = true,
                Command = command,
                ExecutedAt = DateTime.UtcNow,
                Result = result,
                ExecutionTimeMs = executionTimeMs
            };
        }

        /// <summary>
        /// Create a failed execution response.
        /// </summary>
        /// <param name="cmd">Name of the executed command.</param>
        /// <param name="error">Error message.</param>
        /// <param name="errorDetails">Additional error details for debugging.</param>
        /// <returns>A failed response with <see cref="BaseResponse.Error"/>/<see cref="BaseResponse.ErrorDetails"/> set.</returns>
        public static CommandExecutionResponse CmdFailure(string cmd, string error, string errorDetails)
        {
            return new CommandExecutionResponse
            {
                Command = cmd,
                Success = false,
                ExecutedAt = DateTime.UtcNow,
                Error = error,
                ErrorDetails = errorDetails
            };
        }

        /// <summary>
        /// Create the retryable "server busy" response (sent with HTTP 503): the command was not
        /// executed because the host cannot service it yet — distinguishable from a genuine
        /// command failure via status/retryable so callers know to retry shortly instead of
        /// guessing (AUTHAPI-35). <paramref name="busyReason"/> is the specific cause ("settling",
        /// "blocked_by_dialog", ...); omit it only for a generic/legacy busy state.
        /// </summary>
        /// <param name="cmd">Name of the command that could not be serviced yet.</param>
        /// <param name="details">Human-readable reason the host is busy.</param>
        /// <param name="busyReason">The specific cause of the busy state, or null for a generic/legacy busy state.</param>
        /// <returns>A retryable "server busy" response.</returns>
        public static CommandExecutionResponse CmdBusy(string cmd, string details, string busyReason = null)
        {
            return new CommandExecutionResponse
            {
                Command = cmd,
                Success = false,
                ExecutedAt = DateTime.UtcNow,
                Error = "Server Busy",
                ErrorDetails = details,
                Status = "busy",
                Retryable = true,
                BusyReason = busyReason
            };
        }
    }
}