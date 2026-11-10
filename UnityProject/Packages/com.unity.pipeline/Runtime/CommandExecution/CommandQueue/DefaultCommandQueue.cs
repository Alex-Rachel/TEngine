using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Models;
using Unity.Pipeline.Threading;
using UnityEngine;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// <see cref="ICommandQueue"/> for one server: runs every execution behind one gate, each on its server's dispatcher when it needs the main thread. Holds the
    /// executions and the progress of the one running now. The command's own code sees its
    /// execution through <see cref="CommandContext"/>.
    /// </summary>
    /// <remarks>
    /// A command still returns its result: the queue writes it to the request's Out through
    /// <see cref="CommandResult"/>, in the format the request picked, and writes why a command
    /// failed to its Error.
    /// </remarks>
    class DefaultCommandQueue : ICommandQueue
    {
        /// <summary>
        /// Commands allowed waiting or running at once. They run strictly one at a time, so a
        /// backlog beyond this is pure waiting with no throughput benefit — a client loop (or a
        /// retrying script) submitting faster than commands drain would otherwise grow it without
        /// bound.
        /// </summary>
        internal const int MaxCommands = 100;

        const string TooManyCommandsMessage = "Too many commands are queued or running; wait for some to finish and retry.";

        /// <summary>
        /// Serializes every execution: whichever client or transport asked, they queue behind each
        /// other in arrival order, while read-only endpoints (/api/status, /api/progress, …) and
        /// immediate commands answer at once (EXEC-001).
        /// </summary>
        readonly SemaphoreSlim m_Gate = new SemaphoreSlim(1, 1);

        readonly Dispatcher m_Dispatcher;
        readonly Action<CommandExecutionInfo> m_OnEnded;

        /// <param name="dispatcher">Runs the commands that need the main thread; its owner pumps it.</param>
        /// <param name="onEnded">Told about each execution that ran, once it ends.</param>
        public DefaultCommandQueue(Dispatcher dispatcher, Action<CommandExecutionInfo> onEnded)
        {
            m_Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            m_OnEnded = onEnded ?? throw new ArgumentNullException(nameof(onEnded));
        }

        /// <summary>The progress of the execution running now (see CliProgress).</summary>
        internal CliProgressState Progress { get; } = new CliProgressState();

        /// <summary>How many executions wait or run, for <see cref="MaxCommands"/>.</summary>
        int m_Count;

        /// <summary>Guards <see cref="m_Count"/>, so a concurrent Enqueue can't race past the limit.</summary>
        readonly object m_CountGate = new object();

        /// <inheritdoc/>
        public CommandExecution Enqueue(CommandInfo command, IReadOnlyDictionary<string, object> parameters,
            IReadOnlyList<object> arguments, CommandRequest request)
        {
            // The request's timeout counts from here, so it also bounds the wait for the gate.
            var arrivalTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var queueTimeout = ResolveQueueTimeout(command, arguments, request.Timeout);

            // Taken last, so nothing can throw between taking a place and the run that gives it back.
            // An immediate command takes none: it waits for nothing, so it can't build a backlog.
            if (!command.Immediate)
            {
                lock (m_CountGate)
                {
                    if (m_Count >= MaxCommands)
                        throw new TooManyCommandsException(command.FullName, MaxCommands, TooManyCommandsMessage);
                    m_Count++;
                }
            }

            var execution = new CommandExecution(CommandExecutionId.New(), command.FullName, parameters, Progress);
            _ = RunExecution(execution, command, arguments, request, arrivalTimestamp, queueTimeout);
            return execution;
        }

        /// <summary>
        /// Extract, validate, and invoke a pre-resolved command with JSON parameters synchronously on
        /// the current main thread, inside <paramref name="context"/>, returning its unwrapped
        /// result. This is the exact parameter extraction and required-parameter validation the
        /// executor applies, and the reflection invoke and Task unwrap <see cref="Invoke"/> uses, for composite commands
        /// (e.g. <c>batch</c>) that re-dispatch other registered commands from inside their own
        /// main-thread execution (see <see cref="CommandContext.DispatchOnMainThread"/>) — so each
        /// sub-operation behaves, and its result is shaped, identically to a standalone call.
        ///
        /// MUST be called on the main thread. A composite command is itself
        /// <c>MainThreadRequired</c>, so it already runs there; calling off the main thread throws.
        /// A command that throws surfaces exactly as it would through <c>/api/exec</c>
        /// (<see cref="ArgumentException"/> and <see cref="TimeoutException"/> preserved, other exceptions
        /// wrapped in <see cref="InvalidOperationException"/>) so the caller can record a per-operation failure.
        /// </summary>
        /// <param name="timeout">What is left of the composite command's own timeout; written
        /// into the sub-command's <c>timeout</c> parameter, if it has one.</param>
        object DispatchOnMainThread(CommandContext context, CommandInfo command, JObject parametersJson, TimeSpan timeout)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (!m_Dispatcher.IsMainThread())
                throw new InvalidOperationException(
                    "DispatchOnMainThread must be called on the main thread.");

            var parameters = BasePipelineServer.ExtractCommandParameters(command, parametersJson, out var conversionError);

            // Same contract as Invoke: an argument that could not be converted is a rejected
            // request, not a silent fallback to the parameter's default.
            if (!string.IsNullOrEmpty(conversionError))
                throw new ArgumentException(conversionError);

            var validationError = BasePipelineServer.ValidateCommandParameters(command, parameters);
            if (!string.IsNullOrEmpty(validationError))
                throw new ArgumentException(validationError);

            PipelineTimeout.Apply(command, parameters, timeout);
            // Nested: the sub-command returns its result to the composite rather than writing it.
            var raw = InvokeDirect(context.Nested(), command, parameters);
            // Unwrap a Task result inline. WARNING: this GetResult() blocks the main thread, so it is
            // only safe for results that are already complete (synchronous commands, or async methods
            // that happened to complete synchronously). An async command driven to completion by
            // EditorApplication.update callbacks (run_tests/list_tests) could NEVER finish here —
            // update cannot pump while this call blocks — and the Editor would freeze permanently.
            // That is why composite callers (batch) must reject commands with Task return types
            // before dispatching them through this path.
            return UnwrapResult(raw).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Whether a command reported success. A command fails in two ways: by throwing, which the
        /// caller has already accounted for, or by RETURNING a response that carries its own
        /// failure — eval, method reload, run_script and test runs all do the latter, so nothing
        /// throws and the outer envelope says success while the inner result says otherwise.
        /// </summary>
        static bool IsSuccessfulResult(object result)
        {
            switch (result)
            {
                case CommandExecutionResponse commandResponse:
                    return commandResponse.Success;
                case BaseResponse baseResponse:
                    return string.IsNullOrEmpty(baseResponse.Error);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> under the gate: tracks the in-flight count for
        /// /api/progress ("active" while any exec runs) via CliProgress's own atomic
        /// BeginExecutionCount/EndExecutionCount, owns CliProgress for the duration
        /// (BeginExecution/EndExecution) so a queued execution can't surface the previous one's
        /// stale progress, and resets the explicit report when the last in-flight execution
        /// completes.
        /// </summary>
        /// <param name="executionId">Id CliProgress.Report calls during this execution are attributed to.</param>
        /// <param name="body">The gated work to run once the gate is acquired.</param>
        /// <param name="preStartCheck">Runs after the gate is acquired but before
        /// CliProgress.BeginExecution; returning false skips <paramref name="body"/> entirely
        /// (to honor a cancellation requested while still queued).</param>
        /// <param name="gateWait">How long to wait for the gate; unlimited when null.</param>
        /// <param name="cancellation">Stops the wait for the gate; <paramref name="body"/> then
        /// does not run.</param>
        /// <returns>False when the gate was not acquired within <paramref name="gateWait"/>;
        /// <paramref name="body"/> then did not run.</returns>
        async Task<bool> RunGated(string executionId, Func<Task> body, Func<bool> preStartCheck = null,
            TimeSpan? gateWait = null, CancellationToken cancellation = default)
        {
            Progress.BeginExecutionCount();
            try
            {
                try
                {
                    if (!await m_Gate.WaitAsync(gateWait ?? Timeout.InfiniteTimeSpan, cancellation))
                        return false;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return true;
                }
                try
                {
                    if (preStartCheck != null && !preStartCheck())
                        return true;

                    Progress.BeginExecution(executionId);
                    try
                    {
                        await body();
                    }
                    finally
                    {
                        Progress.EndExecution(executionId);
                    }
                    return true;
                }
                finally
                {
                    m_Gate.Release();
                }
            }
            finally
            {
                Progress.EndExecutionCount();
            }
        }

        /// <summary>
        /// The timeout of a request, resolved before it queues so it also bounds the wait at the
        /// gate. A <c>timeout</c> parameter the command would reject counts as the default
        /// here; the command's own validation reports it once it is dispatched.
        /// </summary>
        static TimeSpan ResolveQueueTimeout(CommandInfo command, IReadOnlyList<object> arguments, TimeSpan? requestTimeout)
        {
            if (requestTimeout.HasValue)
                return requestTimeout.Value;

            try
            {
                return PipelineTimeout.Resolve(command, null, arguments?.ToArray());
            }
            catch (ArgumentException)
            {
                return PipelineTimeout.DefaultTimeout;
            }
        }

        /// <summary>
        /// Runs one execution under the gate and ends it: writes what the command returned to
        /// the request's Out, or why it failed to its Error, reports it once it ran, and then lets
        /// its awaiters go on. A cancellation requested while it waits ends it at once, unstarted;
        /// a running command is only cooperatively cancellable (see PipelineCancellation). Never
        /// throws. An immediate command runs at once, outside the gate, and leaves the progress to
        /// the command that holds it.
        /// </summary>
        /// <param name="arrivalTimestamp">When the request arrived; time spent waiting counts against its timeout.</param>
        /// <param name="queueTimeout">How long it may wait for the gate, counted from its arrival.</param>
        async Task RunExecution(CommandExecution execution, CommandInfo command, IReadOnlyList<object> arguments,
            CommandRequest request, long arrivalTimestamp, TimeSpan queueTimeout)
        {
            // Stays Cancelled for an execution cancelled before it started, which runs nothing.
            var result = ExitCode.Cancelled;
            var ran = false;
            var timer = new System.Diagnostics.Stopwatch();
            try
            {
                try
                {
                    async Task Run()
                    {
                        execution.MarkRunning();
                        ran = true;
                        timer.Start();
                        // Made inside the try, so options it can't read end the run as InvalidUsage.
                        CommandResult output = null;
                        try
                        {
                            output = new CommandResult(request);
                            var returned = await Invoke(command, arguments, request.Timeout, arrivalTimestamp, execution, output, new StdStreams(request));
                            if (execution.CancellationRequested)
                            {
                                // The code honored (or raced) a cancellation request; report
                                // cancelled rather than a half-relevant result.
                                result = ExitCode.Cancelled;
                            }
                            else
                            {
                                result = IsSuccessfulResult(returned) ? ExitCode.Success : ExitCode.Failed;
                                // Outside JSON, a failure response is reported on Error.
                                if (result == ExitCode.Failed && request.FormatOutput && output.Format != OutputFormat.Json
                                    && returned is BaseResponse failed)
                                    WriteError(execution, request, DescribeFailure(failed));
                                else
                                    output.WriteReturned(returned);
                                if (returned is CommandExecutionResponse { Success: false, FailureStatusCode: not null } failure)
                                    execution.ReturnedFailure = failure;
                            }
                        }
                        catch (Exception ex)
                        {
                            // The command layer wraps what a command throws (an
                            // OperationCanceledException from PipelineCancellation surfaces as a
                            // wrapped execution failure) — after a cancellation request, any failure
                            // is the cancellation taking effect.
                            result = execution.CancellationRequested ? ExitCode.Cancelled : Classify(ex);
                            if (result != ExitCode.Cancelled)
                                WriteError(execution, request, ex.Message);
                        }
                        finally
                        {
                            output?.Complete();
                            timer.Stop();
                        }
                    }

                    var acquired = true;
                    if (command.Immediate)
                    {
                        if (!execution.CancellationRequested)
                            await Run();
                    }
                    else
                    {
                        acquired = await RunGated(execution.Id.ToString(), Run,
                            preStartCheck: () => !execution.CancellationRequested,
                            gateWait: PipelineTimeout.Remaining(queueTimeout, arrivalTimestamp),
                            cancellation: execution.CancellationToken);
                    }

                    if (!acquired)
                    {
                        result = ExitCode.TimedOut;
                        WriteError(execution, request, new PipelineTimeoutException(command.FullName, queueTimeout, started: false).Message);
                    }
                }
                catch (Exception ex)
                {
                    // A failure of the queue itself must be recorded, never thrown: the run is detached,
                    // and its awaiters wait for its result.
                    Debug.LogError($"Running {command.FullName} ({execution.Id}) failed: {ex.Message}");
                    result = ExitCode.Failed;
                    WriteError(execution, request, ex.Message);
                }
                finally
                {
                    execution.MarkEnded(result);
                    if (!command.Immediate)
                    {
                        lock (m_CountGate)
                            m_Count--;
                    }
                }

                // Only once the command's real result is known; an execution that never started
                // reports nothing.
                if (ran)
                    m_OnEnded(CommandExecutionInfo.ForExecution(request, command, execution.Parameters, result == ExitCode.Success,
                        timer.ElapsedMilliseconds, CommandExecutionInfo.FailureKindOf(result, execution.ReturnedFailure?.ErrorCode),
                        execution.EvalDetails));
            }
            finally
            {
                // Whatever happened above, and after the report, so whoever awaits the execution
                // finds it made and is never left waiting.
                execution.Complete(result);
            }
        }

        /// <summary>
        /// Writes why a command failed to its request's Error, as far as the writer lets it: a
        /// writer that fails, such as a stream whose client went away, is logged, never thrown, so
        /// the execution still ends.
        /// </summary>
        static void WriteError(CommandExecution execution, CommandRequest request, string message)
        {
            try
            {
                request.Error.WriteLine(message);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Couldn't write why {execution.Command} ({execution.Id}) failed: {ex.Message}");
            }
        }

        /// <summary>The error a failure response carries.</summary>
        static string DescribeFailure(BaseResponse failed)
        {
            var message = string.IsNullOrEmpty(failed.Error) ? "Unknown error" : failed.Error;
            return string.IsNullOrEmpty(failed.ErrorDetails) ? message : $"{message}. {failed.ErrorDetails}";
        }

        /// <summary>How a command that threw ended.</summary>
        static ExitCode Classify(Exception ex)
        {
            switch (ex)
            {
                case PipelineTimeoutException _:
                    return ExitCode.TimedOut;
                case OperationCanceledException _:
                    return ExitCode.Cancelled;
                // A command that finds its own arguments wrong, which the executor's conversion
                // couldn't tell.
                case ArgumentException _:
                    return ExitCode.InvalidUsage;
                default:
                    return ExitCode.Failed;
            }
        }

        /// <summary>
        /// Invokes a resolved command with its converted arguments, with the threading it needs.
        /// Throws <see cref="PipelineTimeoutException"/> once the timeout, counted from
        /// <paramref name="arrivalTimestamp"/>, runs out; the command itself cannot be stopped and
        /// keeps running.
        /// </summary>
        /// <param name="timeout">The request's own timeout, if it sent one.</param>
        /// <param name="arrivalTimestamp">When the request arrived (a Stopwatch timestamp); time spent
        /// waiting since then counts against the timeout. Defaults to now.</param>
        /// <param name="execution">The execution the command runs as.</param>
        /// <param name="output">Where the command writes its result.</param>
        /// <param name="std">The command's standard streams.</param>
        async Task<object> Invoke(CommandInfo command, IReadOnlyList<object> arguments, TimeSpan? timeout,
            long? arrivalTimestamp, CommandExecution execution, CommandResult output, StdStreams std)
        {
            var context = new CommandContext(Progress, m_Dispatcher, execution, DispatchOnMainThread, output, std);

            // A copy, since the command's timeout parameter is written into it.
            var parameters = arguments?.ToArray() ?? Array.Empty<object>();

            var effectiveTimeout = PipelineTimeout.Resolve(command, timeout, parameters);
            var start = arrivalTimestamp ?? System.Diagnostics.Stopwatch.GetTimestamp();
            var remaining = PipelineTimeout.Remaining(effectiveTimeout, start);
            if (remaining == TimeSpan.Zero)
                throw new PipelineTimeoutException(command.FullName, effectiveTimeout, started: false);
            PipelineTimeout.Apply(command, parameters, remaining);

            // Execute command with appropriate threading
            try
            {
                object raw;
                if (command.MainThreadRequired)
                {
                    // A caller on the main thread gets its execution back before the command runs:
                    // the command runs on a later tick, still in the order it was queued.
                    if (m_Dispatcher.IsMainThread())
                        await Task.Yield();

                    // Execute on the main thread using this queue's dispatcher.
                    if (m_Dispatcher.IsMainThread())
                    {
                        raw = InvokeDirect(context, command, parameters);
                    }
                    else if (command.Immediate)
                    {
                        // Clients poll these, many at a time while a command holds the main
                        // thread, so none holds a thread while it waits.
                        raw = await m_Dispatcher.InvokeWithoutBlockingAsync(() => InvokeDirect(context, command, parameters),
                            PipelineTimeout.Remaining(effectiveTimeout, start));
                    }
                    else
                    {
                        raw = await Task.Run(() => m_Dispatcher.Invoke(() => InvokeDirect(context, command, parameters),
                            PipelineTimeout.Remaining(effectiveTimeout, start)));
                    }
                }
                else
                {
                    // Execute on background thread
                    raw = await WithinTimeout(Task.Run(() => InvokeDirect(context, command, parameters)),
                        command, effectiveTimeout, start);
                }

                if (raw is Task task)
                    await WithinTimeout(task, command, effectiveTimeout, start);
                return await UnwrapResult(raw);
            }
            catch (TimeoutException ex) when (!(ex is PipelineTimeoutException))
            {
                // The dispatcher's own wait, or a wait inside the command, ran out.
                throw new PipelineTimeoutException(command.FullName, effectiveTimeout, started: true);
            }
        }

        /// <summary>
        /// Awaits <paramref name="task"/> for what is left of the timeout, then gives up on it with a
        /// <see cref="PipelineTimeoutException"/>. An abandoned task's later fault is observed so it
        /// does not surface as an unobserved task exception.
        /// </summary>
        static async Task<T> WithinTimeout<T>(Task<T> task, CommandInfo command, TimeSpan timeout, long start)
        {
            await WithinTimeout((Task)task, command, timeout, start);
            return await task;
        }

        static async Task WithinTimeout(Task task, CommandInfo command, TimeSpan timeout, long start)
        {
            var remaining = PipelineTimeout.Remaining(timeout, start);
            if (PipelineTimeout.IsInfinite(remaining) || task.IsCompleted)
                return;

            if (await Task.WhenAny(task, Task.Delay(remaining)).ConfigureAwait(false) == task)
                return;

            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new PipelineTimeoutException(command.FullName, timeout, started: true);
        }

        /// <summary>
        /// Commands declared as `async Task`/`Task&lt;T&gt;` return their Task from reflection Invoke.
        /// Await it here (on the calling background thread, leaving the main thread free to pump the
        /// dispatcher) so the actual value is serialized rather than the Task itself, and so a faulted
        /// command surfaces its real exception to the request handler instead of being masked when
        /// Newtonsoft reads Task.Result during serialization.
        /// </summary>
        static async Task<object> UnwrapResult(object result)
        {
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                // Task&lt;T&gt; exposes a Result property; non-generic Task does not.
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task);
            }

            return result;
        }

        /// <summary>
        /// Direct command execution using reflection. Makes <paramref name="context"/> current for
        /// the duration so CliProgress/PipelineCancellation calls inside the command body resolve
        /// to its execution, regardless of which thread this ends up running on.
        /// </summary>
        static object InvokeDirect(CommandContext context, CommandInfo command, object[] parameters)
        {
            using var scope = CommandContext.Enter(context);
            try
            {
                // Target is null for every attribute-declared command (they are static) and carries
                // the receiver for a dynamically registered instance-method handler.
                return command.Method.Invoke(command.Target, parameters);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Reflection wraps the command's own exception in a TargetInvocationException; surface
                // the inner message so callers (and agents) get an actionable error instead of the
                // generic "Exception has been thrown by the target of an invocation."
                var inner = tie.InnerException;
                Debug.LogError($"Command '{command.FullName}' failed: {inner.Message}");

                // Preserve validation-oriented exceptions so the exec handler's dedicated
                // catch (ArgumentException) classifies them as "Parameter Validation Failed" rather than
                // collapsing them into "Command Execution Failed", and timeouts so they answer as
                // TIMEOUT. Rethrow the original with its stack.
                if (inner is ArgumentException || inner is TimeoutException)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();

                throw new InvalidOperationException($"Command '{command.FullName}' failed: {inner.Message}", inner);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Command execution failed: {ex.Message}");
                throw new InvalidOperationException($"Command '{command.FullName}' failed: {ex.Message}", ex);
            }
        }
    }
}
