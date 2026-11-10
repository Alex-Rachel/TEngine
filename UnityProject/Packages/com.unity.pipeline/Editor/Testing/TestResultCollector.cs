using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Unity.Pipeline.Editor.Testing
{
    /// <summary>
    /// Collects test results from Unity TestRunner API callbacks.
    /// Adapted from unity-tools implementation with sync/async mode support.
    /// </summary>
    class TestResultCollector : ICallbacks
    {
        /// <summary>True once <see cref="RunFinished"/> has processed a (non-duplicate, non-cancelled) run.</summary>
        public bool IsComplete { get; private set; }
        /// <summary>Set by <see cref="Cancel"/>; subsequent callbacks are ignored once true.</summary>
        public bool IsCancelled { get; set; }
        /// <summary>Per-test results collected so far.</summary>
        public List<TestResult> Results { get; } = new List<TestResult>();
        /// <summary>The completed run's root result tree, set by <see cref="RunFinished"/>.</summary>
        public ITestResultAdaptor RootResult { get; private set; }

        // For async mode
        /// <summary>Invoked when <see cref="RunFinished"/> processes a completed run (async mode).</summary>
        public Action OnRunFinished { get; set; }

        // For sync mode
        private TaskCompletionSource<ITestResultAdaptor> m_CompletionSource;

        private readonly bool m_AttachedMidRun;

        /// <summary>Create a collector ready to receive callbacks for a new run.</summary>
        /// <param name="attachedMidRun">True when the run started before this collector was registered.</param>
        public TestResultCollector(bool attachedMidRun = false)
        {
            m_AttachedMidRun = attachedMidRun;
            m_CompletionSource = new TaskCompletionSource<ITestResultAdaptor>();
        }

        /// <summary>
        /// Wait for test completion (sync mode)
        /// </summary>
        /// <returns>The completed run's root result tree.</returns>
        public Task<ITestResultAdaptor> WaitForCompletionAsync()
        {
            return m_CompletionSource.Task;
        }

        /// <summary>ICallbacks hook: a run has started.</summary>
        /// <param name="testsToRun">The tests about to run.</param>
        public void RunStarted(ITestAdaptor testsToRun)
        {
            if (IsCancelled) return;

            IsComplete = false;
            Results.Clear();
            Debug.Log($"[TestResultCollector] Run started: {testsToRun.TestCaseCount} test(s)");
        }

        /// <summary>ICallbacks hook: a single test has started. No-op.</summary>
        /// <param name="test">The test about to run.</param>
        public void TestStarted(ITestAdaptor test)
        {
            // No action needed for test start
        }

        /// <summary>ICallbacks hook: a single (non-suite) test has finished.</summary>
        /// <param name="result">The finished test's result.</param>
        public void TestFinished(ITestResultAdaptor result)
        {
            if (IsCancelled) return;
            if (result.Test.IsSuite) return;

            Results.Add(BuildTestResult(result));
        }

        /// <summary>ICallbacks hook: the run has finished.</summary>
        /// <param name="result">The run's root result tree.</param>
        public void RunFinished(ITestResultAdaptor result)
        {
            if (IsCancelled)
            {
                Debug.Log("[TestResultCollector] Ignoring RunFinished from cancelled run");
                return;
            }

            // A stale collector can receive a later run's RunFinished (see PipelineTestRunner);
            // re-completing OnRunFinished/m_CompletionSource would throw. IsComplete can't detect
            // this — RunStarted resets it on every non-cancelled collector, stale ones included.
            if (m_CompletionSource.Task.IsCompleted)
            {
                Debug.Log("[TestResultCollector] Ignoring duplicate RunFinished (already completed)");
                return;
            }

            RootResult = result;
            IsComplete = true;

            // A collector attached mid-run (re-registered after a domain reload) missed the
            // TestFinished callbacks delivered before it attached, so rebuild from the authoritative
            // result tree. The normal in-domain path leaves Results already populated.
            if (m_AttachedMidRun || Results.Count == 0)
            {
                Results.Clear();
                CollectLeafResults(result);
            }

            var total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            Debug.Log($"[TestResultCollector] Run finished: {total} total, {result.PassCount} passed, {result.FailCount} failed");

            // Notify async mode
            OnRunFinished?.Invoke();

            // Notify sync mode (guard against double-complete if the task was already
            // completed by SetError/Cancel, mirroring the guards in those methods)
            m_CompletionSource.TrySetResult(result);
        }

        /// <summary>
        /// Cancel the test collection and notify sync waiters
        /// </summary>
        public void Cancel()
        {
            IsCancelled = true;
            if (!m_CompletionSource.Task.IsCompleted)
            {
                m_CompletionSource.SetCanceled();
            }
        }

        /// <summary>
        /// Set error state and notify sync waiters
        /// </summary>
        /// <param name="exception">The error to complete the sync waiter's task with.</param>
        public void SetError(Exception exception)
        {
            if (!m_CompletionSource.Task.IsCompleted)
            {
                m_CompletionSource.SetException(exception);
            }
        }

        /// <summary>
        /// Recursively collect leaf (non-suite) test results from the result tree. Used at
        /// RunFinished to rebuild Results when this collector was registered too late to receive
        /// the incremental TestFinished callbacks (PlayMode resume after a domain reload).
        /// </summary>
        private void CollectLeafResults(ITestResultAdaptor result)
        {
            if (result == null) return;

            if (!result.Test.IsSuite && !result.Test.HasChildren)
            {
                Results.Add(BuildTestResult(result));
                return;
            }

            if (result.Children != null)
            {
                foreach (var child in result.Children)
                {
                    CollectLeafResults(child);
                }
            }
        }

        private static TestResult BuildTestResult(ITestResultAdaptor result)
        {
            return new TestResult
            {
                FullName = result.Test.FullName,
                Status = result.TestStatus.ToString(),
                Duration = result.Duration,
                Message = Truncate(result.Message, 10000),
                StackTrace = Truncate(result.StackTrace, 10000)
            };
        }

        private static string Truncate(string s, int maxLength)
        {
            if (s == null || s.Length <= maxLength) return s;
            return s.Substring(0, maxLength) + "\n... (truncated)";
        }
    }
}