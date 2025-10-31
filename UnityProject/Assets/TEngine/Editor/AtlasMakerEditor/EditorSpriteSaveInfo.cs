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
        private static bool _initialized;

        private static AtlasConfiguration Config => AtlasConfiguration.Instance;

        static EditorSpriteSaveInfo()
        {
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
            if (!ShouldProcess(assetPath)) return;

            var atlasName = GetAtlasName(assetPath);
            if (string.IsNullOrEmpty(atlasName)) return;

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
            if (!ShouldProcess(assetPath)) return;

            var atlasName = GetAtlasName(assetPath);
            if (string.IsNullOrEmpty(atlasName)) return;

            if (_atlasMap.TryGetValue(atlasName, out var list))
            {
                if (list.Remove(assetPath))
                {
                    MarkDirty(atlasName, isCreateNew);
                    MarkParentAtlasesDirty(assetPath, isCreateNew);
                }
            }
        }

        [MenuItem("Tools/图集工具/ForceGenerateAll")]
        public static void ForceGenerateAll()
        {
            _atlasMap.Clear();
            ScanExistingSprites();
            _dirtyAtlasNamesNeedCreateNew.UnionWith(_atlasMap.Keys);
            ProcessDirtyAtlases(true);
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
            var currentPath = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            var rootPath = Config.sourceAtlasRoot.Replace("\\", "/").TrimEnd('/');
            while (currentPath != null && currentPath.StartsWith(rootPath))
            {
                var parentAtlasName = GetAtlasNameForDirectory(currentPath);
                if (!string.IsNullOrEmpty(parentAtlasName))
                {
                    MarkDirty(parentAtlasName, isCreateNew);
                }

                currentPath = Path.GetDirectoryName(currentPath);
            }
        }

        private static void OnUpdate()
        {
            if (_dirtyAtlasNames.Count > 0 || _dirtyAtlasNamesNeedCreateNew.Count > 0)
            {
                ProcessDirtyAtlases();
            }
        }

        private static void ProcessDirtyAtlases(bool force = false)
        {
            try
            {
                AssetDatabase.StartAssetEditing();

                while (_dirtyAtlasNames.Count > 0)
                {
                    var atlasName = _dirtyAtlasNames.First();
                    if (force || ShouldUpdateAtlas(atlasName))
                    {
                        GenerateAtlas(atlasName, false);
                    }
                    _dirtyAtlasNames.Remove(atlasName);
                }

                while (_dirtyAtlasNamesNeedCreateNew.Count > 0)
                {
                    var atlasName = _dirtyAtlasNamesNeedCreateNew.First();
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
            AssetDatabase.Refresh();
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
                AssetDatabase.Refresh();
                EditorApplication.delayCall += () =>
                {
#if UNITY_2022_1_OR_NEWER
                    SpriteAtlasImporter sai = (SpriteAtlasImporter)AssetImporter.GetAtPath(outputPath);
                    ConfigureAtlasV2Settings(sai);
#else
                    ConfigureAtlasV2Settings(spriteAtlasAsset);
                    SpriteAtlasAsset.Save(spriteAtlasAsset, outputPath);
#endif
                    AssetDatabase.WriteImportSettingsIfDirty(outputPath);
                    AssetDatabase.Refresh();
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
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
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
            var normalizedPath = assetPath.Replace("\\", "/");
            var rootPath = Config.sourceAtlasRoot.Replace("\\", "/").TrimEnd('/');

            if (!normalizedPath.StartsWith(rootPath + "/")) return null;

            var relativePath = normalizedPath
                .Substring(rootPath.Length + 1)
                .Split('/');

            if (relativePath.Length < 2) return null;

            var directories = relativePath.Take(relativePath.Length - 1);
            var atlasNamePart = string.Join("_", directories);
            var rootFolderName = Path.GetFileName(rootPath);

            return $"{rootFolderName}_{atlasNamePart}";
        }

        private static bool ShouldProcess(string assetPath)
        {
            return IsImageFile(assetPath) && !IsExcluded(assetPath);
        }

        private static bool IsExcluded(string path)
        {
            return path.StartsWith(Config.excludeFolder) ||
                   Config.excludeKeywords.Any(k => path.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        private static void MarkDirty(string atlasName, bool isCreateNew = false)
        {
            if (isCreateNew)
            {
                _dirtyAtlasNamesNeedCreateNew.Add(atlasName);
            }
            else
            {
                _dirtyAtlasNames.Add(atlasName);
            }
        }

        private static bool ShouldUpdateAtlas(string atlasName)
        {
            // var outputPath = $"{Config.outputAtlasDir}/{atlasName}.spriteatlas";
            return true;
        }

        private static DateTime GetLatestSpriteTime(string atlasName)
        {
            return _atlasMap[atlasName]
                .Select(p => new FileInfo(p).LastWriteTime)
                .DefaultIfEmpty()
                .Max();
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
                AssetDatabase.Refresh();
            }
        }

        private static void ScanExistingSprites(bool isCreateNew = true)
        {
            var guids = AssetDatabase.FindAssets("t:Sprite", new[] { Config.sourceAtlasRoot });
            foreach (var guid in guids)
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
            var normalizedPath = directoryPath.Replace("\\", "/");
            var rootPath = Config.sourceAtlasRoot.Replace("\\", "/").TrimEnd('/');

            if (!normalizedPath.StartsWith(rootPath + "/")) return null;

            var relativePath = normalizedPath
                .Substring(rootPath.Length + 1)
                .Split('/');

            var atlasNamePart = string.Join("_", relativePath);
            var rootFolderName = Path.GetFileName(rootPath);

            return $"{rootFolderName}_{atlasNamePart}";
        }
    }

#endif
}