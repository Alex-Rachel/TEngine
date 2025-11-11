using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;

namespace TEngine.UI.Editor
{
    public enum EBindType
    {
        None,
        Widget,
        ListCom,
    }


    [Serializable]
    internal class UIBindData
    {
        public string Name;
        public List<Component> BindCom;
        public EBindType BindType;

        public UIBindData(string name, List<Component> bindCom, EBindType bindType = EBindType.None)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            BindCom = bindCom ?? new List<Component>();
            BindType = bindType;
        }

        public UIBindData(string name, Component bindCom, EBindType bindType = EBindType.None)
            : this(name, new List<Component> { bindCom }, bindType)
        {
        }
    }

    internal static class UIScriptGeneratorHelper
    {
        private static UIGenerateConfiguration _uiGenerateConfiguration;
        private static IUIGeneratorRuleHelper _uiGeneratorRuleHelper;

        private static List<UIBindData> _uiBindDatas = new List<UIBindData>();
        private static HashSet<string> _arrayComponents = new HashSet<string>(StringComparer.Ordinal);

        public static IUIGeneratorRuleHelper UIGeneratorRuleHelper
        {
            get
            {
                if (_uiGeneratorRuleHelper == null ||
                    (_uiGeneratorRuleHelper != null && !UIConfiguration.UIScriptGeneratorRuleHelper.Equals(_uiGeneratorRuleHelper.GetType().FullName, StringComparison.Ordinal)))
                {
                    var ruleHelperTypeName = UIConfiguration.UIScriptGeneratorRuleHelper;
                    if (string.IsNullOrWhiteSpace(ruleHelperTypeName))
                    {
                        Debug.LogError("UIScriptGeneratorHelper: UIScriptGeneratorRuleHelper not configured.");
                        return null;
                    }

                    Type ruleHelperType = Type.GetType(ruleHelperTypeName);
                    if (ruleHelperType == null)
                    {
                        Debug.LogError($"UIScriptGeneratorHelper: Could not load UI ScriptGeneratorHelper {ruleHelperTypeName}");
                        return null;
                    }

                    _uiGeneratorRuleHelper = Activator.CreateInstance(ruleHelperType) as IUIGeneratorRuleHelper;
                    if (_uiGeneratorRuleHelper == null)
                    {
                        Debug.LogError($"UIScriptGeneratorHelper: Failed to instantiate {ruleHelperTypeName} as IUIGeneratorRuleHelper.");
                    }
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
            if (string.IsNullOrEmpty(uiName)) return string.Empty;
            foreach (var pair in UIConfiguration.UIElementRegexConfigs ?? Enumerable.Empty<UIEelementRegexData>())
            {
                if (string.IsNullOrEmpty(pair?.uiElementRegex)) continue;
                if (uiName.StartsWith(pair.uiElementRegex, StringComparison.Ordinal))
                {
                    return pair.componentType ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string[] SplitComponentName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var common = UIConfiguration.UIGenerateCommonData;
            if (string.IsNullOrEmpty(common?.ComCheckEndName) || !name.Contains(common.ComCheckEndName))
                return null;

            int endIndex = name.IndexOf(common.ComCheckEndName, StringComparison.Ordinal);
            if (endIndex <= 0) return null;

            string comStr = name.Substring(0, endIndex);
            string split = common.ComCheckSplitName ?? "#";
            // 使用 string[] 重载并移除空项，防止错误 overload
            return comStr.Split(new[] { split }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetKeyName(string key, string componentName, EBindType bindType)
        {
            var helper = UIGeneratorRuleHelper;
            if (helper == null)
                throw new InvalidOperationException("UIGeneratorRuleHelper is not configured.");
            return helper.GetPrivateComponentByNameRule(key, componentName, bindType);
        }

        private static void GetBindData(Transform root)
        {
            if (root == null) return;

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform child = root.GetChild(i);
                if (child == null) continue;

                // 排除关键字
                if (UIConfiguration.UIGenerateCommonData.ExcludeKeywords != null &&
                    UIConfiguration.UIGenerateCommonData.ExcludeKeywords.Any(k =>
                        !string.IsNullOrEmpty(k) && child.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                bool hasWidget = child.GetComponent<UIHolderObjectBase>() != null;
                bool isArrayComponent = !string.IsNullOrEmpty(UIConfiguration.UIGenerateCommonData.ArrayComSplitName) &&
                                        child.name.StartsWith(UIConfiguration.UIGenerateCommonData.ArrayComSplitName, StringComparison.Ordinal);

                if (hasWidget)
                {
                    CollectWidget(child);
                }
                else if (isArrayComponent)
                {
                    // 提取 array 标识文本（例如 "*Item*0" -> "Item"）
                    string splitCode = UIConfiguration.UIGenerateCommonData.ArrayComSplitName;
                    int firstIndex = child.name.IndexOf(splitCode, StringComparison.Ordinal);
                    int lastIndex = child.name.LastIndexOf(splitCode, StringComparison.Ordinal);
                    if (firstIndex < 0 || lastIndex <= firstIndex) continue;

                    // 中间文本
                    string text = child.name.Substring(firstIndex + splitCode.Length, lastIndex - (firstIndex + splitCode.Length));
                    if (string.IsNullOrEmpty(text)) continue;

                    if (_arrayComponents.Contains(text)) continue;
                    _arrayComponents.Add(text);

                    // 在同一个父节点下收集包含该 text 的同级节点
                    List<Transform> arrayComponents = new List<Transform>();
                    for (int j = 0; j < root.childCount; j++)
                    {
                        Transform sibling = root.GetChild(j);
                        if (sibling != null && sibling.name.Contains(text, StringComparison.Ordinal))
                        {
                            arrayComponents.Add(sibling);
                        }
                    }

                    CollectArrayComponent(arrayComponents, text);
                }
                else // 普通组件/进一步递归
                {
                    CollectComponent(child);
                    GetBindData(child);
                }
            }
        }

        private static void CollectComponent(Transform node)
        {
            if (node == null) return;

            string[] componentArray = SplitComponentName(node.name);
            if (componentArray == null || componentArray.Length == 0) return;

            foreach (var com in componentArray)
            {
                if (string.IsNullOrEmpty(com)) continue;
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

        private static void CollectWidget(Transform node)
        {
            if (node == null) return;

            var common = UIConfiguration.UIGenerateCommonData;
            if (node.name.IndexOf(common.ComCheckEndName, StringComparison.Ordinal) != -1 &&
                node.name.IndexOf(common.ComCheckSplitName, StringComparison.Ordinal) != -1)
            {
                Debug.LogWarning($"{node.name} child component cannot contain rule definition symbols!");
                return;
            }

            UIHolderObjectBase component = node.GetComponent<UIHolderObjectBase>();
            if (component == null)
            {
                Debug.LogError($"{node.name} expected to be a widget but does not have UIHolderObjectBase.");
                return;
            }

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
            if (arrayNode == null || arrayNode.Count == 0) return;

            // 从 nodeName（例如 "*Item*0"）取出组件描述部分（即名字中 @End 之前那部分）
            string[] componentArray = SplitComponentName(nodeName);

            // 对 arrayNode 做基于后缀的安全排序：提取 last segment 作为索引（int.TryParse）
            string splitCode = UIConfiguration.UIGenerateCommonData.ArrayComSplitName;
            var orderedNodes = arrayNode
                .Select(n => new { Node = n, RawIndex = ExtractArrayIndex(n.name, splitCode) })
                .OrderBy(x => x.RawIndex.HasValue ? x.RawIndex.Value : int.MaxValue)
                .Select(x => x.Node)
                .ToList();

            if (componentArray == null || componentArray.Length == 0)
            {
                Debug.LogWarning($"CollectArrayComponent: {nodeName} has no component definitions.");
                return;
            }

            // 准备临时 bind 列表，每个 componentArray 项对应一个 UIBindData
            List<UIBindData> tempBindDatas = new List<UIBindData>(componentArray.Length);
            for (int i = 0; i < componentArray.Length; i++)
            {
                string keyNamePreview = GetKeyName(componentArray[i], nodeName, EBindType.ListCom);
                tempBindDatas.Add(new UIBindData(keyNamePreview, new List<Component>(), EBindType.ListCom));
            }

            // 遍历元素并填充
            for (int index = 0; index < componentArray.Length; index++)
            {
                string com = componentArray[index];
                if (string.IsNullOrEmpty(com)) continue;

                string typeName = GetVersionType(com);
                if (string.IsNullOrEmpty(typeName)) continue;

                foreach (var node in orderedNodes)
                {
                    Component component = node.GetComponent(typeName);
                    if (component != null)
                    {
                        tempBindDatas[index].BindCom.Add(component);
                    }
                    else
                    {
                        Debug.LogError($"{node.name} does not have component of type {typeName}");
                    }
                }
            }

            // 将结果合并到全局绑定数据
            _uiBindDatas.AddRange(tempBindDatas);
        }

        private static int? ExtractArrayIndex(string nodeName, string splitCode)
        {
            if (string.IsNullOrEmpty(nodeName) || string.IsNullOrEmpty(splitCode)) return null;
            int last = nodeName.LastIndexOf(splitCode, StringComparison.Ordinal);
            if (last < 0) return null;
            string suffix = nodeName.Substring(last + splitCode.Length);
            if (int.TryParse(suffix, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int idx))
                return idx;
            return null;
        }

        private static string GetReferenceNamespace()
        {
            StringBuilder sb = new StringBuilder();
            HashSet<string> namespaces = new HashSet<string>(StringComparer.Ordinal);

            // 基础 namespace
            namespaces.Add("UnityEngine");
            sb.AppendLine("using UnityEngine;");

            bool needCollectionsGeneric = _uiBindDatas.Any(d => d.BindType == EBindType.ListCom);
            if (needCollectionsGeneric)
            {
                namespaces.Add("System.Collections.Generic");
                sb.AppendLine("using System.Collections.Generic;");
            }

            foreach (var bindData in _uiBindDatas)
            {
                var comp = bindData.BindCom?.FirstOrDefault();
                string ns = comp?.GetType().Namespace;
                if (!string.IsNullOrEmpty(ns) && !namespaces.Contains(ns))
                {
                    namespaces.Add(ns);
                    sb.AppendLine($"using {ns};");
                }
            }

            return sb.ToString();
        }

        private static string GetVariableText(List<UIBindData> uiBindDatas)
        {
            if (uiBindDatas == null || uiBindDatas.Count == 0) return string.Empty;

            StringBuilder variableTextBuilder = new StringBuilder();
            var helper = UIGeneratorRuleHelper;
            if (helper == null) throw new InvalidOperationException("UIGeneratorRuleHelper is not configured.");

            foreach (var bindData in uiBindDatas)
            {
                if (bindData == null) continue;
                string variableName = bindData.Name;
                if (string.IsNullOrEmpty(variableName)) continue;

                string publicName = helper.GetPublicComponentByNameRule(variableName);
                variableTextBuilder.Append("\t\t[SerializeField]\n");

                var firstType = bindData.BindCom?.FirstOrDefault()?.GetType();
                string typeName = firstType?.Name ?? "Component";

                if (bindData.BindType == EBindType.None || bindData.BindType == EBindType.Widget)
                {
                    variableTextBuilder.Append($"\t\tprivate {typeName} {variableName};\n");
                    variableTextBuilder.Append($"\t\tpublic {typeName} {publicName} => {variableName};\n\n");
                }
                else if (bindData.BindType == EBindType.ListCom)
                {
                    int count = Math.Max(0, bindData.BindCom?.Count ?? 0);
                    variableTextBuilder.Append($"\t\tprivate {typeName}[] {variableName} = new {typeName}[{count}];\n");
                    variableTextBuilder.Append($"\t\tpublic {typeName}[] {publicName} => {variableName};\n\n");
                }
            }

            return variableTextBuilder.ToString();
        }

        private static string GenerateScript(string className, string generateNameSpace)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentNullException(nameof(className));
            if (string.IsNullOrEmpty(generateNameSpace)) throw new ArgumentNullException(nameof(generateNameSpace));

            StringBuilder scriptBuilder = new StringBuilder();
            scriptBuilder.Append(GetReferenceNamespace());
            scriptBuilder.AppendLine("using TEngine;");
            scriptBuilder.AppendLine($"namespace {generateNameSpace}");
            scriptBuilder.AppendLine("{");
            scriptBuilder.AppendLine("\t#Attribute#");
            scriptBuilder.AppendLine($"\tpublic class {className} : UIHolderObjectBase");
            scriptBuilder.AppendLine("\t{");
            scriptBuilder.AppendLine("\t\tpublic const string ResTag = #Tag#;");
            scriptBuilder.AppendLine("\t\t#region Generated by Script Tool\n");
            scriptBuilder.Append(GetVariableText(_uiBindDatas));
            scriptBuilder.AppendLine("\t\t#endregion");
            scriptBuilder.AppendLine("\t}");
            scriptBuilder.AppendLine("}");
            return scriptBuilder.ToString();
        }

        public static void GenerateAndAttachScript(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            if (!PrefabChecker.IsPrefabAsset(targetObject))
            {
                Debug.LogWarning("请将UI界面保存为对应的目录Prefab 在进行代码生成");
                return;
            }

            var ruleHelper = UIGeneratorRuleHelper;
            if (ruleHelper == null)
            {
                Debug.LogError("UIGeneratorRuleHelper not available, abort.");
                return;
            }

            if (!ruleHelper.CheckCanGenerate(targetObject, scriptGenerateData))
            {
                return;
            }

            EditorPrefs.SetInt("InstanceId", targetObject.GetInstanceID());
            _uiBindDatas.Clear();
            _arrayComponents.Clear();

            string className = ruleHelper.GetClassGenerateName(targetObject, scriptGenerateData);
            if (string.IsNullOrEmpty(className))
            {
                Debug.LogError("Generated className is empty.");
                return;
            }

            GetBindData(targetObject.transform);

            string scriptContent = GenerateScript(className, scriptGenerateData.NameSpace);
            string tagName = $"\"{ruleHelper.GetUIResourceSavePath(targetObject, scriptGenerateData)}\"";

            string uiAttribute = $"[UIRes({className}.ResTag, EUIResLoadType.{scriptGenerateData.LoadType})]";

            scriptContent = scriptContent.Replace("#Attribute#", uiAttribute);
            scriptContent = scriptContent.Replace("#Tag#", tagName);

            ruleHelper.WriteUIScriptContent(className, scriptContent, scriptGenerateData);
        }

        [DidReloadScripts]
        public static void CheckHasAttach()
        {
            if (!EditorPrefs.HasKey("Generate"))
                return;

            _uiBindDatas.Clear();
            _arrayComponents.Clear();

            string className = EditorPrefs.GetString("Generate");
            int instanceId = EditorPrefs.GetInt("InstanceId", -1);

            if (instanceId == -1)
            {
                Debug.LogWarning("CheckHasAttach: InstanceId missing.");
                EditorPrefs.DeleteKey("Generate");
                return;
            }

            GameObject targetObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;

            if (targetObject == null)
            {
                Debug.LogWarning("UI script generation attachment object missing!");
                EditorPrefs.DeleteKey("Generate");
                return;
            }

            // 重新收集 bind 数据并附加脚本
            GetBindData(targetObject.transform);

            AttachScriptToGameObject(targetObject, className);
            Debug.Log($"Generate {className} Successfully attached to game object");
            EditorPrefs.DeleteKey("Generate");
        }

        private static void AttachScriptToGameObject(GameObject targetObject, string scriptClassName)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (string.IsNullOrEmpty(scriptClassName)) throw new ArgumentNullException(nameof(scriptClassName));

            Type scriptType = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // 跳过典型的编辑器专用程序集（但只在程序集名字结束或包含 ".Editor" 时跳过）
                var asmName = assembly.GetName().Name ?? string.Empty;
                if (asmName.EndsWith(".Editor", StringComparison.OrdinalIgnoreCase) ||
                    asmName.Equals("UnityEditor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (!type.IsClass || type.IsAbstract) continue;
                    if (type.Name.Equals(scriptClassName, StringComparison.Ordinal) ||
                        type.Name.Contains(scriptClassName, StringComparison.Ordinal))
                    {
                        scriptType = type;
                        break;
                    }
                }

                if (scriptType != null) break;
            }

            if (scriptType == null)
            {
                Debug.LogError($"Could not find the class: {scriptClassName}");
                return;
            }

            Component component = targetObject.GetOrAddComponent(scriptType);

            FieldInfo[] fields = scriptType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                if (string.IsNullOrEmpty(field.Name)) continue;
                var componentInObjects = _uiBindDatas.Find(data => data.Name == field.Name)?.BindCom;
                if (componentInObjects == null)
                {
                    Debug.LogError($"Field {field.Name} did not find matching component binding");
                    continue;
                }

                if (field.FieldType.IsArray)
                {
                    Type elementType = field.FieldType.GetElementType();
                    if (elementType == null)
                    {
                        Debug.LogError($"Field {field.Name} has unknown element type.");
                        continue;
                    }

                    Array array = Array.CreateInstance(elementType, componentInObjects.Count);
                    for (int i = 0; i < componentInObjects.Count; i++)
                    {
                        Component comp = componentInObjects[i];
                        if (comp == null) continue;

                        if (elementType.IsInstanceOfType(comp))
                        {
                            array.SetValue(comp, i);
                        }
                        else
                        {
                            Debug.LogError($"Element {i} type mismatch for field {field.Name}, expected {elementType.Name}, actual {comp.GetType().Name}");
                        }
                    }

                    field.SetValue(component, array);
                }
                else
                {
                    if (componentInObjects.Count > 0)
                    {
                        var first = componentInObjects[0];
                        if (first == null) continue;

                        if (field.FieldType.IsInstanceOfType(first))
                        {
                            field.SetValue(component, first);
                        }
                        else
                        {
                            Debug.LogError($"Field {field.Name} type mismatch, cannot assign value. Field expects {field.FieldType.Name}, actual {first.GetType().Name}");
                        }
                    }
                }
            }
        }

        public static class PrefabChecker
        {
            public static bool IsEditingPrefabAsset(GameObject go)
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage == null)
                    return false;
                return prefabStage.IsPartOfPrefabContents(go);
            }

            public static bool IsPrefabAsset(GameObject go)
            {
                if (go == null) return false;

                var assetType = PrefabUtility.GetPrefabAssetType(go);
                if (assetType == PrefabAssetType.Regular ||
                    assetType == PrefabAssetType.Variant ||
                    assetType == PrefabAssetType.Model)
                    return true;

                return IsEditingPrefabAsset(go);
            }
        }
    }
}
