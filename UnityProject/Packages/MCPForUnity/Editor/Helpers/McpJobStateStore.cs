using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Utility for persisting tool state across domain reloads. State is stored in
    /// Library so it stays local to the project and is cleared by Unity as needed.
    /// </summary>
    public static class McpJobStateStore
    {
        private static string GetStatePath(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                throw new ArgumentException("toolName cannot be null or empty", nameof(toolName));
            }

            var libraryPath = Path.Combine(Application.dataPath, "..", "Library");
            var fileName = $"McpState_{toolName}.json";
            return Path.GetFullPath(Path.Combine(libraryPath, fileName));
        }

        public static void SaveState<T>(string toolName, T state)
        {
            var path = GetStatePath(toolName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var json = JsonConvert.SerializeObject(state ?? Activator.CreateInstance<T>());
            File.WriteAllText(path, json);
        }

        public static T LoadState<T>(string toolName)
        {
            var path = GetStatePath(toolName);
            if (!File.Exists(path))
            {
                return default;
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception)
            {
                return default;
            }
        }

        public static void ClearState(string toolName)
        {
            var path = GetStatePath(toolName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
