using System;
using System.Threading;
using Unity.Pipeline.Models;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Console
{
    /// <summary>
    /// Captures Unity console output into a process-wide <see cref="ConsoleLogBuffer"/> so the
    /// <c>console</c> command can serve it to CLI clients. This type lives in the runtime
    /// assembly, so it ships in player builds as well as the Editor.
    ///
    /// Lifecycle:
    ///  - In a Player, the <c>[RuntimeInitializeOnLoadMethod]</c> hook starts capture as the app boots.
    ///  - In the Editor, the editor-only <c>EditorConsoleCaptureBootstrap</c> starts capture on every
    ///    domain reload and layers on persistence so entries survive reloads (e.g. after a recompile),
    ///    and re-arms on every play-mode transition (Unity clears
    ///    <see cref="Application.logMessageReceivedThreaded"/>'s subscribers on play-mode exit even
    ///    though exiting play mode does not reload the domain — undocumented behavior, UUM-148802;
    ///    re-verify before retiring this). Both paths funnel through
    ///    <see cref="EnsureCapturing"/>, which is safe to call repeatedly: it always removes any
    ///    existing subscription before adding, so repeat calls (e.g. the runtime hook firing again
    ///    when Play mode is entered in the Editor) can never double-subscribe.
    ///  - <see cref="Application.logMessageReceivedThreaded"/> is used (not the non-threaded variant)
    ///    so logs emitted from background threads are captured too. The buffer is thread-safe.
    ///
    /// Capture starts when this type first initializes. Console entries produced before that — or
    /// before the package's first import — are not retroactively captured; this reads from the public
    /// log callback, not Unity's internal console store.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    public static class ConsoleLogCapture
    {
        static readonly ConsoleLogBuffer s_Buffer = new ConsoleLogBuffer();
        static readonly object s_SubscriptionLock = new object();
        static ConsoleGroundTruth s_GroundTruth;

        /// <summary>The shared buffer holding captured console entries.</summary>
        internal static ConsoleLogBuffer Buffer => s_Buffer;

        /// <summary>
        /// The last Editor ground-truth sample, or null outside the Editor and before the first
        /// sample. Written on the main thread by the Editor sampler and read from request threads,
        /// so the console command can report it while staying off the main thread — which is the
        /// point, since clients poll it while the Editor is busy compiling or importing. The
        /// snapshot is immutable once published, so a reference swap is all the synchronization a
        /// reader needs.
        /// </summary>
        internal static ConsoleGroundTruth GroundTruth
        {
            get => Volatile.Read(ref s_GroundTruth);
            set => Volatile.Write(ref s_GroundTruth, value);
        }

        /// <summary>
        /// Player entry point. Runs as the application boots so console output is captured from the
        /// start. In the Editor this also fires when entering Play mode, but <see cref="EnsureCapturing"/>
        /// is idempotent so it does not double-subscribe.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeBootstrap()
        {
            EnsureCapturing();
        }

        /// <summary>
        /// Subscribe to Unity's log callback, removing any existing subscription first. Safe to call
        /// repeatedly and from either the runtime bootstrap or the Editor bootstrap.
        /// </summary>
        public static void EnsureCapturing()
        {
            lock (s_SubscriptionLock)
            {
                Application.logMessageReceivedThreaded -= OnLogMessage;
                Application.logMessageReceivedThreaded += OnLogMessage;
            }
        }

        static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            s_Buffer.Add(type, condition, stackTrace, DateTime.UtcNow);
        }
    }
}
