using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.Profiling
{
    /// <summary>Where a Profiler recording or memory snapshot is written.</summary>
    static class CaptureOutput
    {
        /// <summary>
        /// An explicit <paramref name="output"/> (absolute, or relative to the project root) as given;
        /// otherwise <c>&lt;folder&gt;/&lt;productName&gt;_&lt;timestamp&gt;_&lt;id&gt;&lt;extension&gt;</c>.
        /// The id is in the name because the timestamp only resolves to the second, so two short
        /// captures would otherwise write the same file. A null <paramref name="folder"/> falls back to
        /// <paramref name="fallbackFolder"/> under the project root.
        /// </summary>
        public static string ResolvePath(string output, string folder, string fallbackFolder, string id, string extension)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);

            if (!string.IsNullOrWhiteSpace(output))
                return Path.IsPathRooted(output) ? output : Path.GetFullPath(Path.Combine(projectRoot, output));

            var directory = string.IsNullOrEmpty(folder) ? Path.Combine(projectRoot, fallbackFolder) : folder;
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            return Path.Combine(directory, $"{Sanitize(Application.productName)}_{timestamp}_{id}{extension}");
        }

        /// <summary>Creates the file's directory, so the native writer does not fail on it.</summary>
        public static void EnsureDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        public static long? SizeOf(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : (long?)null;
        }

        static string Sanitize(string name)
        {
            var sb = new StringBuilder(string.IsNullOrEmpty(name) ? "Project" : name);
            foreach (var c in Path.GetInvalidFileNameChars())
                sb.Replace(c, '_');
            return sb.ToString();
        }
    }
}
