using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TEngine.Editor
{
    using UnityEditor;
    using UnityEngine;

    public class SpritePostprocessor : AssetPostprocessor
    {
        private static List<string> m_resourcesToDelete = new List<string>();

        // 文件名缓存：key=小写文件名(不含扩展名), value=完整路径列表
        private static Dictionary<string, HashSet<string>> s_fileNameCache;
        private static bool s_cacheInitialized = false;

        /// <summary>
        /// 初始化文件名缓存
        /// </summary>
        private static void EnsureCacheInitialized()
        {
            if (s_cacheInitialized && s_fileNameCache != null) return;

            s_fileNameCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var config = AtlasConfiguration.Instance;

            var tempRootDirArr = new List<string>(config.sourceAtlasRootDir);
            tempRootDirArr.AddRange(config.rootChildAtlasDir);
            tempRootDirArr.AddRange(config.singleAtlasDir);

            foreach (var rootDir in tempRootDirArr)
            {
                if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir)) continue;

                var files = Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories)
                    .Where(CheckIsValidImageFile);

                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var normalizedPath = Path.GetFullPath(file).Replace("\\", "/");

                    if (!s_fileNameCache.TryGetValue(fileName, out var pathSet))
                    {
                        pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        s_fileNameCache[fileName] = pathSet;
                    }
                    pathSet.Add(normalizedPath);
                }
            }

            s_cacheInitialized = true;
        }

        /// <summary>
        /// 重置缓存（配置变更时调用）
        /// </summary>
        public static void ResetCache()
        {
            s_cacheInitialized = false;
            s_fileNameCache?.Clear();
            s_fileNameCache = null;
        }

        /// <summary>
        /// 从缓存中移除文件
        /// </summary>
        private static void RemoveFromCache(string assetPath)
        {
            if (s_fileNameCache == null) return;

            var fileName = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            var normalizedPath = Path.GetFullPath(assetPath).Replace("\\", "/");

            if (s_fileNameCache.TryGetValue(fileName, out var pathSet))
            {
                pathSet.Remove(normalizedPath);
                if (pathSet.Count == 0)
                {
                    s_fileNameCache.Remove(fileName);
                }
            }
        }

        /// <summary>
        /// 添加文件到缓存
        /// </summary>
        private static void AddToCache(string assetPath)
        {
            if (s_fileNameCache == null) return;

            var fileName = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            var normalizedPath = Path.GetFullPath(assetPath).Replace("\\", "/");

            if (!s_fileNameCache.TryGetValue(fileName, out var pathSet))
            {
                pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                s_fileNameCache[fileName] = pathSet;
            }
            pathSet.Add(normalizedPath);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            m_resourcesToDelete.Clear();
            var config = AtlasConfiguration.Instance;

            if (!config.autoGenerate) return;

            // 计算需要处理的资源总数（用于进度显示）
            int totalAssets = (importedAssets?.Length ?? 0) + (deletedAssets?.Length ?? 0) + (movedAssets?.Length ?? 0);
            bool showProgress = totalAssets > 10; // 超过10个资源时显示进度条

            try
            {
                if (showProgress)
                {
                    EditorUtility.DisplayProgressBar("处理图集资源", "正在分析资源变更...", 0f);
                }

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
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }

                bool isDelete = m_resourcesToDelete.Count > 0;
                foreach (var res in m_resourcesToDelete)
                {
                    AssetDatabase.DeleteAsset(res);
                }
                if (isDelete)
                {
                    Debug.LogError($"<color=red>针对 {config.sourceAtlasRootDir} 路径下资源</color>\n<color=red>移除了空格和同名资源，请检查重新合入相关资源</color>");
                    AssetDatabase.Refresh();
                }
            }
        }

        private static void ProcessAssetChanges(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromPaths)
        {
            // 处理删除的资源（先处理删除，再处理导入）
            ProcessAssets(deletedAssets, (path) =>
            {
                RemoveFromCache(path);
                EditorSpriteSaveInfo.OnDeleteSprite(path);
                LogProcessed("[Deleted]", path);
            }, isDelete: true);

            // 处理导入的资源
            ProcessAssets(importedAssets, (path) =>
            {
                AddToCache(path);
                EditorSpriteSaveInfo.OnImportSprite(path);
                LogProcessed("[Added]", path);
            }, isDelete: false);

            ProcessMovedAssets(movedFromPaths, movedAssets);
        }

        private static void ProcessAssets(string[] assets, Action<string> processor, bool isDelete = false)
        {
            if (assets == null) return;

            foreach (var asset in assets)
            {
                if (ShouldProcessAsset(asset))
                {
                    if (!isDelete && (CheckFileNameContainsSpace(asset) || CheckDuplicateAssetName(asset) || ChangeSpriteTextureType(asset)))
                    {
                        continue;
                    }

                    processor?.Invoke(asset);
                }
            }
        }

        private static bool ChangeSpriteTextureType(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                return false;
            }
            bool isChange = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                isChange = true;
            }

            if (AtlasConfiguration.Instance.checkMipmaps)
            {
                if (AtlasConfiguration.Instance.enableMipmaps && !importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = true;
                    isChange = true;
                }
                else if (!AtlasConfiguration.Instance.enableMipmaps && importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    isChange = true;
                }
            }

            if (isChange)
            {
                LogProcessed("[Sprite Import Changed Reimport]", path);
                importer.SaveAndReimport();
            }
            return isChange;
        }

        private static bool CheckFileNameContainsSpace(string assetPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(assetPath);

            if (fileName.Contains(" "))
            {
                m_resourcesToDelete.Add(assetPath);
                Debug.LogError($"<color=red>发现资源名存在空格: {assetPath}</color>");
                return true;
            }
            return false;
        }

        private static bool CheckDuplicateAssetName(string assetPath)
        {
            // 确保缓存已初始化
            EnsureCacheInitialized();

            var currentFileName = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            var normalizedCurrentPath = Path.GetFullPath(assetPath).Replace("\\", "/");

            // 使用缓存快速查找同名文件
            if (s_fileNameCache.TryGetValue(currentFileName, out var existingPaths))
            {
                // 收集需要移除的过期路径
                List<string> pathsToRemove = null;

                foreach (var existingPath in existingPaths)
                {
                    // 跳过自身
                    if (existingPath.Equals(normalizedCurrentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 确保文件确实存在（防止缓存过期）
                    if (File.Exists(existingPath))
                    {
                        m_resourcesToDelete.Add(assetPath);
                        Debug.LogError($"<color=red>发现同名资源冲突: 合入资源: {assetPath} 存在资源: {existingPath}</color>");
                        return true;
                    }
                    else
                    {
                        // 文件不存在，标记为需要移除
                        pathsToRemove ??= new List<string>();
                        pathsToRemove.Add(existingPath);
                    }
                }

                // 遍历结束后再移除过期路径
                if (pathsToRemove != null)
                {
                    foreach (var path in pathsToRemove)
                    {
                        existingPaths.Remove(path);
                    }
                }
            }

            return false;
        }

        private static bool CheckIsValidImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext.Equals(".png") || ext.Equals(".jpg") || ext.Equals(".jpeg");
        }

        private static void ProcessMovedAssets(string[] oldPaths, string[] newPaths)
        {
            if (oldPaths == null || newPaths == null) return;

            for (int i = 0; i < oldPaths.Length; i++)
            {
                if (ShouldProcessAsset(oldPaths[i]))
                {
                    RemoveFromCache(oldPaths[i]);
                    EditorSpriteSaveInfo.OnDeleteSprite(oldPaths[i]);
                    LogProcessed("[Moved From]", oldPaths[i]);
                    EditorSpriteSaveInfo.MarkParentAtlasesDirty(oldPaths[i], true);
                }

                if (ShouldProcessAsset(newPaths[i]))
                {
                    if (CheckFileNameContainsSpace(newPaths[i]) || CheckDuplicateAssetName(newPaths[i]) || ChangeSpriteTextureType(newPaths[i]))
                    {
                        continue;
                    }
                    AddToCache(newPaths[i]);
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

            if (!CheckIsShowProcessPath(assetPath)) return false;
            if (CheckIsExcludeFolder(assetPath)) return false;

            if (!IsValidImageFile(assetPath)) return false;

            foreach (var keyword in config.excludeKeywords)
            {
                if (assetPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            return true;
        }

        private static bool CheckIsShowProcessPath(string assetPath)
        {
            var tempRootDirArr = new List<string>(AtlasConfiguration.Instance.sourceAtlasRootDir);
            tempRootDirArr.AddRange(AtlasConfiguration.Instance.rootChildAtlasDir);
            foreach (var rootPath in tempRootDirArr)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');
                if (!assetPath.StartsWith(tempPath + "/"))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        private static bool CheckIsExcludeFolder(string assetPath)
        {
            foreach (var rootPath in AtlasConfiguration.Instance.excludeFolder)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');
                if (assetPath.StartsWith(tempPath + "/"))
                {
                    return true;
                }
            }
            return false;
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