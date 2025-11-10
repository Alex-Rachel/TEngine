using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TEngine.UI.Editor
{
    public class UISettingEditorWindow_NoOdin_v2 : EditorWindow
    {
        [MenuItem("TEngine/UISetting Window")]
        private static void OpenWindow()
        {
            var w = GetWindow<UISettingEditorWindow_NoOdin_v2>("UI Setting");
            w.minSize = new Vector2(760, 520);
            w.Show();
        }


        private UIGenerateConfiguration uiGenerateConfiguration;
        private UIGenerateCommonData UIGenerateCommonData;
        private List<UIEelementRegexData> UIElementRegexConfigs;
        private List<UIScriptGenerateData> UIScriptGenerateConfigs;

        private List<string> excludeKeywordsList = new List<string>();


        private Vector2 scroll;
        private int toolbarTab;
        private readonly string[] toolbarTitles = { "UI基础设置", "UI构建配置", "UI元素映射" };


        private ReorderableList combineList;
        private ReorderableList regexList;
        private ReorderableList projectList;
        private ReorderableList excludeList;


        private TextAsset importText;


        private string previewLabel;
        private string previewCompLabel;

        private List<string> m_ScriptGeneratorHelperTypes = new();
        private int m_ScriptGeneratorHelperSelectIndex;

        private void OnEnable()
        {
            uiGenerateConfiguration = UIGenerateConfiguration.Instance;
            if (uiGenerateConfiguration == null)
            {
                uiGenerateConfiguration = ScriptableObject.CreateInstance<UIGenerateConfiguration>();
            }

            UIGenerateCommonData = uiGenerateConfiguration.UIGenerateCommonData ?? new UIGenerateCommonData();
            UIElementRegexConfigs = uiGenerateConfiguration.UIElementRegexConfigs ?? new List<UIEelementRegexData>();
            UIScriptGenerateConfigs = uiGenerateConfiguration.UIScriptGenerateConfigs ?? new List<UIScriptGenerateData>();

            excludeKeywordsList = (UIGenerateCommonData.ExcludeKeywords ?? new string[0]).ToList();

            SetupLists();
            RefreshLabel();
            RefreshScriptGeneratorHelperTypes();
        }

        private void SetupLists()
        {
            combineList = new ReorderableList(UIGenerateCommonData.CombineWords, typeof(StringPair), true, true, true, true);
            combineList.drawHeaderCallback = (r) => EditorGUI.LabelField(r, "路径拼接映射 (Key -> Value)");
            combineList.drawElementCallback = (rect, index, active, focused) =>
            {
                var p = UIGenerateCommonData.CombineWords[index];
                rect.y += 2;
                float half = rect.width / 2 - 8;
                p.Key = EditorGUI.TextField(new Rect(rect.x, rect.y, half, EditorGUIUtility.singleLineHeight), p.Key);
                p.Value = EditorGUI.TextField(new Rect(rect.x + half + 16, rect.y, half, EditorGUIUtility.singleLineHeight), p.Value);
            };
            combineList.onAddCallback = (r) => UIGenerateCommonData.CombineWords.Add(new StringPair("Key", "Value"));
            combineList.onRemoveCallback = (r) =>
            {
                if (r.index >= 0) UIGenerateCommonData.CombineWords.RemoveAt(r.index);
            };


            excludeList = new ReorderableList(excludeKeywordsList, typeof(string), true, true, true, true);
            excludeList.drawHeaderCallback = (r) => EditorGUI.LabelField(r, "排除关键字（匹配则不生成）");
            excludeList.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 2;
                excludeKeywordsList[index] = EditorGUI.TextField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), excludeKeywordsList[index]);
            };
            excludeList.onAddCallback = (r) => excludeKeywordsList.Add(string.Empty);
            excludeList.onRemoveCallback = (r) =>
            {
                if (r.index >= 0) excludeKeywordsList.RemoveAt(r.index);
            };


            regexList = new ReorderableList(UIElementRegexConfigs, typeof(UIEelementRegexData), true, true, true, true);
            regexList.drawHeaderCallback = (r) => EditorGUI.LabelField(r, "UI元素映射 (正则 -> 组件)");
            regexList.elementHeightCallback = (i) => EditorGUIUtility.singleLineHeight * 2 + 8;
            regexList.drawElementCallback = (rect, index, active, focused) =>
            {
                var item = UIElementRegexConfigs[index];
                rect.y += 2;
                float lh = EditorGUIUtility.singleLineHeight;

                EditorGUI.BeginChangeCheck();
                item.uiElementRegex = EditorGUI.TextField(new Rect(rect.x + 70, rect.y, rect.width - 70, lh), item.uiElementRegex);
                if (EditorGUI.EndChangeCheck()) RefreshLabel();


                EditorGUI.LabelField(new Rect(rect.x, rect.y + lh + 4, 60, lh), "Component");
                var btnRect = new Rect(rect.x + 70, rect.y + lh + 4, 180, lh);

                string btnLabel = string.IsNullOrEmpty(item.componentType) ? "(选择类型)" : item.componentType;
                if (GUI.Button(btnRect, btnLabel, EditorStyles.popup))
                {
                    var opts = CollectComponentTypeNamesFallback();


                    Rect anchor = new Rect(btnRect.x, btnRect.y + btnRect.height, Math.Min(360f, Mathf.Max(btnRect.width, 200f)), btnRect.height);

                    SearchablePopup.Show(anchor, opts, Math.Max(0, opts.IndexOf(item.componentType)), (selIndex) =>
                    {
                        if (selIndex >= 0 && selIndex < opts.Count)
                        {
                            item.componentType = opts[selIndex];
                            Repaint();
                        }
                    });
                }

                item.componentType = EditorGUI.TextField(new Rect(rect.x + 260, rect.y + lh + 4, rect.width - 260, lh), item.componentType);
            };
            regexList.onAddCallback = (r) => UIElementRegexConfigs.Add(new UIEelementRegexData { uiElementRegex = "", componentType = "" });
            regexList.onRemoveCallback = (r) =>
            {
                if (r.index >= 0) UIElementRegexConfigs.RemoveAt(r.index);
            };


            projectList = new ReorderableList(UIScriptGenerateConfigs, typeof(UIScriptGenerateData), true, true, true, true);
            projectList.drawHeaderCallback = (r) => EditorGUI.LabelField(r, "UI脚本生成配置（多个项目）");
            projectList.elementHeightCallback = (i) => EditorGUIUtility.singleLineHeight * 5 + 10;
            projectList.drawElementCallback = (rect, index, active, focused) =>
            {
                var d = UIScriptGenerateConfigs[index];
                float lh = EditorGUIUtility.singleLineHeight;
                float pad = 2;

                d.ProjectName = EditorGUI.TextField(new Rect(rect.x, rect.y, rect.width, lh), "项目名", d.ProjectName);
                d.NameSpace = EditorGUI.TextField(new Rect(rect.x, rect.y + (lh + pad), rect.width, lh), "命名空间", d.NameSpace);

                d.GenerateHolderCodePath = DrawFolderField("生成脚本路径", d.GenerateHolderCodePath, rect.x, rect.y + 2 * (lh + pad), rect.width, lh);
                d.UIPrefabRootPath = DrawFolderField("Prefab根目录", d.UIPrefabRootPath, rect.x, rect.y + 3 * (lh + pad), rect.width, lh);
                d.LoadType = (EUIResLoadType)EditorGUI.EnumPopup(new Rect(rect.x, rect.y + 4 * (lh + pad), rect.width, lh), "加载类型", d.LoadType);
            };
            projectList.onAddCallback = (r) => UIScriptGenerateConfigs.Add(new UIScriptGenerateData { ProjectName = "NewProject", NameSpace = "Game.UI", GenerateHolderCodePath = "Assets/Scripts/UI/Generated", UIPrefabRootPath = "Assets/Resources/UI", LoadType = EUIResLoadType.Resources });
            projectList.onRemoveCallback = (r) =>
            {
                if (r.index >= 0) UIScriptGenerateConfigs.RemoveAt(r.index);
            };
        }

        private string DrawFolderField(string label, string value, float x, float y, float width, float h)
        {
            var txtRect = new Rect(x, y, width - 76, h);
            var btnRect = new Rect(x + width - 72, y, 68, h);
            value = EditorGUI.TextField(txtRect, label, value);
            if (GUI.Button(btnRect, "选择"))
            {
                string p = EditorUtility.OpenFolderPanel("选择路径", Application.dataPath, "");
                if (!string.IsNullOrEmpty(p))
                {
                    if (p.StartsWith(Application.dataPath))
                        value = "Assets" + p.Substring(Application.dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("提示", "请选择 Assets 下的路径", "确定");
                }
            }

            return value;
        }

        private void RefreshScriptGeneratorHelperTypes()
        {
            m_ScriptGeneratorHelperTypes = new List<string>();

            m_ScriptGeneratorHelperTypes.AddRange(Utility.Assembly.GetRuntimeTypeNames(typeof(IUIGeneratorRuleHelper)));

            m_ScriptGeneratorHelperSelectIndex = m_ScriptGeneratorHelperTypes.IndexOf(UIGenerateConfiguration.Instance.UIScriptGeneratorRuleHelper);
            if (m_ScriptGeneratorHelperSelectIndex < 0)
            {
                m_ScriptGeneratorHelperSelectIndex = 0;
            }
        }

        private void OnGUI()
        {
            GUILayout.Space(6);


            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            for (int i = 0; i < toolbarTitles.Length; i++)
            {
                bool isActive = (toolbarTab == i);
                bool result = GUILayout.Toggle(isActive, toolbarTitles[i], EditorStyles.toolbarButton, GUILayout.Height(22));
                if (result && toolbarTab != i)
                {
                    toolbarTab = i;
                    Repaint();
                }
            }

            GUILayout.FlexibleSpace();


            var saveIcon = EditorGUIUtility.IconContent("SaveActive");
            var refreshIcon = EditorGUIUtility.IconContent("Refresh");
            var reloadIcon = EditorGUIUtility.IconContent("RotateTool");
            GUIContent saveBtn = new GUIContent(saveIcon.image, "保存 (Save Now)");
            GUIContent refreshBtn = new GUIContent(refreshIcon.image, "刷新预览");
            GUIContent reloadBtn = new GUIContent(reloadIcon.image, "重载配置");

            if (GUILayout.Button(saveBtn, EditorStyles.toolbarButton, GUILayout.Width(36)))
                SaveConfig();
            if (GUILayout.Button(refreshBtn, EditorStyles.toolbarButton, GUILayout.Width(36)))
                RefreshLabel();
            if (GUILayout.Button(reloadBtn, EditorStyles.toolbarButton, GUILayout.Width(36)))
            {
                OnEnable();
                Repaint();
            }

            GUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            GUILayout.Space(8);
            switch (toolbarTab)
            {
                case 0: DrawCommonPane(); break;
                case 1: DrawScriptPane(); break;
                case 2: DrawElementPane(); break;
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(8);
        }


        private void DrawCommonPane()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("通用生成配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical();
            EditorGUI.BeginChangeCheck();
            UIGenerateCommonData.ComCheckSplitName = EditorGUILayout.TextField(new GUIContent("组件检查分隔符", "例如 Button#Close"), UIGenerateCommonData.ComCheckSplitName);
            UIGenerateCommonData.ComCheckEndName = EditorGUILayout.TextField(new GUIContent("组件结尾分隔符", "例如 @End"), UIGenerateCommonData.ComCheckEndName);
            UIGenerateCommonData.ArrayComSplitName = EditorGUILayout.TextField(new GUIContent("数组组件分隔符", "例如 *Item"), UIGenerateCommonData.ArrayComSplitName);
            UIGenerateCommonData.GeneratePrefix = EditorGUILayout.TextField(new GUIContent("生成脚本前缀"), UIGenerateCommonData.GeneratePrefix);
            m_ScriptGeneratorHelperSelectIndex = EditorGUILayout.Popup("解密服务", m_ScriptGeneratorHelperSelectIndex, m_ScriptGeneratorHelperTypes.ToArray());
            string selectService = m_ScriptGeneratorHelperTypes[m_ScriptGeneratorHelperSelectIndex];
            if (uiGenerateConfiguration.UIScriptGeneratorRuleHelper != selectService)
            {
                UIGenerateConfiguration.Instance.UIScriptGeneratorRuleHelper = selectService;
                UIGenerateConfiguration.Save();
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            excludeList.DoLayoutList();

            GUILayout.Space(8);

            combineList.DoLayoutList();

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("脚本生成预览", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(previewLabel ?? "", MessageType.None);
            EditorGUILayout.LabelField("组件生成预览 (下标0开始)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(previewCompLabel ?? "", MessageType.None);

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
        }

        private void DrawScriptPane()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("UI脚本生成配置（支持多个项目）", EditorStyles.boldLabel);
            GUILayout.Space(6);
            projectList.DoLayoutList();
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
        }

        private void DrawElementPane()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("UI元素映射（正则 -> 组件）", EditorStyles.boldLabel);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                if (GUILayout.Button("加载默认", EditorStyles.toolbarButton, GUILayout.Width(90)))
                    LoadDefault();

                if (GUILayout.Button("导出", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    ExportConfig();

                GUILayout.Space(8);

                importText = (TextAsset)EditorGUILayout.ObjectField(importText, typeof(TextAsset), false, GUILayout.Height(18), GUILayout.MinWidth(200));

                GUI.enabled = importText != null;
                if (GUILayout.Button("执行导入", EditorStyles.toolbarButton, GUILayout.Width(84)))
                {
                    if (importText != null)
                    {
                        ImportConfig(importText);
                        importText = null;
                    }
                }

                GUI.enabled = true;

                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);


            regexList.elementHeightCallback = (i) => EditorGUIUtility.singleLineHeight + 6;
            regexList.drawElementCallback = (rect, index, active, focused) =>
            {
                if (index < 0 || index >= UIElementRegexConfigs.Count) return;
                var item = UIElementRegexConfigs[index];

                rect.y += 2;
                float lh = EditorGUIUtility.singleLineHeight;
                float padding = 6f;


                float leftWidth = rect.width * 0.80f;
                Rect regexRect = new Rect(rect.x, rect.y, leftWidth - 8, lh);
                EditorGUI.BeginChangeCheck();
                string newRegex = EditorGUI.TextField(regexRect, item.uiElementRegex);
                if (EditorGUI.EndChangeCheck())
                {
                    item.uiElementRegex = newRegex;
                    RefreshLabel();
                }


                float rightX = rect.x + leftWidth + 8;
                float rightWidth = rect.width - leftWidth - 8;
                Rect btnRect = new Rect(rightX, rect.y, Math.Min(180, rightWidth), lh);

                string btnLabel = string.IsNullOrEmpty(item.componentType) ? "(选择类型)" : item.componentType;
                if (GUI.Button(btnRect, btnLabel, EditorStyles.popup))
                {
                    var opts = CollectComponentTypeNamesFallback();
                    Rect anchor = new Rect(btnRect.x, btnRect.y + btnRect.height, Math.Min(360f, Mathf.Max(btnRect.width, 200f)), btnRect.height);

                    SearchablePopup.Show(anchor, opts, Math.Max(0, opts.IndexOf(item.componentType)), (selIndex) =>
                    {
                        if (selIndex >= 0 && selIndex < opts.Count)
                        {
                            item.componentType = opts[selIndex];
                            Repaint();
                        }
                    });
                }
            };


            regexList.DoLayoutList();

            EditorGUILayout.EndVertical();
        }

        private static List<string> cacheFilterType;


        private List<string> CollectComponentTypeNamesFallback()
        {
            if (cacheFilterType == null)
            {
                cacheFilterType = Utility.Assembly.GetTypes()
                    .Where(m => !m.FullName.Contains("Editor"))
                    .Where(x => !x.IsAbstract || x.IsInterface)
                    .Where(x => !x.IsGenericTypeDefinition)
                    .Where(x => !x.IsSubclassOf(typeof(UIHolderObjectBase)))
                    .Where(x => x.IsSubclassOf(typeof(Component)))
                    .Where(x => !x.FullName.Contains("YooAsset"))
                    .Where(x => !x.FullName.Contains(("Unity.VisualScripting")))
                    .Where(x => !x.FullName.Contains(("Cysharp.Threading")))
                    .Where(x => !x.FullName.Contains(("UnityEngine.Rendering.UI.Debug")))
                    .Where(x => !x.FullName.Contains(("Unity.PerformanceTesting")))
                    .Where(x => !x.FullName.Contains(("UnityEngine.TestTools")))
                    .Select(x => x.Name).ToList();

                cacheFilterType.Add(typeof(GameObject).Name);
            }

            return cacheFilterType;
        }

        private void LoadDefault()
        {
            string defaultPath = null;
            try
            {
                var f = typeof(UIGlobalPath).GetField("DefaultComPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (f != null) defaultPath = f.GetValue(null) as string;
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(defaultPath)) defaultPath = "Assets/uielementconfig.txt";
            if (!File.Exists(defaultPath))
            {
                EditorUtility.DisplayDialog("加载默认", $"未找到默认文件：{defaultPath}", "OK");
                return;
            }

            string txt = File.ReadAllText(defaultPath);
            var list = JsonConvert.DeserializeObject<List<UIEelementRegexData>>(txt);
            if (list != null)
            {
                UIElementRegexConfigs = list;
                regexList.list = UIElementRegexConfigs;
                RefreshLabel();
            }
        }

        private void ImportConfig(TextAsset text)
        {
            try
            {
                var list = JsonConvert.DeserializeObject<List<UIEelementRegexData>>(text.text);
                if (list != null)
                {
                    UIElementRegexConfigs = list;
                    regexList.list = UIElementRegexConfigs;
                    RefreshLabel();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("错误", "导入失败，请查看控制台", "OK");
            }
        }

        private void ExportConfig()
        {
            string json = JsonConvert.SerializeObject(UIElementRegexConfigs, Formatting.Indented);
            string path = EditorUtility.SaveFilePanel("导出 UI 元素配置为 JSON", Application.dataPath, "uielementconfig", "txt");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, json);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("导出完成", "已导出", "OK");
            }
        }

        private void RefreshLabel()
        {
            previewLabel = $"{UIGenerateCommonData.GeneratePrefix}_UITestWindow";
            previewCompLabel = $"{UIGenerateCommonData.ArrayComSplitName}Text{UIGenerateCommonData.ComCheckSplitName}Img{UIGenerateCommonData.ComCheckEndName}Test{UIGenerateCommonData.ArrayComSplitName}0";
            Repaint();
        }

        private void SaveConfig()
        {
            UIGenerateCommonData.ExcludeKeywords = excludeKeywordsList.ToArray();

            uiGenerateConfiguration.UIGenerateCommonData = UIGenerateCommonData;
            uiGenerateConfiguration.UIElementRegexConfigs = UIElementRegexConfigs;
            uiGenerateConfiguration.UIScriptGenerateConfigs = UIScriptGenerateConfigs;
            UIGenerateConfiguration.Save();
            Debug.Log("UIGenerateConfiguration Saved...");
        }

        private void OnDisable() => SaveConfig();
    }

    internal class SearchablePopup : PopupWindowContent
    {
        private readonly List<string> allItems;
        private List<string> filtered;
        private readonly Action<int> onSelect;
        private int currentIndex;
        private string search = "";
        private Vector2 scroll;

        private static GUIStyle searchFieldStyle;
        private static GUIStyle cancelStyle;
        private static GUIStyle rowStyle;
        private static GUIStyle selectedRowStyle;
        private const float ROW_HEIGHT = 20f;

        private SearchablePopup(List<string> items, int currentIndex, Action<int> onSelect)
        {
            this.allItems = items ?? new List<string>();
            this.filtered = new List<string>(this.allItems);
            this.currentIndex = Mathf.Clamp(currentIndex, -1, this.allItems.Count - 1);
            this.onSelect = onSelect;
        }

        public static void Show(Rect anchorRect, List<string> items, int currentIndex, Action<int> onSelect)
        {
            PopupWindow.Show(anchorRect, new SearchablePopup(items, currentIndex, onSelect));
        }

        public override Vector2 GetWindowSize() => new Vector2(360, 320);

        public override void OnOpen()
        {
            EditorApplication.delayCall += () => EditorGUI.FocusTextInControl("SearchField");
        }

        public override void OnGUI(Rect rect)
        {
            InitStyles();


            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.SetNextControlName("SearchField");
            search = EditorGUILayout.TextField(search, searchFieldStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("", cancelStyle, GUILayout.Width(18)))
            {
                search = "";
                GUI.FocusControl("SearchField");
            }

            EditorGUILayout.EndHorizontal();


            FilterList(search);


            HandleKeyboard();


            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < filtered.Count; i++)
            {
                bool selected = (i == currentIndex);
                var style = selected ? selectedRowStyle : rowStyle;
                Rect r = GUILayoutUtility.GetRect(new GUIContent(filtered[i]), style, GUILayout.Height(ROW_HEIGHT), GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                    style.Draw(r, filtered[i], false, false, selected, false);

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    Select(filtered[i]);
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (e.keyCode == KeyCode.DownArrow)
            {
                currentIndex = Mathf.Min(currentIndex + 1, filtered.Count - 1);
                e.Use();
                editorWindow.Repaint();
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                currentIndex = Mathf.Max(currentIndex - 1, 0);
                e.Use();
                editorWindow.Repaint();
            }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (filtered.Count > 0 && currentIndex >= 0 && currentIndex < filtered.Count)
                    Select(filtered[currentIndex]);
                e.Use();
            }
        }

        private void FilterList(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                filtered = new List<string>(allItems);
            else
            {
                string lower = keyword.ToLowerInvariant();
                filtered = allItems.Where(i => i != null && i.ToLowerInvariant().Contains(lower)).ToList();
            }

            if (filtered.Count == 0) currentIndex = -1;
            else currentIndex = Mathf.Clamp(currentIndex, 0, filtered.Count - 1);
        }

        private void Select(string item)
        {
            int originalIndex = allItems.IndexOf(item);
            if (originalIndex >= 0)
                onSelect?.Invoke(originalIndex);

            editorWindow.Close();
            GUIUtility.ExitGUI();
        }

        private void InitStyles()
        {
            if (searchFieldStyle == null)
                searchFieldStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarSearchField;
            if (cancelStyle == null)
                cancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? EditorStyles.toolbarButton;

            if (rowStyle == null)
            {
                rowStyle = new GUIStyle("PR Label") { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 2, 2) };
            }

            if (selectedRowStyle == null)
            {
                selectedRowStyle = new GUIStyle(rowStyle) { normal = { background = Texture2D.grayTexture } };
            }
        }
    }
}
