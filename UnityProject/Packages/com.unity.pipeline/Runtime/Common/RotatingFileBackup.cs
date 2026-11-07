using System.IO;

namespace Unity.Pipeline
{
    /// <summary>
    /// The one piece of "rotate a log file" that <c>PipelineTransactionLog</c> (a whole-file JSON
    /// array, rotated once per Unity session) and <c>EvalUsageTelemetry</c> (true JSONL, rotated on
    /// a byte-size cap) actually share verbatim: swapping the active file for a backup. Everything
    /// else — the append shape, the lock, and what triggers a rotation — differs enough between the
    /// two that forcing them into one bigger "rotating append log" abstraction would cost more
    /// clarity than the duplication it removes, so only this file-swap step is factored out.
    /// </summary>
    static class RotatingFileBackup
    {
        /// <summary>
        /// Move <paramref name="activePath"/> to <paramref name="backupPath"/>, replacing any prior
        /// backup, so the active path is free for a fresh file. No-op when there is no active file.
        /// Callers are responsible for their own locking around this — it performs no synchronization
        /// itself.
        /// </summary>
        public static void RotateToBackup(string activePath, string backupPath)
        {
            if (!File.Exists(activePath))
                return;

            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(activePath, backupPath);
        }
    }
}
