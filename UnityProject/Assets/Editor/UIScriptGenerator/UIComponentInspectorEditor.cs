#if UNITY_EDITOR

using System.Collections.Generic;
using System.Linq;
using TEngine.Editor.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GameLogic
{
    [CustomEditor(typeof(UIBindComponent))]
    public class UIComponentInspectorEditor : Editor
    {
        private UIBindComponent m_uiBindComponent;
        private ReorderableList m_reorderableList;

        private SerializedProperty m_componentsProperty;
        private SerializedProperty m_genCodePath;
        private SerializedProperty m_isGenImpClass;
        private SerializedProperty m_impCodePath;
        private SerializedProperty m_className;
        private SerializedProperty m_uiType;

        private List<UIGenType> m_uiGenTypes = new List<UIGenType>();
        private string[] m_uiTypeOptions;
        private int m_selectedIndex = 0;

        private void OnEnable()
        {
            m_uiBindComponent = (UIBindComponent)target;
            m_componentsProperty = serializedObject.FindProperty("m_components");
            m_genCodePath = serializedObject.FindProperty("genCodePath");
            m_isGenImpClass = serializedObject.FindProperty("isGenImpClass");
            m_impCodePath = serializedObject.FindProperty("impCodePath");
            m_className = serializedObject.FindProperty("className");
            m_uiType = serializedObject.FindProperty("uiType");

            serializedObject.Update();
            // 仅在首次（为空）时初始化，避免覆盖用户在 UIComponentEditor.cs 中配置的持久化数据
            if (string.IsNullOrEmpty(m_className.stringValue))
            {
                m_className.stringValue = target.name;
            }
            if (string.IsNullOrEmpty(m_genCodePath.stringValue))
            {
                m_genCodePath.stringValue = ScriptGeneratorSetting.GetGenCodePath();
            }
            if (string.IsNullOrEmpty(m_impCodePath.stringValue))
            {
                m_impCodePath.stringValue = ScriptGeneratorSetting.GetImpCodePath();
            }
            serializedObject.ApplyModifiedProperties();
            CreateReorderableList();
        }

        private void CreateReorderableList()
        {
            m_reorderableList = new ReorderableList(serializedObject, m_componentsProperty, true, true, true, true);
            m_reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                float width = rect.width - 20;
                float indexWidth = 90f;
                float nameWidth = 150f;
                float componentWidth = width - indexWidth - nameWidth - 15f;

                EditorGUI.LabelField(new Rect(rect.x, rect.y, indexWidth, rect.height), "序号");
                EditorGUI.LabelField(new Rect(rect.x + indexWidth, rect.y, nameWidth, rect.height), "对象名称");
                EditorGUI.LabelField(new Rect(rect.x + indexWidth + nameWidth, rect.y, componentWidth, rect.height),
                    "组件引用");
            };

            m_reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                SerializedProperty element = m_componentsProperty.GetArrayElementAtIndex(index);
                Component component = element.objectReferenceValue as Component;

                float height = EditorGUIUtility.singleLineHeight;
                float padding = 2f;
                float indexWidth = 70f;
                float nameWidth = 150f;
                float componentWidth = rect.width - indexWidth - nameWidth - 10f;

                // 序号（不可编辑）
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.LabelField(new Rect(rect.x, rect.y + padding, indexWidth, height), $"【{index}】");
                EditorGUI.EndDisabledGroup();

                // 对象名称（不可编辑）
                EditorGUI.BeginDisabledGroup(true);
                string objectName = component != null ? component.gameObject.name : "Null Reference";
                EditorGUI.TextField(new Rect(rect.x + indexWidth, rect.y + padding, nameWidth, height), objectName);
                EditorGUI.EndDisabledGroup();

                // 组件引用（可编辑）
                EditorGUI.PropertyField(
                    new Rect(rect.x + indexWidth + nameWidth + 8, rect.y + padding, componentWidth, height),
                    element, GUIContent.none);
            };

            m_reorderableList.elementHeight = EditorGUIUtility.singleLineHeight + 4f;
            m_reorderableList.onAddCallback = (ReorderableList list) =>
            {
                m_componentsProperty.arraySize++;
                serializedObject.ApplyModifiedProperties();
            };
            m_reorderableList.onRemoveCallback = (ReorderableList list) =>
            {
                if (list.index >= 0 && list.index < m_componentsProperty.arraySize)
                {
                    m_componentsProperty.DeleteArrayElementAtIndex(list.index);
                    serializedObject.ApplyModifiedProperties();
                }
            };
            m_reorderableList.drawNoneElementCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "列表为空 - 点击上方重新绑定进行组件重绑");
            };
        }

        private void RemoveComponentAtIndex(int index)
        {
            if (index >= 0 && index < m_componentsProperty.arraySize)
            {
                m_componentsProperty.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();

                // 重新选择相邻的元素，避免选择丢失
                if (m_reorderableList.index >= m_componentsProperty.arraySize)
                {
                    m_reorderableList.index = m_componentsProperty.arraySize - 1;
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawTopButtons();
            EditorGUILayout.Space();
            m_reorderableList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopButtons()
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("重新绑定组件", GUILayout.Height(25)))
                {
                    RebindComponents();
                }
                if (GUILayout.Button("生成脚本", GUILayout.Height(25)))
                {
                    // RemoveNullComponents();
                    ScriptGenerator.GenerateCSharpScript(true, false, true, m_genCodePath.stringValue,
                        m_className.stringValue, m_uiTypeOptions[m_selectedIndex], m_isGenImpClass.boolValue, m_impCodePath.stringValue);
                }
                if (GUILayout.Button("生成UniTask脚本", GUILayout.Height(25)))
                {
                    // RemoveNullComponents();
                    ScriptGenerator.GenerateCSharpScript(true, true, true, m_genCodePath.stringValue,
                        m_className.stringValue, m_uiTypeOptions[m_selectedIndex], m_isGenImpClass.boolValue, m_impCodePath.stringValue);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("生成标准版绑定代码", GUILayout.Height(25)))
                {
                    ScriptGenerator.GenerateCSharpScript(false);
                }
                if (GUILayout.Button("生成UniTask代码", GUILayout.Height(25)))
                {
                    ScriptGenerator.GenerateCSharpScript(false, true);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("HelpBox");
            {
                // 绘制序列化属性字段
                EditorGUILayout.LabelField("代码生成设置", EditorStyles.boldLabel);

                m_uiGenTypes = ScriptGeneratorSetting.Instance.UIGenTypes;
                // 获取所有的 uiTypeName
                m_uiTypeOptions = m_uiGenTypes.Select(t => t.uiTypeName).ToArray();
                // 确保有选项时才显示 Popup
                if (m_uiTypeOptions.Length > 0)
                {
                    // 优先根据 uiType(字符串) 还原选择，避免规则顺序变化导致 index 错位
                    if (m_uiType != null && !string.IsNullOrEmpty(m_uiType.stringValue))
                    {
                        int indexByName = System.Array.IndexOf(m_uiTypeOptions, m_uiType.stringValue);
                        if (indexByName >= 0)
                        {
                            m_selectedIndex = indexByName;
                        }
                    }

                    // index 越界保护（规则数量变更/配置变更）
                    m_selectedIndex = Mathf.Clamp(m_selectedIndex, 0, m_uiTypeOptions.Length - 1);

                    int newIndex = EditorGUILayout.Popup("UI类型", m_selectedIndex, m_uiTypeOptions);
                    if (newIndex != m_selectedIndex)
                    {
                        m_selectedIndex = newIndex;
                    }

                    if (m_uiType != null)
                    {
                        string selectedTypeName = m_uiTypeOptions[m_selectedIndex];
                        if (m_uiType.stringValue != selectedTypeName)
                        {
                            m_uiType.stringValue = selectedTypeName;
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("请先去UISetting中设置UI类型规则", MessageType.Info);
                }
                // EditorGUILayout.PropertyField(m_uiType, new GUIContent("UI类型"));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(m_className, new GUIContent("类名"));
                if(GUILayout.Button("物体名", GUILayout.Width(60), GUILayout.Height(18)))
                {
                    m_className.stringValue = target.name;
                }
                EditorGUILayout.EndHorizontal();

                DrawFolderField("生成组件代码路径", string.Empty, m_genCodePath);

                // 是否生成实现类
                EditorGUILayout.PropertyField(m_isGenImpClass, new GUIContent("生成实现类", "是否同时生成实现类文件"));

                // 如果启用了生成实现类，显示实现类路径
                if (m_isGenImpClass.boolValue)
                {
                    DrawFolderField("生成实现类路径", string.Empty, m_impCodePath);
                }
            }
            EditorGUILayout.EndVertical();

            if (serializedObject.hasModifiedProperties)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void RebindComponents()
        {
            if (m_uiBindComponent == null) return;
            m_uiBindComponent.Clear();
            ScriptGenerator.GenerateUIComponentScript();
            ScriptGenerator.GenerateCSharpScript(false);
        }

        private void RemoveNullComponents()
        {
            if (m_uiBindComponent == null) return;

            for (int i = m_componentsProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = m_componentsProperty.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == null)
                {
                    m_componentsProperty.DeleteArrayElementAtIndex(i);
                }
            }
            serializedObject.ApplyModifiedProperties();
            Debug.Log($"已清除空引用，剩余组件数量: {m_componentsProperty.arraySize}");
        }

        private static void DrawFolderField(string label, string labelIcon, SerializedProperty pathProperty)
        {
            EditorGUILayout.BeginHorizontal();
            string path = pathProperty.stringValue;
            var buttonGUIContent = new GUIContent("选择", EditorGUIUtility.IconContent("Folder Icon").image);

            if (!string.IsNullOrEmpty(labelIcon))
            {
                var labelGUIContent = new GUIContent(" " + label, EditorGUIUtility.IconContent(labelIcon).image);
                path = EditorGUILayout.TextField(labelGUIContent, path);
            }
            else
            {
                path = EditorGUILayout.TextField(label, path);
            }
            if (path != pathProperty.stringValue)
            {
                pathProperty.stringValue = path;
            }
            if (GUILayout.Button(buttonGUIContent, GUILayout.Width(60), GUILayout.Height(20)))
            {
                string currentPath = pathProperty.stringValue;

                EditorApplication.delayCall += () =>
                {
                    var newPath = EditorUtility.OpenFolderPanel(label, currentPath, string.Empty);
                    if (string.IsNullOrEmpty(newPath))
                    {
                        Debug.LogError("路径不能为空" + newPath);
                        string defaultPath = GetDefaultPathByPropertyType(pathProperty);
                        pathProperty.stringValue = defaultPath;

                    }
                    else
                    {
                        if (newPath.StartsWith(Application.dataPath))
                        {
                            newPath = newPath.Replace(Application.dataPath, "Assets");
                        }
                        pathProperty.stringValue = newPath;
                    }
                    pathProperty.serializedObject.ApplyModifiedProperties();
                };
            }
            if (GUILayout.Button("默认", GUILayout.Width(60), GUILayout.Height(20)))
            {
                // 根据不同的属性类型获取不同的默认路径
                string defaultPath = GetDefaultPathByPropertyType(pathProperty);
                if (!string.IsNullOrEmpty(defaultPath))
                {
                    pathProperty.stringValue = defaultPath;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string GetDefaultPathByPropertyType(SerializedProperty property)
        {
            // 通过属性名称来判断类型
            switch (property.name)
            {
                case "m_genCodePath":
                case "genCodePath":
                    return ScriptGeneratorSetting.GetGenCodePath();

                case "m_impCodePath":
                case "impCodePath":
                    return ScriptGeneratorSetting.GetImpCodePath();

                default:
                    // 如果无法识别属性类型，返回空字符串或当前值
                    Debug.LogWarning($"未知的属性类型: {property.name}，无法获取默认路径");
                    return property.stringValue;
            }
        }
    }
#endif
}