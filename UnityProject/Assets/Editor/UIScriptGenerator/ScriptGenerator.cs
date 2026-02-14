using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor.UI
{
    public partial class ScriptGenerator
    {
        private const string Gap = "/";

        [MenuItem("GameObject/ScriptGenerator/UIProperty", priority = 41)]
        public static void MemberProperty()
        {
            Generate(false);
        }

        [MenuItem("GameObject/ScriptGenerator/UIProperty", true, priority = 41)]
        public static bool ValidateMemberProperty()
        {
            return !ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIProperty - UniTask", priority = 43)]
        public static void MemberPropertyUniTask()
        {
            Generate(false, true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIProperty - UniTask", true, priority = 41)]
        public static bool ValidateMemberPropertyUniTask()
        {
            return !ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener", priority = 42)]
        public static void MemberPropertyAndListener()
        {
            Generate(true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener", true, priority = 41)]
        public static bool ValidateMemberPropertyAndListener()
        {
            return !ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener - UniTask", priority = 44)]
        public static void MemberPropertyAndListenerUniTask()
        {
            Generate(true, true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener - UniTask", true, priority = 41)]
        public static bool ValidateMemberPropertyAndListenerUniTask()
        {
            return !ScriptGeneratorSetting.Instance.UseBindComponent;
        }

        private static void Generate(bool includeListener, bool isUniTask = false)
        {
            var root = Selection.activeTransform;
            if (root != null)
            {
                StringBuilder strVar = new StringBuilder();
                StringBuilder strBind = new StringBuilder();
                StringBuilder strOnCreate = new StringBuilder();
                StringBuilder strCallback = new StringBuilder();
                Ergodic(root, root, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
                StringBuilder strFile = new StringBuilder();

                if (includeListener)
                {
#if TextMeshPro
                    strFile.Append("using TMPro;\n");
#endif
                    if (isUniTask)
                    {
                        strFile.Append("using Cysharp.Threading.Tasks;\n");
                    }

                    strFile.Append("using UnityEngine;\n");
                    strFile.Append("using UnityEngine.UI;\n");
                    strFile.Append("using TEngine;\n\n");
                    strFile.Append($"namespace {ScriptGeneratorSetting.GetUINameSpace()}\n");
                    strFile.Append("{\n");

                    var widgetPrefix = $"{(ScriptGeneratorSetting.GetCodeStyle() == UIFieldCodeStyle.MPrefix ? "m_" : "_")}{ScriptGeneratorSetting.GetWidgetName()}";
                    if (root.name.StartsWith(widgetPrefix))
                    {
                        strFile.Append("\tclass " + root.name.Replace(widgetPrefix, "") + " : UIWidget\n");
                    }
                    else
                    {
                        strFile.Append($"\t[Window(UILayer.UI,location:\"{root.name}\")]\n");
                        strFile.Append("\tclass " + root.name + " : UIWindow\n");
                    }

                    strFile.Append("\t{\n");
                }

                // 脚本工具生成的代码
                strFile.Append("\t\t#region 脚本工具生成的代码\n");
                strFile.Append("\n");
                strFile.Append(strVar);
                strFile.Append("\n");
                strFile.Append("\t\tprotected override void ScriptGenerator()\n");
                strFile.Append("\t\t{\n");
                strFile.Append(strBind);
                strFile.Append(strOnCreate);
                strFile.Append("\t\t}\n");
                strFile.Append("\n");
                strFile.Append("\t\t#endregion");

                if (includeListener)
                {
                    strFile.Append("\n\n");
                    // #region 事件
                    strFile.Append("\t\t#region 事件\n");
                    strFile.Append(strCallback);
                    strFile.Append("\t\t#endregion\n\n");

                    strFile.Append("\t}\n");
                    strFile.Append("}\n");
                }

                TextEditor te = new TextEditor();
                te.text = strFile.ToString();
                te.SelectAll();
                te.Copy();
            }

            Debug.Log($"脚本已生成到剪贴板，请自行Ctl+V粘贴");
        }

        public static void Ergodic(Transform root, Transform transform, ref StringBuilder strVar, ref StringBuilder strBind, ref StringBuilder strOnCreate,
            ref StringBuilder strCallback, bool isUniTask)
        {
            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                WriteScript(root, child, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
                if (child.name.StartsWith(GetUIWidgetName()))
                {
                    continue;
                }

                Ergodic(root, child, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
            }
        }

        private static string GetRelativePath(Transform child, Transform root)
        {
            StringBuilder path = new StringBuilder();
            path.Append(child.name);
            while (child.parent != null && child.parent != root)
            {
                child = child.parent;
                path.Insert(0, Gap);
                path.Insert(0, child.name);
            }

            return path.ToString();
        }

        public static string GetBtnFuncName(string varName)
        {
            var codeStyle = ScriptGeneratorSetting.Instance.CodeStyle;
            if (codeStyle == UIFieldCodeStyle.MPrefix)
            {
                return "OnClick" + varName.Replace("m_btn", string.Empty) + "Btn";
            }
            else
            {
                return "OnClick" + varName.Replace("_btn", string.Empty) + "Btn";
            }
        }

        public static string GetToggleFuncName(string varName)
        {
            var codeStyle = ScriptGeneratorSetting.Instance.CodeStyle;
            if (codeStyle == UIFieldCodeStyle.MPrefix)
            {
                return "OnToggle" + varName.Replace("m_toggle", string.Empty) + "Change";
            }
            else
            {
                return "OnToggle" + varName.Replace("_toggle", string.Empty) + "Change";
            }
        }

        public static string GetSliderFuncName(string varName)
        {
            var codeStyle = ScriptGeneratorSetting.Instance.CodeStyle;
            if (codeStyle == UIFieldCodeStyle.MPrefix)
            {
                return "OnSlider" + varName.Replace("m_slider", string.Empty) + "Change";
            }
            else
            {
                return "OnSlider" + varName.Replace("_slider", string.Empty) + "Change";
            }
        }

        private static void WriteScript(Transform root, Transform child, ref StringBuilder strVar, ref StringBuilder strBind, ref StringBuilder strOnCreate,
            ref StringBuilder strCallback, bool isUniTask)
        {
            string varName = child.name;

            string componentName = string.Empty;

            var rule = ScriptGeneratorSetting.GetScriptGenerateRule().Find(t => varName.StartsWith(t.uiElementRegex));

            if (rule != null)
            {
                componentName = rule.componentName.ToString();
            }

            bool isUIWidget = rule is { isUIWidget: true };

            if (componentName == string.Empty)
            {
                return;
            }

            var codeStyle = ScriptGeneratorSetting.Instance.CodeStyle;
            if (codeStyle == UIFieldCodeStyle.UnderscorePrefix)
            {
                if (varName.StartsWith("_"))
                {
                }
                else if (varName.StartsWith("m_"))
                {
                    varName = varName.Substring(1);
                }
                else
                {
                    varName = $"_{varName}";
                }
            }
            else if (codeStyle == UIFieldCodeStyle.MPrefix)
            {
                if (varName.StartsWith("m_"))
                {
                }
                else if (varName.StartsWith("_"))
                {
                    varName = $"m{varName}";
                }
                else
                {
                    varName = $"m_{varName}";
                }
            }

            string varPath = GetRelativePath(child, root);
            if (!string.IsNullOrEmpty(varName))
            {
                strVar.Append("\t\tprivate " + componentName + " " + varName + (ScriptGeneratorSetting.Instance.NullableEnable ? " = null!;" : ";") + "\n");
                switch (componentName)
                {
                    case "Transform":
                        strBind.Append($"\t\t\t{varName} = FindChild(\"{varPath}\");\n");
                        break;
                    case "GameObject":
                        strBind.Append($"\t\t\t{varName} = FindChild(\"{varPath}\").gameObject;\n");
                        break;
                    case "AnimationCurve":
                        strBind.Append($"\t\t\t{varName} = FindChildComponent<AnimCurveObject>(\"{varPath}\").m_animCurve;\n");
                        break;
                    default:
                        if (isUIWidget)
                        {
                            strBind.Append($"\t\t\t{varName} = CreateWidget<{componentName}>(\"{varPath}\");\n");
                        }
                        else
                        {
                            strBind.Append($"\t\t\t{varName} = FindChildComponent<{componentName}>(\"{varPath}\");\n");
                        }
                        break;
                }

                if (componentName == "Button")
                {
                    string varFuncName = GetBtnFuncName(varName);
                    if (isUniTask)
                    {
                        strOnCreate.Append($"\t\t\t{varName}.onClick.AddListener(UniTask.UnityAction({varFuncName}));\n");
                        strCallback.Append($"\t\tprivate async UniTaskVoid {varFuncName}()\n");
                        strCallback.Append("\t\t{\n await UniTask.Yield();\n\t\t}\n");
                    }
                    else
                    {
                        strOnCreate.Append($"\t\t\t{varName}.onClick.AddListener({varFuncName});\n");
                        strCallback.Append($"\t\tprivate void {varFuncName}()\n");
                        strCallback.Append("\t\t{\n\t\t}\n");
                    }
                }
                else if (componentName == "Toggle")
                {
                    string varFuncName = GetToggleFuncName(varName);
                    strOnCreate.Append($"\t\t\t{varName}.onValueChanged.AddListener({varFuncName});\n");
                    strCallback.Append($"\t\tprivate void {varFuncName}(bool isOn)\n");
                    strCallback.Append("\t\t{\n\t\t}\n");
                }
                else if (componentName == "Slider")
                {
                    string varFuncName = GetSliderFuncName(varName);
                    strOnCreate.Append($"\t\t\t{varName}.onValueChanged.AddListener({varFuncName});\n");
                    strCallback.Append($"\t\tprivate void {varFuncName}(float value)\n");
                    strCallback.Append("\t\t{\n\t\t}\n");
                }
            }
        }

        public class GeneratorHelper 
        {
            [MenuItem("GameObject/ScriptGenerator/About", priority = 49)]
            public static void About()
            {
                TEngineUISettingsProvider.OpenSettings();
            }
        }
    }
}