using TEngine;
using UnityEditor;
using UnityEngine;

public static class TEngineSettingsProvider  
{  
    [MenuItem("TEngine/Settings/TEngine UpdateSettings", priority = -1)]
    public static void OpenSettings() => SettingsService.OpenProjectSettings("Project/TEngine/UpdateSettings");
    
    private const string SettingsPath = "Project/TEngine/UpdateSettings";  

    [SettingsProvider]  
    public static SettingsProvider CreateMySettingsProvider()  
    {  
        return new SettingsProvider(SettingsPath, SettingsScope.Project)  
        {  
            label = "TEngine/UpdateSettings",  
            guiHandler = (searchContext) =>
            {
                DrawHybridCLRSettings();
                var settings = Settings.UpdateSetting;  
                var serializedObject = new SerializedObject(settings);  

                EditorGUILayout.PropertyField(serializedObject.FindProperty("projectName"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("HotUpdateAssemblies"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AOTMetaAssemblies"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("LogicMainDllName"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AssemblyTextAssetExtension"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AssemblyTextAssetPath"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("UpdateStyle"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ResDownLoadPath"));  
                EditorGUILayout.PropertyField(serializedObject.FindProperty("FallbackResDownLoadPath"));  
                serializedObject.ApplyModifiedProperties();  
            },  
            keywords = new[] { "TEngine", "Settings", "Custom" }  
        };  
    }

    private static void DrawHybridCLRSettings()
    {
        EditorGUILayout.BeginVertical("HelpBox");
        {
            EditorGUILayout.LabelField(new GUIContent("HybridCLR 设置", "启用或禁用热更新功能"), EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            {
                var originalColor = GUI.color;

                // 启用按钮
                GUI.color = new Color(0.2f, 0.8f, 0.3f, 1f);
                if (GUILayout.Button(new GUIContent("启用 HybridCLR", "启用热更新功能"),
                        GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("启用 HybridCLR", "确定要启用 HybridCLR 热更新功能吗？", "确定", "取消"))
                    {
                        BuildDLLCommand.EnableHybridCLR();
                        Debug.Log("HybridCLR 已启用");
                    }
                }

                // 禁用按钮
                GUI.color = new Color(1f, 0.4f, 0.3f, 1f);
                if (GUILayout.Button(new GUIContent("禁用 HybridCLR", "禁用热更新功能"),
                        GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("禁用 HybridCLR", "确定要禁用 HybridCLR 热更新功能吗？", "确定", "取消"))
                    {
                        BuildDLLCommand.DisableHybridCLR();
                        Debug.Log("HybridCLR 已禁用");
                    }
                }

                GUI.color = originalColor;
            }
            EditorGUILayout.EndHorizontal();

            // 状态显示
            bool isHybridCLREnabled =
#if ENABLE_HYBRIDCLR
                true;
#else
                false;
#endif
            string statusText = isHybridCLREnabled ? "HybridCLR 已启用" : "HybridCLR 已禁用";
            MessageType statusType = isHybridCLREnabled ? MessageType.Info : MessageType.Warning;

            EditorGUILayout.HelpBox(statusText, statusType);
        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(10);
    }
}  