using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit UI 基类。
    /// </summary>
    public class UITKBase
    {
        /// <summary>
        /// 依赖注入回调。
        /// </summary>
        public static Action<UITKBase> Injector;

        public enum UIType { None, Window, Widget }

        /// <summary>
        /// 父 UI 节点。
        /// </summary>
        protected UITKBase _parent;
        public UITKBase Parent => _parent;

        /// <summary>
        /// 自定义数据集。
        /// </summary>
        protected object[] _userDatas;
        public object UserData => _userDatas is { Length: >= 1 } ? _userDatas[0] : null;
        public object[] UserDatas => _userDatas;

        /// <summary>
        /// 根 VisualElement。
        /// </summary>
        public virtual VisualElement RootElement { get; protected set; }

        /// <summary>
        /// UI 类型。
        /// </summary>
        public virtual UIType Type => UIType.None;

        /// <summary>
        /// 资源是否加载完毕。
        /// </summary>
        public bool IsPrepare { get; protected set; }

        /// <summary>
        /// 子 Widget 列表。
        /// </summary>
        internal readonly List<UITKWidget> ListChild = new List<UITKWidget>();

        /// <summary>
        /// 有 Update 行为的子 Widget 列表。
        /// </summary>
        protected List<UITKWidget> _listUpdateChild;

        /// <summary>
        /// Update 列表是否有效。
        /// </summary>
        protected bool _updateListValid = false;

        /// <summary>
        /// 是否需要 Update。
        /// </summary>
        internal bool _hasOverrideUpdate = true;

        // ━━━ 生命周期钩子 ━━━

        protected virtual void OnCreate() { }
        protected virtual void OnRefresh() { }
        protected virtual void OnUpdate() { _hasOverrideUpdate = false; }
        protected virtual void OnDestroy() { }
        protected virtual void OnSetVisible(bool visible) { }
        protected virtual void OnSortDepth() { }

        // ━━━ 自动绑定钩子（由 Editor 生成工具 override）━━━

        /// <summary>
        /// 自动绑定 UI 元素。由 .bindgen.cs 重写。
        /// </summary>
        protected virtual void __UITKAutoBind(UnityEngine.UIElements.VisualElement root) { }

        /// <summary>
        /// 自动绑定事件。由 .bindgen.cs 重写。
        /// </summary>
        protected virtual void __UITKAutoBindEvents() { }

        /// <summary>
        /// 自动解绑事件。由 .bindgen.cs 重写。
        /// </summary>
        protected virtual void __UITKAutoUnbindEvents() { }

        /// <summary>
        /// 依赖注入。
        /// </summary>
        protected void Inject()
        {
            Injector?.Invoke(this);
        }

        /// <summary>
        /// 注册事件（子类重写）。
        /// </summary>
        protected virtual void RegisterEvent() { }

        internal void SetUpdateDirty()
        {
            _updateListValid = false;
            Parent?.SetUpdateDirty();
        }

        internal void CallDestroy()
        {
            OnDestroy();
        }

        // ━━━ 事件系统 ━━━

        private GameEventMgr _eventMgr;

        protected GameEventMgr EventMgr
        {
            get
            {
                if (_eventMgr == null)
                {
                    _eventMgr = MemoryPool.Acquire<GameEventMgr>();
                }
                return _eventMgr;
            }
        }

        public void AddUIEvent(int eventType, Action handler)
        {
            EventMgr.AddEvent(eventType, handler);
        }

        protected void AddUIEvent<T>(int eventType, Action<T> handler)
        {
            EventMgr.AddEvent(eventType, handler);
        }

        protected void AddUIEvent<T, U>(int eventType, Action<T, U> handler)
        {
            EventMgr.AddEvent(eventType, handler);
        }

        protected void AddUIEvent<T, U, V>(int eventType, Action<T, U, V> handler)
        {
            EventMgr.AddEvent(eventType, handler);
        }

        protected void AddUIEvent<T, U, V, W>(int eventType, Action<T, U, V, W> handler)
        {
            EventMgr.AddEvent(eventType, handler);
        }

        protected void RemoveAllUIEvent()
        {
            if (_eventMgr != null)
            {
                MemoryPool.Release(_eventMgr);
                _eventMgr = null;
            }
        }

        // ━━━ Widget 工厂方法 ━━━

        /// <summary>
        /// 通过父节点上已有的子元素创建 Widget（同步，元素已存在）。
        /// </summary>
        public T CreateWidget<T>(VisualElement widgetRoot, bool visible = true) where T : UITKWidget, new()
        {
            var widget = new T();
            if (widget.Create(this, widgetRoot, visible))
            {
                return widget;
            }
            return null;
        }

        /// <summary>
        /// 通过资源路径异步创建 Widget。
        /// </summary>
        public async UniTask<T> CreateWidgetAsync<T>(VisualElement parentElement, string assetLocation = null, bool visible = true) where T : UITKWidget, new()
        {
            string location = assetLocation ?? typeof(T).Name;
            var widget = new T();
            bool ok = await widget.CreateByPath(this, parentElement, location, visible);
            return ok ? widget : null;
        }

        /// <summary>
        /// 通过类型名创建 Widget（资源名 = 类名）。
        /// </summary>
        public async UniTask<T> CreateWidgetByTypeAsync<T>(VisualElement parentElement, bool visible = true) where T : UITKWidget, new()
        {
            return await CreateWidgetAsync<T>(parentElement, typeof(T).Name, visible);
        }

        // ━━━ MVVM 绑定支持 ━━━

        private List<Action> _unbindActions;

        /// <summary>
        /// 自动绑定 ViewModel。由 .bindgen.cs 重写，框架在 OnCreate 后自动调用。
        /// 生成代码直接读取本类的 ViewModel 字段并调用下方 Bind* helper。
        /// </summary>
        protected virtual void __UITKAutoBindMVVM() { }

        /// <summary>
        /// 解绑所有 MVVM 订阅。框架在 OnDestroy 前自动调用。
        /// 由于 Bind* helper 登记的是同一委托实例，-= 可正确移除，无泄漏。
        /// </summary>
        protected virtual void __UITKAutoUnbindMVVM()
        {
            if (_unbindActions != null)
            {
                for (int i = 0; i < _unbindActions.Count; i++) _unbindActions[i]();
                _unbindActions.Clear();
            }
        }

        // ━━━ Bind* 运行时绑定 helper（订阅/解绑使用同一委托实例，杜绝 lambda -= 泄漏）━━━

        /// <summary>
        /// OneWay：BindableProperty → Label.text。toText 为 null 时用 ToString。
        /// </summary>
        protected void BindLabel<TSource>(Label label, BindableProperty<TSource> prop, Func<TSource, string> toText = null)
        {
            if (label == null || prop == null) return;
            _unbindActions ??= new List<Action>();

            Action<TSource> handler = v => label.text = toText != null ? toText(v) : v?.ToString() ?? "";
            prop.OnValueChanged += handler;
            handler(prop.Value); // 初始同步
            _unbindActions.Add(() => prop.OnValueChanged -= handler);
        }

        /// <summary>
        /// 字段同类型绑定（TextField/Slider/Toggle…），mode 控制方向。
        /// </summary>
        protected void BindField<TValue>(INotifyValueChanged<TValue> field, BindableProperty<TValue> prop, BindingMode mode = BindingMode.TwoWay)
        {
            if (field == null || prop == null) return;
            _unbindActions ??= new List<Action>();

            if (mode != BindingMode.OneWayToSource)
            {
                Action<TValue> toView = v => field.SetValueWithoutNotify(v);
                prop.OnValueChanged += toView;
                field.SetValueWithoutNotify(prop.Value); // 初始同步
                _unbindActions.Add(() => prop.OnValueChanged -= toView);
            }
            if (mode != BindingMode.OneWay)
            {
                EventCallback<ChangeEvent<TValue>> toSource = evt => prop.Value = evt.newValue;
                field.RegisterValueChangedCallback(toSource);
                _unbindActions.Add(() => field.UnregisterValueChangedCallback(toSource));
            }
        }

        /// <summary>
        /// 字段异类型绑定（VM 类型 TSource ↔ 控件类型 TTarget，经 IValueConverter 转换）。
        /// </summary>
        protected void BindField<TSource, TTarget>(INotifyValueChanged<TTarget> field, BindableProperty<TSource> prop, IValueConverter<TSource, TTarget> conv, BindingMode mode = BindingMode.TwoWay)
        {
            if (field == null || prop == null || conv == null) return;
            _unbindActions ??= new List<Action>();

            if (mode != BindingMode.OneWayToSource)
            {
                Action<TSource> toView = v => field.SetValueWithoutNotify(conv.Convert(v));
                prop.OnValueChanged += toView;
                field.SetValueWithoutNotify(conv.Convert(prop.Value)); // 初始同步
                _unbindActions.Add(() => prop.OnValueChanged -= toView);
            }
            if (mode != BindingMode.OneWay)
            {
                EventCallback<ChangeEvent<TTarget>> toSource = evt => prop.Value = conv.ConvertBack(evt.newValue);
                field.RegisterValueChangedCallback(toSource);
                _unbindActions.Add(() => field.UnregisterValueChangedCallback(toSource));
            }
        }

        /// <summary>
        /// 命令绑定：Button.clicked → Execute；CanExecuteChanged → SetEnabled。
        /// </summary>
        protected void BindCommand(Button button, BindableCommand command)
        {
            if (button == null || command == null) return;
            _unbindActions ??= new List<Action>();

            Action exec = command.Execute;
            Action canExec = () => button.SetEnabled(command.CanExecute());
            button.clicked += exec;
            command.CanExecuteChanged += canExec;
            button.SetEnabled(command.CanExecute()); // 初始状态
            _unbindActions.Add(() =>
            {
                button.clicked -= exec;
                command.CanExecuteChanged -= canExec;
            });
        }
    }
}
