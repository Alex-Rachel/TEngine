using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Authoring
{
    /// <summary>
    /// Project-path handling for authoring commands.
    ///
    /// - Paths resolve relative to the configurable <see cref="AuthoringRoot"/> (default "Assets"),
    ///   so callers can pass bare paths ("Gameplay/Enemies") without repeating the "Assets/" prefix.
    /// - Explicit "Assets/..." (or "Packages/...") paths are treated as project-relative as-is. Like the
    ///   AssetDatabase, root folders are matched case-insensitively and the path keeps the casing it was
    ///   given, so "assets/Foo" names the same folder as "Assets/Foo" on a case-insensitive file system.
    /// - Every path resolved by <see cref="Resolve"/> is confined to <see cref="AuthoringRoot"/> (the
    ///   sandbox for writes). With the default root ("Assets") that means full Assets access; set a
    ///   sub-folder to confine an agent. <see cref="ResolveReference"/>, for paths that only name an
    ///   existing asset, applies the same normalization without the confinement.
    /// - "../" traversal and writes outside the project root are always rejected.
    ///
    /// NOTE: minimal seed for the shared safety policy (CAT-2509).
    /// </summary>
    public static class ProjectPaths
    {
        private const string DefaultRoot = "Assets";

        // Per-project EditorPrefs key so the root doesn't leak across projects on the same machine.
        private static string PrefKey => $"Unity.Pipeline.AuthoringRoot:{Application.dataPath}";

        /// <summary>Absolute path to the project root (the folder that contains Assets/).</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>
        /// Base folder (project-relative, under Assets/) that bare paths resolve against and that all
        /// authoring writes are confined to. Defaults to "Assets". Persisted per project via EditorPrefs.
        /// Setting an invalid value (outside Assets/ or containing "..") throws.
        /// </summary>
        public static string AuthoringRoot
        {
            get
            {
                var root = EditorPrefs.GetString(PrefKey, DefaultRoot);
                return string.IsNullOrEmpty(root) ? DefaultRoot : root;
            }
            set
            {
                var normalized = NormalizeRoot(value, out var error);
                if (normalized == null)
                    throw new ArgumentException(error);
                EditorPrefs.SetString(PrefKey, normalized);
            }
        }

        /// <summary>Reset the authoring root to the default ("Assets").</summary>
        public static void ResetAuthoringRoot() => EditorPrefs.DeleteKey(PrefKey);

        /// <summary>
        /// Resolve an agent-supplied path to a normalized, project-relative path confined to
        /// <see cref="AuthoringRoot"/>. Bare paths are taken relative to the root; explicit
        /// "Assets/..." paths and absolute paths under the project are honored. Returns null with an
        /// <paramref name="error"/> when the path escapes the root or the project.
        /// </summary>
        /// <param name="path">The agent-supplied path to resolve.</param>
        /// <param name="error">A human-readable reason on failure, or null on success.</param>
        /// <returns>The normalized, project-relative path, or null on failure.</returns>
        public static string Resolve(string path, out string error)
        {
            var root = AuthoringRoot;
            var resolved = Normalize(path, root, out error);
            if (resolved == null)
                return null;

            // Confine to the authoring root (default "Assets" => full Assets access).
            if (!IsUnder(resolved, root))
            {
                error = $"Path '{path}' resolves to '{resolved}', which is outside the authoring root '{root}'.";
                return null;
            }

            return resolved;
        }

        /// <summary>
        /// Resolve a path that <em>references</em> an existing asset rather than one a command writes to —
        /// a scene in a build list, say. Normalized exactly like <see cref="Resolve"/> (bare paths relative to
        /// the root, "Assets/" and "Packages/" as-is, absolute paths under the project honored, ".." and
        /// paths outside the project rejected), but not confined to <see cref="AuthoringRoot"/>: the root
        /// sandboxes writes, and a reference writes nothing to the asset it names.
        /// </summary>
        internal static string ResolveReference(string path, out string error) => Normalize(path, AuthoringRoot, out error);

        /// <summary>
        /// The absolute file for a caller-supplied path that is not an asset: a capture, a build, a report,
        /// a script to run. A relative path resolves from <paramref name="projectRoot"/> (default
        /// <see cref="ProjectRoot"/>, the folder that contains Assets/); an absolute one is used as-is.
        /// Unlike <see cref="Resolve"/> it is not confined to the project: the caller decides where the
        /// file lives.
        /// </summary>
        internal static string ResolveFile(string path, string projectRoot = null)
        {
            var trimmed = path.Trim();
            return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(projectRoot ?? ProjectRoot, trimmed));
        }

        /// <summary>
        /// Whether a project-relative path (as returned by <see cref="Resolve"/> or the AssetDatabase) lies
        /// under <see cref="AuthoringRoot"/>. For commands that reach an asset by lookup rather than by a
        /// caller-supplied path, so their writes stay inside the sandbox too.
        /// </summary>
        internal static bool IsUnderAuthoringRoot(string projectRelativePath) =>
            !string.IsNullOrEmpty(projectRelativePath) && IsUnder(projectRelativePath.Replace('\\', '/').TrimEnd('/'), AuthoringRoot);

        // Case-insensitive, as the AssetDatabase's own root check is (BeginsWithCaseInsensitive in GetAssetFolderInfo).
        static bool IsUnder(string resolved, string root) =>
            resolved.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            resolved.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>The normalization shared by <see cref="Resolve"/> and <see cref="ResolveReference"/>, without confinement.</summary>
        static string Normalize(string path, string root, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Path is required.";
                return null;
            }

            var normalized = path.Replace('\\', '/').Trim();

            // Absolute path: must live under the project root; convert to project-relative.
            if (Path.IsPathRooted(normalized))
            {
                var full = Path.GetFullPath(normalized).Replace('\\', '/');
                var projectRoot = ProjectRoot.Replace('\\', '/').TrimEnd('/');
                if (!full.Equals(projectRoot, StringComparison.OrdinalIgnoreCase) &&
                    !full.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Path '{path}' is outside the project root '{projectRoot}'.";
                    return null;
                }

                normalized = full.Length > projectRoot.Length ? full.Substring(projectRoot.Length + 1) : string.Empty;
            }

            normalized = normalized.TrimStart('/');

            if (normalized.Split('/').Contains(".."))
            {
                error = $"Path '{path}' must not contain '..'.";
                return null;
            }

            // Explicit project-relative paths ("Assets/..." / "Packages/...") are used as-is; anything
            // else is a bare path taken relative to the authoring root.
            var isProjectRelative = IsUnder(normalized, "Assets") || IsUnder(normalized, "Packages");

            return (isProjectRelative ? normalized : CombineUnder(root, normalized)).TrimEnd('/');
        }

        private static string CombineUnder(string root, string relative)
        {
            relative = relative.TrimStart('/');
            return string.IsNullOrEmpty(relative) ? root : $"{root}/{relative}";
        }

        private static string NormalizeRoot(string value, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Authoring root is required.";
                return null;
            }

            var normalized = value.Replace('\\', '/').Trim().TrimStart('/').TrimEnd('/');
            if (normalized.Split('/').Contains(".."))
            {
                error = $"Authoring root '{value}' must not contain '..'.";
                return null;
            }

            if (!IsUnder(normalized, "Assets"))
            {
                error = $"Authoring root '{value}' must be under the project's Assets/ folder.";
                return null;
            }

            return normalized;
        }
    }
}
