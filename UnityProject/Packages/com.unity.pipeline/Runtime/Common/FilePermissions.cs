using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.Pipeline
{
    /// <summary>
    /// Restricts a file so that only the user who started the process can read or write it.
    /// Used to protect the instance descriptor (port file), which carries the auth token.
    ///
    /// The managed ACL APIs (System.Security.AccessControl) are not part of Unity's runtime
    /// profile, so Windows is handled via icacls and Unix via libc chmod.
    /// </summary>
    static class FilePermissions
    {
        /// <summary>
        /// Restrict the file at <paramref name="path"/> to the current user only.
        /// Failures are logged but never thrown, so they cannot block server startup.
        /// </summary>
        /// <param name="path">The file to restrict.</param>
        public static void RestrictToCurrentUser(string path)
        {
            try
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.WindowsPlayer:
                        RestrictWindows(path);
                        break;
                    default:
                        RestrictUnix(path);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: Could not restrict permissions on '{path}': {ex.Message}");
            }
        }

        private static void RestrictWindows(string path)
        {
            // Grant by SID rather than account name: AzureAD/Entra-joined accounts (e.g.
            // "AzureAD\\user") have no name-to-SID mapping for icacls, but the SID always resolves.
            var sid = GetCurrentUserSid();
            if (string.IsNullOrEmpty(sid))
            {
                Debug.LogWarning($"Pipeline: Could not resolve current user SID; leaving default permissions on '{path}'.");
                return;
            }

            // /inheritance:r drops inherited ACEs; /grant:r replaces the DACL with a single
            // full-control grant to the current user.
            Run("icacls", $"\"{path}\" /inheritance:r /grant:r \"*{sid}:(F)\"", out _);
        }

        private static string GetCurrentUserSid()
        {
            if (!Run("whoami", "/user /fo csv /nh", out var output) || string.IsNullOrEmpty(output))
                return null;

            // Output looks like: "domain\user","S-1-5-21-..."
            var idx = output.IndexOf("S-1-", StringComparison.Ordinal);
            if (idx < 0)
                return null;

            var end = idx;
            while (end < output.Length && (char.IsLetterOrDigit(output[end]) || output[end] == '-'))
                end++;

            return output.Substring(idx, end - idx);
        }

        private static bool Run(string fileName, string arguments, out string stdout)
        {
            stdout = null;
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                    return false;

                // Read before WaitForExit to avoid a full-pipe deadlock.
                stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int chmod(string pathname, uint mode);

        private static void RestrictUnix(string path)
        {
            // 0600 (octal) == owner read/write only.
            const uint ownerReadWrite = 0x180;
            chmod(path, ownerReadWrite);
        }
    }
}
