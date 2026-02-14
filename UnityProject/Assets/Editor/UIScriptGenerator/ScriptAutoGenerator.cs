using System;
using System.IO;
using System.Text;
using GameLogic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TEngine.Editor.UI
{
    public partial class ScriptGenerator
    {
        private static TextEditor m_textEditor = new TextEditor();
        private static string[] VARIABLE_NAME_REGEX;
        private static void CheckVariableNames()
        {
            var cnt = (int)UIFieldCodeStyle.Max;
            VARIABLE_NAME_REGEX = new string[cnt];

            for (int i = 0; i < cnt; i++)
            {
                VARIABLE_NAME_REGEX[i] = GetPrefixNameByCodeStyle((UIFieldCodeStyle)i);
            }
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyBindComponent", priority = 84)]
        public static void UIPropertyBindComponent()
        {
            GenerateCSharpScript(false);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyBindComponent", true)]
        public static bool ValidateUIPropertyBindComponent()
        {
            return ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyBindComponent - UniTask", priority = 85)]
        public static void UIPropertyBindComponentUniTask()
        {
            GenerateCSharpScript(false, true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyBindComponent - UniTask", true)]
        public static bool ValidateUIPropertyBindComponentUniTask()
        {
            return ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListenerBindComponent", priority = 86)]
        public static void UIPropertyAndListenerBindComponent()
        {
            GenerateCSharpScript(true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListenerBindComponent", true)]
        public static bool ValidateUIPropertyAndListenerBindComponent()
        {
            return ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListenerBindComponentUniTask - UniTask", priority = 87)]
        public static void UIPropertyAndListenerBindComponentUniTask()
        {
            GenerateCSharpScript(true, true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListenerBindComponentUniTask - UniTask", true)]
        public static bool ValidateUIPropertyAndListenerBindComponentUniTask()
        {
            return ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        private static string GetUITypeName(string uiGenTypeName, string fileName)
        {
            var uiGenType = ScriptGeneratorSetting.GetUIGenType(uiGenTypeName);

            if (uiGenType == null)
            {
                return "UIWindow";
            }
            return !uiGenType.isGeneric ? uiGenType.uiTypeName : $"{uiGenType.uiTypeName}<{fileName}>";
        }

        public static bool GenerateCSharpScript(bool includeListener, bool isUniTask = false,
            bool isAutoGenerate = false, string savePath = null, string className = null,
            string uiGenTypeName = null, bool isGenImp = false,
            string impSavePath = null)
        {
            var root = Selection.activeTransform;
            if (root == null)
            {
                return false;
            }
            CheckVariableNames();
            StringBuilder strVar = new StringBuilder();
            StringBuilder strBind = new StringBuilder();
            StringBuilder strOnCreate = new StringBuilder();
            StringBuilder strCallback = new StringBuilder();

            var widgetPrefix = GetUIWidgetName();
            string fileName = $"{root.name}.cs";
            if (!string.IsNullOrEmpty(className))
            {
                fileName = $"{className}.cs";
            }
            string uiTypeName = GetUITypeName(uiGenTypeName, className);
            if (!isAutoGenerate)
            {
                uiTypeName = "UIWindow";
                if (root.name.StartsWith(widgetPrefix))
                {
                    uiTypeName = "UIWidget";
                    fileName = $"{root.name.Replace(GetUIWidgetName(), string.Empty)}.cs";
                }
            }

            strVar.AppendLine($"\t\tprivate UIBindComponent m_bindComponent;");

            strBind.AppendLine($"\t\t\tm_bindComponent = gameObject.GetComponent<UIBindComponent>();");
            strBind.AppendLine($"\t\t\tif(m_bindComponent == null)");
            strBind.AppendLine($"\t\t\t{{");
            strBind.AppendLine($"\t\t\t\tLog.Error($\"根物体: {{gameObject.name}} 缺少组件 UIBindComponent, 请检查！！！\");");
            strBind.AppendLine($"\t\t\t\treturn;");
            strBind.AppendLine($"\t\t\t}}");
            m_bindIndex = 0;
            AutoErgodic(root, root, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
            StringBuilder strFile = new StringBuilder();

            if (includeListener)
            {
                strFile.AppendLine("//----------------------------------------------------------");
                strFile.AppendLine("// <auto-generated>");
                strFile.AppendLine("// -This code was generated.");
                strFile.AppendLine("// -Changes to this file may cause incorrect behavior.");
                strFile.AppendLine("// -will be lost if the code is regenerated.");
                strFile.AppendLine("// <auto-generated/>");
                strFile.AppendLine("//----------------------------------------------------------");
#if TextMeshPro
                strFile.AppendLine("using TMPro;");
#endif
                if (isUniTask)
                {
                    strFile.AppendLine("using Cysharp.Threading.Tasks;");
                }

                strFile.AppendLine("using UnityEngine;");
                strFile.AppendLine("using UnityEngine.UI;");
                strFile.AppendLine("using TEngine;");
                strFile.AppendLine();
                strFile.AppendLine($"namespace {ScriptGeneratorSetting.GetUINameSpace()}");
                strFile.AppendLine("{");
                {
                    if (!isAutoGenerate)
                    {
                        if (root.name.StartsWith(widgetPrefix))
                        {
                            strFile.AppendLine($"\tpublic partial class {fileName.Replace(".cs", "")} : {uiTypeName}");
                        }
                        else
                        {
                            strFile.AppendLine($"\t[Window(UILayer.UI, location : \"{fileName.Replace(".cs", "")}\")]");
                            strFile.AppendLine($"\tpublic partial class {fileName.Replace(".cs", "")} : {uiTypeName}");
                        }
                    }
                    else
                    {
                        //if (string.Equals(uiTypeName, "UIWindow", StringComparison.Ordinal))
                        //{
                        //    strFile.AppendLine($"\t[Window(UILayer.UI, location : \"{fileName.Replace(".cs", "")}\")]");
                        //}

                        strFile.AppendLine($"\tpublic partial class {fileName.Replace(".cs", "")} : {uiTypeName}");
                    }

                    strFile.AppendLine("\t{");
                }
            }

            // 脚本工具生成的代码
            strFile.AppendLine($"\t\t#region 脚本工具生成的代码");
            strFile.AppendLine();
            strFile.AppendLine(strVar.ToString());
            strFile.AppendLine("\t\tprotected override void ScriptGenerator()");
            strFile.AppendLine("\t\t{");
            {
                strFile.Append(strBind.ToString());
                strFile.Append(strOnCreate.ToString());
            }
            strFile.AppendLine("\t\t}");
            strFile.AppendLine();
            strFile.Append($"\t\t#endregion");
            strFile.AppendLine();

            if (includeListener)
            {
                strFile.AppendLine();
                strFile.AppendLine("\t\t#region 事件");
                strFile.AppendLine();
                strFile.Append(strCallback.ToString());
                strFile.AppendLine($"\t\t#endregion");
                strFile.AppendLine("\t}");
                strFile.AppendLine("}");
            }

            m_textEditor.Delete();
            m_textEditor.text = strFile.ToString();
            m_textEditor.SelectAll();
            m_textEditor.Copy();

            if (isAutoGenerate)
            {
                string path = savePath?.Replace("\\", "/");
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                var saveFileName = fileName.Replace(".cs", "_Gen.g.cs");
                var filePath = Path.Combine(path, saveFileName).Replace("\\", "/");

                if (File.Exists(filePath))
                {
                    FileAttributes attributes = File.GetAttributes(filePath);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                    }
                    File.Delete(filePath);
                    AssetDatabase.Refresh();
                }

                File.WriteAllText(filePath, strFile.ToString(), Encoding.UTF8);
                File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.ReadOnly);
                if (isGenImp)
                {
                    GenerateImpCSharpScript(isUniTask, fileName, impSavePath, uiTypeName);
                }
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.Log($"<color=#1E90FF>脚本已生成到剪贴板，请自行Ctl+V粘贴</color>");
            }

            return true;
        }

        private static int m_bindIndex = 0;
        public static void AutoErgodic(Transform root, Transform transform, ref StringBuilder strVar,
            ref StringBuilder strBind, ref StringBuilder strOnCreate, ref StringBuilder strCallback, bool isUniTask)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                WriteAutoScript(root, child, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
                // 跳过 "m_item"
                if (child.name.StartsWith(GetUIWidgetName()))
                {
                    continue;
                }

                AutoErgodic(root, child, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
            }
        }

        private static void WriteAutoScript(Transform root, Transform child, ref StringBuilder strVar,
            ref StringBuilder strBind, ref StringBuilder strOnCreate, ref StringBuilder strCallback, bool isUniTask)
        {
            string varName = child.name;
            // 查找相关的规则定义
            var rule = ScriptGeneratorSetting.GetScriptGenerateRule()
                .Find(r => varName.StartsWith(r.uiElementRegex));

            if (rule == null)
            {
                return;
            }

            var componentName = rule.componentName.ToString();

            if (string.IsNullOrEmpty(componentName))
            {
                return;
            }

            varName = GetVariableName(varName);

            if (string.IsNullOrEmpty(varName))
            {
                return;
            }

            // strVar.AppendLine($"\t\tprivate {componentName} {varName};");
            strVar.AppendLine($"\t\tprivate {componentName} {varName}{(ScriptGeneratorSetting.Instance.NullableEnable?" = null!;":";")}");
            if (rule.componentName == UIComponentName.GameObject)
            {
                strBind.AppendLine($"\t\t\t{varName} = m_bindComponent.GetComponent<RectTransform>({m_bindIndex}).gameObject;");
            }
            else if (rule.componentName != UIComponentName.GameObject && rule.isUIWidget)
            {
                strBind.AppendLine($"\t\t\t{varName} = CreateWidget<{componentName}>(m_bindComponent.GetComponent<RectTransform>({m_bindIndex}).gameObject);");
            }
            else
            {
                strBind.AppendLine($"\t\t\t{varName} = m_bindComponent.GetComponent<{componentName}>({m_bindIndex});");
            }
            m_bindIndex++;

            switch (rule.componentName)
            {
                case UIComponentName.Button:
                    var btnFuncName = GetBtnFuncName(varName);

                    if (isUniTask)
                    {
                        strOnCreate.AppendLine(
                            $"\t\t\t{varName}.onClick.AddListener(UniTask.UnityAction({btnFuncName}));");
                        strCallback.AppendLine($"\t\tprivate partial UniTaskVoid {btnFuncName}();");
                    }
                    else
                    {
                        strOnCreate.AppendLine($"\t\t\t{varName}.onClick.AddListener({btnFuncName});");
                        strCallback.AppendLine($"\t\tprivate partial void {btnFuncName}();");
                    }

                    strCallback.AppendLine();
                    break;

                case UIComponentName.Toggle:
                    var toggleFuncName = GetToggleFuncName(varName);
                    strOnCreate.AppendLine($"\t\t\t{varName}.onValueChanged.AddListener({toggleFuncName});");
                    strCallback.AppendLine($"\t\tprivate partial void {toggleFuncName}(bool isOn);");
                    strCallback.AppendLine();
                    break;

                case UIComponentName.Slider:
                    var sliderFuncName = GetSliderFuncName(varName);
                    strOnCreate.AppendLine($"\t\t\t{varName}.onValueChanged.AddListener({sliderFuncName});");
                    strCallback.AppendLine($"\t\tprivate partial void {sliderFuncName}(float value);");
                    strCallback.AppendLine();
                    break;
            }
        }
        
        #region GenerateImpCSharp

        private static bool GenerateImpCSharpScript(bool isUniTask = false, string fileName = null, string impSavePath = null, string uiTypeName = null)
        {
            var root = Selection.activeTransform;
            if (root == null || string.IsNullOrEmpty(fileName))
            {
                return false;
            }
            CheckVariableNames();
            StringBuilder strCallback = new StringBuilder();

            AutoImpErgodic(root, root, ref strCallback, isUniTask);
            StringBuilder strFile = new StringBuilder();

#if TextMeshPro
                strFile.AppendLine("using TMPro;");
#endif
            if (isUniTask)
            {
                strFile.AppendLine("using Cysharp.Threading.Tasks;");
            }

            strFile.AppendLine("using UnityEngine;");
            strFile.AppendLine("using UnityEngine.UI;");
            strFile.AppendLine("using TEngine;");
            strFile.AppendLine();
            strFile.AppendLine($"namespace {ScriptGeneratorSetting.GetUINameSpace()}");
            strFile.AppendLine("{");
            {
                if (string.Equals(uiTypeName, "UIWindow", StringComparison.Ordinal))
                {
                    strFile.AppendLine($"\t[Window(UILayer.UI, location : \"{fileName.Replace(".cs", "")}\")]");
                }
                strFile.AppendLine($"\tpublic partial class {fileName.Replace(".cs", "")}");
                strFile.AppendLine("\t{");
                {
                    strFile.AppendLine("\t\t#region 事件");
                    strFile.AppendLine();
                    strFile.Append(strCallback.ToString());
                    strFile.AppendLine($"\t\t#endregion");
                }
                strFile.AppendLine("\t}");
            }
            strFile.AppendLine("}");

            m_textEditor.Delete();
            m_textEditor.text = strFile.ToString();
            m_textEditor.SelectAll();
            m_textEditor.Copy();

            string path = impSavePath?.Replace("\\", "/");
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            var filePath = Path.Combine(path, fileName).Replace("\\", "/");

            if (File.Exists(filePath))
            {
                Debug.LogWarning("相关实现类脚本已生成，再次生成跳过");
                return false;
            }

            File.WriteAllText(filePath, strFile.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            return true;
        }

        public static void AutoImpErgodic(Transform root, Transform transform, ref StringBuilder strCallback, bool isUniTask)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                WriteAutoImpScript(root, child, ref strCallback, isUniTask);
                // 跳过 "m_item"
                if (child.name.StartsWith(GetUIWidgetName()))
                {
                    continue;
                }

                AutoImpErgodic(root, child, ref strCallback, isUniTask);
            }
        }

        private static void WriteAutoImpScript(Transform root, Transform child, ref StringBuilder strCallback, bool isUniTask)
        {
            string varName = child.name;
            // 查找相关的规则定义
            var rule = ScriptGeneratorSetting.GetScriptGenerateRule()
                .Find(r => varName.StartsWith(r.uiElementRegex));

            if (rule == null)
            {
                return;
            }

            var componentName = rule.componentName.ToString();

            if (string.IsNullOrEmpty(componentName))
            {
                return;
            }

            varName = GetVariableName(varName);

            if (string.IsNullOrEmpty(varName))
            {
                return;
            }

            switch (rule.componentName)
            {
                case UIComponentName.Button:
                    var btnFuncName = GetBtnFuncName(varName);

                    if (isUniTask)
                    {
                        strCallback.AppendLine($"\t\tprivate async partial UniTaskVoid {btnFuncName}()");
                        strCallback.AppendLine("\t\t{");
                        strCallback.AppendLine("\t\t\tawait UniTask.Yield();");
                        strCallback.AppendLine("\t\t}");
                    }
                    else
                    {
                        strCallback.AppendLine($"\t\tprivate partial void {btnFuncName}()");
                        strCallback.AppendLine("\t\t{");
                        strCallback.AppendLine("\t\t}");
                    }

                    strCallback.AppendLine();
                    break;

                case UIComponentName.Toggle:
                    var toggleFuncName = GetToggleFuncName(varName);
                    strCallback.AppendLine($"\t\tprivate partial void {toggleFuncName}(bool isOn)");
                    strCallback.AppendLine("\t\t{");
                    strCallback.AppendLine("\t\t}");
                    strCallback.AppendLine();
                    break;

                case UIComponentName.Slider:
                    var sliderFuncName = GetSliderFuncName(varName);
                    strCallback.AppendLine($"\t\tprivate partial void {sliderFuncName}(float value)");
                    strCallback.AppendLine("\t\t{");
                    strCallback.AppendLine("\t\t}");
                    strCallback.AppendLine();
                    break;
            }
        }

        #endregion


        #region GenerateUIComponent

         public static bool GenerateUIComponentScript()
        {
            var root = Selection.activeTransform;

            if (root == null)
            {
                return false;
            }
            // 检查是否在预制体编辑模式下
            bool isInPrefabMode = IsInPrefabMode(root.gameObject);

            CheckVariableNames();
            var uiComponent = AddComponent2Window();

            if (uiComponent == null)
            {
                return false;
            }

            ErgodicUIComponent(root, root, uiComponent);
            // 如果是预制体模式，需要特殊处理保存
            if (isInPrefabMode)
            {
                SavePrefabChanges(root.gameObject);
            }
            AssetDatabase.Refresh();
            return true;
            // Debug.Log($"<color=#1E90FF>脚本已生成到剪贴板，请自行Ctl+V粘贴</color>");
        }

        public static void ErgodicUIComponent(Transform root, Transform transform, UIBindComponent uiBindComponent)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                WriteScriptUIComponent(root, child, uiBindComponent);

                // 跳过 "m_item"
                if (child.name.StartsWith(GetUIWidgetName()))
                {
                    continue;
                }

                ErgodicUIComponent(root, child, uiBindComponent);
            }
        }

        private static void WriteScriptUIComponent(Transform root, Transform child, UIBindComponent uiBindComponent)
        {
            string varName = child.name;
            // 查找相关的规则定义
            var rule = ScriptGeneratorSetting.GetScriptGenerateRule()
                .Find(r => varName.StartsWith(r.uiElementRegex));

            if (rule == null)
            {
                return;
            }

            var componentName = rule.componentName.ToString();

            if (string.IsNullOrEmpty(componentName))
            {
                return;
            }

            if (rule.componentName == UIComponentName.GameObject || rule.isUIWidget)
            {
                var c = child.gameObject.GetComponent<RectTransform>();
                uiBindComponent.AddComponent(c);
                return;
            }

            Type componentType = GetComponentTypeFromEnumName(rule.componentName);

            if (componentType == null)
            {
                componentType = GetComponentTypeFromEnumName(componentName);

                if (componentType == null)
                {
                    Debug.LogWarning($"未找到对应的组件类型: {componentName}");
                    return;
                }
            }

            varName = GetVariableName(varName);

            if (string.IsNullOrEmpty(varName))
            {
                return;
            }

            var com = child.GetComponent(componentType);
            uiBindComponent.AddComponent(com);
        }

        private static Type GetComponentTypeFromEnumName(string enumName)
        {
            Type type = Type.GetType($"UnityEngine.{enumName}, UnityEngine");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.UI.{enumName}, UnityEngine.UI");
            if (type != null) return type;

            type = Type.GetType($"GameLogic.{enumName}, GameLogic");
            if (type != null) return type;

            type = Type.GetType(enumName);
            if (type != null) return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(enumName);
                if (type != null) return type;

                type = assembly.GetType($"UnityEngine.{enumName}");
                if (type != null) return type;

                type = assembly.GetType($"UnityEngine.UI.{enumName}");
                if (type != null) return type;

                type = assembly.GetType($"GameLogic.{enumName}");
                if (type != null) return type;
            }

            return null;
        }

        private static Type GetComponentTypeFromEnumName(UIComponentName enumName)
        {
            return enumName switch
            {
                UIComponentName.GameObject => typeof(GameObject),
                UIComponentName.Button => typeof(Button),
                UIComponentName.Toggle => typeof(Toggle),
                UIComponentName.Slider => typeof(Slider),
                UIComponentName.Text => typeof(Text),
                UIComponentName.Canvas => typeof(Canvas),
                UIComponentName.Image => typeof(Image),
                UIComponentName.RectTransform => typeof(RectTransform),
                UIComponentName.Transform => typeof(Transform),
                UIComponentName.AnimationCurve => typeof(AnimationCurve),
                UIComponentName.Scrollbar => typeof(Scrollbar),
                UIComponentName.ScrollRect => typeof(ScrollRect),
                UIComponentName.CanvasGroup => typeof(CanvasGroup),
                UIComponentName.InputField => typeof(InputField),
                UIComponentName.ToggleGroup => typeof(ToggleGroup),
                UIComponentName.RawImage => typeof(RawImage),
                UIComponentName.GridLayoutGroup => typeof(GridLayoutGroup),
                UIComponentName.HorizontalLayoutGroup => typeof(HorizontalLayoutGroup),
                UIComponentName.VerticalLayoutGroup => typeof(VerticalLayoutGroup),
                UIComponentName.Dropdown => typeof(Dropdown),
                UIComponentName.TextMeshProUGUI => typeof(TextMeshProUGUI),
                UIComponentName.TMP_InputField => typeof(TMP_InputField),
                _ => null,
            };
        }

        private static UIBindComponent AddComponent2Window()
        {
            var root = Selection.activeTransform;

            if (root == null)
            {
                Debug.LogWarning("请先选中一个物体，再进行脚本生成");
                return null;
            }

            GameObject rootObj = root.gameObject;

            var compt = rootObj.GetComponent<UIBindComponent>();

            if (compt == null)
            {
                compt = rootObj.AddComponent<UIBindComponent>();
            }

            compt.Clear();
            return compt;
        }

        private static bool IsInPrefabMode(GameObject gameObject)
        {
#if UNITY_EDITOR
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return true;
            }

            var prefabAssetType = PrefabUtility.GetPrefabAssetType(gameObject);

            if (prefabAssetType != PrefabAssetType.NotAPrefab)
            {
                return true;
            }

            var prefabInstanceStatus = PrefabUtility.GetPrefabInstanceStatus(gameObject);
            return prefabInstanceStatus != PrefabInstanceStatus.NotAPrefab;
#else
            return false;
#endif
        }

        private static void SavePrefabChanges(GameObject prefabObject)
        {
#if UNITY_EDITOR
            try
            {
                EditorUtility.SetDirty(prefabObject);
                var prefabInstanceStatus = PrefabUtility.GetPrefabInstanceStatus(prefabObject);

                if (prefabInstanceStatus == PrefabInstanceStatus.Connected)
                {
                    var rootPrefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefabObject);

                    if (rootPrefab != null)
                    {
                        PrefabUtility.ApplyPrefabInstance(prefabObject,
                            InteractionMode.AutomatedAction);
                    }
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Debug.Log("预制体修改已保存");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"保存预制体时出错: {e.Message}");
            }
#endif
        }

        #endregion

        private static string GetPrefixNameByCodeStyle(UIFieldCodeStyle style)
        {
            return ScriptGeneratorSetting.GetPrefixNameByCodeStyle(style);
        }

        private static string GetUIWidgetName()
        {
            return GetComponentName(ScriptGeneratorSetting.GetWidgetName());
        }

        private static string GetComponentName(string componentName)
        {
            return GetPrefixName() + componentName;
        }

        private static string GetPrefixName()
        {
            return ScriptGeneratorSetting.GetPrefixNameByCodeStyle(ScriptGeneratorSetting.Instance.CodeStyle);
        }

        private static string GetVariableName(string varName)
        {
            if (string.IsNullOrEmpty(varName))
            {
                return varName;
            }

            for (int i = 0; i < VARIABLE_NAME_REGEX.Length; i++)
            {
                var prefix = VARIABLE_NAME_REGEX[i];
                if (varName.StartsWith(prefix))
                {
                    varName = varName.Replace(prefix, string.Empty);
                    varName = GetComponentName(varName);
                    break;
                }
            }
            return varName;
        }
    }
}