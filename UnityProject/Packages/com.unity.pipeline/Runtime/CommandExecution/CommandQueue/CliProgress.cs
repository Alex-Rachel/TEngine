using System;

namespace Unity.Pipeline
{
    /// <summary>
    /// Per-queue progress-reporting state. Owned one-per-<see cref="Commands.DefaultCommandQueue"/>
    /// so a test server's execution can never cross-attribute progress with the live server's. See
    /// <see cref="CliProgress"/> for the public static façade that arbitrary command code calls.
    /// </summary>
    internal sealed class CliProgressState
    {
        /// <summary>Immutable copy of the current progress state (thread-safe to hand across threads).</summary>
        internal readonly struct Snapshot
        {
            public readonly bool HasReport;
            public readonly string Title;
            public readonly string Info;
            public readonly long? Current;
            public readonly long? Total;
            public readonly double? Progress01;

            public Snapshot(string title, string info, long? current, long? total, double? progress01)
            {
                HasReport = true;
                Title = title;
                Info = info;
                Current = current;
                Total = total;
                Progress01 = progress01;
            }
        }

        readonly object m_Gate = new object();
        Snapshot m_Explicit;
        string m_ExplicitOwnerId;
        string m_CurrentOwnerId;
        Snapshot m_Ambient;
        int m_ActiveCount;

        /// <summary>Whether any execution is currently in flight. Read by /api/progress.</summary>
        internal bool IsActive
        {
            get
            {
                lock (m_Gate)
                {
                    return m_ActiveCount > 0;
                }
            }
        }

        /// <summary>Marks one more execution in flight (see <see cref="EndExecutionCount"/>).</summary>
        internal void BeginExecutionCount()
        {
            lock (m_Gate)
            {
                m_ActiveCount++;
            }
        }

        /// <summary>
        /// Marks one execution finished, resetting the explicit-report state when this was the
        /// last one in flight. The decrement and the reset-on-zero check happen under the same
        /// lock as <see cref="BeginExecution"/>, so a new execution starting in the gap can never
        /// have its fresh BeginExecution/Report wiped by this reset — the previous implementation
        /// decremented a separate counter and reset separately, leaving a window where a finishing
        /// execution could decrement to zero, get preempted, and then wipe a new execution's
        /// owner id and early report once it resumed.
        /// </summary>
        internal void EndExecutionCount()
        {
            lock (m_Gate)
            {
                m_ActiveCount--;
                if (m_ActiveCount == 0)
                {
                    m_Explicit = default;
                    m_ExplicitOwnerId = null;
                    m_CurrentOwnerId = null;
                }
            }
        }

        /// <summary>
        /// Report the current task's progress so a connected CLI can render it live.
        /// Safe to call from any thread, at any frequency (the server samples on poll).
        /// </summary>
        /// <param name="title">Short task title, e.g. "Generating World".</param>
        /// <param name="info">Detail line, e.g. "Processing 42/100".</param>
        /// <param name="current">Current step, when the task has countable steps.</param>
        /// <param name="total">Total steps, when the task has countable steps.</param>
        /// <param name="progress">Completion in the 0–1 range; omit for indeterminate tasks
        /// (when omitted and both <paramref name="current"/> and <paramref name="total"/> are
        /// present, the CLI derives a percentage from them).</param>
        internal void Report(string title, string info = null, long? current = null, long? total = null, double? progress = null)
        {
            var clamped = progress.HasValue ? Math.Max(0d, Math.Min(1d, progress.Value)) : (double?)null;
            lock (m_Gate)
            {
                m_Explicit = new Snapshot(title, info, current, total, clamped);
                m_ExplicitOwnerId = m_CurrentOwnerId;
            }
        }

        /// <summary>Clear an explicit <see cref="Report"/> (e.g. when the reported task finishes).</summary>
        internal void Clear()
        {
            lock (m_Gate)
            {
                m_Explicit = default;
                m_ExplicitOwnerId = null;
            }
        }

        /// <summary>Ambient-source mirror (UnityEditor.Progress items). Explicit reports win.</summary>
        internal void ReportAmbient(string title, string info, long? current, long? total, double? progress01)
        {
            lock (m_Gate)
            {
                m_Ambient = new Snapshot(title, info, current, total, progress01);
            }
        }

        /// <summary>Clear the ambient mirror (no running UnityEditor.Progress items remain).</summary>
        internal void ClearAmbient()
        {
            lock (m_Gate)
            {
                m_Ambient = default;
            }
        }

        /// <summary>
        /// Marks the start of the execution identified by <paramref name="ownerId"/> (a job id,
        /// or a synthetic id for a plain <c>/api/exec</c>). Clears any leftover explicit report
        /// from whatever ran before it and becomes the only owner <see cref="Current"/> will
        /// accept explicit reports from until <see cref="EndExecution"/> is called — so a queued
        /// execution can never surface the previous one's progress before reporting its own.
        /// </summary>
        internal void BeginExecution(string ownerId)
        {
            lock (m_Gate)
            {
                m_CurrentOwnerId = ownerId;
                m_Explicit = default;
                m_ExplicitOwnerId = null;
            }
        }

        /// <summary>
        /// Marks the end of the execution identified by <paramref name="ownerId"/>. A no-op if
        /// some other execution is already current (e.g. called after a swallowed exception),
        /// so callers can call this unconditionally in a <c>finally</c>.
        /// </summary>
        internal void EndExecution(string ownerId)
        {
            lock (m_Gate)
            {
                if (m_CurrentOwnerId == ownerId)
                {
                    m_CurrentOwnerId = null;
                }
            }
        }

        /// <summary>
        /// The snapshot the /api/progress endpoint serves: the explicit report, but only when it
        /// was made by the currently executing owner (see <see cref="BeginExecution"/>); falls
        /// back to the ambient mirror otherwise.
        /// </summary>
        internal Snapshot Current
        {
            get
            {
                lock (m_Gate)
                {
                    var explicitValid = m_Explicit.HasReport && m_ExplicitOwnerId == m_CurrentOwnerId;
                    return explicitValid ? m_Explicit : m_Ambient;
                }
            }
        }
    }

    /// <summary>
    /// Structured progress reporting for long-running pipeline commands — the server side of the
    /// CLI's terminal progress bars (CLI-488, coordinated with CLI-335).
    ///
    /// While a command executes over <c>/api/exec</c>, the CLI polls <c>GET /api/progress</c> and
    /// renders whatever is reported here (a progress bar in a terminal, NDJSON progress frames in
    /// machine formats). Command authors call <see cref="Report"/> from any thread — including a
    /// main thread that is blocked inside a long synchronous command, which is exactly the
    /// <c>EditorUtility.DisplayProgressBar</c> scenario this exists for. The state is read by the
    /// HTTP listener thread, never the main thread, so progress stays visible even while the
    /// Editor is busy.
    ///
    /// This is a thin static façade over the executing queue's own <see cref="CliProgressState"/>
    /// (resolved via <see cref="Commands.CommandContext.Current"/>) so command code — which has
    /// no reference to "which execution is running me" — can keep calling this the same way
    /// regardless of which server dispatched it.
    ///
    /// Ambient <c>UnityEditor.Progress</c> items are mirrored in by the Editor assembly
    /// (<c>EditorProgressMirror</c>), targeting the live server directly; an explicit
    /// <see cref="Report"/> always wins over the ambient mirror. Every execution (a plain
    /// <c>/api/exec</c> or a detached job) is bracketed with BeginExecution/EndExecution on the
    /// queue's own <see cref="CliProgressState"/>, which tags who the current explicit report is
    /// allowed to belong to — so a job queued behind one that just finished never inherits its
    /// stale progress before making its own first report, no matter how long that job takes to
    /// get around to reporting anything.
    /// </summary>
    public static class CliProgress
    {
        /// <summary>
        /// Report the current task's progress so a connected CLI can render it live.
        /// Safe to call from any thread, at any frequency (the server samples on poll). A no-op
        /// when called outside a command's own execution (no queue to attribute it to).
        /// </summary>
        /// <param name="title">Short title for the operation.</param>
        /// <param name="info">Optional detail text (e.g. current step).</param>
        /// <param name="current">Current progress amount, if determinate.</param>
        /// <param name="total">Total amount, if determinate.</param>
        /// <param name="progress">Progress in [0, 1], used instead of current/total when set.</param>
        public static void Report(string title, string info = null, long? current = null, long? total = null, double? progress = null)
        {
            Commands.CommandContext.Current?.Progress.Report(title, info, current, total, progress);
        }

        /// <summary>Clear an explicit <see cref="Report"/> (e.g. when the reported task finishes).</summary>
        public static void Clear()
        {
            Commands.CommandContext.Current?.Progress.Clear();
        }
    }
}
