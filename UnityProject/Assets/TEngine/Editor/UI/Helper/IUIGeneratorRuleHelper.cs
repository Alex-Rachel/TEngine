using System;
using System.IO;
using System.Text;
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
            var common = UIGenerateConfiguration.Instance.UIGenerateCommonData;
            int endNameIndex = componentName.IndexOf(common.ComCheckEndName, StringComparison.Ordinal);

            string componentSuffix = endNameIndex >= 0 ? componentName.Substring(endNameIndex + 1) : componentName;

            return $"m{regexName}{componentSuffix}{endPrefix}";
        }

        public string GetPublicComponentByNameRule(string variableName)
        {
            if (string.IsNullOrEmpty(variableName)) return variableName;
            if (variableName.Length > 1)
                return variableName.Substring(1);
            return variableName;
        }

        public string GetClassGenerateName(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            var config = UIGenerateConfiguration.Instance.UIGenerateCommonData;
            string prefix = config.GeneratePrefix ?? "ui";
            return $"{prefix}_{targetObject.name}";
        }

        public string GetUIResourceSavePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null)
                return $"\"{nameof(targetObject)}\"";

            string defaultPath = targetObject.name;
            string assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return defaultPath;

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

                    try
                    {
                        var defaultPackage = YooAsset.Editor.AssetBundleCollectorSettingData.Setting.GetPackage("DefaultPackage");
                        if (defaultPackage != null && defaultPackage.EnableAddressable)
                            return defaultPath;
                    }
                    catch
                    {
                    }

                    if (!assetPath.StartsWith(bundleRoot, StringComparison.OrdinalIgnoreCase))
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

        private string GetResourcesRelativePath(string assetPath, string resourcesRoot)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(resourcesRoot)) return null;
            assetPath = assetPath.Replace('\\', '/');
            resourcesRoot = resourcesRoot.Replace('\\', '/');

            if (!assetPath.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            string relPath = assetPath.Substring(resourcesRoot.Length).TrimStart('/');
            return Path.ChangeExtension(relPath, null);
        }

        public void WriteUIScriptContent(string className, string scriptContent, UIScriptGenerateData scriptGenerateData)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentNullException(nameof(className));
            if (scriptContent == null) throw new ArgumentNullException(nameof(scriptContent));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            string scriptFolderPath = scriptGenerateData.GenerateHolderCodePath;
            string scriptFilePath = Path.Combine(scriptFolderPath, className + ".cs");

            if (!Directory.Exists(scriptFolderPath))
            {
                Directory.CreateDirectory(scriptFolderPath);
            }

            if (File.Exists(scriptFilePath))
            {
                string oldText = File.ReadAllText(scriptFilePath, Encoding.UTF8);
                if (oldText.Equals(scriptContent, StringComparison.Ordinal))
                {
                    // 文件未变更：标记并等待脚本 reload 去做附加
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
            if (targetObject == null || scriptGenerateData == null) return false;

            string assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return false; // 不在 Assets 下

            assetPath = assetPath.Replace('\\', '/');
            bool result = assetPath.StartsWith(scriptGenerateData.UIPrefabRootPath, StringComparison.OrdinalIgnoreCase);
            if (!result)
            {
                Debug.LogWarning($"UI存储位置与配置生成规则不符合 请检查对应配置的UIPrefabRootPath\n[AssetPath]{assetPath}\n[ConfigPath]{scriptGenerateData.UIPrefabRootPath}");
            }

            return result;
        }
    }
}
