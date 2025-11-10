using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TEngine.UI.Editor;
using TEngine;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

public enum EBindType
{
    None,
    Widget,
    ListCom,
}

namespace TEngine.UI.Editor
{
    [Serializable]
    class UIBindData
    {
        public string Name;
        public List<Component> BindCom;
        public EBindType BindType;

        public UIBindData(string name, List<Component> bindCom, EBindType bindType = EBindType.None)
        {
            Name = name;
            BindCom = bindCom;
            BindType = bindType;
        }

        public UIBindData(string name, Component bindCom, EBindType bindType = EBindType.None)
        {
            Name = name;
            BindCom = new List<Component>() { bindCom };
            BindType = bindType;
        }
    }


    internal static class UIScriptGeneratorHelper
    {
        private static UIGenerateConfiguration _uiGenerateConfiguration;
        private static IUIGeneratorRuleHelper _uiGeneratorRuleHelper;

        /// <summary>
        /// 设置自定义命名规则助手[4](@ref)
        /// </summary>
        public static IUIGeneratorRuleHelper UIGeneratorRuleHelper
        {
            get
            {
                if (_uiGeneratorRuleHelper == null || (_uiGeneratorRuleHelper != null && !UIConfiguration.UIScriptGeneratorRuleHelper.Equals(_uiGeneratorRuleHelper.GetType().FullName)))
                {
                    Type ruleHelperType = Type.GetType(UIConfiguration.UIScriptGeneratorRuleHelper);
                    if (ruleHelperType == null)
                    {
                        Debug.LogError($"UIScriptGeneratorHelper: Could not load UI ScriptGeneratorHelper {UIConfiguration.UIScriptGeneratorRuleHelper}");
                        return null;
                    }

                    _uiGeneratorRuleHelper = Activator.CreateInstance(ruleHelperType) as IUIGeneratorRuleHelper;
                }

                return _uiGeneratorRuleHelper;
            }
        }

        private static UIGenerateConfiguration UIConfiguration
        {
            get
            {
                if (_uiGenerateConfiguration == null)
                {
                    _uiGenerateConfiguration = UIGenerateConfiguration.Instance;
                }

                return _uiGenerateConfiguration;
            }
        }

        private static string GetVersionType(string uiName)
        {
            foreach (var pair in UIConfiguration.UIElementRegexConfigs)
            {
                if (uiName.StartsWith(pair.uiElementRegex, StringComparison.Ordinal))
                {
                    return pair.componentType;
                }
            }

            return string.Empty;
        }

        private static string[] SplitComponentName(string name)
        {
            bool hasCom = name.Contains(UIConfiguration.UIGenerateCommonData.ComCheckEndName);
            if (!hasCom) return null;

            string comStr = name.Substring(0,
                name.IndexOf(UIConfiguration.UIGenerateCommonData.ComCheckEndName, StringComparison.Ordinal));
            return comStr.Split(UIConfiguration.UIGenerateCommonData.ComCheckSplitName);
        }

        private static string GetKeyName(string key, string componentName, EBindType bindType)
        {
            return UIGeneratorRuleHelper.GetPrivateComponentByNameRule(key, componentName, bindType);
        }

        private static List<UIBindData> _uiBindDatas = new List<UIBindData>();
        private static List<string> _arrayComponents = new List<string>();

        private static void GetBindData(Transform root)
        {
            for (int i = 0; i < root.childCount; ++i)
            {
                Transform child = root.GetChild(i);

                bool hasWidget = child.GetComponent<UIHolderObjectBase>() != null;

                if (UIConfiguration.UIGenerateCommonData.ExcludeKeywords.Any(k =>
                        child.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                bool isArrayComponent = child.name.StartsWith(
                    UIConfiguration.UIGenerateCommonData.ArrayComSplitName, StringComparison.Ordinal);

                if (hasWidget)
                {
                    CollectWidget(child);
                }
                else if (isArrayComponent)
                {
                    string splitCode = UIConfiguration.UIGenerateCommonData.ArrayComSplitName;
                    int lastIndex = child.name.LastIndexOf(splitCode, StringComparison.Ordinal);
                    string text = child.name.Substring(
                        child.name.IndexOf(splitCode, StringComparison.Ordinal) + 1,
                        lastIndex - 1);

                    if (_arrayComponents.Contains(text)) continue;
                    _arrayComponents.Add(text);

                    List<Transform> arrayComponents = new List<Transform>();
                    for (int j = 0; j < root.childCount; j++)
                    {
                        if (root.GetChild(j).name.Contains(text, StringComparison.Ordinal))
                        {
                            arrayComponents.Add(root.GetChild(j));
                        }
                    }

                    CollectArrayComponent(arrayComponents, text);
                }
                else if (!isArrayComponent && !hasWidget)
                {
                    CollectComponent(child);
                    GetBindData(child);
                }
            }
        }

        private static void CollectComponent(Transform node)
        {
            string[] componentArray = SplitComponentName(node.name);
            if (componentArray != null)
            {
                foreach (var com in componentArray)
                {
                    string typeName = GetVersionType(com);
                    if (string.IsNullOrEmpty(typeName)) continue;

                    Component component = node.GetComponent(typeName);
                    if (component != null)
                    {
                        string keyName = GetKeyName(com, node.name, EBindType.None);
                        if (_uiBindDatas.Exists(a => a.Name == keyName))
                        {
                            Debug.LogError($"Duplicate key found: {keyName}");
                            continue;
                        }

                        _uiBindDatas.Add(new UIBindData(keyName, component));
                    }
                    else
                    {
                        Debug.LogError($"{node.name} does not have component of type {typeName}");
                    }
                }
            }
        }

        private static void CollectWidget(Transform node)
        {
            if (node.name.IndexOf(UIConfiguration.UIGenerateCommonData.ComCheckEndName, StringComparison.Ordinal) != -1 &&
                node.name.IndexOf(UIConfiguration.UIGenerateCommonData.ComCheckSplitName, StringComparison.Ordinal) != -1)
            {
                Debug.LogWarning($"{node.name} child component cannot contain rule definition symbols!");
                return;
            }

            UIHolderObjectBase component = node.GetComponent<UIHolderObjectBase>();
            string keyName = GetKeyName(string.Empty, node.name, EBindType.Widget);

            if (_uiBindDatas.Exists(a => a.Name == keyName))
            {
                Debug.LogError($"Duplicate key found: {keyName}");
                return;
            }

            _uiBindDatas.Add(new UIBindData(keyName, component, EBindType.Widget));
        }

        private static void CollectArrayComponent(List<Transform> arrayNode, string nodeName)
        {
            string[] componentArray = SplitComponentName(nodeName);
            arrayNode = arrayNode.OrderBy(s => int.Parse(s.name.Split('*').Last(),
                System.Globalization.CultureInfo.InvariantCulture)).ToList();

            List<UIBindData> tempBindDatas = new List<UIBindData>(componentArray.Length);

            if (componentArray != null)
            {
                int index = 0;
                foreach (var com in componentArray)
                {
                    foreach (var node in arrayNode)
                    {
                        string typeName = GetVersionType(com);
                        if (string.IsNullOrEmpty(typeName)) continue;

                        Component component = node.GetComponent(typeName);
                        if (component != null)
                        {
                            string keyName = GetKeyName(com, nodeName, EBindType.ListCom);
                            if (tempBindDatas.Count - 1 < index)
                                tempBindDatas.Add(new UIBindData(keyName, new List<Component>(), EBindType.ListCom));

                            tempBindDatas[index].BindCom.Add(component);
                        }
                        else
                        {
                            Debug.LogError($"{node.name} does not have component of type {typeName}");
                        }
                    }

                    index++;
                }
            }

            _uiBindDatas.AddRange(tempBindDatas.ToArray());
        }

        private static string GetReferenceNamespace()
        {
            StringBuilder referenceNamespaceBuilder = new StringBuilder();
            HashSet<string> namespaces = new HashSet<string>();
            namespaces.Add("UnityEngine");
            referenceNamespaceBuilder.Append("using UnityEngine;\n");

            foreach (var bindData in _uiBindDatas)
            {
                string nameSpace = bindData.BindCom.FirstOrDefault()?.GetType().Namespace;
                if (bindData.BindType == EBindType.ListCom)
                {
                    if (!namespaces.Contains("System.Collections.Generic"))
                    {
                        referenceNamespaceBuilder.Append("using System.Collections.Generic;\n");
                        namespaces.Add("System.Collections.Generic");
                    }
                }

                if (!string.IsNullOrEmpty(nameSpace) && !namespaces.Contains(nameSpace))
                {
                    namespaces.Add(nameSpace);
                    referenceNamespaceBuilder.Append($"using {nameSpace};\n");
                }
            }

            return referenceNamespaceBuilder.ToString();
        }

        private static string GetVariableText(List<UIBindData> uiBindDatas)
        {
            StringBuilder variableTextBuilder = new StringBuilder();
            foreach (var bindData in uiBindDatas)
            {
                string variableName = bindData.Name;
                string publicName = UIGeneratorRuleHelper.GetPublicComponentByNameRule(variableName);
                variableTextBuilder.Append("\t\t[SerializeField]\n");

                if (bindData.BindType == EBindType.None)
                {
                    variableTextBuilder.Append(
                        $"\t\tprivate {bindData.BindCom.FirstOrDefault()?.GetType().Name} {variableName};\n");
                    variableTextBuilder.Append(
                        $"\t\tpublic {bindData.BindCom.FirstOrDefault()?.GetType().Name} {publicName} => {variableName};\n\n");
                }
                else if (bindData.BindType == EBindType.ListCom)
                {
                    variableTextBuilder.Append(
                        $"\t\tprivate {bindData.BindCom.FirstOrDefault()?.GetType().Name}[] {variableName} = " +
                        $"new {bindData.BindCom.FirstOrDefault()?.GetType().Name}[{bindData.BindCom.Count}];\n");
                    variableTextBuilder.Append(
                        $"\t\tpublic {bindData.BindCom.FirstOrDefault()?.GetType().Name}[] {publicName} => {variableName};\n\n");
                }
                else if (bindData.BindType == EBindType.Widget)
                {
                    variableTextBuilder.Append(
                        $"\t\tprivate {bindData.BindCom.FirstOrDefault()?.GetType().Name} {variableName};\n");
                    variableTextBuilder.Append(
                        $"\t\tpublic {bindData.BindCom.FirstOrDefault()?.GetType().Name} {publicName} => {variableName};\n\n");
                }
            }

            return variableTextBuilder.ToString();
        }

        private static string GenerateScript(string className, string generateNameSpace)
        {
            StringBuilder scriptBuilder = new StringBuilder();
            scriptBuilder.Append(GetReferenceNamespace());

            scriptBuilder.Append("using TEngine;\n");
            scriptBuilder.Append($"namespace {generateNameSpace}\n");
            scriptBuilder.Append("{\n");
            scriptBuilder.Append("\t#Attribute#\n");
            scriptBuilder.Append($"\tpublic class {className} : UIHolderObjectBase\n");
            scriptBuilder.Append("\t{\n");

            scriptBuilder.Append("\t\tpublic const string ResTag = #Tag#;\n");

            scriptBuilder.Append("\t\t#region Generated by Script Tool\n\n");
            scriptBuilder.Append(GetVariableText(_uiBindDatas));
            scriptBuilder.Append("\n\t\t#endregion\n");

            scriptBuilder.Append("\t}\n");
            scriptBuilder.Append("}\n");

            return scriptBuilder.ToString();
        }

        public static void GenerateAndAttachScript(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (!PrefabChecker.IsPrefabAsset(targetObject))
            {
                Debug.LogWarning("请将UI界面保存为对应的目录Prefab 在进行代码生成");
                return;
            }

            if (!UIGeneratorRuleHelper.CheckCanGenerate(targetObject, scriptGenerateData))
            {
                return;
            }

            EditorPrefs.SetInt("InstanceId", targetObject.GetInstanceID());
            _uiBindDatas.Clear();
            _arrayComponents.Clear();

            string className = UIGeneratorRuleHelper.GetClassGenerateName(targetObject, scriptGenerateData);

            GetBindData(targetObject.transform);

            string scriptContent = GenerateScript(className, scriptGenerateData.NameSpace);
            string tagName = $"\"{UIGeneratorRuleHelper.GetUIResourceSavePath(targetObject, scriptGenerateData)}\"";

            string uiAttribute = $"[UIRes({className}.ResTag, EUIResLoadType.{scriptGenerateData.LoadType})]";

            scriptContent = scriptContent.Replace("#Attribute#", uiAttribute);
            scriptContent = scriptContent.Replace("#Tag#", tagName);

            UIGeneratorRuleHelper.WriteUIScriptContent(className, scriptContent, scriptGenerateData);
        }

        [DidReloadScripts]
        public static void CheckHasAttach()
        {
            bool has = EditorPrefs.HasKey("Generate");
            if (has)
            {
                _uiBindDatas.Clear();
                _arrayComponents.Clear();
                string className = EditorPrefs.GetString("Generate");
                int instanceId = EditorPrefs.GetInt("InstanceId", -1);

                if (instanceId == -1)
                {
                    return;
                }

                GameObject targetObject = (GameObject)EditorUtility.InstanceIDToObject(instanceId);

                if (!targetObject)
                {
                    Debug.Log("UI script generation attachment object missing!");
                }

                EditorPrefs.DeleteKey("Generate");
                GetBindData(targetObject.transform);

                AttachScriptToGameObject(targetObject, className);
                Debug.Log($"Generate {className} Successfully attached to game object");
            }
        }

        private static void AttachScriptToGameObject(GameObject targetObject, string scriptClassName)
        {
            Type scriptType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName.Contains("Editor")) continue;
                Type[] types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.IsClass && !type.IsAbstract && type.Name.Contains(scriptClassName, StringComparison.Ordinal))
                    {
                        scriptType = type;
                    }
                }
            }

            if (scriptType != null)
            {
                Component component = targetObject.GetOrAddComponent(scriptType);
                FieldInfo[] fields = scriptType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (FieldInfo field in fields)
                {
                    List<Component> componentInObjects = _uiBindDatas.Find(data => data.Name == field.Name)?.BindCom;
                    if (componentInObjects != null)
                    {
                        if (field.FieldType.IsArray)
                        {
                            Type elementType = field.FieldType.GetElementType();
                            Array array = Array.CreateInstance(elementType, componentInObjects.Count);

                            for (int i = 0; i < componentInObjects.Count; i++)
                            {
                                Component comp = componentInObjects[i];
                                if (elementType.IsInstanceOfType(comp))
                                {
                                    array.SetValue(comp, i);
                                }
                                else
                                {
                                    Debug.LogError($"Element {i} type mismatch, expected {elementType.Name}, actual {comp.GetType().Name}");
                                }
                            }

                            field.SetValue(component, array);
                        }
                        else
                        {
                            if (componentInObjects.Count > 0)
                            {
                                if (field.FieldType.IsInstanceOfType(componentInObjects[0]))
                                {
                                    field.SetValue(component, componentInObjects[0]);
                                }
                                else
                                {
                                    Debug.LogError($"Field {field.Name} type mismatch, cannot assign value");
                                }
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError($"Field {field.Name} did not find matching component binding");
                    }
                }
            }
            else
            {
                Debug.LogError($"Could not find the class: {scriptClassName}");
            }
        }

        public static class PrefabChecker
        {
            public static bool IsEditingPrefabAsset(GameObject go)
            {
                // 检查当前是否在Prefab编辑模式
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage == null)
                    return false;

                // 检查选中对象是否属于当前PrefabStage
                return prefabStage.IsPartOfPrefabContents(go);
            }

            public static bool IsPrefabAsset(GameObject go)
            {
                // 普通Asset目录中的Prefab
                var assetType = PrefabUtility.GetPrefabAssetType(go);
                if (assetType == PrefabAssetType.Regular ||
                    assetType == PrefabAssetType.Variant ||
                    assetType == PrefabAssetType.Model)
                    return true;

                // Prefab编辑模式下
                return IsEditingPrefabAsset(go);
            }
        }
    }
}
