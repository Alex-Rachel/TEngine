using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline
{
    /// <summary>
    /// The one timeout of an <c>/api/exec</c> request. A command that is bounded by time declares a
    /// <c>[CliArg("timeout")] double timeoutInSeconds</c> parameter: its C# default is the command's
    /// default timeout, and the server writes the time left into it before the command runs. Everything past that
    /// parameter carries the timeout as a <see cref="TimeSpan"/>; <see cref="Timeout.InfiniteTimeSpan"/>
    /// means no timeout.
    /// </summary>
    internal static class PipelineTimeout
    {
        internal const string ParameterName = "timeout";

        /// <summary>The timeout of a command that declares no <c>timeout</c> parameter.</summary>
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        /// <summary>The wire value (milliseconds) that means "no timeout", sent for detached jobs.</summary>
        internal const int UnboundedRequestMs = int.MaxValue;

        /// <summary>Index of the command's <c>double</c> <c>timeout</c> parameter, or -1.</summary>
        internal static int IndexOfParameter(CommandInfo command)
        {
            for (var i = 0; i < command.Parameters.Count; i++)
            {
                var parameter = command.Parameters[i];
                if (parameter.Name == ParameterName && parameter.ParameterType == typeof(double))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The effective timeout: the request's own timeout, else the value bound to the command's
        /// <c>timeout</c> parameter (explicit or its default), else <see cref="DefaultTimeout"/>.
        /// </summary>
        /// <exception cref="ArgumentException">The bound <c>timeout</c> is not a positive number.</exception>
        internal static TimeSpan Resolve(CommandInfo command, TimeSpan? requestTimeout, object[] parameters)
        {
            if (requestTimeout.HasValue)
                return requestTimeout.Value;

            var index = IndexOfParameter(command);
            if (index < 0)
                return DefaultTimeout;

            var timeoutInSeconds = (double)parameters[index];
            if (!(timeoutInSeconds > 0))
                throw new ArgumentException($"Parameter '{ParameterName}' must be a positive number of seconds, got {timeoutInSeconds}.");
            return FromSeconds(timeoutInSeconds);
        }

        /// <summary>Writes <paramref name="timeout"/> into the command's <c>timeout</c> slot, if it has one.</summary>
        internal static void Apply(CommandInfo command, object[] parameters, TimeSpan timeout)
        {
            var index = IndexOfParameter(command);
            if (index >= 0)
                parameters[index] = IsInfinite(timeout) ? double.PositiveInfinity : timeout.TotalSeconds;
        }

        /// <summary>A command's timeout in seconds as a TimeSpan; positive infinity is no timeout.</summary>
        internal static TimeSpan FromSeconds(double timeoutInSeconds)
        {
            if (double.IsPositiveInfinity(timeoutInSeconds) || timeoutInSeconds >= TimeSpan.MaxValue.TotalSeconds)
                return Timeout.InfiniteTimeSpan;
            return TimeSpan.FromSeconds(Math.Max(0, timeoutInSeconds));
        }

        /// <summary>The request's wire timeout (milliseconds); <see cref="UnboundedRequestMs"/> is no timeout.</summary>
        internal static TimeSpan? FromRequest(int? milliseconds)
        {
            if (!milliseconds.HasValue)
                return null;
            return milliseconds.Value == UnboundedRequestMs
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromMilliseconds(milliseconds.Value);
        }

        internal static bool IsInfinite(TimeSpan timeout) => timeout == Timeout.InfiniteTimeSpan;

        /// <summary>
        /// The point into <paramref name="timeout"/> at which a command should stop and report its own
        /// timeout: early enough (min(2 s, 10%)) that its reply lands before the server's, and the
        /// client's, give up.
        /// </summary>
        internal static TimeSpan StopBefore(TimeSpan timeout)
        {
            if (IsInfinite(timeout))
                return timeout;
            return timeout - TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(2).Ticks, timeout.Ticks / 10));
        }

        /// <summary>
        /// What is left of <paramref name="timeout"/>, counted from <paramref name="startTimestamp"/> (a
        /// <see cref="Stopwatch"/> timestamp). Zero once it has run out; infinite stays infinite.
        /// </summary>
        internal static TimeSpan Remaining(TimeSpan timeout, long startTimestamp)
        {
            if (IsInfinite(timeout))
                return timeout;
            var elapsed = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency);
            var remaining = timeout - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>Formats a timeout for messages, e.g. <c>0.3 s</c>.</summary>
        internal static string Describe(TimeSpan timeout)
        {
            return IsInfinite(timeout)
                ? "unlimited"
                : timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";
        }
    }

    /// <summary>A command did not finish within its timeout; answered as HTTP 504 <c>TIMEOUT</c>.</summary>
    internal sealed class PipelineTimeoutException : TimeoutException
    {
        internal string CommandName { get; }
        internal TimeSpan Timeout { get; }

        /// <summary>False when the timeout ran out before the command started.</summary>
        internal bool Started { get; }

        internal PipelineTimeoutException(string commandName, TimeSpan timeout, bool started)
            : base(started
                ? $"'{commandName}' did not complete within its {PipelineTimeout.Describe(timeout)} timeout. It may still be running and its effects may still land. Pass a larger --timeout (seconds)."
                : $"'{commandName}' did not start within its {PipelineTimeout.Describe(timeout)} timeout while waiting for another command to finish; it did not run.")
        {
            CommandName = commandName;
            Timeout = timeout;
            Started = started;
        }
    }
}
