using System;
using System.IO;

namespace TEngine.Editor
{
    using UnityEditor;
    using UnityEngine;

    public class SpritePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var config = AtlasConfiguration.Instance;

            if (!config.autoGenerate) return;

            try
            {
                ProcessAssetChanges(
                    importedAssets: importedAssets,
                    deletedAssets: deletedAssets,
                    movedAssets: movedAssets,
                    movedFromPaths: movedFromAssetPaths
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"Atlas processing error: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                AssetDatabase.Refresh();
            }
        }

        private static void ProcessAssetChanges(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromPaths)
        {
            ProcessAssets(importedAssets, (path) =>
            {
                EditorSpriteSaveInfo.OnImportSprite(path);
                LogProcessed("[Added]", path);
            });

            ProcessAssets(deletedAssets, (path) =>
            {
                EditorSpriteSaveInfo.OnDeleteSprite(path);
                LogProcessed("[Deleted]", path);
            });

            ProcessMovedAssets(movedFromPaths, movedAssets);
        }

        private static void ProcessAssets(string[] assets, Action<string> processor)
        {
            if (assets == null) return;

            foreach (var asset in assets)
            {
                if (ShouldProcessAsset(asset))
                {
                    processor?.Invoke(asset);
                }
            }
        }

        private static void ProcessMovedAssets(string[] oldPaths, string[] newPaths)
        {
            if (oldPaths == null || newPaths == null) return;

            for (int i = 0; i < oldPaths.Length; i++)
            {
                if (ShouldProcessAsset(oldPaths[i]))
                {
                    EditorSpriteSaveInfo.OnDeleteSprite(oldPaths[i]);
                    LogProcessed("[Moved From]", oldPaths[i]);
                    EditorSpriteSaveInfo.MarkParentAtlasesDirty(oldPaths[i], true);
                }

                if (ShouldProcessAsset(newPaths[i]))
                {
                    EditorSpriteSaveInfo.OnImportSprite(newPaths[i]);
                    LogProcessed("[Moved To]", newPaths[i]);
                    EditorSpriteSaveInfo.MarkParentAtlasesDirty(newPaths[i], false);
                }
            }
        }

        private static bool ShouldProcessAsset(string assetPath)
        {
            var config = AtlasConfiguration.Instance;

            if (string.IsNullOrEmpty(assetPath)) return false;
            if (assetPath.StartsWith("Packages/")) return false;

            if (!assetPath.StartsWith(config.sourceAtlasRoot)) return false;
            if (assetPath.StartsWith(config.excludeFolder)) return false;

            if (!IsValidImageFile(assetPath)) return false;

            foreach (var keyword in config.excludeKeywords)
            {
                if (assetPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            return true;
        }

        private static bool IsValidImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".png" => true,
                ".jpg" => true,
                ".jpeg" => true,
                _ => false
            };
        }

        private static void LogProcessed(string operation, string path)
        {
            if (AtlasConfiguration.Instance.enableLogging)
            {
                Debug.Log($"{operation} {Path.GetFileName(path)}\nPath: {path}");
            }
        }
    }
}