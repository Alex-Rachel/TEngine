using System;
using System.IO;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;

namespace Unity.Pipeline.Editor.Commands.Assets
{
    /// <summary>
    /// Asset-folder authoring commands. Reference exemplar for the authoring foundation (CLI-190):
    /// it validates input through the project-path sandbox (<see cref="ProjectPaths"/>) and returns
    /// the canonical <see cref="AuthoringResult"/> envelope so an agent can reference the created
    /// folder in follow-up calls.
    /// </summary>
    static class FolderCommands
    {
        const string Group = "asset";

        [CliGroupCommand(Group, "Create a folder under the authoring root (creates intermediate folders).", Tags = new[] { "assets" })]
        public static AuthoringResult CreateFolder(
            [CliArg("path", "Folder path relative to the authoring root (default Assets/); the Assets/ prefix is optional. e.g. Gameplay/Enemies or Assets/Gameplay/Enemies", Required = true)] string path)
        {
            var normalized = ProjectPaths.Resolve(path, out var error);
            if (normalized == null)
                throw new ArgumentException(error);

            CreateFolderRecursive(normalized);
            AssetDatabase.Refresh();

            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(normalized);
            var result = ObjectResolver.Describe(folder) ?? new AuthoringResult { Type = "DefaultAsset" };
            result.AssetPath = normalized;
            return result;
        }

        private static void CreateFolderRecursive(string assetsPath)
        {
            if (AssetDatabase.IsValidFolder(assetsPath))
                return;

            var parent = Path.GetDirectoryName(assetsPath)?.Replace('\\', '/');
            var name = Path.GetFileName(assetsPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new ArgumentException($"Invalid folder path '{assetsPath}'.");

            if (!AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
