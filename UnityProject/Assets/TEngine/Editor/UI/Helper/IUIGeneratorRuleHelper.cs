using System;
using System.IO;
using System.Text;
using TEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TEngine.UI.Editor
{
    public interface IUIGeneratorRuleHelper
    {
        string GetPrivateComponentByNameRule(string regexName, string componetName, EBindType bindType);

        string GetPublicComponentByNameRule(string variableName);

        string GetClassGenerateName(GameObject targetObject, UIScriptGenerateData scriptGenerateData);

        string GetUIResourceSavePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData);

        void WriteUIScriptContent(string className, string scriptContent, UIScriptGenerateData scriptGenerateData);

        bool CheckCanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData);
    }

    public class DefaultIuiGeneratorRuleHelper : IUIGeneratorRuleHelper
    {
        public string GetPrivateComponentByNameRule(string regexName, string componentName, EBindType bindType)
        {
            string endPrefix = bindType == EBindType.ListCom ? "List" : string.Empty;
            int endNameIndex = componentName.IndexOf(
                UIGenerateConfiguration.Instance.UIGenerateCommonData.ComCheckEndName,
                StringComparison.Ordinal);

            string componentSuffix = endNameIndex >= 0 ? componentName.Substring(endNameIndex + 1) : componentName;

            return $"m{regexName}{componentSuffix}{endPrefix}";
        }

        public string GetPublicComponentByNameRule(string variableName)
        {
            return variableName.Substring(1);
        }

        public string GetClassGenerateName(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            return $"{UIGenerateConfiguration.Instance.UIGenerateCommonData.GeneratePrefix}_{targetObject.name}";
        }

        public string GetUIResourceSavePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null)
                return $"\"{nameof(targetObject)}\"";

            // 默认返回资源名
            string defaultPath = targetObject.name;

            // 获取对应的Prefab资源路径（支持场景Prefab实例 & Prefab编辑模式）
            string assetPath = GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/"))
                return defaultPath; // 不在 Assets 下

            // 统一使用正斜杠
            assetPath = assetPath.Replace('\\', '/');

            switch (scriptGenerateData.LoadType)
            {
                case EUIResLoadType.Resources:
                {
                    string resourcesRoot = scriptGenerateData.UIPrefabRootPath; // 例如 "Assets/Resources/UI"
                    string relPath = GetResourcesRelativePath(assetPath, resourcesRoot);

                    if (relPath == null)
                    {
                        Debug.LogWarning($"[UI生成] 资源 {assetPath} 不在配置的 Resources 根目录下: {resourcesRoot}");
                        return defaultPath;
                    }

                    return relPath;
                }

                case EUIResLoadType.AssetBundle:
                {
                    string bundleRoot = scriptGenerateData.UIPrefabRootPath; // 例如 "Assets/Bundles/UI"
                    var defaultPackage = YooAsset.Editor.AssetBundleCollectorSettingData.Setting.GetPackage("DefaultPackage");
                    if (defaultPackage.EnableAddressable)
                        return defaultPath;

                    if (!assetPath.StartsWith(bundleRoot, System.StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"[UI生成] 资源 {assetPath} 不在配置的 AssetBundle 根目录下: {bundleRoot}");
                        return defaultPath;
                    }

                    return Path.ChangeExtension(assetPath, null);
                }

                default:
                    return defaultPath;
            }
        }

        /// <summary>
        /// 获取 GameObject 对应的 Prefab 资源路径，如果是 Prefab 编辑模式也可以获取
        /// </summary>
        private string GetPrefabAssetPath(GameObject go)
        {
            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefabAsset != null)
                return AssetDatabase.GetAssetPath(prefabAsset);

            // Prefab 编辑模式
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.IsPartOfPrefabContents(go))
                return prefabStage.assetPath;

            return null;
        }

        /// <summary>
        /// 获取 Resources.Load 可用路径（去掉扩展名），如果不在指定的 Resources 根目录返回 null
        /// </summary>
        private string GetResourcesRelativePath(string assetPath, string resourcesRoot)
        {
            // 统一正斜杠
            assetPath = assetPath.Replace('\\', '/');
            resourcesRoot = resourcesRoot.Replace('\\', '/');

            if (!assetPath.StartsWith(resourcesRoot, System.StringComparison.OrdinalIgnoreCase))
                return null;

            // 获取相对路径
            string relPath = assetPath.Substring(resourcesRoot.Length).TrimStart('/');
            return Path.ChangeExtension(relPath, null); // 去掉扩展名
        }


        public void WriteUIScriptContent(string className, string scriptContent, UIScriptGenerateData scriptGenerateData)
        {
            string scriptFolderPath = scriptGenerateData.GenerateHolderCodePath;
            string scriptFilePath = Path.Combine(scriptFolderPath, className + ".cs");

            if (!Directory.Exists(scriptFolderPath))
            {
                Directory.CreateDirectory(scriptFolderPath);
            }

            if (File.Exists(scriptFilePath))
            {
                string oldText = File.ReadAllText(scriptFilePath);
                if (oldText.Equals(scriptContent))
                {
                    EditorPrefs.SetString("Generate", className);
                    UIScriptGeneratorHelper.CheckHasAttach();
                    return;
                }
            }

            File.WriteAllText(scriptFilePath, scriptContent, Encoding.UTF8);
            EditorPrefs.SetString("Generate", className);
            AssetDatabase.Refresh();
        }

        public bool CheckCanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            string assetPath = GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/"))
                return false; // 不在 Assets 下

            // 统一使用正斜杠
            assetPath = assetPath.Replace('\\', '/');
            bool result = assetPath.StartsWith(scriptGenerateData.UIPrefabRootPath, System.StringComparison.OrdinalIgnoreCase);
            if (!result)
            {
                Debug.LogWarning($"UI存储位置与配置生成规则不符合 请检查对应配置的UIPrefabRootPath\n[AssetPath]{assetPath}\n[ConfigPath]{scriptGenerateData.UIPrefabRootPath}");
            }

            return result;
        }
    }
}
