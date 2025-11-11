using System;
using System.Collections.Generic;
using TEngine.Editor;
using TEngine;
using UnityEngine;
using UnityEditor;
using UnityEngine.Serialization;

namespace TEngine.UI.Editor
{
    [TEngine.Editor.FilePath("ProjectSettings/UIGenerateConfiguration.asset")]
    internal class UIGenerateConfiguration : EditorScriptableSingleton<UIGenerateConfiguration>
    {
        [Header("通用生成配置")] public UIGenerateCommonData UIGenerateCommonData = new UIGenerateCommonData();

        [Header("UI生成规则（根据正则匹配）")] public List<UIEelementRegexData> UIElementRegexConfigs = new List<UIEelementRegexData>();

        [Header("UI脚本生成配置（支持多个项目）")] public List<UIScriptGenerateData> UIScriptGenerateConfigs = new List<UIScriptGenerateData>();

        [Header("UI脚本生成辅助类")] public string UIScriptGeneratorRuleHelper;
    }

    [Serializable]
    public class UIGenerateCommonData
    {
        [Header("命名规则")] [Tooltip("组件检查分隔符，例如：Button#Close")]
        public string ComCheckSplitName = "#";

        [Tooltip("组件结尾分隔符，例如：@End")] public string ComCheckEndName = "@";

        [Tooltip("数组组件检查分隔符，例如：*Item")] public string ArrayComSplitName = "*";

        [Tooltip("生成脚本前缀")] public string GeneratePrefix = "ui";

        [Tooltip("排除的关键字（匹配则不生成）")] public string[] ExcludeKeywords = { "ViewHolder" };

        [Tooltip("路径拼接映射，如：Window -> Window 文件夹")]
        public List<StringPair> CombineWords = new List<StringPair>()
        {
            new StringPair("Window", "Window"),
            new StringPair("ViewHolder", "ViewHolder"),
            new StringPair("Widget", "Widget"),
        };
    }

    [Serializable]
    public class UIEelementRegexData
    {
        [Tooltip("匹配UI元素名称的正则表达式")] public string uiElementRegex;

        [Tooltip("匹配到的UI组件类型")] public string componentType;
    }

    [Serializable]
    public class UIScriptGenerateData
    {
        [Header("项目识别信息")] [Tooltip("该UI工程的名称（例如：MainProject, HotFix, EditorUI）")]
        public string ProjectName = "MainProject";

        [Tooltip("该UI工程所属命名空间")] public string NameSpace = "Game.UI";

        [Header("路径设置")] [Tooltip("生成的UI脚本路径（相对Assets）")]
        public string GenerateHolderCodePath = "Assets/Scripts/UI/Generated";

        [Tooltip("UI Prefab根目录")] public string UIPrefabRootPath = "Assets/Resources/UI/";

        [Header("加载类型")] [Tooltip("UI资源加载方式（本地 / YooAsset ）")]
        public EUIResLoadType LoadType = EUIResLoadType.Resources;
    }

    [Serializable]
    public class StringPair
    {
        public string Key;
        public string Value;

        public StringPair(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
