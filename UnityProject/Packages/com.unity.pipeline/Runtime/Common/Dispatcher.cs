using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.Pipeline.Threading
{
    /// <summary>
    /// Dispatches work to Unity's main thread from background threads (required for accessing Unity
    /// APIs from HTTP request handlers).
    ///
    /// Each pipeline server owns its own instance (no global singleton): it is initialized on Start
    /// and pumped from the main thread — auto-pumped via EditorApplication.update in the editor, and
    /// by RuntimePipelineDriver.Update in a player.
    /// </summary>
    public class Dispatcher
    {
        private readonly ConcurrentQueue<WorkItem> m_WorkQueue = new ConcurrentQueue<WorkItem>();
        private volatile bool m_IsInitialized;
        private int m_MainThreadId = -1;

        /// <summary>True once <see cref="Initialize"/> has run and this dispatcher is pumping its queue.</summary>
        public bool IsInitialized => m_IsInitialized;

        /// <summary>
        /// Initialize the dispatcher. Must be called from Unity's main thread.
        /// </summary>
        public void Initialize()
        {
            if (m_IsInitialized)
                return;

            m_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            m_IsInitialized = true;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += ProcessWorkQueue;
#endif
        }

        /// <summary>
        /// Shutdown the dispatcher and cancel any pending work.
        /// </summary>
        public void Shutdown()
        {
            if (!m_IsInitialized)
                return;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= ProcessWorkQueue;
#endif

            while (m_WorkQueue.TryDequeue(out var item))
            {
                try
                {
                    item.SetException(new OperationCanceledException("Dispatcher is shutting down"));
                }
                catch { }
            }

            m_IsInitialized = false;
        }

        /// <summary>
        /// Execute a function on the main thread and return the result (synchronous wait).
        /// </summary>
        /// <typeparam name="T">The function's return type.</typeparam>
        /// <param name="function">Function to run on the main thread.</param>
        /// <param name="timeoutMs">Time to wait before throwing <see cref="TimeoutException"/>.</param>
        /// <returns>The function's result.</returns>
        public T Invoke<T>(Func<T> function, int timeoutMs = 60000)
        {
            return Invoke(function, TimeSpan.FromMilliseconds(timeoutMs));
        }

        /// <summary>
        /// Execute a function on the main thread and return the result (synchronous wait).
        /// </summary>
        /// <typeparam name="T">The function's return type.</typeparam>
        /// <param name="function">Function to run on the main thread.</param>
        /// <param name="timeout">Time to wait before throwing <see cref="TimeoutException"/>;
        /// <see cref="Timeout.InfiniteTimeSpan"/> waits forever.</param>
        /// <returns>The function's result.</returns>
        public T Invoke<T>(Func<T> function, TimeSpan timeout)
        {
            if (!m_IsInitialized)
                throw new InvalidOperationException("Dispatcher must be initialized first");

            if (IsMainThread())
                return function();

            var workItem = new WorkItem<T>(function);
            m_WorkQueue.Enqueue(workItem);

            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var task = workItem.TaskCompletionSource.Task;

            while (!task.IsCompleted)
            {
                if (timeout != Timeout.InfiniteTimeSpan && elapsed.Elapsed > timeout)
                {
                    // No-op if execution already started on the main thread — only prevents a
                    // still-queued item from running after we've reported the timeout.
                    workItem.TryCancel();
                    throw new TimeoutException($"Main thread operation timed out after {timeout.TotalMilliseconds:0}ms");
                }

                Thread.Sleep(1);
            }

            if (task.IsFaulted)
                throw task.Exception?.GetBaseException() ?? new Exception("Unknown error");

            if (task.IsCanceled)
                throw new OperationCanceledException("Main thread operation was cancelled");

            return task.Result;
        }

        /// <summary>
        /// Execute an action on the main thread.
        /// </summary>
        /// <param name="action">Action to run on the main thread.</param>
        /// <param name="timeoutMs">Time to wait before throwing <see cref="TimeoutException"/>.</param>
        public void Invoke(Action action, int timeoutMs = 60000)
        {
            Invoke(action, TimeSpan.FromMilliseconds(timeoutMs));
        }

        /// <summary>
        /// Execute an action on the main thread.
        /// </summary>
        /// <param name="action">Action to run on the main thread.</param>
        /// <param name="timeout">Time to wait before throwing <see cref="TimeoutException"/>;
        /// <see cref="Timeout.InfiniteTimeSpan"/> waits forever.</param>
        public void Invoke(Action action, TimeSpan timeout)
        {
            Invoke<object>(() =>
            {
                action();
                return null;
            }, timeout);
        }

        /// <summary>
        /// Execute a function on the main thread and return the result (async version).
        /// </summary>
        /// <typeparam name="T">The function's return type.</typeparam>
        /// <param name="function">Function to run on the main thread.</param>
        /// <param name="timeoutMs">Time to wait before throwing <see cref="TimeoutException"/>.</param>
        /// <returns>The function's result.</returns>
        public async Task<T> InvokeAsync<T>(Func<T> function, int timeoutMs = 60000)
        {
            return await Task.Run(() => Invoke(function, timeoutMs));
        }

        /// <summary>
        /// Execute an action on the main thread (async version).
        /// </summary>
        /// <param name="action">Action to run on the main thread.</param>
        /// <param name="timeoutMs">Time to wait before throwing <see cref="TimeoutException"/>.</param>
        /// <returns>A task that completes once the action has run on the main thread.</returns>
        public async Task InvokeAsync(Action action, int timeoutMs = 60000)
        {
            await Task.Run(() => Invoke(action, timeoutMs));
        }

        /// <summary>
        /// Execute a function on the main thread and complete with its result, holding no thread
        /// while it waits, unlike <see cref="InvokeAsync{T}(Func{T}, int)"/>: for callers that may
        /// wait in numbers while the main thread is blocked. Runs inline on the main thread. The
        /// caller resumes on a thread-pool thread, never on the main thread.
        /// </summary>
        /// <param name="function">Function to run on the main thread.</param>
        /// <param name="timeout">Time to wait before throwing <see cref="TimeoutException"/>; a
        /// function still queued then never runs.</param>
        internal async Task<T> InvokeWithoutBlockingAsync<T>(Func<T> function, TimeSpan timeout)
        {
            if (!m_IsInitialized)
                throw new InvalidOperationException("Dispatcher must be initialized first");

            if (IsMainThread())
                return function();

            var workItem = new WorkItem<T>(function);
            m_WorkQueue.Enqueue(workItem);

            var task = workItem.TaskCompletionSource.Task;
            if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task)
            {
                // No-op if execution already started on the main thread; then the result is
                // dropped, as Invoke drops it.
                workItem.TryCancel();
                throw new TimeoutException($"Main thread operation timed out after {timeout.TotalMilliseconds:0}ms");
            }

            return await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Queue an action to run on the main thread and return immediately.
        ///
        /// For work with no caller left to return to — reporting, logging, telemetry, anything
        /// whose result nobody reads. <see cref="Invoke(Action, int)"/> is wrong for that: it parks
        /// the calling thread until the next pump, and on a request thread that delay is paid by
        /// whoever is waiting on the response. InvokeAsync only moves the same wait onto a
        /// threadpool thread.
        ///
        /// The action always runs on a later pump, never inline, even when called from the main
        /// thread, so posted work runs in the order it was posted. It has nowhere to report a
        /// failure, so an exception is logged and swallowed rather than propagated. Work still
        /// queued when the dispatcher shuts down is dropped, and posting to a dispatcher that is
        /// not running is a no-op for the same reason — losing a report is always preferable to
        /// throwing at a caller that was only trying to record something.
        /// </summary>
        /// <param name="action">Action to run on the main thread on a later pump.</param>
        public void Post(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (!m_IsInitialized)
                return;

            m_WorkQueue.Enqueue(new PostedWorkItem(action));
        }

        /// <summary>
        /// Check if we're currently on Unity's main thread.
        /// </summary>
        /// <returns>True if the calling thread is the main thread captured by <see cref="Initialize"/>.</returns>
        public bool IsMainThread()
        {
            return m_MainThreadId != -1 && Thread.CurrentThread.ManagedThreadId == m_MainThreadId;
        }

        /// <summary>
        /// Process queued work items. Called from EditorApplication.update or MonoBehaviour.Update.
        /// </summary>
        /// <param name="maxItemsPerFrame">Max items to process per call, to limit frame-rate impact.</param>
        public void ProcessWorkQueue(int maxItemsPerFrame)
        {
            int processedCount = 0;

            while (processedCount < maxItemsPerFrame && m_WorkQueue.TryDequeue(out var workItem))
            {
                try
                {
                    workItem.Execute();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Dispatcher work item failed: {ex.Message}");
                    workItem.SetException(ex);
                }

                processedCount++;
            }
        }

        /// <summary>Process queued work items, up to a default limit of 10 per call.</summary>
        public void ProcessWorkQueue()
        {
            ProcessWorkQueue(10);
        }

        private abstract class WorkItem
        {
            private const int Pending = 0;
            private const int Started = 1;
            private const int Canceled = 2;
            private int m_State;

            /// <summary>Marks the item canceled if it hasn't started executing yet. Returns false
            /// (no-op) once <see cref="Execute"/> has already claimed it.</summary>
            public bool TryCancel() => Interlocked.CompareExchange(ref m_State, Canceled, Pending) == Pending;

            public void Execute()
            {
                if (Interlocked.CompareExchange(ref m_State, Started, Pending) != Pending)
                    return; // Canceled before it could start.

                ExecuteCore();
            }

            protected abstract void ExecuteCore();
            public abstract void SetException(Exception exception);
        }

        /// <summary>
        /// A <see cref="Post(Action)"/> work item. Unlike <see cref="WorkItem{T}"/> it carries no
        /// TaskCompletionSource: nobody is waiting on the result, so a fault has nowhere to go but
        /// the log — and faulting a TCS no one awaits would surface later as an unobserved task
        /// exception. A shutdown cancellation simply drops the work.
        /// </summary>
        private class PostedWorkItem : WorkItem
        {
            private readonly Action m_Action;

            public PostedWorkItem(Action action)
            {
                m_Action = action;
            }

            protected override void ExecuteCore()
            {
                try
                {
                    m_Action();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Dispatcher posted work failed: {ex.Message}");
                }
            }

            public override void SetException(Exception exception)
            {
                // Nobody is waiting on this item, so there is nothing to hand the exception to.
            }
        }

        private class WorkItem<T> : WorkItem
        {
            private readonly Func<T> m_Function;
            public TaskCompletionSource<T> TaskCompletionSource { get; }

            public WorkItem(Func<T> function)
            {
                m_Function = function ?? throw new ArgumentNullException(nameof(function));
                // Continuations run on the thread pool, so a caller awaiting the result never
                // resumes on the main thread inside ProcessWorkQueue.
                TaskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            protected override void ExecuteCore()
            {
                try
                {
                    var result = m_Function();
                    TaskCompletionSource.SetResult(result);
                }
                catch (Exception ex)
                {
                    TaskCompletionSource.SetException(ex);
                }
            }

            public override void SetException(Exception exception)
            {
                TaskCompletionSource.SetException(exception);
            }
        }
    }
}
