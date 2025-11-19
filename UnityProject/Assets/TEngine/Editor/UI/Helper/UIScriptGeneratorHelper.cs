using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TEngine.UI.Editor
{
    public enum EBindType
    {
        None,
        Widget,
        ListCom
    }

    [Serializable]
    public class UIBindData
    {
        public string Name { get; }

        public List<GameObject> Objs { get; set; }
        public EBindType BindType { get; }
        public bool IsGameObject => nameof(GameObject).Equals(TypeName);

        public string TypeName = string.Empty;

        public Type GetFirstOrDefaultType()
        {
            if (IsGameObject)
            {
                return typeof(GameObject);
            }

            return Objs.FirstOrDefault()?.GetComponent(TypeName).GetType();
        }

        public UIBindData(string name, List<GameObject> objs, string typeName = "", EBindType bindType = EBindType.None)
        {
            Name = name;
            Objs = objs ?? new List<GameObject>();
            BindType = bindType;
            TypeName = typeName;
        }

        public UIBindData(string name, GameObject obj, string typeName = "", EBindType bindType = EBindType.None)
            : this(name, new List<GameObject> { obj }, typeName, bindType)
        {
        }
    }

    internal static class UIScriptGeneratorHelper
    {
        private static UIGenerateConfiguration _uiGenerateConfiguration;
        private static IUIGeneratorRuleHelper _uiGeneratorRuleHelper;
        private static readonly List<UIBindData> _uiBindDatas = new List<UIBindData>();
        private static readonly HashSet<string> _arrayComponents = new HashSet<string>(StringComparer.Ordinal);

        private static IUIGeneratorRuleHelper UIGeneratorRuleHelper =>
            _uiGeneratorRuleHelper ?? InitializeRuleHelper();

        private static UIGenerateConfiguration UIConfiguration =>
            _uiGenerateConfiguration ??= UIGenerateConfiguration.Instance;

        private static IUIGeneratorRuleHelper InitializeRuleHelper()
        {
            var ruleHelperTypeName = UIConfiguration.UIScriptGeneratorRuleHelper;
            if (string.IsNullOrWhiteSpace(ruleHelperTypeName))
            {
                Debug.LogError("UIScriptGeneratorHelper: UIScriptGeneratorRuleHelper not configured.");
                return null;
            }

            var ruleHelperType = Type.GetType(ruleHelperTypeName);
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

            return _uiGeneratorRuleHelper;
        }

        private static string GetUIElementComponentType(string uiName)
        {
            if (string.IsNullOrEmpty(uiName)) return string.Empty;

            return UIConfiguration.UIElementRegexConfigs
                ?.Where(pair => !string.IsNullOrEmpty(pair?.uiElementRegex))
                .FirstOrDefault(pair => uiName.StartsWith(pair.uiElementRegex, StringComparison.Ordinal))
                ?.componentType ?? string.Empty;
        }

        private static string[] SplitComponentName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var common = UIConfiguration.UIGenerateCommonData;
            if (string.IsNullOrEmpty(common?.ComCheckEndName) || !name.Contains(common.ComCheckEndName))
                return null;

            var endIndex = name.IndexOf(common.ComCheckEndName, StringComparison.Ordinal);
            if (endIndex <= 0) return null;

            var comStr = name.Substring(0, endIndex);
            var split = common.ComCheckSplitName ?? "#";

            return comStr.Split(new[] { split }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void CollectBindData(Transform root)
        {
            if (root == null) return;

            foreach (Transform child in root.Cast<Transform>().Where(child => child != null))
            {
                if (ShouldSkipChild(child)) continue;
                var hasWidget = child.GetComponent<UIHolderObjectBase>() != null;
                var isArrayComponent = IsArrayComponent(child.name);

                if (hasWidget)
                {
                    CollectWidget(child);
                }
                else if (isArrayComponent)
                {
                    ProcessArrayComponent(child, root);
                }
                else
                {
                    CollectComponent(child);
                    CollectBindData(child);
                }
            }
        }

        private static bool ShouldSkipChild(Transform child)
        {
            var keywords = UIConfiguration.UIGenerateCommonData.ExcludeKeywords;
            return keywords?.Any(k =>
                !string.IsNullOrEmpty(k) &&
                child.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) == true;
        }

        private static bool IsArrayComponent(string componentName)
        {
            var splitName = UIConfiguration.UIGenerateCommonData.ArrayComSplitName;
            return !string.IsNullOrEmpty(splitName) &&
                   componentName.StartsWith(splitName, StringComparison.Ordinal);
        }

        private static void ProcessArrayComponent(Transform child, Transform root)
        {
            var splitCode = UIConfiguration.UIGenerateCommonData.ArrayComSplitName;
            var firstIndex = child.name.IndexOf(splitCode, StringComparison.Ordinal);
            var lastIndex = child.name.LastIndexOf(splitCode, StringComparison.Ordinal);

            if (firstIndex < 0 || lastIndex <= firstIndex) return;

            var text = child.name.Substring(firstIndex + splitCode.Length, lastIndex - (firstIndex + splitCode.Length));
            if (string.IsNullOrEmpty(text) || _arrayComponents.Contains(text)) return;

            _arrayComponents.Add(text);

            var arrayComponents = root.Cast<Transform>()
                .Where(sibling => sibling.name.Contains(text, StringComparison.Ordinal))
                .ToList();

            CollectArrayComponent(arrayComponents, text);
        }

        private static void CollectComponent(Transform node)
        {
            if (node == null) return;

            var componentArray = SplitComponentName(node.name);
            if (componentArray == null || componentArray.Length == 0) return;

            foreach (var com in componentArray.Where(com => !string.IsNullOrEmpty(com)))
            {
                var typeName = GetUIElementComponentType(com);
                if (string.IsNullOrEmpty(typeName)) continue;


                bool isGameObject = typeName.Equals(nameof(GameObject));
                if (!isGameObject)
                {
                    var component = node.GetComponent(typeName);
                    if (component == null)
                    {
                        Debug.LogError($"{node.name} does not have component of type {typeName}");
                        continue;
                    }
                }

                var keyName = UIGeneratorRuleHelper.GetPrivateComponentByNameRule(com, node.name, EBindType.None);
                if (_uiBindDatas.Exists(data => data.Name == keyName))
                {
                    Debug.LogError($"Duplicate key found: {keyName}");
                    continue;
                }

                _uiBindDatas.Add(new UIBindData(keyName, node.gameObject, typeName));
            }
        }

        private static void CollectWidget(Transform node)
        {
            if (node == null) return;

            var common = UIConfiguration.UIGenerateCommonData;
            if (node.name.Contains(common.ComCheckEndName, StringComparison.Ordinal) &&
                node.name.Contains(common.ComCheckSplitName, StringComparison.Ordinal))
            {
                Debug.LogWarning($"{node.name} child component cannot contain rule definition symbols!");
                return;
            }

            var component = node.GetComponent<UIHolderObjectBase>();
            if (component == null)
            {
                Debug.LogError($"{node.name} expected to be a widget but does not have UIHolderObjectBase.");
                return;
            }

            var keyName = UIGeneratorRuleHelper.GetPrivateComponentByNameRule(string.Empty, node.name, EBindType.Widget);
            if (_uiBindDatas.Exists(data => data.Name == keyName))
            {
                Debug.LogError($"Duplicate key found: {keyName}");
                return;
            }

            _uiBindDatas.Add(new UIBindData(keyName, component.gameObject, component.name, EBindType.Widget));
        }

        private static void CollectArrayComponent(List<Transform> arrayNode, string nodeName)
        {
            if (arrayNode == null || !arrayNode.Any()) return;

            var componentArray = SplitComponentName(nodeName);
            if (componentArray == null || componentArray.Length == 0)
            {
                Debug.LogWarning($"CollectArrayComponent: {nodeName} has no component definitions.");
                return;
            }

            var orderedNodes = OrderArrayNodes(arrayNode);
            var tempBindDatas = CreateTempBindDatas(componentArray, nodeName);

            PopulateArrayComponents(componentArray, orderedNodes, tempBindDatas);
            _uiBindDatas.AddRange(tempBindDatas);
        }

        private static List<Transform> OrderArrayNodes(List<Transform> arrayNode)
        {
            var splitCode = UIConfiguration.UIGenerateCommonData.ArrayComSplitName;
            return arrayNode
                .Select(node => new { Node = node, Index = ExtractArrayIndex(node.name, splitCode) })
                .OrderBy(x => x.Index ?? int.MaxValue)
                .Select(x => x.Node)
                .ToList();
        }

        private static List<UIBindData> CreateTempBindDatas(string[] componentArray, string nodeName)
        {
            return componentArray.Select((com, index) =>
            {
                var keyName = UIGeneratorRuleHelper.GetPrivateComponentByNameRule(com, nodeName, EBindType.ListCom);
                return new UIBindData(keyName, new List<GameObject>(), com, EBindType.ListCom);
            }).ToList();
        }

        private static void PopulateArrayComponents(string[] componentArray, List<Transform> orderedNodes, List<UIBindData> tempBindDatas)
        {
            for (var index = 0; index < componentArray.Length; index++)
            {
                var com = componentArray[index];
                if (string.IsNullOrEmpty(com)) continue;

                var typeName = GetUIElementComponentType(com);
                if (string.IsNullOrEmpty(typeName)) continue;
                tempBindDatas[index].TypeName = typeName;
                foreach (var node in orderedNodes)
                {
                    var isGameObject = typeName.Equals(nameof(GameObject));
                    var component = isGameObject ? null : node.GetComponent(typeName);

                    if (component != null || isGameObject)
                    {
                        tempBindDatas[index].Objs.Add(node.gameObject);
                    }
                    else
                    {
                        Debug.LogError($"{node.name} does not have component of type {typeName}");
                    }
                }
            }
        }

        private static int? ExtractArrayIndex(string nodeName, string splitCode)
        {
            if (string.IsNullOrEmpty(nodeName) || string.IsNullOrEmpty(splitCode)) return null;

            var lastIndex = nodeName.LastIndexOf(splitCode, StringComparison.Ordinal);
            if (lastIndex < 0) return null;

            var suffix = nodeName.Substring(lastIndex + splitCode.Length);
            return int.TryParse(suffix, out var idx) ? idx : (int?)null;
        }

        public static void GenerateUIBindScript(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            if (!PrefabChecker.IsPrefabAsset(targetObject))
            {
                Debug.LogWarning("请将UI界面保存为对应的目录Prefab 在进行代码生成");
                return;
            }

            var ruleHelper = UIGeneratorRuleHelper;
            if (ruleHelper == null || !ruleHelper.CheckCanGenerate(targetObject, scriptGenerateData))
            {
                return;
            }

            InitializeGenerationContext(targetObject);

            var className = ruleHelper.GetClassGenerateName(targetObject, scriptGenerateData);
            if (string.IsNullOrEmpty(className))
            {
                Debug.LogError("Generated className is empty.");
                return;
            }

            CollectBindData(targetObject.transform);
            GenerateScript(targetObject, className, scriptGenerateData, ruleHelper);
        }

        private static void InitializeGenerationContext(GameObject targetObject)
        {
            EditorPrefs.SetInt("InstanceId", targetObject.GetInstanceID());
            _uiBindDatas.Clear();
            _arrayComponents.Clear();
        }

        private static void GenerateScript(GameObject targetObject, string className, UIScriptGenerateData scriptGenerateData, IUIGeneratorRuleHelper ruleHelper)
        {
            var templateText = File.ReadAllText(UIGlobalPath.TemplatePath);
            var processedText = ProcessTemplateText(targetObject, templateText, className, scriptGenerateData, ruleHelper);

            ruleHelper.WriteUIScriptContent(className, processedText, scriptGenerateData);
            EditorPrefs.SetString("Generate", className);
        }

        private static string ProcessTemplateText(GameObject targetObject, string templateText, string className, UIScriptGenerateData scriptGenerateData, IUIGeneratorRuleHelper ruleHelper)
        {
            return templateText
                .Replace("#ReferenceNameSpace#", ruleHelper.GetReferenceNamespace(_uiBindDatas))
                .Replace("#ClassNameSpace#", scriptGenerateData.NameSpace)
                .Replace("#ClassName#", className)
                .Replace("#TagName#", ruleHelper.GetUIResourceSavePath(targetObject, scriptGenerateData))
                .Replace("#LoadType#", scriptGenerateData.LoadType.ToString())
                .Replace("#Variable#", ruleHelper.GetVariableContent(_uiBindDatas));
        }

        [DidReloadScripts]
        public static void BindUIScript()
        {
            if (!EditorPrefs.HasKey("Generate")) return;

            var className = EditorPrefs.GetString("Generate");
            var instanceId = EditorPrefs.GetInt("InstanceId", -1);
            var targetObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;

            if (targetObject == null)
            {
                Debug.LogWarning("UI script generation attachment object missing!");
                return;
            }

            _uiBindDatas.Clear();
            _arrayComponents.Clear();

            CollectBindData(targetObject.transform);
            BindScriptPropertyField(targetObject, className);
            CleanupContext();
            Debug.Log($"Generate {className} Successfully attached to game object");
        }

        private static void CleanupContext()
        {
            EditorPrefs.DeleteKey("Generate");
            _uiBindDatas.Clear();
            _arrayComponents.Clear();
        }

        private static void BindScriptPropertyField(GameObject targetObject, string scriptClassName)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (string.IsNullOrEmpty(scriptClassName)) throw new ArgumentNullException(nameof(scriptClassName));

            var scriptType = FindScriptType(scriptClassName);
            if (scriptType == null)
            {
                Debug.LogError($"Could not find the class: {scriptClassName}");
                return;
            }

            var targetHolder = targetObject.GetOrAddComponent(scriptType);
            BindFieldsToComponents(targetHolder, scriptType);
        }

        private static Type FindScriptType(string scriptClassName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(asm => !asm.GetName().Name.EndsWith(".Editor") &&
                              !asm.GetName().Name.Equals("UnityEditor"))
                .SelectMany(asm => asm.GetTypes())
                .FirstOrDefault(type => type.IsClass && !type.IsAbstract &&
                                        type.Name.Equals(scriptClassName, StringComparison.Ordinal));
        }

        private static void BindFieldsToComponents(Component targetHolder, Type scriptType)
        {
            var fields = scriptType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields.Where(field => !string.IsNullOrEmpty(field.Name)))
            {
                var bindData = _uiBindDatas.Find(data => data.Name == field.Name);
                var components = bindData.Objs;
                if (components == null)
                {
                    Debug.LogError($"Field {field.Name} did not find matching component binding");
                    continue;
                }

                SetFieldValue(field, components, bindData.TypeName, targetHolder);
            }
        }

        private static void SetFieldValue(FieldInfo field, IReadOnlyList<GameObject> components, string typeName, Component targetComponent)
        {
            if (field.FieldType.IsArray)
            {
                SetArrayFieldValue(field, components, typeName, targetComponent);
            }
            else
            {
                SetSingleFieldValue(field, components, typeName, targetComponent);
            }
        }

        private static void SetArrayFieldValue(FieldInfo field, IReadOnlyList<GameObject> components, string typeName, Component targetComponent)
        {
            var elementType = field.FieldType.GetElementType();
            if (elementType == null)
            {
                Debug.LogError($"Field {field.Name} has unknown element type.");
                return;
            }

            var array = Array.CreateInstance(elementType, components.Count);
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] == null) continue;

                var isGameobject = typeName.Equals(nameof(GameObject));
                object ComponentObject = isGameobject ? components[i] : components[i].GetComponent(typeName);

                if (elementType.IsInstanceOfType(ComponentObject))
                {
                    array.SetValue(ComponentObject, i);
                }
                else
                {
                    Debug.LogError($"Element {i} type mismatch for field {field.Name}");
                }
            }

            field.SetValue(targetComponent, array);
        }

        private static void SetSingleFieldValue(FieldInfo field, IReadOnlyList<GameObject> components, string typeName, Component targetComponent)
        {
            if (components.Count == 0) return;

            var isGameobject = typeName.Equals(nameof(GameObject));
            object firstComponent = isGameobject ? components[0] : components[0].GetComponent(typeName);
            if (firstComponent == null) return;

            if (field.FieldType.IsInstanceOfType(firstComponent))
            {
                field.SetValue(targetComponent, firstComponent);
            }
            else
            {
                Debug.LogError($"Field {field.Name} type mismatch");
            }
        }

        public static class PrefabChecker
        {
            public static bool IsEditingPrefabAsset(GameObject go)
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                return prefabStage?.IsPartOfPrefabContents(go) == true;
            }

            public static bool IsPrefabAsset(GameObject go)
            {
                if (go == null) return false;

                var assetType = PrefabUtility.GetPrefabAssetType(go);
                var isRegularPrefab = assetType == PrefabAssetType.Regular ||
                                      assetType == PrefabAssetType.Variant ||
                                      assetType == PrefabAssetType.Model;

                return isRegularPrefab || IsEditingPrefabAsset(go);
            }
        }
    }
}
