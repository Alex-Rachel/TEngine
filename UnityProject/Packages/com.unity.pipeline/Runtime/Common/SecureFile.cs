using System;
using System.IO;
using UnityEngine;

namespace Unity.Pipeline
{
    /// <summary>
    /// Writes files that carry secrets, readable only by the current user.
    /// </summary>
    /// <remarks>
    /// Files are rewritten in place, so permissions are restricted once, on creation, and kept by
    /// every later write. A write to a new file restricts it before any contents reach it. Readers
    /// can see a partially written file while a rewrite is under way.
    /// </remarks>
    static class SecureFile
    {
        /// <summary>
        /// Writes <paramref name="contents"/> to <paramref name="path"/>, creating the parent directory
        /// if needed. Thread-safe only if callers don't write the same path concurrently.
        /// </summary>
        /// <param name="path">The file to create or overwrite.</param>
        /// <param name="contents">The text to write.</param>
        /// <exception cref="IOException">The file couldn't be written.</exception>
        public static void WriteAllText(string path, string contents)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

                // Restricted before the contents are written to it.
                using (File.Create(path)) { }
                FilePermissions.RestrictToCurrentUser(path);
            }

            File.WriteAllText(path, contents);
        }

        /// <summary>Deletes <paramref name="path"/>, if it exists. Failures are logged, not thrown.</summary>
        /// <param name="path">The file to delete.</param>
        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: Failed to delete '{path}': {ex.Message}");
            }
        }
    }
}
