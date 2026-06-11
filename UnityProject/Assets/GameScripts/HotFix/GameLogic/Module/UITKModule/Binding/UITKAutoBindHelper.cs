using System;
using System.Collections.Generic;
using System.Reflection;
using TEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 运行时自动绑定助手。通过反射扫描 [Q]/[OnClick]/[OnChange] 完成自动绑定。
    /// 性能：每个类型仅首次反射，后续从缓存读取。
    /// 后续 Source Generator 就绪后可无缝替换（接口一致）。
    /// </summary>
    public static class UITKAutoBindHelper
    {
        // ━━━ 类型缓存 ━━━
        private static readonly Dictionary<Type, BindingCache> _cache = new();

        /// <summary>
        /// 对目标实例执行自动绑定（Q查询 + 事件注册）。
        /// 在 OnCreate 时调用一次。
        /// </summary>
        public static void AutoBind(UITKBase target, VisualElement root)
        {
            if (target == null || root == null) return;

            var type = target.GetType();
            var cache = GetOrCreateCache(type);

            // 绑定 [Q] 字段
            foreach (var qInfo in cache.QFields)
            {
                var element = root.Q(qInfo.UxmlName, qInfo.ClassName);
                if (element == null)
                {
                    Log.Warning($"[UITKAutoBind] Element '{qInfo.UxmlName}' not found for {type.Name}.{qInfo.Field.Name}");
                    continue;
                }
                qInfo.Field.SetValue(target, element);
            }

            // 绑定 [OnClick] 方法
            foreach (var clickInfo in cache.OnClickMethods)
            {
                var element = root.Q(clickInfo.UxmlTarget);
                if (element == null)
                {
                    Log.Warning($"[UITKAutoBind] Click target '{clickInfo.UxmlTarget}' not found for {type.Name}.{clickInfo.Method.Name}");
                    continue;
                }

                if (element is Button button)
                {
                    var action = (Action)Delegate.CreateDelegate(typeof(Action), target, clickInfo.Method);
                    button.clicked += action;
                }
            }

            // 绑定 [OnChange] 方法
            foreach (var changeInfo in cache.OnChangeMethods)
            {
                var element = root.Q(changeInfo.UxmlTarget);
                if (element == null)
                {
                    Log.Warning($"[UITKAutoBind] Change target '{changeInfo.UxmlTarget}' not found for {type.Name}.{changeInfo.Method.Name}");
                    continue;
                }

                // 通过反射调用 RegisterValueChangedCallback
                RegisterChangeCallback(element, target, changeInfo.Method);
            }
        }

        /// <summary>
        /// 解绑事件（OnClick/OnChange）。
        /// </summary>
        public static void AutoUnbind(UITKBase target, VisualElement root)
        {
            if (target == null || root == null) return;

            var type = target.GetType();
            if (!_cache.TryGetValue(type, out var cache)) return;

            foreach (var clickInfo in cache.OnClickMethods)
            {
                var element = root.Q(clickInfo.UxmlTarget);
                if (element is Button button)
                {
                    var action = (Action)Delegate.CreateDelegate(typeof(Action), target, clickInfo.Method);
                    button.clicked -= action;
                }
            }

            // OnChange 的 UnregisterValueChangedCallback 需要持有原始 delegate 引用
            // 这里简化处理：通过 RemoveFromHierarchy 时自动清理
        }

        /// <summary>
        /// 自动绑定 ViewModel（[Bind] + [BindCommand]）。
        /// </summary>
        public static void AutoBindViewModel(UITKBase target, VisualElement root, ViewModelBase vm, List<Action> unbindActions)
        {
            if (target == null || root == null || vm == null) return;

            var type = target.GetType();
            var cache = GetOrCreateCache(type);
            var vmType = vm.GetType();

            // [Bind] 字段
            foreach (var bindInfo in cache.BindFields)
            {
                var vmProp = vmType.GetProperty(bindInfo.Path);
                if (vmProp == null)
                {
                    Log.Warning($"[UITKAutoBind] ViewModel property '{bindInfo.Path}' not found in {vmType.Name}");
                    continue;
                }

                var vmValue = vmProp.GetValue(vm);
                var element = (VisualElement)bindInfo.Field.GetValue(target);
                if (element == null) continue;

                BindProperty(element, vmValue, bindInfo, unbindActions);
            }

            // [BindCommand] 字段
            foreach (var cmdInfo in cache.BindCommands)
            {
                var vmProp = vmType.GetProperty(cmdInfo.CommandName);
                if (vmProp == null)
                {
                    Log.Warning($"[UITKAutoBind] ViewModel command '{cmdInfo.CommandName}' not found in {vmType.Name}");
                    continue;
                }

                var command = vmProp.GetValue(vm) as BindableCommand;
                if (command == null) continue;

                var element = (VisualElement)cmdInfo.Field.GetValue(target);
                if (element is Button button)
                {
                    button.clicked += command.Execute;
                    Action canExecHandler = () => button.SetEnabled(command.CanExecute());
                    command.CanExecuteChanged += canExecHandler;
                    canExecHandler(); // 初始状态

                    unbindActions.Add(() =>
                    {
                        button.clicked -= command.Execute;
                        command.CanExecuteChanged -= canExecHandler;
                    });
                }
            }
        }

        // ━━━ 内部实现 ━━━

        private static BindingCache GetOrCreateCache(Type type)
        {
            if (_cache.TryGetValue(type, out var cache)) return cache;

            cache = new BindingCache();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 扫描字段
            foreach (var field in type.GetFields(flags))
            {
                // [Q]
                var qAttr = field.GetCustomAttribute<QAttribute>();
                if (qAttr != null)
                {
                    string uxmlName = qAttr.Name ?? NamingHelper.ToKebabCase(field.Name);
                    cache.QFields.Add(new QFieldCache { Field = field, UxmlName = uxmlName, ClassName = null });
                }

                // [Bind]
                var bindAttr = field.GetCustomAttribute<BindAttribute>();
                if (bindAttr != null)
                {
                    cache.BindFields.Add(new BindFieldCache
                    {
                        Field = field,
                        Path = bindAttr.Path,
                        Mode = bindAttr.Mode,
                        Format = bindAttr.Format,
                    });
                }

                // [BindCommand]
                var cmdAttr = field.GetCustomAttribute<BindCommandAttribute>();
                if (cmdAttr != null)
                {
                    cache.BindCommands.Add(new BindCommandCache
                    {
                        Field = field,
                        CommandName = cmdAttr.CommandName,
                    });
                }
            }

            // 扫描方法
            foreach (var method in type.GetMethods(flags))
            {
                // [OnClick]
                var clickAttr = method.GetCustomAttribute<OnClickAttribute>();
                if (clickAttr != null)
                {
                    string target = clickAttr.Target ?? NamingHelper.MethodNameToTarget(method.Name);
                    cache.OnClickMethods.Add(new EventMethodCache { Method = method, UxmlTarget = target });
                }

                // [OnChange]
                var changeAttr = method.GetCustomAttribute<OnChangeAttribute>();
                if (changeAttr != null)
                {
                    cache.OnChangeMethods.Add(new EventMethodCache { Method = method, UxmlTarget = changeAttr.Target });
                }
            }

            _cache[type] = cache;
            return cache;
        }

        private static void BindProperty(VisualElement element, object vmValue, BindFieldCache bindInfo, List<Action> unbindActions)
        {
            // 获取 BindableProperty<T> 的泛型类型
            var vmValueType = vmValue.GetType();
            if (!vmValueType.IsGenericType) return;

            var eventInfo = vmValueType.GetEvent("OnValueChanged");
            if (eventInfo == null) return;

            var valueProperty = vmValueType.GetProperty("Value");
            if (valueProperty == null) return;

            // 创建更新 UI 的委托
            Action<object> updateUI = _ =>
            {
                var val = valueProperty.GetValue(vmValue);
                string text = bindInfo.Format != null
                    ? string.Format(bindInfo.Format, val)
                    : val?.ToString() ?? "";

                if (element is Label label) label.text = text;
                else if (element is TextField tf) { if (tf.value != text) tf.value = text; }
                else if (element is Button btn) btn.text = text;
            };

            // 构造正确类型的 Action<T> 委托
            var genericArg = vmValueType.GetGenericArguments()[0];
            var actionType = typeof(Action<>).MakeGenericType(genericArg);

            // 用 lambda 包装
            var handler = CreateTypedHandler(genericArg, updateUI);
            eventInfo.AddEventHandler(vmValue, handler);

            // 初始同步
            updateUI(null);

            unbindActions.Add(() => eventInfo.RemoveEventHandler(vmValue, handler));

            // TwoWay: View → ViewModel
            if (bindInfo.Mode == BindingMode.TwoWay)
            {
                if (element is TextField textField)
                {
                    EventCallback<ChangeEvent<string>> cb = evt =>
                    {
                        if (genericArg == typeof(string))
                            valueProperty.SetValue(vmValue, evt.newValue);
                        else if (genericArg == typeof(int) && int.TryParse(evt.newValue, out int intVal))
                            valueProperty.SetValue(vmValue, intVal);
                        else if (genericArg == typeof(float) && float.TryParse(evt.newValue, out float fVal))
                            valueProperty.SetValue(vmValue, fVal);
                    };
                    textField.RegisterValueChangedCallback(cb);
                    unbindActions.Add(() => textField.UnregisterValueChangedCallback(cb));
                }
                else if (element is Slider slider && genericArg == typeof(float))
                {
                    EventCallback<ChangeEvent<float>> cb = evt => valueProperty.SetValue(vmValue, evt.newValue);
                    slider.RegisterValueChangedCallback(cb);
                    unbindActions.Add(() => slider.UnregisterValueChangedCallback(cb));
                }
                else if (element is Toggle toggle && genericArg == typeof(bool))
                {
                    EventCallback<ChangeEvent<bool>> cb = evt => valueProperty.SetValue(vmValue, evt.newValue);
                    toggle.RegisterValueChangedCallback(cb);
                    unbindActions.Add(() => toggle.UnregisterValueChangedCallback(cb));
                }
            }
        }

        private static Delegate CreateTypedHandler(Type argType, Action<object> updateUI)
        {
            var actionType = typeof(Action<>).MakeGenericType(argType);
            var method = typeof(UITKAutoBindHelper).GetMethod(nameof(GenericHandlerInvoker), BindingFlags.NonPublic | BindingFlags.Static);
            var genericMethod = method.MakeGenericMethod(argType);
            return Delegate.CreateDelegate(actionType, updateUI, genericMethod.GetMethodInfo() ?? genericMethod);
        }

        private static void GenericHandlerInvoker<T>(Action<object> updateUI, T value)
        {
            updateUI(value);
        }

        // 简化：直接用 Action<T> 包装
        private static Delegate CreateTypedHandler2(Type argType, Action<object> callback)
        {
            // 使用表达式树或动态方法更高效，这里用简单反射
            if (argType == typeof(int))
                return new Action<int>(v => callback(v));
            if (argType == typeof(float))
                return new Action<float>(v => callback(v));
            if (argType == typeof(string))
                return new Action<string>(v => callback(v));
            if (argType == typeof(bool))
                return new Action<bool>(v => callback(v));
            if (argType == typeof(long))
                return new Action<long>(v => callback(v));

            // 回退：通过反射
            var actionType = typeof(Action<>).MakeGenericType(argType);
            return Delegate.CreateDelegate(actionType, callback.Target, callback.Method);
        }

        private static void RegisterChangeCallback(VisualElement element, UITKBase target, MethodInfo method)
        {
            // 为常见类型注册值变化回调
            if (element is TextField tf)
            {
                var cb = (EventCallback<ChangeEvent<string>>)Delegate.CreateDelegate(
                    typeof(EventCallback<ChangeEvent<string>>), target, method);
                tf.RegisterValueChangedCallback(cb);
            }
            else if (element is Slider slider)
            {
                var cb = (EventCallback<ChangeEvent<float>>)Delegate.CreateDelegate(
                    typeof(EventCallback<ChangeEvent<float>>), target, method);
                slider.RegisterValueChangedCallback(cb);
            }
            else if (element is Toggle toggle)
            {
                var cb = (EventCallback<ChangeEvent<bool>>)Delegate.CreateDelegate(
                    typeof(EventCallback<ChangeEvent<bool>>), target, method);
                toggle.RegisterValueChangedCallback(cb);
            }
        }
    }

    /// <summary>
    /// 命名转换工具。camelCase → kebab-case。
    /// </summary>
    internal static class NamingHelper
    {
        public static string ToKebabCase(string camelCase)
        {
            if (string.IsNullOrEmpty(camelCase)) return camelCase;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < camelCase.Length; i++)
            {
                char c = camelCase[i];
                if (char.IsUpper(c))
                {
                    bool isAcronymPart = i > 0 && char.IsUpper(camelCase[i - 1]);
                    bool isAcronymEnd = i + 1 < camelCase.Length && char.IsLower(camelCase[i + 1]);

                    if (i > 0 && !isAcronymPart)
                        sb.Append('-');
                    else if (isAcronymPart && isAcronymEnd && i > 1)
                        sb.Append('-');

                    sb.Append(char.ToLower(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static string MethodNameToTarget(string methodName)
        {
            if (methodName.StartsWith("On") && methodName.Length > 2)
            {
                string remainder = char.ToLower(methodName[2]) + methodName.Substring(3);
                return ToKebabCase(remainder);
            }
            return ToKebabCase(methodName);
        }
    }

    // ━━━ 缓存数据结构 ━━━

    internal class BindingCache
    {
        public List<QFieldCache> QFields = new();
        public List<EventMethodCache> OnClickMethods = new();
        public List<EventMethodCache> OnChangeMethods = new();
        public List<BindFieldCache> BindFields = new();
        public List<BindCommandCache> BindCommands = new();
    }

    internal class QFieldCache
    {
        public FieldInfo Field;
        public string UxmlName;
        public string ClassName;
    }

    internal class EventMethodCache
    {
        public MethodInfo Method;
        public string UxmlTarget;
    }

    internal class BindFieldCache
    {
        public FieldInfo Field;
        public string Path;
        public BindingMode Mode;
        public string Format;
    }

    internal class BindCommandCache
    {
        public FieldInfo Field;
        public string CommandName;
    }
}
