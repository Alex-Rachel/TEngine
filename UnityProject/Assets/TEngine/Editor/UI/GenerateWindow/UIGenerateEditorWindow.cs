using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TEngine.UI.Editor
{
    public class UIGenerateEditorWindow : EditorWindow
    {
        private GameObject selectedObject;
        private string[] menuItems;

        [MenuItem("GameObject/UI生成绑定", priority = 10)]
        public static void ShowWindow()
        {
            GameObject selectedObject = Selection.gameObjects.FirstOrDefault();
            if (selectedObject == null) return;

            var uiScriptConfigs = UIGenerateConfiguration.Instance.UIScriptGenerateConfigs;
            if (uiScriptConfigs == null || uiScriptConfigs.Count == 0) return;

            var window = GetWindow<UIGenerateEditorWindow>(true, "UI 生成绑定", true);
            window.selectedObject = selectedObject;

            window.menuItems = uiScriptConfigs.Select(config => $"{config.ProjectName}").ToArray();

            var windowWidth = 300f;
            var windowHeight = Mathf.Max(1, window.menuItems.Length) * 35f + 10f;


            Vector3 objectWorldPosition = selectedObject.transform.position;
            Vector2 screenPoint;

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {

                Vector2 guiPointInSceneView = HandleUtility.WorldToGUIPoint(objectWorldPosition);

                screenPoint = new Vector2(sceneView.position.x + guiPointInSceneView.x,
                                          sceneView.position.y + guiPointInSceneView.y);
            }
            else if (EditorWindow.mouseOverWindow != null)
            {

                Vector2 guiPoint = HandleUtility.WorldToGUIPoint(objectWorldPosition);
                var host = EditorWindow.mouseOverWindow;
                screenPoint = new Vector2(host.position.x + guiPoint.x, host.position.y + guiPoint.y);
            }
            else
            {

                var mainDisplay = Display.displays.Length > 0 ? Display.displays[0] : null;
                float centerX = (mainDisplay != null) ? (mainDisplay.systemWidth / 2f) : (Screen.width / 2f);
                float centerY = (mainDisplay != null) ? (mainDisplay.systemHeight / 2f) : (Screen.height / 2f);
                screenPoint = new Vector2(centerX, centerY);
            }


            Vector2 windowPosition = new Vector2(screenPoint.x, screenPoint.y - windowHeight - 5f);


            float screenW = Mathf.Max(100, Display.main.systemWidth);
            float screenH = Mathf.Max(100, Display.main.systemHeight);
            if (windowPosition.x + windowWidth > screenW) windowPosition.x = screenW - windowWidth - 5f;
            if (windowPosition.x < 5f) windowPosition.x = 5f;
            if (windowPosition.y < 5f) windowPosition.y = 5f;
            if (windowPosition.y + windowHeight > screenH) windowPosition.y = screenH - windowHeight - 5f;

            window.minSize = new Vector2(windowWidth, windowHeight);
            window.maxSize = new Vector2(windowWidth, windowHeight);

            window.position = new Rect(windowPosition, new Vector2(windowWidth, windowHeight));
            window.ShowPopup();
        }

        private void OnGUI()
        {
            GUILayout.Space(5);
            if (menuItems == null || menuItems.Length == 0)
            {
                EditorGUILayout.LabelField("没有可用配置");
                return;
            }

            foreach (var item in menuItems)
            {
                if (GUILayout.Button(item, EditorStyles.toolbarButton, GUILayout.Height(28)))
                {
                    GenerateScriptForConfig(selectedObject, item);
                    Close();
                }

                GUILayout.Space(6);
            }
        }

        private void GenerateScriptForConfig(GameObject selectedObject, string itemName)
        {
            var uiScriptConfigs = UIGenerateConfiguration.Instance.UIScriptGenerateConfigs;
            var config = uiScriptConfigs.FirstOrDefault(c => $"{c.ProjectName}" == itemName);

            if (config != null)
            {
                UIScriptGeneratorHelper.GenerateAndAttachScript(selectedObject, config);
            }
            else
            {
                Debug.LogWarning("Configuration not found for item: " + itemName);
            }
        }
    }
}
