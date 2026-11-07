using System.Globalization;
using UnityEditor;
using Unity.Pipeline.Console;

namespace Unity.Pipeline.Editor.Console
{
    /// <summary>
    /// Editor-only bootstrap for <see cref="ConsoleLogCapture"/>. The capture itself (the shared
    /// buffer and the log-callback subscription) lives in the runtime assembly so it also works in
    /// player builds; this type adds the things that only make sense in the Editor:
    ///
    ///  - <c>[InitializeOnLoad]</c> starts capture on every editor load and after every domain reload.
    ///  - Persistence to a Temp file across domain reloads, so entries and the cursor survive a
    ///    <c>recompile</c>. Players have no domain reloads, so the runtime path skips persistence.
    ///  - Re-subscribing on every play-mode transition: Unity clears
    ///    <see cref="UnityEngine.Application.logMessageReceivedThreaded"/>'s subscribers on play-mode
    ///    exit even though exiting play mode does not reload the domain, so capture would otherwise go
    ///    dead until the next recompile.
    ///  - A session identifier and a sequence floor in <see cref="SessionState"/>, so a client can
    ///    tell a cursor this editor session issued from one an earlier session did.
    ///
    /// The static constructor restores the persisted buffer first, then starts capture, so logs are
    /// never appended ahead of a restore.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorConsoleCaptureBootstrap
    {
        // Lives under Temp/ like the recompile status file: cleared by Unity on project-level cleanup,
        // survives domain reloads, and never committed.
        internal const string PersistencePath = "Temp/pipeline_console_log.json";

        // SessionState survives domain reloads and is cleared when the Editor restarts, which is
        // exactly the lifetime of the Temp file the buffer persists to. Both therefore start over
        // together, and the session identifier is what tells a client that happened.
        internal const string SessionKey = "pipeline.console.session";
        internal const string SeqFloorKey = "pipeline.console.seq";

        static EditorConsoleCaptureBootstrap()
        {
            var session = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(session))
            {
                session = System.Guid.NewGuid().ToString("N");
                SessionState.SetString(SessionKey, session);
            }

            ConsoleLogCapture.Buffer.SetSession(session);

            // Restore before capture starts so restored entries precede any newly captured ones, and
            // load before raising the floor: the floor is the same value the snapshot recorded, so
            // raising it first would make every snapshot entry look already-issued and drop it.
            ConsoleLogCapture.Buffer.Load(PersistencePath);
            if (long.TryParse(SessionState.GetString(SeqFloorKey, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var floor))
                ConsoleLogCapture.Buffer.RaiseLastSeq(floor);

            ConsoleLogCapture.EnsureCapturing();

            AssemblyReloadEvents.beforeAssemblyReload -= Persist;
            AssemblyReloadEvents.beforeAssemblyReload += Persist;

            EditorApplication.quitting -= Persist;
            EditorApplication.quitting += Persist;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void Persist()
        {
            ConsoleLogCapture.Buffer.Save(PersistencePath);
            SessionState.SetString(SeqFloorKey, ConsoleLogCapture.Buffer.LastSeq.ToString(CultureInfo.InvariantCulture));
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state) => ConsoleLogCapture.EnsureCapturing();
    }
}
