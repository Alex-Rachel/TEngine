#if UNITY_6000_7_OR_NEWER
using System;
using System.Linq;
using UnityEditor;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Mirrors UnityEditor.EditorDialogEvents into the live pipeline server's DialogStateTracker,
    /// so GET /api/dialog, /api/status and the dialog busy-gate all observe modal
    /// dialogs with no per-command instrumentation (see the trunk-side design doc). Ambient
    /// dialogs are a live-editor-wide concept, not per-test-server — targets the live server
    /// explicitly, the same way EditorProgressMirror does, so a test server's DialogStateTracker
    /// never receives them.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorDialogStateMirror
    {
        static EditorDialogStateMirror()
        {
            Subscribe();
        }

        /// <summary>Subscribes to <see cref="EditorDialogEvents"/>, replacing any earlier subscription.</summary>
        internal static void Subscribe()
        {
            EditorDialogEvents.dialogWillShow -= OnDialogWillShow;
            EditorDialogEvents.dialogWillShow += OnDialogWillShow;
            EditorDialogEvents.dialogDismissed -= OnDialogDismissed;
            EditorDialogEvents.dialogDismissed += OnDialogDismissed;
        }

        static void OnDialogWillShow(DialogEventInfo info)
        {
            var server = PipelineServerStartup.Server;
            if (server == null)
                return;

            // Still on the main thread here — dialogWillShow fires before the dialog actually
            // blocks it — so this is the last chance to read live Editor state for the dialog
            // busy-gate's editor_status fallback (see SnapshotStatusForDialogGate's doc).
            server.SnapshotStatusForDialogGate();

            server.Dialogs.OnShown(info.Id, info.Source.ToString(), info.Title, info.Message,
                info.ButtonLabels?.ToArray(), info.Level?.ToString(), info.OpenedAtUtc);
        }

        static void OnDialogDismissed(DialogEventInfo info)
        {
            var server = PipelineServerStartup.Server;
            if (server == null)
                return;

            server.Dialogs.OnDismissed(info.Id, DateTime.UtcNow);
        }
    }
}
#endif
