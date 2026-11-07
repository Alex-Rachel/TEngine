using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Commands;
using Unity.Pipeline.Models;
using Unity.Pipeline.Security;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class EditorPipelineServer : BasePipelineServer
    {
        /// <summary>What the player-loop sampler last saw of the Play session (CLI-1432).</summary>
        internal readonly struct PlayerLoopState
        {
            public readonly bool Playing;
            public readonly int FrameCount;
            public readonly bool Ticking;
            /// <summary>Application.runInBackground as last sampled; meaningful in any mode.</summary>
            public readonly bool RunInBackground;

            public PlayerLoopState(bool playing, int frameCount, bool ticking, bool runInBackground)
            {
                Playing = playing;
                FrameCount = frameCount;
                Ticking = ticking;
                RunInBackground = runInBackground;
            }
        }

        // Read at construction, on the main thread, because locating the file needs the Package Manager.
        private readonly IReadOnlyDictionary<string, string> m_ObsoleteCommandNames = ObsoleteCommandNamesFile.Load();

        /// <inheritdoc/>
        protected override IReadOnlyDictionary<string, string> ObsoleteCommandNames => m_ObsoleteCommandNames;

        /// <summary>The obsolete command names, for the Pipeline protocol transport beside this server.</summary>
        internal IReadOnlyDictionary<string, string> ObsoleteCommandNamesForTransport => m_ObsoleteCommandNames;

        /// <summary>
        /// Whether this instance incremented <see cref="s_PlayerLoopSamplerRefs"/> in
        /// <see cref="ServerStarted"/>, so <see cref="ServerStopped"/> decrements at most once per
        /// instance even if Stop() is somehow called without a matching successful Start().
        /// </summary>
        private bool m_CountsTowardPlayerLoopSampler;

        /// <summary>When true, every command transaction is written to the project transaction log.</summary>
        public bool LogRequestsResponses { get; set; }

        /// <summary>
        /// Whether /api/status's ordinary (non-settling, non-dialog-blocked) case should report
        /// "ready" rather than "error": whether the endpoint is registered. Overridable for test servers
        /// that don't advertise.
        /// </summary>
        protected virtual bool IsAdvertised => IsEndpointRegistered;

        /// <summary>This server's executor, with the Editor's rules (see <see cref="CreateExecutor"/>).</summary>
        internal EditorCommandExecutor EditorExecutor => (EditorCommandExecutor)Executor;

        /// <summary>The Editor's executor, over this server's modal-dialog state and dispatcher.</summary>
        internal override DefaultCommandExecutor CreateExecutor(ICommandRegistry registry, ICommandQueue queue)
        {
            return new EditorCommandExecutor(registry, queue, Dialogs, Dispatcher);
        }

        /// <summary>
        /// Arm the post-startup settle latch, and join the Editor-global player-loop sampler
        /// (see <see cref="s_PlayerLoopSamplerRefs"/>).
        /// </summary>
        protected override void ServerStarted()
        {
            EditorExecutor.ArmSettleLatch();

            m_CountsTowardPlayerLoopSampler = true;
            if (s_PlayerLoopSamplerRefs++ == 0)
                EditorApplication.update += SamplePlayerLoop;
        }

        /// <summary>
        /// Unsubscribe the settle-tick handler, and leave the Editor-global player-loop sampler
        /// (see <see cref="s_PlayerLoopSamplerRefs"/>) — unsubscribing and recording a stale-safe
        /// "not playing" reading once the last server instance has stopped.
        /// </summary>
        protected override void ServerStopped()
        {
            EditorExecutor.DisarmSettleLatch();

            if (!m_CountsTowardPlayerLoopSampler)
                return;

            m_CountsTowardPlayerLoopSampler = false;
            if (s_PlayerLoopSamplerRefs > 0)
                s_PlayerLoopSamplerRefs--;

            if (s_PlayerLoopSamplerRefs == 0)
            {
                EditorApplication.update -= SamplePlayerLoop;
                RecordPlayerLoop(false, false, 0, PlayerLoopNowMs());
            }
        }

        // Player-loop sampling, for editor status and /api/status (CLI-1432). "Playing" alone is not
        // evidence: an unfocused Editor without runInBackground stays in Play mode with
        // Time.frameCount stuck.
        //
        // Sampled from EditorApplication.update because Time.frameCount is main-thread only and
        // /api/status is answered on the listener thread; every read goes through a lock for the same
        // reason. A stalled main thread stops the sampling, so the last advance ages out and the
        // reading turns false on its own. Elapsed time comes from a monotonic clock, so a system clock
        // set backwards cannot keep a stopped loop reading as ticking.
        //
        // The state is static because Play mode is Editor-global and several server instances (the
        // live server plus test servers) can exist at once. s_PlayerLoopSamplerRefs counts the running
        // instances, so the update subscription starts with the first and ends with the last
        // (ServerStarted / ServerStopped). The count and the subscription share one lifetime, like
        // the watchdog's armed flag and its subscription: whatever a reload keeps or wipes, it keeps
        // or wipes both.

        /// <summary>How recently the frame count must have moved for the loop to count as ticking.</summary>
        internal const long PlayerLoopTickingWindowMs = 1000;

        private static readonly object s_PlayerLoopLock = new object();
        private static bool s_PlayerLoopPlaying;
        private static bool s_PlayerLoopPaused;
        private static int s_PlayerLoopFrameCount;
        // Monotonic time of the last observed frame advance. Unset until the session has advanced
        // once, so a loop frozen from its first frame never reads as ticking.
        private static bool s_PlayerLoopHasAdvanced;
        private static long s_PlayerLoopLastAdvanceMs;
        // Application.runInBackground is main-thread only too; sampled with the rest so the listener
        // thread (/api/status) and the dialog fallback can report it.
        private static bool s_PlayerLoopRunInBackground;

        /// <summary>How many running server instances currently need the player loop sampled.</summary>
        private static int s_PlayerLoopSamplerRefs;

        /// <summary>Record the live Editor state. Call on the main thread.</summary>
        internal static void SamplePlayerLoop()
        {
            RecordPlayerLoop(EditorApplication.isPlaying, EditorApplication.isPaused, Time.frameCount, PlayerLoopNowMs(),
                Application.runInBackground);
        }

        /// <summary>Record one observation. Split from <see cref="SamplePlayerLoop"/> so tests can drive it.</summary>
        internal static void RecordPlayerLoop(bool playing, bool paused, int frameCount, long nowMs,
            bool runInBackground = false)
        {
            lock (s_PlayerLoopLock)
            {
                s_PlayerLoopRunInBackground = runInBackground;

                if (!playing)
                {
                    s_PlayerLoopPlaying = false;
                    s_PlayerLoopPaused = false;
                    s_PlayerLoopHasAdvanced = false;
                    return;
                }

                if (!s_PlayerLoopPlaying)
                {
                    // First sample of a session: a baseline, not an advance.
                    s_PlayerLoopPlaying = true;
                    s_PlayerLoopFrameCount = frameCount;
                    s_PlayerLoopHasAdvanced = false;
                }
                else if (frameCount != s_PlayerLoopFrameCount)
                {
                    s_PlayerLoopFrameCount = frameCount;
                    s_PlayerLoopHasAdvanced = true;
                    s_PlayerLoopLastAdvanceMs = nowMs;
                }

                s_PlayerLoopPaused = paused;
            }
        }

        /// <summary>Milliseconds on a monotonic clock; only differences are meaningful.</summary>
        // Scaled in double: timestamp * 1000 overflows a long after ~106 days on a nanosecond clock.
        // Fully qualified rather than `using System.Diagnostics;`: this file also uses
        // UnityEngine.Debug, and the two namespaces both declare a `Debug` type.
        internal static long PlayerLoopNowMs() =>
            (long)(System.Diagnostics.Stopwatch.GetTimestamp() * (1000.0 / System.Diagnostics.Stopwatch.Frequency));

        /// <summary>The current reading. Safe from any thread.</summary>
        internal static PlayerLoopState ReadPlayerLoop() => ReadPlayerLoop(PlayerLoopNowMs());

        internal static PlayerLoopState ReadPlayerLoop(long nowMs)
        {
            lock (s_PlayerLoopLock)
            {
                if (!s_PlayerLoopPlaying)
                    return new PlayerLoopState(false, 0, false, s_PlayerLoopRunInBackground);

                var ticking = !s_PlayerLoopPaused
                    && s_PlayerLoopHasAdvanced
                    && nowMs - s_PlayerLoopLastAdvanceMs <= PlayerLoopTickingWindowMs;
                return new PlayerLoopState(true, s_PlayerLoopFrameCount, ticking, s_PlayerLoopRunInBackground);
            }
        }

        /// <summary>
        /// Writes the transaction log, on the HTTP thread: its append is thread-safe file I/O by
        /// design (see <see cref="PipelineTransactionLog"/>). The executor reports the commands
        /// that ran to analytics.
        /// </summary>
        internal override void OnCommandDone(in CommandExecutionInfo info)
        {
            if (LogRequestsResponses && info.RequestJson != null)
                PipelineTransactionLog.Append(info.RequestJson, info.ResponseJson);
        }

        /// <summary>
        /// The Editor's state as of the instant a dialog was about to be shown — see
        /// <see cref="SnapshotStatusForDialogGate"/>. Once the dialog actually blocks the main
        /// thread, editor_status can no longer run for real (unlike the settle case, where the
        /// thread is merely busy, not stuck inside a native message loop), so its refusal is
        /// answered from this snapshot instead. Nothing it describes (compiling/domain reload/play
        /// mode) can change while the thread that would update it is stuck, so it stays correct for
        /// the whole blocked window. Null until the first dialog of the session is about to be shown.
        /// </summary>
        private StatusResponse m_LastStatusBeforeDialog;

        /// <summary>
        /// Reads the Editor's state on the main thread, waiting for it as long as a command's
        /// default timeout. Not queued behind the commands: it only reads. Holds no thread while it
        /// waits, since clients may poll many times while the main thread is blocked.
        /// </summary>
        internal override Task<StatusResponse> GetEditorStatus()
        {
            return Dispatcher.InvokeWithoutBlockingAsync(EditorStatusCommand.Status, PipelineTimeout.DefaultTimeout);
        }

        /// <summary>The asynchronous test run's status file, read on the HTTP thread.</summary>
        internal override object GetTestStatus()
        {
            return TestCommands.Status();
        }

        /// <summary>
        /// Called by EditorDialogStateMirror right before a dialog blocks the main thread (while
        /// it's still free to read live Editor state) — captures the Editor's state for
        /// <see cref="TryAnswerRefusal"/> to serve while the dialog is open.
        /// </summary>
        internal void SnapshotStatusForDialogGate()
        {
            m_LastStatusBeforeDialog = EditorStatusCommand.Status();
        }

        /// <summary>
        /// editor_status is the one main-thread command whose refusal by a dialog isn't answered
        /// with the busy envelope: it can't run for real (see <see cref="m_LastStatusBeforeDialog"/>'s
        /// doc), but its answer is a snapshot taken the instant before the block started, which
        /// stays accurate for the whole window. No snapshot exists only if a dialog opened before
        /// this server ever saw one coming (should not happen in practice —
        /// EditorDialogStateMirror snapshots on every dialogWillShow), in which case the refusal
        /// gets the normal 503.
        /// </summary>
        internal override bool TryAnswerRefusal(CommandUnavailableException refusal, out object answer)
        {
            answer = null;
            var snapshot = m_LastStatusBeforeDialog;
            if (refusal.Command != CommandNames.EditorStatus
                || refusal.Reason != CommandUnavailableReason.BlockedByDialog
                || refusal.Dialogs.Count == 0
                || snapshot == null)
                return false;

            // Not from the snapshot: the player loop is the one thing that does change here — it
            // stops — and the sampler's reading (runInBackground included, since
            // Application.runInBackground is off-limits on this thread) ages to "not ticking" on its own while blocked.
            var playerLoop = ReadPlayerLoop();

            answer = new StatusResponse
            {
                Status = "blocked_by_dialog",
                Compiling = snapshot.Compiling,
                DomainReloadInProgress = snapshot.DomainReloadInProgress,
                CodeLoadCount = snapshot.CodeLoadCount,
                AutoRefresh = snapshot.AutoRefresh,
                PlayMode = snapshot.PlayMode,
                FrameCount = playerLoop.Playing ? playerLoop.FrameCount : (int?)null,
                PlayerLoopTicking = playerLoop.Playing ? playerLoop.Ticking : (bool?)null,
                RunInBackground = playerLoop.RunInBackground,
                LastHeartbeat = DateTime.UtcNow,
                ProjectPath = snapshot.ProjectPath,
                UnityVersion = snapshot.UnityVersion,
                Dialog = BuildDialogPayload(refusal.Dialogs[0])
            };
            return true;
        }

        /// <summary>Build the Editor-specific status/heartbeat payload for /api/status, including the settle state.</summary>
        /// <returns>The status payload.</returns>
        protected override object GetServerStatus()
        {
            // "blocked_by_dialog" is checked first: a modal dialog blocks the main thread
            // regardless of settle state, and is the more actionable fact for a caller — settling
            // resolves on its own, a dialog needs a human. Read off DialogStateTracker's lock, same
            // as /api/dialog, so this stays fast and needs no main-thread access. "settling" is
            // checked next: on a cold import the settle window precedes normal operation, and
            // clients waiting for "ready" (instead of firing commands blindly) land after the
            // Editor is actually serviceable (AUTHAPI-35).
            var openDialogs = Dialogs.CurrentlyOpen;
            string status = openDialogs.Count > 0 ? "blocked_by_dialog"
                : !EditorExecutor.IsSettled ? "settling"
                : (IsAdvertised ? "ready" : "error");

            // Keep the common case's shape unchanged (no "dialog" key at all) rather than
            // always emitting "dialog": null — this is a widely-polled endpoint, not worth
            // changing its byte shape for every request just to cover the rare blocked case.
            // Gated on status itself, not openDialogs.Count, so a dialog that opens during the
            // settle window doesn't attach a "dialog" key to a "settling" response (StatusResponse's
            // dialog field is documented as present only when status is "blocked_by_dialog").
            // lastHeartbeat below is the response time; see StatusResponse.LastHeartbeat.
            // The player-loop keys follow the same rule: present only in Play mode (CLI-1432).
            var playerLoop = ReadPlayerLoop();
            return new ServerStatusResponse
            {
                Status = status,
                LastHeartbeat = DateTime.UtcNow,
                Capabilities = EditorCapabilities,
                FrameCount = playerLoop.Playing ? playerLoop.FrameCount : (int?)null,
                PlayerLoopTicking = playerLoop.Playing ? playerLoop.Ticking : (bool?)null,
                RunInBackground = playerLoop.Playing ? playerLoop.RunInBackground : (bool?)null,
                Dialog = status == "blocked_by_dialog" ? BasePipelineServer.BuildDialogPayload(openDialogs[0]) : null
            };
        }

        /// <summary>The bearer token clients must present to authenticate requests.</summary>
        /// <returns>The current live token.</returns>
        protected override string GetToken()
        {
            // Validate the live token so a rotation/revocation takes effect on the next request (not
            // the next heartbeat). The descriptor's EvalToken is discovery-only.
            return SecurityTokenManager.GetOrCreateToken();
        }
    }
}
