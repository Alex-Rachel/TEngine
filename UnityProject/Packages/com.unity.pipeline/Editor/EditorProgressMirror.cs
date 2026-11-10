using UnityEditor;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Mirrors running <see cref="UnityEditor.Progress"/> items into <see cref="CliProgress"/> so
    /// background Editor tasks (async imports, bakes, anything instrumented with
    /// <c>Progress.Report</c>) surface on <c>GET /api/progress</c> with no explicit
    /// <see cref="CliProgress"/> calls (CLI-488).
    ///
    /// The Progress events fire on the main thread, so this mirror only advances while the main
    /// thread is pumping. A long SYNCHRONOUS main-thread task never yields — code inside such a
    /// task should call <see cref="CliProgress.Report"/> (or the
    /// <see cref="CliEditorProgress"/> wrappers) directly; that path records from within the
    /// blocked call and is what the HTTP listener thread serves.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorProgressMirror
    {
        static EditorProgressMirror()
        {
            Progress.added += OnProgressChanged;
            Progress.updated += OnProgressChanged;
            Progress.removed += OnProgressChanged;
        }

        static void OnProgressChanged(Progress.Item[] items)
        {
            Refresh();
        }

        static void Refresh()
        {
            // Ambient UnityEditor.Progress is a live-editor-wide concept, not per-test-server —
            // target the live server explicitly so a test server's CliProgress never receives it
            // (and vice versa). No-op while the live server isn't running.
            var server = PipelineServerStartup.Server;
            if (server == null)
                return;

            // Mirror the most recently ADDED running item (enumeration is registration order,
            // so the last running one wins) — for nested/sequential operations that is the one
            // a user watching the Editor would consider "current".
            Progress.Item best = null;
            foreach (var item in Progress.EnumerateItems())
            {
                if (item.running)
                {
                    best = item;
                }
            }

            if (best == null)
            {
                server.Progress.ClearAmbient();
                return;
            }

            server.Progress.ReportAmbient(
                best.name,
                best.description,
                best.currentStep > 0 ? best.currentStep : (long?)null,
                best.totalSteps > 0 ? best.totalSteps : (long?)null,
                !best.indefinite && best.progress >= 0f ? (double?)best.progress : null);
        }
    }
}
