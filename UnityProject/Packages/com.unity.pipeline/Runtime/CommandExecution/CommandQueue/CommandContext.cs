using System;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Threading;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// The execution running on this thread, as the command's own code sees it: where its progress
    /// goes, whether its cancellation was asked for, and how it reaches the main thread. Lets
    /// CliProgress, PipelineCancellation and commands that run other commands find their execution
    /// without a reference to it.
    /// </summary>
    /// <remarks>
    /// <see cref="Current"/> is set only around the command's own invocation, so it is correct
    /// whichever thread runs the command: the set, the reads and the reset happen within one
    /// synchronous call frame. A command that awaits and resumes on another thread before reading
    /// it would not see it (no command does today).
    /// </remarks>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    sealed class CommandContext
    {
        [ThreadStatic] static CommandContext s_Current;

        readonly CommandExecution m_Execution;
        readonly Func<CommandContext, CommandInfo, JObject, TimeSpan, object> m_DispatchOnMainThread;

        /// <param name="progress">Where the execution reports its progress.</param>
        /// <param name="dispatcher">The dispatcher that runs work on the main thread.</param>
        /// <param name="execution">The execution, or null outside the queue.</param>
        /// <param name="dispatchOnMainThread">Runs another command on the main thread inside the given context.</param>
        /// <param name="result">Where the execution writes its result; null outside the queue.</param>
        /// <param name="std">The execution's standard streams; null outside the queue.</param>
        internal CommandContext(CliProgressState progress, Dispatcher dispatcher, CommandExecution execution,
            Func<CommandContext, CommandInfo, JObject, TimeSpan, object> dispatchOnMainThread, CommandResult result = null,
            StdStreams std = null)
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            m_Execution = execution;
            m_DispatchOnMainThread = dispatchOnMainThread ?? throw new ArgumentNullException(nameof(dispatchOnMainThread));
            Result = result;
            Std = std;
        }

        /// <summary>Where the execution writes its result; null outside the queue.</summary>
        internal CommandResult Result { get; }

        /// <summary>The execution's standard streams; null outside the queue.</summary>
        internal StdStreams Std { get; }

        /// <summary>The execution running on this thread, or null outside a command.</summary>
        internal static CommandContext Current => s_Current;

        /// <summary>Where the execution reports its progress.</summary>
        internal CliProgressState Progress { get; }

        /// <summary>Whether a client asked the execution to stop.</summary>
        internal bool IsCancellationRequested => m_Execution != null && m_Execution.CancellationRequested;

        /// <summary>Cancelled once a client asks the execution to stop; none outside the queue.</summary>
        internal System.Threading.CancellationToken CancellationToken =>
            m_Execution?.CancellationToken ?? System.Threading.CancellationToken.None;

        /// <summary>The dispatcher that runs work on the main thread.</summary>
        internal Dispatcher Dispatcher { get; }

        /// <summary>
        /// Where an eval records what it reports for analytics; null outside the queue and while
        /// nothing is collected.
        /// </summary>
        internal Telemetry.EvalAnalyticsDetails EvalDetails =>
            m_Execution == null ? null
            : m_Execution.EvalDetails ??= Telemetry.EvalAnalyticsDetails.IsCollecting() ? new Telemetry.EvalAnalyticsDetails() : null;

        /// <summary>
        /// Runs another command on the main thread, inside this execution, the way the queue runs
        /// one: same argument conversion, same required-parameter check, same result. Must be
        /// called on the main thread, and only with a command that completes synchronously.
        /// </summary>
        /// <param name="timeout">What is left of this execution's own timeout; written into the
        /// command's <c>timeout</c> parameter, if it has one.</param>
        /// <exception cref="ArgumentException">The parameters don't fit the command.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread, or the command failed.</exception>
        /// <exception cref="TimeoutException">The command ran out of time.</exception>
        internal object DispatchOnMainThread(CommandInfo command, JObject parameters, TimeSpan timeout)
        {
            return m_DispatchOnMainThread(this, command, parameters, timeout);
        }

        /// <summary>
        /// This execution as a command it runs sees it: without <see cref="Result"/> or
        /// <see cref="Std"/>, so that command returns its result instead of writing it.
        /// </summary>
        internal CommandContext Nested()
        {
            return new CommandContext(Progress, Dispatcher, m_Execution, m_DispatchOnMainThread);
        }

        /// <summary>Makes <paramref name="context"/> current until the returned scope is disposed.</summary>
        internal static Scope Enter(CommandContext context)
        {
            var previous = s_Current;
            s_Current = context;
            return new Scope(previous);
        }

        /// <summary>Restores the execution that was current before <see cref="Enter"/>.</summary>
        internal readonly struct Scope : IDisposable
        {
            readonly CommandContext m_Previous;

            internal Scope(CommandContext previous)
            {
                m_Previous = previous;
            }

            public void Dispose()
            {
                s_Current = m_Previous;
            }
        }
    }
}
