using UnityEditor;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// <para>Drop-in replacements for <see cref="EditorUtility"/>'s progress-bar calls that ALSO make
    /// the progress visible to a connected CLI over <c>GET /api/progress</c> (CLI-488).</para>
    /// <para><c>EditorUtility.DisplayProgressBar</c> has no public getter and cannot be observed from
    /// the pipeline server, so existing code keeps its Editor progress dialog but stays invisible
    /// to the terminal. Swapping the call site to this class keeps the exact same Editor behavior
    /// and adds the CLI reporting — including while the main thread is blocked inside the task,
    /// which is when the dialog (and the terminal bar) matter most:</para>
    /// <code>
    /// CliEditorProgress.DisplayProgressBar("Generating World", $"Processing {i}/{n}", (float)i / n);
    /// // …
    /// CliEditorProgress.ClearProgressBar();
    /// </code>
    /// <para>Code that can take a package dependency but runs headless (batchmode) can call
    /// <see cref="CliProgress.Report"/> directly instead.</para>
    /// </summary>
    public static class CliEditorProgress
    {
        /// <summary>Same as <see cref="EditorUtility.DisplayProgressBar"/>, plus CLI reporting.</summary>
        /// <param name="title">Short title for the operation.</param>
        /// <param name="info">Detail text (e.g. current step).</param>
        /// <param name="progress">Progress in [0, 1].</param>
        public static void DisplayProgressBar(string title, string info, float progress)
        {
            CliProgress.Report(title, info, progress: progress);
            EditorUtility.DisplayProgressBar(title, info, progress);
        }

        /// <summary>Same as <see cref="EditorUtility.DisplayCancelableProgressBar"/>, plus CLI reporting.</summary>
        /// <param name="title">Short title for the operation.</param>
        /// <param name="info">Detail text (e.g. current step).</param>
        /// <param name="progress">Progress in [0, 1].</param>
        /// <returns>True if the user clicked Cancel.</returns>
        public static bool DisplayCancelableProgressBar(string title, string info, float progress)
        {
            CliProgress.Report(title, info, progress: progress);
            return EditorUtility.DisplayCancelableProgressBar(title, info, progress);
        }

        /// <summary>Same as <see cref="EditorUtility.ClearProgressBar"/>, plus clearing the CLI report.</summary>
        public static void ClearProgressBar()
        {
            CliProgress.Clear();
            EditorUtility.ClearProgressBar();
        }
    }
}
