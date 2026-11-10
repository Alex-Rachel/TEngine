using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Pipeline;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// One active or completed wait tracked by <see cref="WaitRegistry"/> (AUTHAPI-25).
    ///
    /// The condition is evaluated by <see cref="PollOnce"/>, which MUST run on the main thread: the
    /// synchronous <c>wait_for</c> path marshals it there via the server dispatcher between sleeps
    /// (so the main thread is never held hostage), while an <c>async=true</c> wait is polled by
    /// <see cref="WaitRegistry.Tick"/> on <c>EditorApplication.update</c>. Any follow-up action
    /// (<c>on-met.capture</c>/<c>pause</c>) runs inside the same <see cref="PollOnce"/> call that
    /// first observes the condition true, so it acts in that same editor frame.
    /// </summary>
    internal sealed class WaitJob
    {
        internal const int MaxHistorySamples = 200;

        public string Id;
        public WaitConditionInput Condition;
        public WaitConditionPlan Plan;
        public WaitOnMetInput OnMet;
        public bool ReturnHistory;
        public bool IsAsync;
        public bool TolerateMissing;
        public double PollIntervalMs;
        public TimeSpan Timeout;
        public DateTime StartedAt;

        /// <summary>Guards mutable state below; read from the request/background thread, mutated on the main thread.</summary>
        public readonly object Gate = new object();

        // --- guarded by Gate ---
        /// <summary>Single source of truth for the lifecycle. Every public view (the five
        /// booleans, the wire string, IsTerminal) derives from it, so a state transition can never
        /// be half-applied and the terminal guard lives in exactly one place.</summary>
        private WaitState m_State = WaitState.Pending;
        /// <summary>Set while still-<c>Pending</c> but already committed to running the <c>on-met</c>
        /// follow-up (its pause/capture side effects are about to happen for real, off the lock).
        /// Blocks <see cref="TryEnterTerminal"/> the same way a non-Pending state does, so a
        /// concurrent cancel/interrupt/timeout cannot claim the job mid-follow-up: the follow-up's
        /// side effects have already fired and must not be reported as if they never happened.</summary>
        private bool m_Resolving;
        public object InitialValue;
        public bool HaveInitial;
        public object LastValue;
        public string Error;
        public string ResolutionNote;
        public int FramesObserved;

        /// <summary>Error from the on-met follow-up (capture/pause) when the condition held but the
        /// follow-up threw — reported as <c>onMetError</c>, distinct from <see cref="Error"/>.</summary>
        public string FollowupError;
        public WaitCaptureResult Capture;
        public readonly List<WaitHistorySample> History = new List<WaitHistorySample>();
        public DateTime? LastPollAt;
        /// <summary>UTC time of the terminal transition; freezes <see cref="ElapsedMs"/> so a
        /// completed wait's elapsed time stops growing while it sits retained for wait_status.</summary>
        private DateTime? m_TerminalAtUtc;
        // --- end guarded ---

        /// <summary>NOTE: terminal transitions must go through <see cref="TryEnterTerminal"/> —
        /// it also checks <see cref="m_Resolving"/> (committed-to-on-met, not yet Met); a path
        /// testing only <c>m_State != Pending</c> would reopen the cancel-vs-met race.</summary>
        internal enum WaitState { Pending, Met, TimedOut, Interrupted, Canceled, Failed }

        public bool Met => ReadState() == WaitState.Met;
        public bool TimedOut => ReadState() == WaitState.TimedOut;
        public bool Interrupted => ReadState() == WaitState.Interrupted;
        public bool Canceled => ReadState() == WaitState.Canceled;
        public bool Failed => ReadState() == WaitState.Failed;
        public bool IsTerminal => ReadState() != WaitState.Pending;

        /// <summary>Wire form: "pending" | "met" | "timedOut" | "interrupted" | "canceled" | "failed".</summary>
        public string State => StateName(ReadState());

        private WaitState ReadState()
        {
            lock (Gate) { return m_State; }
        }

        private static string StateName(WaitState state)
        {
            switch (state)
            {
                case WaitState.Met: return "met";
                case WaitState.TimedOut: return "timedOut";
                case WaitState.Interrupted: return "interrupted";
                case WaitState.Canceled: return "canceled";
                case WaitState.Failed: return "failed";
                default: return "pending";
            }
        }

        /// <summary>Milliseconds since the wait started, frozen at the terminal transition.</summary>
        public long ElapsedMs => (long)Elapsed.TotalMilliseconds;

        /// <summary>Time since the wait started, frozen at the terminal transition.</summary>
        public TimeSpan Elapsed
        {
            get
            {
                lock (Gate)
                {
                    var end = m_TerminalAtUtc ?? DateTime.UtcNow;
                    return end - StartedAt;
                }
            }
        }

        /// <summary>Record the terminal timestamp. Must be called with <see cref="Gate"/> held.</summary>
        private void MarkTerminalLocked()
        {
            if (m_TerminalAtUtc == null)
                m_TerminalAtUtc = DateTime.UtcNow;
        }

        /// <summary>UTC time this wait became terminal, or null while still pending. Set atomically
        /// with the state transition (see <see cref="MarkTerminalLocked"/>), so any job observed as
        /// <see cref="IsTerminal"/> already has this set — used to prune the OLDEST-COMPLETED jobs
        /// first, not the oldest-SUBMITTED ones (a long-running wait can have the earliest
        /// <see cref="StartedAt"/> in the registry yet be the most recently finished).</summary>
        public DateTime? TerminalAtUtc
        {
            get { lock (Gate) { return m_TerminalAtUtc; } }
        }

        /// <summary>
        /// Evaluate the condition once on the main thread. Reads the member, records the observation,
        /// and — on the first frame the condition holds — runs the follow-up action and transitions
        /// to <c>met</c>. Resolution failures transition to <c>failed</c> (never to <c>timedOut</c>)
        /// unless <c>tolerate-missing</c> is set, in which case they are recorded as the last error
        /// and retried as condition-not-met (enables "wait until X exists"). No-op once the job is
        /// already terminal.
        /// </summary>
        internal void PollOnce()
        {
            lock (Gate)
            {
                if (m_State != WaitState.Pending)
                    return;
                LastPollAt = DateTime.UtcNow;
            }

            var plan = Plan ?? (Plan = new WaitConditionPlan(Condition));

            object value;
            Type memberType;
            try
            {
                value = WaitConditionEvaluator.ReadMember(plan, out memberType);
            }
            catch (Exception ex)
            {
                // WaitEvaluationException is the evaluator's own structured failure; anything else
                // is a member getter throwing (reflection wraps those in TargetInvocationException)
                // — a property body that throws, a MissingReferenceException, etc. Both must
                // resolve the wait (or be tolerated), never escape: in the async Tick path an
                // escaped exception is swallowed by Unity's event dispatch and the job would just
                // time out with no cause; in the sync path it would propagate out of WaitFor raw.
                var inner = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
                var resolutionFailure = inner as WaitEvaluationException;
                var isResolutionFailure = resolutionFailure != null;
                var message = isResolutionFailure
                    ? inner.Message
                    : $"Condition read threw {inner.GetType().Name}: {inner.Message}";
                lock (Gate)
                {
                    if (m_State != WaitState.Pending)
                        return; // canceled/interrupted while the poll was reading
                    FramesObserved++;
                    if (TolerateMissing && isResolutionFailure && !resolutionFailure.IsPermanent)
                    {
                        // tolerate-missing is scoped to the evaluator's STRUCTURED resolution
                        // failures (type/member/instance not found) — its documented contract.
                        // A member getter that THROWS is a real failure, not "does not exist
                        // yet": retrying it would leave the wait pending until timeout and
                        // bury the actual exception. Nor is a PERMANENT resolution failure (a
                        // malformed path, a findType naming a non-Object type, a member that
                        // doesn't exist on the resolved type) — no amount of retrying makes those
                        // resolvable, so tolerating them would just bury a real misconfiguration
                        // until the timeout instead of failing fast.
                        Error = message;
                        return;
                    }
                    m_State = WaitState.Failed;
                    Error = message;
                    MarkTerminalLocked();
                }
                return;
            }

            object initialSnapshot;
            bool haveInitial;
            lock (Gate)
            {
                if (m_State != WaitState.Pending)
                    return; // canceled/interrupted while the poll was reading
                FramesObserved++;
                // A successful read supersedes any tolerated resolution error recorded above.
                Error = null;
                ResolutionNote = plan.ResolutionNote;
                if (!HaveInitial)
                {
                    InitialValue = value;
                    HaveInitial = true;
                }
                LastValue = value;
                initialSnapshot = InitialValue;
                haveInitial = HaveInitial;

                if (ReturnHistory)
                {
                    // Ring buffer keeping the LAST samples: the tail near the transition is what
                    // diagnoses a wait, not the first 200 identical pre-transition observations.
                    if (History.Count >= MaxHistorySamples)
                        History.RemoveAt(0);
                    History.Add(new WaitHistorySample { Value = WaitForCommands.Jsonify(value), ElapsedMs = ElapsedMs });
                }
            }

            bool met;
            string evalError;
            try
            {
                met = WaitConditionEvaluator.Evaluate(Condition.Op, value, Condition.Value, memberType,
                    initialSnapshot, haveInitial, out evalError);
            }
            catch (Exception ex)
            {
                met = false;
                evalError = ex.Message;
            }

            if (!string.IsNullOrEmpty(evalError))
            {
                lock (Gate)
                {
                    if (m_State != WaitState.Pending)
                        return;
                    if (TolerateMissing)
                    {
                        // Same contract as tolerated resolution failures: "cannot compare YET"
                        // (e.g. greaterThan while a nullable field is still null) retries until
                        // the budget expires, with the last error surfaced on timeout. The
                        // permanently-misconfigured variant (contains on a non-string member)
                        // spins the budget instead of failing fast — the same indistinguishable
                        // transient-vs-permanent tradeoff as null intermediates, accepted and
                        // documented. Getter exceptions still fail immediately (handled above).
                        Error = evalError;
                        return;
                    }
                    m_State = WaitState.Failed;
                    Error = evalError;
                    MarkTerminalLocked();
                }
                return;
            }

            if (!met)
                return;

            // wait_cancel/MarkInterrupted/MarkTimedOut run on a different thread and can land any
            // time from here on. Claim the job now (still Pending, so a status read in this window
            // sees "pending") BEFORE running the on-met follow-up's side effects: this claim, not a
            // state check either side of the follow-up, is what must be atomic with "am I about to
            // pause/capture for real". Checking-then-running-then-checking-again left a window
            // where a cancel could land IN BETWEEN — the pause/screenshot would still fire for
            // real, but the job would then commit to canceled, silently discarding all trace that a
            // real side effect happened (see review round 5). Once claimed, MarkCanceled /
            // MarkInterrupted / MarkTimedOut all no-op (see TryEnterTerminal) for the rest of this
            // poll, so the final commit below needs no re-check.
            lock (Gate)
            {
                if (m_State != WaitState.Pending)
                    return;
                m_Resolving = true;
            }

            // Same-frame follow-up: this runs on the main thread within the poll that first saw the
            // condition true, so a capture reflects that frame and a pause takes effect immediately.
            WaitCaptureResult capture = null;
            string followupError = null;
            if (OnMet != null)
            {
                try
                {
                    if (OnMet.Pause && Application.isPlaying)
                        EditorApplication.isPaused = true;
                    if (OnMet.Capture != null)
                        capture = WaitForCommands.RunCapture(OnMet.Capture);
                }
                catch (Exception ex)
                {
                    followupError = $"on-met action failed: {ex.Message}";
                }
            }

            lock (Gate)
            {
                m_State = WaitState.Met;
                m_Resolving = false;
                Capture = capture;
                // The CONDITION held — the wait is met; a failed follow-up (capture/pause) is
                // reported on its own field, never in Error: docs define error as the reason a
                // wait FAILED, and stashing it there made a met-with-broken-capture reply look
                // like a documented failure shape while state said otherwise.
                FollowupError = followupError;
                MarkTerminalLocked();
            }
        }

        /// <summary>
        /// Claim the terminal transition to <paramref name="state"/>, running <paramref name="onClaimed"/>
        /// (e.g. to stamp <see cref="Error"/>) under the same lock before freezing <see cref="ElapsedMs"/>.
        /// No-ops once the job is already terminal, OR mid-<c>on-met</c>-follow-up (see
        /// <see cref="m_Resolving"/>) — a wait that already committed to running real side effects
        /// must finish committing to <c>met</c>, never be re-claimed as canceled/interrupted/timedOut.
        /// </summary>
        private void TryEnterTerminal(WaitState state, Action onClaimed = null)
        {
            lock (Gate)
            {
                if (m_State != WaitState.Pending || m_Resolving)
                    return;
                m_State = state;
                onClaimed?.Invoke();
                MarkTerminalLocked();
            }
        }

        internal void MarkTimedOut() => TryEnterTerminal(WaitState.TimedOut);

        internal void MarkCanceled(string reason = null) => TryEnterTerminal(WaitState.Canceled, () =>
        {
            if (!string.IsNullOrEmpty(reason))
                Error = reason;
        });

        internal void MarkInterrupted(string reason = null) => TryEnterTerminal(WaitState.Interrupted, () =>
        {
            // The interruption cause always wins: a stale tolerated-resolution message left by
            // an earlier poll must not masquerade as the reason the wait ended.
            Error = !string.IsNullOrEmpty(reason)
                ? reason
                : "Wait interrupted by a domain reload or exiting play mode.";
        });

        internal WaitResult ToResult(bool includeWaitId)
        {
            lock (Gate)
            {
                return new WaitResult
                {
                    WaitId = includeWaitId ? Id : null,
                    State = State,
                    Met = Met,
                    TimedOut = TimedOut,
                    Interrupted = Interrupted ? (bool?)true : null,
                    Member = Condition?.Member,
                    Op = string.IsNullOrEmpty(Condition?.Op) ? "equals" : Condition.Op,
                    Value = HaveInitial ? WaitForCommands.Jsonify(LastValue) : null,
                    InitialValue = HaveInitial ? WaitForCommands.Jsonify(InitialValue) : null,
                    ElapsedMs = ElapsedMs,
                    FramesObserved = FramesObserved,
                    Error = Error,
                    OnMetError = FollowupError,
                    Note = ResolutionNote,
                    Capture = Capture,
                    History = (ReturnHistory && History.Count > 0)
                        ? new List<WaitHistorySample>(History)
                        : null
                };
            }
        }
    }

    /// <summary>
    /// Process-wide registry of <see cref="WaitJob"/>s (AUTHAPI-25). Async waits are polled here on
    /// <c>EditorApplication.update</c>; synchronous waits register too (so the concurrency cap and
    /// interruption hooks cover them) but are driven by their own command loop. A domain reload or
    /// exiting play mode interrupts every active wait so it resolves to a structured
    /// <c>interrupted</c> result rather than hanging or leaking. ENTERING play mode does not
    /// interrupt: with domain reload disabled a wait survives into play mode; with reload enabled,
    /// <c>beforeAssemblyReload</c> interrupts it anyway.
    ///
    /// PROCESS-WIDE by design — deliberately unlike a server's detached jobs, which are
    /// per-server so one server's command results never cross into another. A wait has no
    /// per-server state to isolate: it observes process-global editor state, is polled by the
    /// single <c>EditorApplication.update</c> pump, and is interrupted by process-level events
    /// (domain reload, play-mode exit). Ids are GUIDs, so waits from different servers cannot
    /// collide, and <see cref="MaxConcurrentWaits"/> intentionally bounds the shared update pump
    /// rather than acting as a per-server quota. If true per-server isolation ever becomes a
    /// requirement, scope this like the server's jobs at that point.
    ///
    /// In-memory: a domain reload discards pending waits the same way it discards every other
    /// static — a poller then gets a clear "not found / does not survive domain reloads" result.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class WaitRegistry
    {
        /// <summary>Maximum simultaneously-active (non-terminal) waits; a further submission is rejected.</summary>
        internal const int MaxConcurrentWaits = 8;

        /// <summary>Completed async jobs retained (for wait_status) beyond this count are pruned oldest-first.</summary>
        internal const int MaxRetainedTerminal = 32;

        private static readonly Dictionary<string, WaitJob> s_Jobs = new Dictionary<string, WaitJob>();
        private static readonly object s_Gate = new object();

        /// <summary>
        /// Register a job, unless <see cref="MaxConcurrentWaits"/> waits are already active (returns
        /// false with a message the caller surfaces as a structured error).
        /// </summary>
        internal static bool TryRegister(WaitJob job, out string error)
        {
            lock (s_Gate)
            {
                PruneTerminal();

                var active = s_Jobs.Values.Count(j => !j.IsTerminal);
                if (active >= MaxConcurrentWaits)
                {
                    error = $"Too many concurrent waits: {active} active (max {MaxConcurrentWaits}). Cancel one with {CommandNames.JobWaitCancel} or wait for one to complete.";
                    return false;
                }

                s_Jobs[job.Id] = job;
            }

            error = null;
            return true;
        }

        internal static bool TryGet(string id, out WaitJob job)
        {
            lock (s_Gate)
            {
                return s_Jobs.TryGetValue(id ?? string.Empty, out job);
            }
        }

        /// <summary>
        /// Cancel a tracked wait by id. Returns true whenever the id is known — including when the
        /// job had already reached a terminal state before this call: <see cref="WaitJob.MarkCanceled"/>
        /// no-ops in that case, so <paramref name="job"/>'s state stays whatever it actually resolved
        /// to (never forced to "canceled") — see the honest-cancel-result note in wait.md.
        /// </summary>
        internal static bool Cancel(string id, out WaitJob job)
        {
            if (!TryGet(id, out job))
                return false;
            job.MarkCanceled();
            return true;
        }

        internal static void Remove(string id)
        {
            lock (s_Gate)
            {
                s_Jobs.Remove(id ?? string.Empty);
            }
        }

        internal static int ActiveCount
        {
            get
            {
                lock (s_Gate)
                {
                    return s_Jobs.Values.Count(j => !j.IsTerminal);
                }
            }
        }

        /// <summary>
        /// Poll all active async waits whose poll interval has elapsed, and time out any that have
        /// exceeded their budget. Subscribed to <c>EditorApplication.update</c>; also callable
        /// directly from tests to drive async waits deterministically.
        /// </summary>
        internal static void Tick()
        {
            List<WaitJob> pending = null;
            lock (s_Gate)
            {
                foreach (var job in s_Jobs.Values)
                {
                    if (job.IsAsync && !job.IsTerminal)
                        (pending ?? (pending = new List<WaitJob>())).Add(job);
                }
            }

            if (pending != null)
            {
                foreach (var job in pending)
                {
                    if (job.Elapsed >= job.Timeout)
                    {
                        // Final look, unconditionally — full parity with the sync loop, which polls
                        // before every deadline check. A condition that flipped true between the
                        // last scheduled poll and the deadline must resolve met, not timedOut (and
                        // a never-polled job still reports a real value/framesObserved).
                        job.PollOnce();
                        if (!job.IsTerminal)
                            job.MarkTimedOut();
                        continue;
                    }

                    double sincePoll;
                    lock (job.Gate)
                    {
                        sincePoll = job.LastPollAt.HasValue
                            ? (DateTime.UtcNow - job.LastPollAt.Value).TotalMilliseconds
                            : double.MaxValue;
                    }

                    if (sincePoll >= job.PollIntervalMs)
                        job.PollOnce();
                }
            }

        }

        /// <summary>
        /// Subscriptions are installed unconditionally once per domain load, on the main thread —
        /// the same idiom as every other <c>EditorApplication.update</c> poller in this package
        /// (bake status, build, target switch). <see cref="Tick"/> and <see cref="InterruptAll"/>
        /// no-op when nothing is registered, so the permanent hooks cost one delegate call per
        /// update. This replaces a lazy install/uninstall state machine whose main-thread marshal
        /// could fail while the very jobs that needed the pump sat frozen.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void HookOnDomainLoad()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += InterruptAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Interrupt only when LEAVING play mode: the objects a wait observes are torn down
            // then. Entering play mode does not interrupt — with domain reload disabled the wait
            // survives into play mode (the common wait-for-gameplay-state case); with reload
            // enabled, beforeAssemblyReload interrupts it anyway.
            if (change == PlayModeStateChange.ExitingPlayMode)
                InterruptAll();
        }

        private static void InterruptAll()
        {
            List<WaitJob> active;
            lock (s_Gate)
            {
                active = s_Jobs.Values.Where(j => !j.IsTerminal).ToList();
            }
            foreach (var job in active)
                job.MarkInterrupted();
        }

        private static void PruneTerminal()
        {
            // Oldest-COMPLETED first, not oldest-submitted: a long-running wait can have the
            // earliest StartedAt of any job in the registry yet be the most recently finished, and
            // sorting by StartedAt would evict it the instant it resolves while far-shorter waits
            // that finished long ago (but were submitted later) survive.
            var terminal = s_Jobs.Values
                .Where(j => j.IsTerminal)
                .OrderBy(j => j.TerminalAtUtc)
                .ToList();

            var excess = terminal.Count - MaxRetainedTerminal;
            for (var i = 0; i < excess; i++)
                s_Jobs.Remove(terminal[i].Id);
        }

        /// <summary>Test hook: forget every tracked wait (main thread). The editor hooks installed
        /// by <see cref="HookOnDomainLoad"/> are permanent by design and deliberately NOT removed —
        /// do not re-subscribe them from tests.</summary>
        internal static void ResetForTests()
        {
            lock (s_Gate)
            {
                s_Jobs.Clear();
            }
        }

        /// <summary>Test hook: exercise the domain-reload/play-mode interruption transition without a live reload.</summary>
        internal static void InterruptAllForTests() => InterruptAll();

        /// <summary>Test hook: exercise the play-mode filter (interrupt on exit only) without entering play mode.</summary>
        internal static void SimulatePlayModeChangeForTests(PlayModeStateChange change) => OnPlayModeStateChanged(change);
    }
}
