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
            await widget.CreateByPath(this, parentElement, location, visible);
            return widget;
        }

        /// <summary>
        /// 通过类型名创建 Widget（资源名 = 类名）。
        /// </summary>
        public async UniTask<T> CreateWidgetByTypeAsync<T>(VisualElement parentElement, bool visible = true) where T : UITKWidget, new()
        {
            return await CreateWidgetAsync<T>(parentElement, typeof(T).Name, visible);
        }

        // ━━━ MVVM 绑定支持 ━━━

        private List<System.Action> _unbindActions;

        /// <summary>
        /// 绑定 ViewModel。调用由 Editor 工具生成的 __UITKAutoBindMVVM 方法。
        /// </summary>
        protected void BindContext(ViewModelBase vm)
        {
            _unbindActions ??= new List<System.Action>();
            __UITKAutoBindMVVM(vm);
        }

        /// <summary>
        /// 解绑 ViewModel。
        /// </summary>
        protected void UnbindContext()
        {
            __UITKAutoUnbindMVVM();
        }

        /// <summary>
        /// 由 Editor 生成工具重写。绑定 ViewModel。默认空实现。
        /// </summary>
        protected virtual void __UITKAutoBindMVVM(ViewModelBase vm) { }

        /// <summary>
        /// 由 Editor 生成工具重写。解绑 ViewModel。
        /// </summary>
        protected virtual void __UITKAutoUnbindMVVM()
        {
            if (_unbindActions != null)
            {
                foreach (var action in _unbindActions) action();
                _unbindActions.Clear();
            }
        }
    }
}
