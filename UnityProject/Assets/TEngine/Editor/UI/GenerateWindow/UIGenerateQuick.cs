using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TEngine.UI.Editor
{
    public static class UIGenerateQuick
    {
        [MenuItem("GameObject/UI生成绑定", priority = 10)]
        public static void ShowWindow()
        {
            GameObject selectedObject = Selection.gameObjects.FirstOrDefault();
            if (selectedObject == null)
            {
                Debug.LogError("没有选中物体!");
                return;
            }

            var uiScriptConfigs = UIGenerateConfiguration.Instance.UIScriptGenerateConfigs;
            if (uiScriptConfigs == null || uiScriptConfigs.Count == 0)
            {
                Debug.Log("没有UI生成配置 请前往UISettingWindow添加");
                return;
            }

            foreach (var config in uiScriptConfigs)
            {
                if (CheckCanGenerate(selectedObject, config))
                {
                    UIScriptGeneratorHelper.GenerateUIBindScript(selectedObject, config);
                    return;
                }
            }

            Debug.Log("没有找到符合规则路径的生成配置 请检查!");
        }

        public static bool CheckCanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null || scriptGenerateData == null) return false;

            string assetPath = GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return false; // 不在 Assets 下

            assetPath = assetPath.Replace('\\', '/');
            bool result = assetPath.StartsWith(scriptGenerateData.UIPrefabRootPath, StringComparison.OrdinalIgnoreCase);
            return result;
        }

        public static string GetPrefabAssetPath(GameObject go)
        {
            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefabAsset != null)
                return AssetDatabase.GetAssetPath(prefabAsset);

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.IsPartOfPrefabContents(go))
                return prefabStage.assetPath;

            return null;
        }
    }
}
