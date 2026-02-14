using System;
using System.Collections.Generic;
using TEngine.Editor.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public static class TEngineUISettingsProvider
{
    private static ReorderableList _reorderableList;

    [MenuItem("TEngine/Settings/TEngineUISettings", priority = -1)]
    public static void OpenSettings() => SettingsService.OpenProjectSettings("Project/TEngine/UISettings");

    private const string SettingsPath = "Project/TEngine/UISettings";

    // UI状态
    private static bool m_showBasicSettings = true;
    private static bool m_showCodeSettings = true;
    private static bool m_showRuleSettings = true;
    private static bool m_showuiGenTypeList = true;
    private static ReorderableList m_reorderableList;
    private static ReorderableList m_uiGenTypeRecoedList;
    private static Vector2 m_scrollPosition;

    private static SerializedProperty m_useBindComponentProperty;
    private static SerializedProperty m_nullableEnable;
    private static SerializedObject m_serializedObject;

    [SettingsProvider]
    public static SettingsProvider CreateUIGeneratorSettingsProvider()
    {
        return new SettingsProvider(SettingsPath, SettingsScope.Project)
        {
            label = "UI代码生成器",
            activateHandler = (searchContext, rootElement) =>
            {
                var uiScriptGeneratorSettings = ScriptGeneratorSetting.Instance;
                m_serializedObject = new SerializedObject(uiScriptGeneratorSettings);
                m_useBindComponentProperty = m_serializedObject.FindProperty("useBindComponent");
                m_nullableEnable = m_serializedObject.FindProperty("NullableEnable");
            },
            guiHandler = (searchContext) =>
            {
                var uiScriptGeneratorSettings = ScriptGeneratorSetting.Instance;

                if (uiScriptGeneratorSettings == null)
                {
                    EditorGUILayout.HelpBox("未找到UI代码生成器设置文件", MessageType.Error);
                    return;
                }

                // 绘制标题区域
                DrawHeader();

                m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
                {
                    DrawBasicSettings(m_serializedObject);
                    DrawCodeSettings(m_serializedObject);
                    DrawUIGenTypesSettings(m_serializedObject);
                    DrawRuleSettings(m_serializedObject);
                    DrawStatistics(m_serializedObject);
                }
                EditorGUILayout.EndScrollView();

                // DrawActionButtons(serializedObject);
            },
            keywords = new HashSet<string>(new[]
            {
                "TEngine", "Settings", "Custom", "UISettings"
            })
        };
    }

    private static void DrawHeader()
    {
        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        var titleStyle = new GUIStyle(EditorStyles.largeLabel)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        EditorGUILayout.LabelField(new GUIContent("TEngine UI代码生成器", "UI Script Generator Configuration"),
            titleStyle, GUILayout.Height(30));

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // 副标题
        var subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) }
        };

        EditorGUILayout.LabelField("自动化UI脚本生成和组件绑定配置", subtitleStyle);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Space(10);
    }

    private static void DrawBasicSettings(SerializedObject serializedObject)
    {
        m_showBasicSettings = EditorGUILayout.BeginFoldoutHeaderGroup(m_showBasicSettings,
            new GUIContent("基础设置", "UI根路径和基本配置"));

        if (m_showBasicSettings)
        {
            EditorGUILayout.BeginVertical("HelpBox");
            {
                // 组件绑定设置
                EditorGUILayout.LabelField("组件绑定设置", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(m_useBindComponentProperty,
                    new GUIContent("启用组件绑定", "是否自动生成组件绑定代码"));
                EditorGUILayout.PropertyField(m_nullableEnable,
                    new GUIContent("支持Nullable"));

                EditorGUILayout.Space(3);

                if (m_useBindComponentProperty.boolValue)
                {
                    EditorGUILayout.HelpBox("已启用自动组件绑定功能", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("组件绑定功能已禁用", MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        GUILayout.Space(8);
    }

    private static void DrawCodeSettings(SerializedObject serializedObject)
    {
        m_showCodeSettings = EditorGUILayout.BeginFoldoutHeaderGroup(m_showCodeSettings,
            new GUIContent("代码设置", "代码生成路径和命名规范"));

        if (m_showCodeSettings)
        {
            EditorGUILayout.BeginVertical("HelpBox");
            {
                // var useBindComponentProperty = serializedObject.FindProperty("useBindComponent");

                if (m_useBindComponentProperty.boolValue)
                {
                    // 代码生成路径
                    EditorGUILayout.LabelField("自动代码生成路径", EditorStyles.boldLabel);
                    var genCodePathProperty = serializedObject.FindProperty("genCodePath");
                    genCodePathProperty.stringValue = DrawEnhancedFolderField(
                        "组件代码文件生成路径",
                        "组件代码将生成到此目录",
                        genCodePathProperty.stringValue);
                    var impCodePathProperty = serializedObject.FindProperty("impCodePath");
                    impCodePathProperty.stringValue = DrawEnhancedFolderField(
                        "实现类代码文件生成路径",
                        "实现类代码将生成到此目录",
                        impCodePathProperty.stringValue);

                    EditorGUILayout.Space(10);
                }

                EditorGUILayout.Space(5);

                // 命名空间和代码风格
                EditorGUILayout.LabelField("代码结构设置", EditorStyles.boldLabel);

                var nameSpaceProperty = serializedObject.FindProperty("_namespace");
                EditorGUILayout.PropertyField(nameSpaceProperty,
                    new GUIContent("命名空间", "生成代码的命名空间"));

                var widgetNameProperty = serializedObject.FindProperty("_widgetName");
                EditorGUILayout.PropertyField(widgetNameProperty,
                    new GUIContent("Widget基类名", "UI组件的基类名称"));

                var codeStyleProperty = serializedObject.FindProperty("CodeStyle");
                EditorGUILayout.PropertyField(codeStyleProperty,
                    new GUIContent("代码风格", "生成的代码风格模板"));

                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox("配置代码的命名空间和基础结构", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        GUILayout.Space(8);
    }

    private static void DrawUIGenTypesSettings(SerializedObject serializedObject)
    {
        if (m_useBindComponentProperty.boolValue)
        {
            m_showuiGenTypeList = EditorGUILayout.BeginFoldoutHeaderGroup(m_showuiGenTypeList,
                new GUIContent("UI类型"));

            if (m_showuiGenTypeList)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    // 规则说明
                    EditorGUILayout.HelpBox(
                        "• 非泛型: public class TestWindow : UIWindow\n" +
                        "• 泛型: public class TestEventItem : UIEventItem<TestEventItem>",
                        MessageType.Info);

                    EditorGUILayout.Space(5);

                    DrawUIGenTypesReorderableList(serializedObject);

                    EditorGUILayout.Space(3);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(8);
        }
    }

    private static void DrawRuleSettings(SerializedObject serializedObject)
    {
        m_showRuleSettings = EditorGUILayout.BeginFoldoutHeaderGroup(m_showRuleSettings,
            new GUIContent("生成规则", "UI元素到代码的映射规则"));

        if (m_showRuleSettings)
        {
            EditorGUILayout.BeginVertical("HelpBox");
            {
                EditorGUILayout.LabelField("脚本生成规则配置", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("配置UI元素名称与组件类型的映射规则", MessageType.Info);

                EditorGUILayout.Space(5);

                DrawEnhancedReorderableList(serializedObject);

                EditorGUILayout.Space(3);

                // 规则说明
                EditorGUILayout.HelpBox(
                    // "规则匹配优先级: 从上到下依次匹配\n" +
                    "• 命名前缀: UI元素名称前缀\n" +
                    "• 组件类型: 生成的组件类型\n" +
                    "• 是否Widget: 标记是否为独立Widget组件",
                    MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        GUILayout.Space(8);
    }

    private static void DrawStatistics(SerializedObject serializedObject)
    {
        EditorGUILayout.BeginVertical("Box");
        {
            EditorGUILayout.LabelField("配置概览", EditorStyles.boldLabel);

            var uiScriptGeneratorSettings = serializedObject.targetObject as ScriptGeneratorSetting;

            if (uiScriptGeneratorSettings != null)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("组件绑定:", GUILayout.Width(80));
                    string bindStatus = uiScriptGeneratorSettings.UseBindComponent ? "启用" : "禁用";
                    EditorGUILayout.LabelField(bindStatus, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("命名空间:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(uiScriptGeneratorSettings.Namespace ?? "未设置", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("生成规则:", GUILayout.Width(80));
                    int ruleCount = uiScriptGeneratorSettings.ScriptGenerateRule?.Count ?? 0;
                    EditorGUILayout.LabelField($"{ruleCount} 条", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("组件代码路径:", GUILayout.Width(80));
                    string genCodePath = string.IsNullOrEmpty(uiScriptGeneratorSettings.GenCodePath) ? "未设置" : uiScriptGeneratorSettings.GenCodePath;
                    EditorGUILayout.LabelField(genCodePath, EditorStyles.miniLabel);

                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("实现类代码路径:", GUILayout.Width(80));
                    string impCodePath = string.IsNullOrEmpty(uiScriptGeneratorSettings.ImpCodePath) ? "未设置" : uiScriptGeneratorSettings.ImpCodePath;
                    EditorGUILayout.LabelField(impCodePath, EditorStyles.miniLabel);

                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();
    }

    private static string DrawEnhancedFolderField(string label, string tooltip, string path)
    {
        EditorGUILayout.BeginVertical();
        {
            EditorGUILayout.BeginHorizontal();
            {
                // 标签
                EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.Width(140));

                // 路径字段
                path = EditorGUILayout.TextField(path, GUILayout.ExpandWidth(true));
                var buttonGUIContent = new GUIContent("选择", EditorGUIUtility.IconContent("Folder Icon").image);

                // 选择按钮
                if (GUILayout.Button(buttonGUIContent, GUILayout.Width(60), GUILayout.Height(18)))
                {
                    string newPath = EditorUtility.OpenFolderPanel(label, Application.dataPath, string.Empty);

                    if (string.IsNullOrEmpty(newPath))
                    {
                        Debug.LogError("路径不能为空");
                    }
                    else
                    {
                        if (newPath.StartsWith(Application.dataPath))
                        {
                            newPath = newPath.Replace(Application.dataPath, "Assets");
                        }

                        path = newPath;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 路径验证
            if (string.IsNullOrEmpty(path))
            {
                EditorGUILayout.HelpBox("路径不能为空", MessageType.Warning);
            }
        }
        EditorGUILayout.EndVertical();

        return path;
    }

    private static void DrawEnhancedReorderableList(SerializedObject serializedObject)
    {
        SerializedProperty ruleListProperty = serializedObject.FindProperty("scriptGenerateRule");
        if (ruleListProperty == null) return;

        if (m_reorderableList == null)
        {
            m_reorderableList = new ReorderableList(serializedObject, ruleListProperty, true, true, true, true)
            {
                drawHeaderCallback = (Rect rect) =>
                {
                    float padding = 5f;
                    float columnWidth = (rect.width - padding * 3) / 4f;

                    GUI.Label(new Rect(rect.x, rect.y, columnWidth, rect.height), "规则名称", EditorStyles.boldLabel);
                    GUI.Label(new Rect(rect.x + columnWidth + padding + 20, rect.y, columnWidth, rect.height), "UI元素名称前缀", EditorStyles.boldLabel);
                    GUI.Label(new Rect(rect.x + (columnWidth + padding) * 2 + 20, rect.y, columnWidth, rect.height), "组件类型", EditorStyles.boldLabel);
                    GUI.Label(new Rect(rect.x + (columnWidth + padding) * 3, rect.y, columnWidth, rect.height), "是否Widget", EditorStyles.boldLabel);
                },

                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    EditorGUI.BeginChangeCheck();
                    SerializedProperty element = ruleListProperty.GetArrayElementAtIndex(index);
                    rect.y += 2;

                    float padding = 5f;
                    float fieldHeight = EditorGUIUtility.singleLineHeight;
                    float columnWidth = (rect.width - padding * 3) / 4f;

                    // 规则名称（显示匹配示例）
                    SerializedProperty regexProperty = element.FindPropertyRelative("uiElementRegex");
                    string ruleName = GetRuleDisplayName(regexProperty.stringValue);
                    Rect nameRect = new Rect(rect.x, rect.y, columnWidth, fieldHeight);
                    EditorGUI.LabelField(nameRect, ruleName);

                    // 正则表达式
                    Rect regexRect = new Rect(rect.x + columnWidth + padding, rect.y, columnWidth, fieldHeight);
                    EditorGUI.PropertyField(regexRect, regexProperty, GUIContent.none);

                    // 组件类型
                    Rect componentRect = new Rect(rect.x + (columnWidth + padding) * 2, rect.y, columnWidth, fieldHeight);
                    SerializedProperty componentProperty = element.FindPropertyRelative("componentName");
                    EditorGUI.PropertyField(componentRect, componentProperty, GUIContent.none);

                    // 是否Widget
                    Rect widgetRect = new Rect(rect.x + (columnWidth + padding) * 3 + 10, rect.y, columnWidth, fieldHeight);
                    SerializedProperty widgetProperty = element.FindPropertyRelative("isUIWidget");
                    EditorGUI.PropertyField(widgetRect, widgetProperty, GUIContent.none);

                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(serializedObject.targetObject);
                        AssetDatabase.SaveAssets();
                    }
                },

                elementHeight = EditorGUIUtility.singleLineHeight + 6,

                onChangedCallback = (ReorderableList list) =>
                {
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(serializedObject.targetObject);
                },

                onAddCallback = (ReorderableList list) =>
                {
                    int newIndex = list.serializedProperty.arraySize;
                    list.serializedProperty.arraySize++;
                    list.index = newIndex;

                    // 设置默认值
                    SerializedProperty newElement = list.serializedProperty.GetArrayElementAtIndex(newIndex);
                    newElement.FindPropertyRelative("uiElementRegex").stringValue = "m_go";
                    newElement.FindPropertyRelative("componentName").enumValueIndex = 0;
                    newElement.FindPropertyRelative("isUIWidget").boolValue = false;

                    serializedObject.ApplyModifiedProperties();
                },

                onRemoveCallback = (ReorderableList list) =>
                {
                    if (EditorUtility.DisplayDialog("确认删除", "确定要删除这条生成规则吗？", "删除", "取消"))
                    {
                        ReorderableList.defaultBehaviours.DoRemoveButton(list);
                        serializedObject.ApplyModifiedProperties();
                    }
                }
            };
        }

        m_reorderableList.DoLayoutList();

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(serializedObject.targetObject);
        }
    }

    private static void DrawUIGenTypesReorderableList(SerializedObject serializedObject)
    {
        SerializedProperty ruleListProperty = serializedObject.FindProperty("uiGenTypes");
        if (ruleListProperty == null) return;

        if (m_uiGenTypeRecoedList == null)
        {
            m_uiGenTypeRecoedList = new ReorderableList(serializedObject, ruleListProperty, true, true, true, true)
            {
                drawHeaderCallback = (Rect rect) =>
                {
                    float padding = 5f;
                    float columnWidth = (rect.width - padding * 3) / 4f;

                    GUI.Label(new Rect(rect.x, rect.y, columnWidth, rect.height), "UI类型名称", EditorStyles.boldLabel);
                    GUI.Label(new Rect(rect.x + columnWidth + padding + 20, rect.y, columnWidth, rect.height), "自动生成的UI类继承的类名", EditorStyles.boldLabel);
                    GUI.Label(new Rect(rect.x + (columnWidth + padding) * 3, rect.y, columnWidth, rect.height), "是否是泛型", EditorStyles.boldLabel);
                },

                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    EditorGUI.BeginChangeCheck();
                    SerializedProperty element = ruleListProperty.GetArrayElementAtIndex(index);
                    rect.y += 2;

                    float padding = 5f;
                    float fieldHeight = EditorGUIUtility.singleLineHeight;
                    float columnWidth = (rect.width - padding * 3) / 4f;

                    // UI类型名称
                    SerializedProperty regexProperty = element.FindPropertyRelative("uiTypeName");
                    string ruleName = GetRuleDisplayName(regexProperty.stringValue);
                    Rect nameRect = new Rect(rect.x, rect.y, columnWidth, fieldHeight);
                    EditorGUI.LabelField(nameRect, ruleName);

                    Rect regexRect = new Rect(rect.x + columnWidth + padding, rect.y, columnWidth, fieldHeight);
                    EditorGUI.PropertyField(regexRect, regexProperty, GUIContent.none);

                    // 是否Widget
                    Rect widgetRect = new Rect(rect.x + (columnWidth + padding) * 3 + 10, rect.y, columnWidth, fieldHeight);
                    SerializedProperty widgetProperty = element.FindPropertyRelative("isGeneric");
                    EditorGUI.PropertyField(widgetRect, widgetProperty, GUIContent.none);

                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(serializedObject.targetObject);
                        AssetDatabase.SaveAssets();
                    }
                },

                elementHeight = EditorGUIUtility.singleLineHeight + 6,

                onChangedCallback = (ReorderableList list) =>
                {
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(serializedObject.targetObject);
                },

                onAddCallback = (ReorderableList list) =>
                {
                    int newIndex = list.serializedProperty.arraySize;
                    list.serializedProperty.arraySize++;
                    list.index = newIndex;

                    // 设置默认值
                    SerializedProperty newElement = list.serializedProperty.GetArrayElementAtIndex(newIndex);
                    newElement.FindPropertyRelative("uiTypeName").stringValue = "UIWindow";
                    newElement.FindPropertyRelative("isGeneric").boolValue = false;

                    serializedObject.ApplyModifiedProperties();
                },

                onRemoveCallback = (ReorderableList list) =>
                {
                    if (EditorUtility.DisplayDialog("确认删除", "确定要删除这条生成规则吗？", "删除", "取消"))
                    {
                        ReorderableList.defaultBehaviours.DoRemoveButton(list);
                        serializedObject.ApplyModifiedProperties();
                    }
                }
            };
        }

        m_uiGenTypeRecoedList.DoLayoutList();

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(serializedObject.targetObject);
        }
    }

    private static string GetRuleDisplayName(string regex)
    {
        if (string.IsNullOrEmpty(regex)) return "新规则";

        // 将常见的正则表达式转换为可读的名称
        if (regex.Contains("Btn")) return "按钮规则";
        if (regex.Contains("Text") || regex.Contains("Label")) return "文本规则";
        if (regex.Contains("Img") || regex.Contains("Image")) return "图片规则";
        if (regex.Contains("Slider")) return "滑动条规则";
        if (regex.Contains("Toggle")) return "开关规则";
        if (regex.Contains("Input")) return "输入框规则";

        return regex.Length > 10 ? regex.Substring(0, 10) + "..." : regex;
    }
}