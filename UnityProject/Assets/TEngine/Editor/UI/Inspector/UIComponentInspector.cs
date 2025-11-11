using TEngine.Editor.Inspector;
using TEngine;
using UnityEditor;
using UnityEngine;

namespace TEngine.UI.Editor
{
    [CustomEditor(typeof(UIComponent))]
    internal sealed class UIComponentInspector : GameFrameworkInspector
    {
        private SerializedProperty uiRoot;
        private SerializedProperty _isOrthographic;


        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                if (uiRoot.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("uiroot can not be null!", MessageType.Error);
                }

                EditorGUILayout.BeginHorizontal();

                GameObject rootPrefab = (GameObject)EditorGUILayout.ObjectField("UI根预设", uiRoot.objectReferenceValue, typeof(GameObject), false);

                if (rootPrefab != uiRoot.objectReferenceValue)
                {
                    uiRoot.objectReferenceValue = rootPrefab;
                }

                if (uiRoot.objectReferenceValue == null)
                {
                    if (GUILayout.Button("设置默认"))
                    {
                        GameObject defaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UIGlobalPath.UIPrefabPath);
                        uiRoot.objectReferenceValue = defaultPrefab;
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(_isOrthographic);
            }
            EditorGUI.EndDisabledGroup();
            serializedObject.ApplyModifiedProperties();
            Repaint();
        }


        private void OnEnable()
        {
            uiRoot = serializedObject.FindProperty("uiRoot");
            _isOrthographic = serializedObject.FindProperty("_isOrthographic");
        }
    }
}
