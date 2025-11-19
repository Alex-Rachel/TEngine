using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        string GetReferenceNamespace(List<UIBindData> uiBindDatas);

        string GetVariableContent(List<UIBindData> uiBindDatas);
    }


    public class DefaultUIGeneratorRuleHelper : IUIGeneratorRuleHelper
    {
        public string GetPrivateComponentByNameRule(string regexName, string componentName, EBindType bindType)
        {
            var endPrefix = bindType == EBindType.ListCom ? "List" : string.Empty;
            var common = UIGenerateConfiguration.Instance.UIGenerateCommonData;
            var endNameIndex = componentName.IndexOf(common.ComCheckEndName, StringComparison.Ordinal);

            var componentSuffix = endNameIndex >= 0 ? componentName.Substring(endNameIndex + 1) : componentName;
            return $"m{regexName}{componentSuffix}{endPrefix}";
        }

        public string GetPublicComponentByNameRule(string variableName)
        {
            if (string.IsNullOrEmpty(variableName)) return variableName;
            return variableName.Length > 1 ? variableName.Substring(1) : variableName;
        }

        public string GetClassGenerateName(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            var config = UIGenerateConfiguration.Instance.UIGenerateCommonData;
            var prefix = config.GeneratePrefix ?? "ui";
            return $"{prefix}_{targetObject.name}";
        }

        public string GetUIResourceSavePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null) return $"\"{nameof(targetObject)}\"";

            var defaultPath = targetObject.name;
            var assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);

            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return defaultPath;

            assetPath = assetPath.Replace('\\', '/');
            return scriptGenerateData.LoadType switch
            {
                EUIResLoadType.Resources => GetResourcesPath(assetPath, scriptGenerateData, defaultPath),
                EUIResLoadType.AssetBundle => GetAssetBundlePath(assetPath, scriptGenerateData, defaultPath),
                _ => defaultPath
            };
        }

        private static string GetResourcesPath(string assetPath, UIScriptGenerateData scriptGenerateData, string defaultPath)
        {
            var resourcesRoot = scriptGenerateData.UIPrefabRootPath;
            var relPath = GetResourcesRelativePath(assetPath, resourcesRoot);

            if (relPath == null)
            {
                Debug.LogWarning($"[UI生成] 资源 {assetPath} 不在配置的 Resources 根目录下: {resourcesRoot}");
                return defaultPath;
            }

            return relPath;
        }

        private static string GetAssetBundlePath(string assetPath, UIScriptGenerateData scriptGenerateData, string defaultPath)
        {
            try
            {
                var defaultPackage = YooAsset.Editor.AssetBundleCollectorSettingData.Setting.GetPackage("DefaultPackage");
                if (defaultPackage?.EnableAddressable == true)
                    return defaultPath;
            }
            catch
            {
                // 忽略异常，继续处理
            }

            var bundleRoot = scriptGenerateData.UIPrefabRootPath;
            if (!assetPath.StartsWith(bundleRoot, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[UI生成] 资源 {assetPath} 不在配置的 AssetBundle 根目录下: {bundleRoot}");
                return defaultPath;
            }

            return Path.ChangeExtension(assetPath, null);
        }

        private static string GetResourcesRelativePath(string assetPath, string resourcesRoot)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(resourcesRoot)) return null;

            assetPath = assetPath.Replace('\\', '/');
            resourcesRoot = resourcesRoot.Replace('\\', '/');

            if (!assetPath.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            var relPath = assetPath.Substring(resourcesRoot.Length).TrimStart('/');
            return Path.ChangeExtension(relPath, null);
        }

        public void WriteUIScriptContent(string className, string scriptContent, UIScriptGenerateData scriptGenerateData)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentNullException(nameof(className));
            if (scriptContent == null) throw new ArgumentNullException(nameof(scriptContent));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            var scriptFolderPath = scriptGenerateData.GenerateHolderCodePath;
            var scriptFilePath = Path.Combine(scriptFolderPath, $"{className}.cs");

            Directory.CreateDirectory(scriptFolderPath);

            if (File.Exists(scriptFilePath) && IsContentUnchanged(scriptFilePath, scriptContent))
            {
                UIScriptGeneratorHelper.BindUIScript();
                return;
            }

            File.WriteAllText(scriptFilePath, scriptContent, Encoding.UTF8);
            AssetDatabase.Refresh();
        }

        private static bool IsContentUnchanged(string filePath, string newContent)
        {
            var oldText = File.ReadAllText(filePath, Encoding.UTF8);
            return oldText.Equals(newContent, StringComparison.Ordinal);
        }

        public bool CheckCanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null || scriptGenerateData == null) return false;

            var assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            var isValidPath = assetPath.StartsWith(scriptGenerateData.UIPrefabRootPath, StringComparison.OrdinalIgnoreCase);

            if (!isValidPath)
            {
                Debug.LogWarning($"UI存储位置与配置生成规则不符合 请检查对应配置的UIPrefabRootPath\n[AssetPath]{assetPath}\n[ConfigPath]{scriptGenerateData.UIPrefabRootPath}");
            }

            return isValidPath;
        }

        public string GetReferenceNamespace(List<UIBindData> uiBindDatas)
        {
            var namespaceSet = new HashSet<string>(StringComparer.Ordinal) { "UnityEngine" };

            if (uiBindDatas?.Any(d => d.BindType == EBindType.ListCom) == true)
            {
                namespaceSet.Add("System.Collections.Generic");
            }

            uiBindDatas?
                .Where(bindData => bindData?.Objs?.FirstOrDefault() != null)
                .Select(bindData => bindData.GetFirstOrDefaultType().Namespace)
                .Where(ns => !string.IsNullOrEmpty(ns))
                .ToList()
                .ForEach(ns => namespaceSet.Add(ns));

            return string.Join(Environment.NewLine, namespaceSet.Select(ns => $"using {ns};"));
        }

        public string GetVariableContent(List<UIBindData> uiBindDatas)
        {
            if (uiBindDatas == null || uiBindDatas.Count == 0) return string.Empty;

            var variableBuilder = new StringBuilder();
            var variables = uiBindDatas
                .Where(b => b != null && !string.IsNullOrEmpty(b.Name))
                .Select(b => GenerateVariableDeclaration(b))
                .Where(declaration => !string.IsNullOrEmpty(declaration));

            return string.Join("\n\n", variables);
        }

        private string GenerateVariableDeclaration(UIBindData bindData)
        {
            var variableName = bindData.Name;
            var publicName = GetPublicComponentByNameRule(variableName);
            var firstType = bindData.GetFirstOrDefaultType();
            var typeName = firstType?.Name ?? "Component";

            var declaration = new StringBuilder();
            declaration.AppendLine("\t\t[SerializeField]");

            switch (bindData.BindType)
            {
                case EBindType.None:
                case EBindType.Widget:
                    declaration.AppendLine($"\t\tprivate {typeName} {variableName};");
                    declaration.Append($"\t\tpublic {typeName} {publicName} => {variableName};");
                    break;

                case EBindType.ListCom:
                    var count = Math.Max(0, bindData.Objs?.Count ?? 0);
                    declaration.AppendLine($"\t\tprivate {typeName}[] {variableName} = new {typeName}[{count}];");
                    declaration.Append($"\t\tpublic {typeName}[] {publicName} => {variableName};");
                    break;
            }

            return declaration.ToString();
        }
    }
}
