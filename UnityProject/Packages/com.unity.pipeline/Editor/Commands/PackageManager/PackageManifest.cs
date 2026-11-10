using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.PackageManager
{
    /// <summary>
    /// Reads the project's UPM manifest (<c>Packages/manifest.json</c>) <c>dependencies</c> as a flat
    /// name → version map, satisfying CLI-203's "return manifest state". The JSON parsing is split into
    /// a pure <see cref="ReadDependencies(string)"/> seam so it is unit-testable without a live project;
    /// <see cref="TryRead"/> resolves the project path and is main-thread only (it reads
    /// <see cref="Application.dataPath"/>).
    /// </summary>
    static class PackageManifest
    {
        /// <summary>Project-relative path to the UPM manifest.</summary>
        public const string RelativePath = "Packages/manifest.json";

        /// <summary>Absolute path to the manifest for the current project. Main thread only.</summary>
        public static string ManifestPath
        {
            get
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.Combine(projectRoot, "Packages", "manifest.json");
            }
        }

        /// <summary>
        /// Parse the <c>dependencies</c> object of a manifest.json document into a name → version map.
        /// Returns an empty map for null/empty input or a manifest without dependencies.
        /// </summary>
        public static Dictionary<string, string> ReadDependencies(string manifestJson)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(manifestJson))
                return result;

            var root = JObject.Parse(manifestJson);
            if (root["dependencies"] is JObject deps)
            {
                foreach (var kv in deps)
                    result[kv.Key] = kv.Value?.ToString();
            }

            return result;
        }

        /// <summary>
        /// Read the current project's manifest dependencies. Returns false with
        /// <paramref name="error"/> set when the manifest is missing or unreadable.
        /// </summary>
        public static bool TryRead(out Dictionary<string, string> dependencies, out string error)
        {
            dependencies = null;
            error = null;
            try
            {
                var path = ManifestPath;
                if (!File.Exists(path))
                {
                    error = $"manifest not found at {path}";
                    return false;
                }

                dependencies = ReadDependencies(File.ReadAllText(path));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
