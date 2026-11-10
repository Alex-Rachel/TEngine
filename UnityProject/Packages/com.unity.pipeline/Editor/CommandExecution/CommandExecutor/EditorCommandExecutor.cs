using System;
using System.Collections.Generic;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Threading;
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// The Editor's <see cref="ICommandExecutor"/>: refuses a main-thread command while a modal
    /// dialog blocks the main thread or while the Editor settles after startup, and reports each
    /// command that ran to analytics once it ends.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class EditorCommandExecutor : DefaultCommandExecutor
    {
        readonly DialogStateTracker m_Dialogs;
        readonly Dispatcher m_Dispatcher;

        /// <param name="dialogs">The modal dialogs open in the Editor, as the server tracks them.</param>
        /// <param name="dispatcher">Runs the analytics report on the main thread.</param>
        public EditorCommandExecutor(ICommandRegistry registry, ICommandQueue queue, DialogStateTracker dialogs, Dispatcher dispatcher)
            : base(registry, queue)
        {
            m_Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            m_Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>
        /// One-way settle latch (AUTHAPI-35). On a cold project import the server starts (and
        /// writes its descriptor) while the Editor is still importing assets / compiling scripts,
        /// so early main-thread commands land in a not-quite-ready Editor and fail opaquely. The
        /// latch stays false until the Editor is first seen idle after startup, and is scoped to
        /// the EDITOR SESSION via <see cref="SettledSessionKey"/>: once any executor has seen the
        /// Editor idle, every later one (domain-reload restart, test server) starts settled — a
        /// server coming up mid-session while a compile/import happens to be in flight
        /// (script-creating test fixtures, recompile loops) must NOT re-arm the gate.
        /// Volatile: set on the main thread, read from HTTP request threads.
        /// </summary>
        private volatile bool m_Settled;
        private bool m_SettleTickSubscribed;

        /// <summary>
        /// SessionState marker: the Editor has been seen idle at least once this session, i.e.
        /// the post-startup settle window is over for the whole session. SessionState survives
        /// domain reloads and dies with the Editor process — exactly the settle window's scope.
        /// Read/written on the main thread only (arming and the update tick); request threads
        /// only read the volatile <see cref="m_Settled"/> snapshot.
        /// </summary>
        private const string SettledSessionKey = "Unity.Pipeline.EditorSessionSettled";

        /// <summary>
        /// Commands that must stay servable while the Editor is settling: editor_status is the
        /// status surface callers poll to observe the busy/settling state itself.
        /// Background (MainThreadRequired = false) commands are always exempt and don't need
        /// listing here.
        /// </summary>
        private static readonly HashSet<string> CommandsServiceableWhileSettling = new HashSet<string> { CommandNames.EditorStatus };

        /// <summary>
        /// Whether the Editor has been seen idle (not importing, not compiling) at least once
        /// this editor session. Until then the Editor is "settling": main-thread commands are
        /// rejected with a retryable busy signal instead of executing into a half-ready Editor.
        /// Virtual so tests can pin the unsettled state.
        /// </summary>
        internal virtual bool IsSettled => m_Settled;

        /// <summary>
        /// Whether this Editor session has already completed its settle window (the SessionState
        /// marker any settling executor sets on first idle). Main thread only. Exposed so tests
        /// can assert it as a precondition before probing the latch's immediate-settle path.
        /// </summary>
        internal static bool IsSessionSettled => SessionState.GetBool(SettledSessionKey, false);

        /// <summary>
        /// Arm or immediately release the settle latch. Called on the main thread when the server
        /// starts, so the Editor state and SessionState are readable directly. The latch is released
        /// at once when the session has already settled once (mid-session restarts after domain
        /// reloads, test servers started while a test's own compile/import is in flight) or when
        /// the Editor is idle right now — zero behavior change in both cases. Only a server started
        /// before the session's first idle moment (cold import: [InitializeOnLoad] runs inside the
        /// startup AssetDatabase refresh) arms the gate, and settles on the first update tick that
        /// sees the Editor idle.
        /// </summary>
        internal void ArmSettleLatch()
        {
            if (SessionState.GetBool(SettledSessionKey, false)
                || (!EditorApplication.isCompiling && !EditorApplication.isUpdating))
            {
                MarkSettled();
                return;
            }

            m_SettleTickSubscribed = true;
            EditorApplication.update += SettleTick;
        }

        /// <summary>Stop watching for the Editor to settle, when the server stops.</summary>
        internal void DisarmSettleLatch()
        {
            if (!m_SettleTickSubscribed)
                return;

            m_SettleTickSubscribed = false;
            EditorApplication.update -= SettleTick;
        }

        private void SettleTick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            MarkSettled();
            DisarmSettleLatch();
        }

        /// <summary>
        /// Release this executor's latch and mark the whole session settled (main thread only).
        /// </summary>
        private void MarkSettled()
        {
            m_Settled = true;
            SessionState.SetBool(SettledSessionKey, true);
        }

        /// <summary>
        /// Throws when <paramref name="command"/> can't run now. A modal dialog is checked first: it
        /// blocks the main thread whatever else holds, and needs a person, where settling ends on
        /// its own. The refusal carries the open dialogs, for a transport to describe them.
        /// </summary>
        protected override void Refuse(CommandInfo command)
        {
            if (command.MainThreadRequired)
            {
                var openDialogs = m_Dialogs.CurrentlyOpen;
                if (openDialogs.Count > 0)
                {
                    var summary = BasePipelineServer.FormatDialogSummary(openDialogs[0]);
                    var separator = summary.EndsWith(".") || summary.EndsWith("!") || summary.EndsWith("?") ? " " : ". ";
                    throw new CommandUnavailableException(command.FullName, CommandUnavailableReason.BlockedByDialog,
                        $"A modal dialog is currently open and blocking the main thread: {summary}{separator}" +
                        "Retry once it is dismissed.",
                        openDialogs);
                }
            }

            var busyReason = GetSettlingReason(command);
            if (busyReason != null)
                throw new CommandUnavailableException(command.FullName, CommandUnavailableReason.Settling, busyReason);
        }

        /// <summary>
        /// Busy gate: while settling, reject main-thread commands with a retryable busy signal —
        /// during the post-start import/compile window they would otherwise execute into a
        /// half-ready Editor and fail with an opaque null-data envelope (AUTHAPI-35). Background
        /// commands (status polling: recompile_status, package_status, console, ...) and
        /// editor_status stay servable so callers can observe progress. Reads only the volatile
        /// latch — safe on the request thread.
        /// </summary>
        /// <param name="command">The command about to be dispatched.</param>
        /// <returns>A busy reason while settling, or null once the Editor has been seen idle.</returns>
        private string GetSettlingReason(CommandInfo command)
        {
            if (IsSettled)
                return null;

            if (!command.MainThreadRequired || CommandsServiceableWhileSettling.Contains(command.FullName))
                return null;

            return "The Editor is still settling after startup (importing assets / compiling scripts), " +
                   "so main-thread commands are not serviceable yet. Retry once it is ready.";
        }

        /// <summary>
        /// Moves the analytics report to the main thread, on the next dispatcher pump.
        /// </summary>
        protected override void OnCommandEnded(in CommandExecutionInfo info)
        {
            // Copied out of the `in` parameter because a lambda cannot capture one.
            var posted = info;
            m_Dispatcher.Post(() => ReportOnMainThread(posted));
        }

        /// <summary>
        /// Post-command work that needs the main thread, run on the next dispatcher pump. Analytics
        /// lives here, and so does anything else added later that has to touch Unity APIs once a
        /// command is done. Virtual so a test executor can opt out wholesale.
        /// </summary>
        protected virtual void ReportOnMainThread(in CommandExecutionInfo info)
        {
            PipelineAnalytics.RecordCommandExecuted(info);
        }
    }
}
