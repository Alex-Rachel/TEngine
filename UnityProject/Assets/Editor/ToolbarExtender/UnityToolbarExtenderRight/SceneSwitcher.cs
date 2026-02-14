#if !UNITY_6000_3_OR_NEWER

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace TEngine
{
    /// <summary>
    /// SceneSwitcher
    /// </summary>
    public partial class UnityToolbarExtenderRight
    {
        private static List<(string sceneName, string scenePath)> m_InitScenes;
        private static List<(string sceneName, string scenePath)> m_DefaultScenes;
        private static List<(string sceneName, string scenePath)> m_OtherScenes;

        private static string initScenePath = "Assets/Scenes";
        private static string defaultScenePath = "Assets/AssetRaw/Scenes";
        
        static void UpdateScenes()
        {
            // 获取初始化场景和默认场景
            m_InitScenes = SceneSwitcher.GetScenesInPath(initScenePath);
            m_DefaultScenes = SceneSwitcher.GetScenesInPath(defaultScenePath);

            // 获取所有场景路径
            List<(string sceneName, string scenePath)> allScenes = SceneSwitcher.GetAllScenes();

            // 排除初始化场景和默认场景，获得其他场景
            m_OtherScenes = new List<(string sceneName, string scenePath)>(allScenes);
            m_OtherScenes.RemoveAll(scene =>
                m_InitScenes.Exists(init => init.scenePath == scene.scenePath) ||
                m_DefaultScenes.Exists(defaultScene => defaultScene.scenePath == scene.scenePath)
            );
        }

        static void OnToolbarGUI_SceneSwitch()
        {
            // 如果没有场景，直接返回
            if (m_InitScenes.Count == 0 && m_DefaultScenes.Count == 0 && m_OtherScenes.Count == 0)
                return;

            // 获取当前场景名称
            string currentSceneName = SceneManager.GetActiveScene().name;
            EditorGUILayout.LabelField("当前场景:", GUILayout.Width(52));

            // 使用 GUI.skin.button.CalcSize 计算文本的精确宽度
            GUIContent content = new GUIContent(currentSceneName);
            Vector2 textSize = GUI.skin.button.CalcSize(content);

            // 设置按钮宽度为文本的宽度，并限制最大值
            float buttonWidth = textSize.x;
            
            // 创建弹出菜单
            var menu = new GenericMenu();

            // 添加 "初始化路径" 下的场景按钮
            AddScenesToMenu(m_InitScenes, "初始化场景", menu);

            // 添加 "默认路径" 下的场景按钮
            AddScenesToMenu(m_DefaultScenes, "默认场景", menu);

            // 添加 "其他路径" 下的场景按钮
            AddScenesToMenu(m_OtherScenes, "其他场景", menu);

            // 自定义GUIStyle
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft // 左对齐
            };

            // 在工具栏中显示菜单
            if (GUILayout.Button(currentSceneName, buttonStyle, GUILayout.Width(buttonWidth)))
            {
                menu.ShowAsContext();
            }
        }

        private static void AddScenesToMenu(List<(string sceneName, string scenePath)> scenes, string category, GenericMenu menu)
        {
            if (scenes.Count > 0)
            {
                foreach (var scene in scenes)
                {
                    menu.AddItem(new GUIContent($"{category}/{scene.sceneName}"), false, () => SwitchScene(scene.scenePath));
                }
            }
        }

        static void SwitchScene(string scenePath)
        {
            // 保证场景是否保存
            if (SceneSwitcher.PromptSaveCurrentScene())
            {
                // 保存场景后切换到新场景
                EditorSceneManager.OpenScene(scenePath);
            }
        }
    }

    static class SceneSwitcher
    {
        public static bool PromptSaveCurrentScene()
        {
            // 检查当前场景是否有未保存的更改
            if (SceneManager.GetActiveScene().isDirty)
            {
                // 弹出保存对话框
                bool saveScene = EditorUtility.DisplayDialog(
                    "是否保存当前场景",
                    "当前场景有未保存的更改. 你是否想保存?",
                    "保存",
                    "取消"
                );

                // 如果选择保存，保存场景
                if (saveScene)
                {
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                }
                else
                {
                    // 如果选择取消，跳转到目标场景
                    return false; // 表示取消
                }

                return true;
            }

            return true; // 如果场景没有更改，直接返回 true
        }

        public static List<(string sceneName, string scenePath)> GetScenesInPath(string path)
        {
            var scenes = new List<(string sceneName, string scenePath)>();

            // 查找指定路径下的所有场景文件
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { path });
            foreach (var guid in guids)
            {
                var scenePath = AssetDatabase.GUIDToAssetPath(guid);
                var sceneName = Path.GetFileNameWithoutExtension(scenePath);
                scenes.Add((sceneName, scenePath));
            }

            return scenes;
        }

        // 获取项目中所有场景
        public static List<(string sceneName, string scenePath)> GetAllScenes()
        {
            var allScenes = new List<(string sceneName, string scenePath)>();

            // 查找项目中所有场景文件
            string[] guids = AssetDatabase.FindAssets("t:Scene");
            foreach (var guid in guids)
            {
                var scenePath = AssetDatabase.GUIDToAssetPath(guid);
                var sceneName = Path.GetFileNameWithoutExtension(scenePath);
                allScenes.Add((sceneName, scenePath));
            }

            return allScenes;
        }
    }
}
#endif

#endif