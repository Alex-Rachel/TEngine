namespace TEngine.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.U2D;
    using UnityEngine;
    using UnityEngine.U2D;

    public static class EditorSpriteSaveInfo
    {
        private static readonly HashSet<string> _dirtyAtlasNamesNeedCreateNew = new HashSet<string>();
        private static readonly HashSet<string> _dirtyAtlasNames = new HashSet<string>();
        private static readonly Dictionary<string, List<string>> _atlasMap = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string> _atlasPathMap = new Dictionary<string, string>();
        private static bool _initialized;
        private static bool _isInScanExistingSprites;
        private static bool _isBuildChange = false;

        private static AtlasConfiguration Config => AtlasConfiguration.Instance;

        static EditorSpriteSaveInfo()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;
            ScanExistingSprites(false);
            _initialized = true;
        }

        public static void OnImportSprite(string assetPath, bool isCreateNew = false)
        {
            assetPath = assetPath.Replace("\\", "/");
            if (!ShouldProcess(assetPath)) return;

            var atlasName = GetAtlasName(assetPath);
            if (string.IsNullOrEmpty(atlasName)) return;

            if (CheckIsNeedGenerateSingleAtlas(assetPath))
            {
                atlasName = GetSingleAtlasName(assetPath);
            }
            else if (CheckIsNeedGenerateRootChildDirAtlas(assetPath))
            {
                atlasName = GetRootChildDirAtlasName(assetPath);
            }

            if (!_atlasMap.TryGetValue(atlasName, out var list))
            {
                list = new List<string>();
                _atlasMap[atlasName] = list;
            }

            if (!list.Contains(assetPath))
            {
                list.Add(assetPath);
                MarkDirty(atlasName, isCreateNew);
                MarkParentAtlasesDirty(assetPath, isCreateNew);
            }
        }

        public static void OnDeleteSprite(string assetPath, bool isCreateNew = true)
        {
            assetPath = assetPath.Replace("\\", "/");
            if (!ShouldProcess(assetPath)) return;

            var atlasName = GetAtlasName(assetPath);
            if (string.IsNullOrEmpty(atlasName)) return;

            if (CheckIsNeedGenerateSingleAtlas(assetPath))
            {
                atlasName = GetSingleAtlasName(assetPath);
            }
            else if (CheckIsNeedGenerateRootChildDirAtlas(assetPath))
            {
                atlasName = GetRootChildDirAtlasName(assetPath);
            }

            if (_atlasMap.TryGetValue(atlasName, out var list))
            {
                if (list.Remove(assetPath))
                {
                    MarkDirty(atlasName, isCreateNew);
                    MarkParentAtlasesDirty(assetPath, isCreateNew);
                }
            }
        }

        [MenuItem("Tools/图集工具/立即重新生成变动的图集数据")]
        public static void ForceGenerateAll()
        {
            _isBuildChange = true;
            ForceGenerateAll(false);
            _isBuildChange = false;
        }

        public static void ForceGenerateAll(bool isClearAll)
        {
            _isInScanExistingSprites = true;
            try
            {
                EditorUtility.DisplayProgressBar("生成图集", "正在初始化...", 0f);

                if (isClearAll)
                {
                    EditorUtility.DisplayProgressBar("生成图集", "清理缓存...", 0.1f);
                    _atlasPathMap.Clear();
                    ClearCache();
                    ClearAllAtlas();
                }

                _atlasMap.Clear();
                EditorUtility.DisplayProgressBar("生成图集", "扫描现有精灵...", 0.2f);
                ScanExistingSprites();

                EditorUtility.DisplayProgressBar("生成图集", "分析变更...", 0.4f);
                if (_isBuildChange)
                {
                    int current = 0;
                    int total = _atlasMap.Count;
                    foreach (var item in _atlasMap)
                    {
                        current++;
                        if (total > 0)
                        {
                            EditorUtility.DisplayProgressBar("生成图集", $"检查图集时间戳 ({current}/{total})...", 0.4f + 0.2f * current / total);
                        }

                        if (GetLatestAtlasTime(item.Key) >= GetLatestSpriteTime(item.Key))
                        {
                            continue;
                        }
                        else
                        {
                            _dirtyAtlasNamesNeedCreateNew.Add(item.Key);
                        }
                    }
                }
                else
                {
                    _dirtyAtlasNamesNeedCreateNew.UnionWith(_atlasMap.Keys);
                }

                EditorUtility.DisplayProgressBar("生成图集", "生成图集文件...", 0.6f);
                ProcessDirtyAtlases(true);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isInScanExistingSprites = false;
            }
        }

        private static void ClearAllAtlas()
        {
            string[] atlasV2Files =
                Directory.GetFiles(Config.outputAtlasDir, "*.spriteatlasv2", SearchOption.AllDirectories);
            string[] atlasFiles =
                Directory.GetFiles(Config.outputAtlasDir, "*.spriteatlas", SearchOption.AllDirectories);

            foreach (string filePath in atlasFiles)
            {
                AssetDatabase.DeleteAsset(filePath);
            }

            foreach (string filePath in atlasV2Files)
            {
                AssetDatabase.DeleteAsset(filePath);
            }

            AssetDatabase.Refresh();
            Debug.Log($"已删除 {atlasFiles?.Length + atlasV2Files?.Length} 个图集文件");
        }

        public static void ClearCache()
        {
            _dirtyAtlasNamesNeedCreateNew.Clear();
            _dirtyAtlasNames.Clear();
            _atlasMap.Clear();
            AssetDatabase.Refresh();
        }

        public static void MarkParentAtlasesDirty(string assetPath, bool isCreateNew)
        {
            var currentPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");

            if(string.IsNullOrEmpty(currentPath)) return;
            var tempRootDirArr = new List<string>(Config.sourceAtlasRootDir);
            tempRootDirArr.AddRange(Config.rootChildAtlasDir);
            foreach (var rootPath in tempRootDirArr)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');
                var tempCurrentPath = currentPath;

                if (!tempCurrentPath.StartsWith(tempPath))
                {
                    continue;
                }
                while (tempCurrentPath != null && tempCurrentPath.StartsWith(tempPath))
                {
                    var parentAtlasName = GetAtlasNameForDirectory(tempCurrentPath);

                    if (!string.IsNullOrEmpty(parentAtlasName))
                    {
                        MarkDirty(parentAtlasName, isCreateNew);
                    }
                    tempCurrentPath = Path.GetDirectoryName(tempCurrentPath)?.Replace("\\", "/");
                }
            }
        }

        private static void OnUpdate()
        {
            if (_isInScanExistingSprites) return;
            if (_dirtyAtlasNames.Count > 0 || _dirtyAtlasNamesNeedCreateNew.Count > 0)
            {
                ProcessDirtyAtlases();
            }
        }

        private static void ProcessDirtyAtlases(bool force = false)
        {
            int totalCount = _dirtyAtlasNames.Count + _dirtyAtlasNamesNeedCreateNew.Count;
            int processedCount = 0;
            bool showProgress = totalCount > 3 && _isInScanExistingSprites;

            try
            {
                AssetDatabase.StartAssetEditing();

                while (_dirtyAtlasNames.Count > 0)
                {
                    var atlasName = _dirtyAtlasNames.First();
                    if (showProgress)
                    {
                        processedCount++;
                        EditorUtility.DisplayProgressBar("生成图集", $"更新图集: {atlasName} ({processedCount}/{totalCount})", 0.6f + 0.4f * processedCount / totalCount);
                    }

                    if (force || ShouldUpdateAtlas(atlasName))
                    {
                        GenerateAtlas(atlasName, false);
                    }
                    _dirtyAtlasNames.Remove(atlasName);
                }

                while (_dirtyAtlasNamesNeedCreateNew.Count > 0)
                {
                    var atlasName = _dirtyAtlasNamesNeedCreateNew.First();
                    if (showProgress)
                    {
                        processedCount++;
                        EditorUtility.DisplayProgressBar("生成图集", $"创建图集: {atlasName} ({processedCount}/{totalCount})", 0.6f + 0.4f * processedCount / totalCount);
                    }

                    if (force || ShouldUpdateAtlas(atlasName))
                    {
                        GenerateAtlas(atlasName, true);
                    }
                    _dirtyAtlasNamesNeedCreateNew.Remove(atlasName);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void GenerateAtlas(string atlasName, bool createNew = false)
        {
            var outputPath = $"{Config.outputAtlasDir}/{atlasName}.spriteatlas";
            var outputPathV2 = outputPath.Replace(".spriteatlas", ".spriteatlasv2");
            string deletePath = outputPath;
            if (Config.enableV2)
            {
                DeleteAtlas(outputPath);
                deletePath = outputPathV2;
            }
            else
            {
                DeleteAtlas(outputPathV2);
                deletePath = outputPath;
            }

            if (createNew && File.Exists(deletePath))
            {
                AssetDatabase.DeleteAsset(deletePath);
            }
            var sprites = LoadValidSprites(atlasName);
            EnsureOutputDirectory();
            if (sprites.Count == 0)
            {
                DeleteAtlas(deletePath);
                return;
            }
            // 延迟调用生成图集，避免在 AssetEditing 块中调用
            EditorApplication.delayCall += () => { InternalGenerateAtlas(atlasName, sprites, outputPath); };
        }

        private static string InternalGenerateAtlas(string atlasName, List<Sprite> sprites, string outputPath)
        {
            SpriteAtlasAsset spriteAtlasAsset = null;
            SpriteAtlas atlas = null;
            if (Config.enableV2)
            {
                outputPath = outputPath.Replace(".spriteatlas", ".spriteatlasv2");

                if (!File.Exists(outputPath))
                {
                    spriteAtlasAsset = new SpriteAtlasAsset();
                    atlas = new SpriteAtlas();
                }
                else
                {
                    spriteAtlasAsset = SpriteAtlasAsset.Load(outputPath);
                    atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);
                    if (atlas != null)
                    {
                        var olds = atlas.GetPackables();

                        if (olds != null)
                        {
                            spriteAtlasAsset.Remove(olds);
                        }
                    }
                }
            }

            if (Config.enableV2)
            {
                spriteAtlasAsset?.Add(sprites.ToArray());
                SpriteAtlasAsset.Save(spriteAtlasAsset, outputPath);
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
                EditorApplication.delayCall += () =>
                {
#if UNITY_2022_1_OR_NEWER
                    SpriteAtlasImporter sai = (SpriteAtlasImporter)AssetImporter.GetAtPath(outputPath);
                    if (sai != null)
                    {
                        ConfigureAtlasV2Settings(sai);
                        AssetDatabase.WriteImportSettingsIfDirty(outputPath);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }
#else
                    ConfigureAtlasV2Settings(spriteAtlasAsset);
                    SpriteAtlasAsset.Save(spriteAtlasAsset, outputPath);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
#endif
                };
            }
            else
            {
                atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);

                if (atlas != null)
                {
                    var olds = atlas.GetPackables();
                    if (olds != null)
                    {
                        atlas.Remove(olds);
                    }
                    ConfigureAtlasSettings(atlas);
                    atlas.Add(sprites.ToArray());
                    atlas.SetIsVariant(false);
                }
                else
                {
                    atlas = new SpriteAtlas();
                    ConfigureAtlasSettings(atlas);
                    atlas.Add(sprites.ToArray());
                    atlas.SetIsVariant(false);
                    AssetDatabase.CreateAsset(atlas, outputPath);
                }
            }
            if (atlas != null)
            {
                EditorUtility.SetDirty(atlas);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            if (File.Exists(outputPath))
            {
                _atlasPathMap[atlasName] = outputPath;
            }
            if (Config.enableLogging)
            {
                Debug.Log($"<b>[Generate Atlas]</b>: {atlasName} ({sprites.Count} sprites)");
            }

            return outputPath;
        }

        private static List<Sprite> LoadValidSprites(string atlasName)
        {
            if (_atlasMap.TryGetValue(atlasName, out List<string> spriteList))
            {
                var allSprites = new List<Sprite>();

                foreach (var assetPath in spriteList.Where(File.Exists))
                {
                    // 加载所有子图
                    var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                        .OfType<Sprite>()
                        .Where(s => s != null)
                        .ToArray();

                    allSprites.AddRange(sprites);
                }

                return allSprites;
            }
            return new List<Sprite>();
        }


#if UNITY_2022_1_OR_NEWER
        private static void ConfigureAtlasV2Settings(SpriteAtlasImporter atlasImporter)
        {
            void SetPlatform(string platform, TextureImporterFormat format)
            {
                var settings = atlasImporter.GetPlatformSettings(platform);
                if (settings == null) return;
                ;
                settings.overridden = true;
                settings.format = format;
                settings.compressionQuality = Config.compressionQuality;
                atlasImporter.SetPlatformSettings(settings);
            }
            
            SetPlatform("Android", Config.androidFormat);
            SetPlatform("iPhone", Config.iosFormat);
            SetPlatform("WebGL", Config.webglFormat);
            
            var packingSettings = new SpriteAtlasPackingSettings
            {
                padding = Config.padding,
                enableRotation = Config.enableRotation,
                blockOffset = Config.blockOffset,
                enableTightPacking = Config.tightPacking,
                enableAlphaDilation = true
            };
            atlasImporter.packingSettings = packingSettings;
        }
#else
        private static void ConfigureAtlasV2Settings(SpriteAtlasAsset spriteAtlasAsset)
        {
            void SetPlatform(string platform, TextureImporterFormat format)
            {
                var settings = spriteAtlasAsset.GetPlatformSettings(platform);
                if (settings == null) return;
                ;
                settings.overridden = true;
                settings.format = format;
                settings.compressionQuality = Config.compressionQuality;
                spriteAtlasAsset.SetPlatformSettings(settings);
            }

            SetPlatform("Android", Config.androidFormat);
            SetPlatform("iPhone", Config.iosFormat);
            SetPlatform("WebGL", Config.webglFormat);

            var packingSettings = new SpriteAtlasPackingSettings
            {
                padding = Config.padding,
                enableRotation = Config.enableRotation,
                blockOffset = Config.blockOffset,
                enableTightPacking = Config.tightPacking,
                enableAlphaDilation = true
            };
            spriteAtlasAsset.SetPackingSettings(packingSettings);
        }
#endif


        private static void ConfigureAtlasSettings(SpriteAtlas atlas)
        {
            void SetPlatform(string platform, TextureImporterFormat format)
            {
                var settings = atlas.GetPlatformSettings(platform);
                settings.overridden = true;
                settings.format = format;
                settings.compressionQuality = Config.compressionQuality;
                atlas.SetPlatformSettings(settings);
            }

            SetPlatform("Android", Config.androidFormat);
            SetPlatform("iPhone", Config.iosFormat);
            SetPlatform("WebGL", Config.webglFormat);

            var packingSettings = new SpriteAtlasPackingSettings
            {
                padding = Config.padding,
                enableRotation = Config.enableRotation,
                blockOffset = Config.blockOffset,
                enableTightPacking = Config.tightPacking,
            };
            atlas.SetPackingSettings(packingSettings);
        }

        private static string GetAtlasName(string assetPath)
        {
            var tempRootDirArr = new List<string>(Config.sourceAtlasRootDir);
            tempRootDirArr.AddRange(Config.rootChildAtlasDir);
            foreach (var rootPath in tempRootDirArr)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');
                if (!assetPath.StartsWith(tempPath + "/"))
                {
                    continue;
                }
                var relativePath = assetPath.Substring(tempPath.Length + 1).Split('/');
                // 根目录下文本不处理
                if (relativePath.Length < 2)
                {
                    return null;
                }
                // 提取目录部分
                var directories = relativePath.Take(relativePath.Length - 1);
                var atlasNames = string.Join("_", directories);
                // 根目录文件名
                var rootFolderName = Path.GetFileName(tempPath);
                return $"{rootFolderName}_{atlasNames}";
            }
            return null;
        }

        private static bool ShouldProcess(string assetPath)
        {
            return IsImageFile(assetPath) && !IsExcluded(assetPath);
        }

        private static bool IsExcluded(string path)
        {
            return CheckIsExcludeFolder(path)//spritePath.StartsWith(Config.excludeFolder)
                   || Config.excludeKeywords.Any(key => path.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        private static void MarkDirty(string atlasName, bool isCreateNew = false)
        {
            if (_isBuildChange)
            {
                if (GetLatestAtlasTime(atlasName) > GetLatestSpriteTime(atlasName))
                {
                    return;
                }
            }
            if (isCreateNew)
            {
                _dirtyAtlasNamesNeedCreateNew.Add(atlasName);
            }
            else
            {
                if (!_dirtyAtlasNamesNeedCreateNew.Contains(atlasName))
                {
                    _dirtyAtlasNames.Add(atlasName);
                }
            }
        }

        private static bool ShouldUpdateAtlas(string atlasName)
        {
            // var outputPath = $"{Config.outputAtlasDir}/{atlasName}.spriteatlas";
            return true;
        }

        private static DateTime GetLatestSpriteTime(string atlasName)
        {
            if (_atlasMap.TryGetValue(atlasName, out List<string> list))
            {
                DateTime maxTime = DateTime.MinValue;
                foreach (var path in list)
                {
                    if (File.Exists(path))
                    {
                        var time = File.GetLastWriteTime(path);
                        if (time > maxTime) maxTime = time;
                    }
                }
                return maxTime;
            }
            return DateTime.MinValue;
        }

        private static DateTime GetLatestAtlasTime(string atlasName)
        {
            if (_atlasPathMap.TryGetValue(atlasName, out var atlasPath) && File.Exists(atlasPath))
            {
                return File.GetLastWriteTime(atlasPath);
            }
            return DateTime.MinValue;
        }

        private static void DeleteAtlas(string path)
        {
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
                if (Config.enableLogging)
                    Debug.Log($"Deleted empty atlas: {Path.GetFileName(path)}");
            }
        }

        private static void EnsureOutputDirectory()
        {
            if (!Directory.Exists(Config.outputAtlasDir))
            {
                Directory.CreateDirectory(Config.outputAtlasDir);
            }
        }

        private static void ScanExistingSprites(bool isCreateNew = true)
        {
            List<string> sprites = new List<string>();
            var guids = AssetDatabase.FindAssets("t:sprite", Config.sourceAtlasRootDir);
            sprites.AddRange(guids);
            guids = AssetDatabase.FindAssets("t:sprite", Config.rootChildAtlasDir);
            sprites.AddRange(guids);
            foreach (var guid in sprites)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (ShouldProcess(path))
                {
                    OnImportSprite(path, isCreateNew);
                }
            }
        }

        private static string GetAtlasNameForDirectory(string directoryPath)
        {
            foreach (var rootPath in Config.sourceAtlasRootDir)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');
                if (!directoryPath.StartsWith(tempPath + "/"))
                {
                    continue;
                }
                var relativePath = directoryPath.Substring(rootPath.Length + 1).Split('/');
                var atlasNamePart = string.Join("_", relativePath);
                var rootFolderName = Path.GetFileName(rootPath);
                return $"{rootFolderName}_{atlasNamePart}";
            }
            return null;
        }

        private static string GetSingleAtlasName(string spritePath)
        {
            foreach (var rootPath in Config.sourceAtlasRootDir)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');
                if (!spritePath.StartsWith(tempPath + "/"))
                {
                    continue;
                }
                var relativePath = spritePath.Substring(tempPath.Length + 1).Split('/');
                // 根目录下文本不处理
                if (relativePath.Length < 2)
                {
                    return null;
                }
                // 提取目录部分
                // var directories = relativePath.Take(relativePath.Length - 1);
                relativePath[^1] = Path.GetFileNameWithoutExtension(spritePath);
                var atlasNames = string.Join("_", relativePath);
                // 根目录文件名
                var rootFolderName = Path.GetFileName(tempPath);
                return $"{rootFolderName}_{atlasNames}";
            }
            return null;
        }

        private static bool CheckIsNeedGenerateSingleAtlas(string spritePath)
        {
            // 检查是否是需要排除的路径
            return !CheckIsExcludeFolder(spritePath)//spritePath.StartsWith(Config.excludeFolder)
                   && Config.singleAtlasDir.Any(key => spritePath.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool CheckIsNeedGenerateRootChildDirAtlas(string spritePath)
        {
            // 检查是否是需要排除的路径
            return !CheckIsExcludeFolder(spritePath)//spritePath.StartsWith(Config.excludeFolder)
                   && Config.rootChildAtlasDir.Any(key => spritePath.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetRootChildDirAtlasName(string spritePath)
        {
            foreach (var rootPath in Config.rootChildAtlasDir)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');
                if (spritePath.StartsWith(tempPath))
                {
                    string[] subDirectories = AssetDatabase.GetSubFolders(tempPath);
                    foreach (var subDirectory in subDirectories)
                    {
                        if (spritePath.StartsWith(subDirectory))
                        {
                            string rootName = Path.GetFileName(tempPath);
                            string directoryName = Path.GetFileName(subDirectory);
                            return $"{rootName}_{directoryName}";
                        }
                    }
                }
            }
            return null;
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
    }

#endif
}