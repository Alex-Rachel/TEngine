using System.Collections.Generic;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Models;
using Unity.Pipeline.Telemetry;

namespace Unity.Pipeline
{
    /// <summary>
    /// Either one finished <c>/api/exec</c> transaction, handed to
    /// <see cref="BasePipelineServer.OnCommandDone"/>, or one command that ran, which the
    /// executor reports once it ends. Both carry the caller context the request sent.
    ///
    /// The transaction half and the execution half are reported apart, because the two are not
    /// the same set: a request can produce a transaction without executing anything (rejected for
    /// size, malformed JSON, unknown command, busy host, job queue full), and a detached job
    /// executes long after its own response was sent. Consumers read the half they care about.
    /// </summary>
    internal readonly struct CommandExecutionInfo
    {
        /// <summary>
        /// Raw request JSON, or null when there is no transaction to report — a detached job
        /// completing after its submission response was already sent.
        /// </summary>
        public string RequestJson { get; }

        /// <summary>
        /// Raw response JSON as it went on the wire, or null alongside a null
        /// <see cref="RequestJson"/>.
        /// </summary>
        public string ResponseJson { get; }

        /// <summary>
        /// The command that ran, or null when the request was rejected before any command
        /// executed.
        /// </summary>
        public CommandInfo Command { get; }

        /// <summary>
        /// The parameters the command was invoked with, or null when no command ran. Lets a consumer
        /// answer questions about WHAT was asked, not just which command served it — the analytics
        /// reporter reads an eval body from here to decide whether a concrete command already covers
        /// it. Never serialized anywhere: it is in-memory only, for the moment between the command
        /// finishing and the post-command work running.
        /// </summary>
        public IReadOnlyDictionary<string, object> Parameters { get; }

        /// <summary>
        /// Whether the command reported success. False when it threw AND when it returned a
        /// response carrying its own failure (eval, method reload and test runs all do the latter).
        /// Meaningless when <see cref="Command"/> is null.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Wall-clock milliseconds spent inside the command itself, excluding the wait for the
        /// one-command-at-a-time execution gate. Zero when <see cref="Command"/> is null.
        /// </summary>
        public long DurationMs { get; }

        /// <summary>The caller's invocation id from the request, or 0 when it sent none.</summary>
        internal long CommandId { get; }

        /// <summary>The caller's actor from the request (human, agent, ...), or null.</summary>
        internal string ClientSource { get; }

        /// <summary>The caller's AI client family from the request, or null.</summary>
        internal string ClientName { get; }

        /// <summary>What invoked the caller (plugin, other), or null.</summary>
        internal string Caller { get; }

        /// <summary>The agent skill driving the caller, or null.</summary>
        internal string Skill { get; }

        /// <summary>
        /// Why the command failed: the error code it answered with, such as <c>COMPILATION_ERROR</c>,
        /// <c>TIMEOUT</c>, <c>CANCELLED</c>, or <c>OTHER</c>; null when it succeeded or did not run.
        /// </summary>
        internal string FailureKind { get; }

        /// <summary>What an eval recorded about itself for analytics; null for any other command.</summary>
        internal EvalAnalyticsDetails EvalDetails { get; }

        /// <summary>Create an info describing one finished interaction.</summary>
        /// <param name="requestJson">Raw request JSON, or null when there is no transaction.</param>
        /// <param name="responseJson">Raw response JSON, or null alongside a null request.</param>
        /// <param name="command">The command that ran, or null when none did.</param>
        /// <param name="success">Whether the command reported success.</param>
        /// <param name="durationMs">Milliseconds spent inside the command.</param>
        /// <param name="parameters">The parameters the command was invoked with, or null.</param>
        public CommandExecutionInfo(string requestJson, string responseJson, CommandInfo command,
            bool success, long durationMs, IReadOnlyDictionary<string, object> parameters = null)
            : this(requestJson, responseJson, command, success, durationMs, parameters, 0, null, null, null, null, null, null)
        {
        }

        CommandExecutionInfo(string requestJson, string responseJson, CommandInfo command, bool success,
            long durationMs, IReadOnlyDictionary<string, object> parameters, long commandId, string clientSource,
            string clientName, string caller, string skill, string failureKind, EvalAnalyticsDetails evalDetails)
        {
            RequestJson = requestJson;
            ResponseJson = responseJson;
            Command = command;
            Success = success;
            DurationMs = durationMs;
            Parameters = parameters;
            CommandId = commandId;
            ClientSource = Bounded(clientSource);
            ClientName = Bounded(clientName);
            Caller = Bounded(caller);
            Skill = Bounded(skill);
            FailureKind = failureKind;
            EvalDetails = evalDetails;
        }

        /// <summary>
        /// One finished <c>/api/exec</c> transaction, with the caller context its request carried
        /// and no execution: the executor reports the command, if one ran. A null request carries
        /// no caller context.
        /// </summary>
        internal static CommandExecutionInfo ForTransaction(CommandExecutionRequest request, string requestJson,
            string responseJson)
        {
            return new CommandExecutionInfo(requestJson, responseJson, null, false, 0, null,
                request?.CommandId ?? 0, request?.ClientSource, request?.ClientName, request?.Caller, request?.Skill, null, null);
        }

        /// <summary>
        /// A command that ran for <paramref name="request"/>, once it ended: an execution with no
        /// transaction, with the arguments it was bound to and the request's caller context.
        /// </summary>
        internal static CommandExecutionInfo ForExecution(CommandRequest request, CommandInfo command,
            IReadOnlyDictionary<string, object> parameters, bool success, long durationMs, string failureKind = null,
            EvalAnalyticsDetails evalDetails = null)
        {
            var requester = request?.Requester ?? default;
            return new CommandExecutionInfo(null, null, command, success, durationMs, parameters,
                requester.InvocationId ?? 0, requester.Source, requester.Name, requester.Caller, requester.Skill,
                failureKind, evalDetails);
        }

        /// <summary>
        /// The <see cref="FailureKind"/> of an execution that ended with <paramref name="result"/>,
        /// having answered with <paramref name="errorCode"/> if it returned its own failure.
        /// </summary>
        internal static string FailureKindOf(ExitCode result, string errorCode)
        {
            switch (result)
            {
                case ExitCode.Success:
                    return null;
                case ExitCode.TimedOut:
                    return "TIMEOUT";
                case ExitCode.Cancelled:
                    return "CANCELLED";
                default:
                    return string.IsNullOrEmpty(errorCode) ? "OTHER" : errorCode;
            }
        }

        const int MaxClientValueLength = 64;

        // Any loopback caller can write these, and they end up as analytics columns.
        static string Bounded(string value) => value != null && value.Length <= MaxClientValueLength ? value : null;
    }
}
