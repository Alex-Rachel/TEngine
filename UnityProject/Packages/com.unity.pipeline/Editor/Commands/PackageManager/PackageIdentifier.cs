using System;

namespace Unity.Pipeline.Editor.Commands.PackageManager
{
    /// <summary>Where an added package comes from, derived from its identifier string (CLI-203).</summary>
    enum PackageSourceKind
    {
        /// <summary>Could not be classified (e.g. empty input).</summary>
        Unknown,

        /// <summary>A registry package name, optionally pinned: <c>com.unity.foo</c> or <c>com.unity.foo@1.2.3</c>.</summary>
        Registry,

        /// <summary>A git URL: <c>https://…​.git</c>, <c>git+…</c>, <c>ssh://…</c>, <c>git@host:…</c>, optionally <c>#revision</c>.</summary>
        Git,

        /// <summary>A local package reference: <c>file:../RelativePath</c> or <c>file:/abs/path</c>.</summary>
        Local
    }

    /// <summary>A parsed UPM <c>package_add</c> identifier — the normalized string handed to
    /// <c>Client.Add</c> plus the pieces extracted for plan/audit text.</summary>
    sealed class ParsedPackageId
    {
        /// <summary>How the package will be resolved by UPM.</summary>
        public PackageSourceKind Kind { get; set; }

        /// <summary>The identifier passed verbatim to <c>UnityEditor.PackageManager.Client.Add</c>.</summary>
        public string Identifier { get; set; }

        /// <summary>Registry package name (Registry only); null otherwise.</summary>
        public string Name { get; set; }

        /// <summary>Registry version or git revision when given; null otherwise.</summary>
        public string Version { get; set; }

        /// <summary>Human-readable summary for plan/audit text.</summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// Pure, editor-independent classification of a <c>package_add</c> identifier into a registry name,
    /// git URL, or local path (CLI-203). Kept free of any Unity API so the add-path's input handling is
    /// unit-testable without a live editor; the command layer feeds the result to
    /// <c>UnityEditor.PackageManager.Client.Add</c>.
    /// </summary>
    static class PackageIdentifier
    {
        /// <summary>Classify an identifier without parsing out its parts.</summary>
        public static PackageSourceKind Classify(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return PackageSourceKind.Unknown;

            var id = identifier.Trim();

            if (StartsWith(id, "file:"))
                return PackageSourceKind.Local;

            // Explicit git forms and any URL scheme (UPM treats http(s) identifiers as git URLs).
            if (StartsWith(id, "git+") || StartsWith(id, "ssh://") || id.StartsWith("git@", StringComparison.Ordinal))
                return PackageSourceKind.Git;
            if (StartsWith(id, "http://") || StartsWith(id, "https://") || id.Contains("://"))
                return PackageSourceKind.Git;

            // A bare "owner/repo.git" (no scheme) is still a git reference. Strip an optional
            // "#revision" before testing the suffix.
            var core = id;
            var hash = core.IndexOf('#');
            if (hash >= 0)
                core = core.Substring(0, hash);
            if (core.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                return PackageSourceKind.Git;

            return PackageSourceKind.Registry;
        }

        /// <summary>
        /// Classify and split <paramref name="identifier"/>. Returns false with <paramref name="error"/>
        /// set for empty/unclassifiable input.
        /// </summary>
        public static bool TryParse(string identifier, out ParsedPackageId parsed, out string error)
        {
            parsed = null;
            error = null;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                error = "Package identifier is empty. Provide a name (com.unity.foo[@version]), a git URL, or a file: path.";
                return false;
            }

            var id = identifier.Trim();
            switch (Classify(id))
            {
                case PackageSourceKind.Local:
                    parsed = new ParsedPackageId
                    {
                        Kind = PackageSourceKind.Local,
                        Identifier = id,
                        Description = $"local package '{id}'"
                    };
                    return true;

                case PackageSourceKind.Git:
                {
                    string revision = null;
                    var hash = id.IndexOf('#');
                    if (hash >= 0 && hash < id.Length - 1)
                        revision = id.Substring(hash + 1);
                    parsed = new ParsedPackageId
                    {
                        Kind = PackageSourceKind.Git,
                        Identifier = id,
                        Version = revision,
                        Description = revision != null ? $"git package '{id}' @ {revision}" : $"git package '{id}'"
                    };
                    return true;
                }

                case PackageSourceKind.Registry:
                {
                    var name = id;
                    string version = null;

                    // Split on the version separator '@'. Use the last '@' so npm-scoped names
                    // ("@scope/pkg@1.0.0") still split on the version, not the leading scope marker.
                    var at = id.LastIndexOf('@');
                    if (at > 0)
                    {
                        name = id.Substring(0, at);
                        version = id.Substring(at + 1);
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        error = $"Package name is empty in '{identifier}'.";
                        return false;
                    }

                    parsed = new ParsedPackageId
                    {
                        Kind = PackageSourceKind.Registry,
                        Identifier = id,
                        Name = name,
                        Version = version,
                        Description = version != null ? $"'{name}' @ {version}" : $"'{name}' (latest compatible)"
                    };
                    return true;
                }

                default:
                    error = $"Could not classify package identifier '{identifier}'.";
                    return false;
            }
        }

        private static bool StartsWith(string value, string prefix) =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
