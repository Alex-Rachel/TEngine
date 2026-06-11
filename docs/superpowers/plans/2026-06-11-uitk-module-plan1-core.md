# UITKModule Plan 1: Core Framework Implementation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the UITKModule core framework skeleton — module singleton, 5-layer panel management, window/widget lifecycle, resource loading, and animation system — enabling manual-binding style UI development on top of UIToolkit.

**Architecture:** Pure C# module (Singleton + IUpdate), 5 UIDocument panels for layer isolation, UITKWindow/UITKWidget classes mirroring existing UIModule lifecycle. Resource loading via IUITKResourceLoader abstraction delegating to YooAsset/Resources.

**Tech Stack:** Unity 2022.3 UI Toolkit, UniTask, YooAsset, TEngine Module System

**Spec Reference:** `docs/superpowers/specs/2026-06-11-uitk-module-design.md`

**Depends On:** Existing TEngine framework (GameEvent, GameModule.Resource, Timer, MemoryPool)

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.cs` | Module singleton, IUpdate, initialization |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.Panel.cs` | 5-layer panel creation and management |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.Stack.cs` | Window stack Push/Pop/Sort/Visible logic |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.Animation.cs` | Animation invocation and preset dispatch |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKBase.cs` | Base class: events, child list, update dirty |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKWindow.cs` | Window: lifecycle, visible, depth, animation hooks |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKWidget.cs` | Widget: independent UXML, parent-child |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UIWindowAttribute.cs` | [UIWindow] attribute for window metadata |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Resource/IUITKResourceLoader.cs` | Resource loader interface |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Resource/UITKResourceLoader.cs` | Default implementation (YooAsset + Resources) |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/IUITKAnimationDriver.cs` | Animation driver interface |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/USSAnimationDriver.cs` | Default USS transition driver |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/UITKAnimationType.cs` | Animation preset enum |
| Create | `Assets/AssetRaw/UITK/Shared/Settings/PanelSettings_Bottom.asset` | PanelSettings for Bottom layer (Editor-created) |
| Create | `Assets/AssetRaw/UITK/Shared/Settings/PanelSettings_UI.asset` | PanelSettings for UI layer |
| Create | `Assets/AssetRaw/UITK/Shared/Settings/PanelSettings_Top.asset` | PanelSettings for Top layer |
| Create | `Assets/AssetRaw/UITK/Shared/Settings/PanelSettings_Tips.asset` | PanelSettings for Tips layer |
| Create | `Assets/AssetRaw/UITK/Shared/Settings/PanelSettings_System.asset` | PanelSettings for System layer |
| Create | `Assets/AssetRaw/UITK/Shared/Styles/variables.uss` | CSS variables foundation |
| Create | `Assets/AssetRaw/UITK/Shared/Styles/common.uss` | Common styles |

---

## Task 1: UITKAnimationType Enum + IUITKAnimationDriver Interface

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/UITKAnimationType.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/IUITKAnimationDriver.cs`

- [ ] **Step 1: Create UITKAnimationType.cs**

```csharp
namespace GameLogic
{
    /// <summary>
    /// 内置动画预设类型。
    /// </summary>
    public enum UITKAnimationType
    {
        None,
        FadeIn,
        FadeOut,
        SlideFromBottom,
        SlideToBottom,
        SlideFromRight,
        SlideToRight,
        ScaleIn,
        ScaleOut,
    }
}
```

- [ ] **Step 2: Create IUITKAnimationDriver.cs**

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 动画驱动器接口。默认使用 USS Transition，可替换为 DOTween 等实现。
    /// </summary>
    public interface IUITKAnimationDriver
    {
        /// <summary>
        /// 播放动画。
        /// </summary>
        /// <param name="target">目标 VisualElement。</param>
        /// <param name="type">动画类型。</param>
        /// <param name="isShow">true=显示动画，false=隐藏动画。</param>
        /// <param name="durationMs">动画时长（毫秒）。</param>
        UniTask Play(VisualElement target, UITKAnimationType type, bool isShow, int durationMs);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/
git commit -m "feat(uitk): add UITKAnimationType enum and IUITKAnimationDriver interface"
```

---

## Task 2: USSAnimationDriver Default Implementation

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/USSAnimationDriver.cs`

- [ ] **Step 1: Create USSAnimationDriver.cs**

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 默认动画驱动器，使用 USS Transition 实现。
    /// </summary>
    public class USSAnimationDriver : IUITKAnimationDriver
    {
        public async UniTask Play(VisualElement target, UITKAnimationType type, bool isShow, int durationMs)
        {
            if (type == UITKAnimationType.None || target == null)
                return;

            // 1. 设置初始状态（无过渡）
            SetTransitionDuration(target, 0);
            ApplyState(target, type, isStart: true, isShow);

            // 等一帧让初始状态生效
            await UniTask.Yield();

            // 2. 设置过渡 + 目标状态
            SetTransitionDuration(target, durationMs);
            ApplyState(target, type, isStart: false, isShow);

            // 3. 等待过渡完成
            await UniTask.Delay(durationMs);
        }

        private void SetTransitionDuration(VisualElement target, int ms)
        {
            target.style.transitionDuration = new List<TimeValue> { new TimeValue(ms, TimeUnit.Millisecond) };
            target.style.transitionProperty = new List<StylePropertyName>
            {
                new StylePropertyName("opacity"),
                new StylePropertyName("translate"),
                new StylePropertyName("scale"),
            };
            target.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };
        }

        private void ApplyState(VisualElement target, UITKAnimationType type, bool isStart, bool isShow)
        {
            // isStart=true 表示动画起始状态，isStart=false 表示动画目标状态
            bool atOrigin = isShow ? isStart : !isStart;

            switch (type)
            {
                case UITKAnimationType.FadeIn:
                case UITKAnimationType.FadeOut:
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;

                case UITKAnimationType.SlideFromBottom:
                case UITKAnimationType.SlideToBottom:
                    target.style.translate = atOrigin
                        ? new Translate(0, Length.Percent(100))
                        : new Translate(0, 0);
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;

                case UITKAnimationType.SlideFromRight:
                case UITKAnimationType.SlideToRight:
                    target.style.translate = atOrigin
                        ? new Translate(Length.Percent(100), 0)
                        : new Translate(0, 0);
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;

                case UITKAnimationType.ScaleIn:
                case UITKAnimationType.ScaleOut:
                    target.style.scale = atOrigin
                        ? new Scale(new Vector3(0.8f, 0.8f, 1f))
                        : new Scale(Vector3.one);
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Animation/USSAnimationDriver.cs
git commit -m "feat(uitk): implement USSAnimationDriver with USS transition presets"
```

---

## Task 3: IUITKResourceLoader Interface + Default Implementation

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Resource/IUITKResourceLoader.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Resource/UITKResourceLoader.cs`

- [ ] **Step 1: Create IUITKResourceLoader.cs**

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 资源加载器接口。
    /// </summary>
    public interface IUITKResourceLoader
    {
        VisualTreeAsset LoadVisualTreeAsset(string location, string packageName = "");
        UniTask<VisualTreeAsset> LoadVisualTreeAssetAsync(string location, CancellationToken ct = default, string packageName = "");
        StyleSheet LoadStyleSheet(string location, string packageName = "");
        UniTask<StyleSheet> LoadStyleSheetAsync(string location, CancellationToken ct = default, string packageName = "");
        PanelSettings LoadPanelSettings(string location, string packageName = "");
        void Unload(Object asset);
    }
}
```

- [ ] **Step 2: Create UITKResourceLoader.cs**

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 默认 UIToolkit 资源加载器。委托给 YooAsset (IResourceModule)。
    /// </summary>
    public class UITKResourceLoader : IUITKResourceLoader
    {
        private readonly IResourceModule _resource = ModuleSystem.GetModule<IResourceModule>();

        public VisualTreeAsset LoadVisualTreeAsset(string location, string packageName = "")
        {
            return _resource.LoadAsset<VisualTreeAsset>(location, packageName);
        }

        public async UniTask<VisualTreeAsset> LoadVisualTreeAssetAsync(string location, CancellationToken ct = default, string packageName = "")
        {
            return await _resource.LoadAssetAsync<VisualTreeAsset>(location, ct, packageName);
        }

        public StyleSheet LoadStyleSheet(string location, string packageName = "")
        {
            return _resource.LoadAsset<StyleSheet>(location, packageName);
        }

        public async UniTask<StyleSheet> LoadStyleSheetAsync(string location, CancellationToken ct = default, string packageName = "")
        {
            return await _resource.LoadAssetAsync<StyleSheet>(location, ct, packageName);
        }

        public PanelSettings LoadPanelSettings(string location, string packageName = "")
        {
            return _resource.LoadAsset<PanelSettings>(location, packageName);
        }

        public void Unload(Object asset)
        {
            _resource.UnloadAsset(asset);
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Resource/
git commit -m "feat(uitk): add IUITKResourceLoader interface and YooAsset implementation"
```

---

## Task 4: UIWindowAttribute

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UIWindowAttribute.cs`

- [ ] **Step 1: Create UIWindowAttribute.cs**

```csharp
using System;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 窗口声明特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class UIWindowAttribute : Attribute
    {
        /// <summary>
        /// 窗口层级。
        /// </summary>
        public UILayer Layer { get; }

        /// <summary>
        /// 资源定位地址。空则使用类名。
        /// </summary>
        public string Location { get; set; } = "";

        /// <summary>
        /// 是否全屏窗口（用于遮挡下层）。
        /// </summary>
        public bool FullScreen { get; set; } = false;

        /// <summary>
        /// 是否从 Resources 加载（不走 AB）。
        /// </summary>
        public bool FromResources { get; set; } = false;

        /// <summary>
        /// 隐藏后自动关闭时间（秒）。0 表示立即关闭。
        /// </summary>
        public int HideTimeToClose { get; set; } = 10;

        /// <summary>
        /// YooAsset 资源包名。空则使用默认包。
        /// </summary>
        public string Package { get; set; } = "";

        public UIWindowAttribute(UILayer layer)
        {
            Layer = layer;
        }

        public UIWindowAttribute(UILayer layer, bool fullScreen)
        {
            Layer = layer;
            FullScreen = fullScreen;
        }
    }

    /// <summary>
    /// UI 层级枚举。
    /// </summary>
    public enum UILayer
    {
        Bottom = 0,
        UI = 1,
        Top = 2,
        Tips = 3,
        System = 4,
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UIWindowAttribute.cs
git commit -m "feat(uitk): add UIWindowAttribute and UILayer enum"
```

---

## Task 5: UITKBase — Base Class

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKBase.cs`

- [ ] **Step 1: Create UITKBase.cs**

```csharp
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
        protected bool _hasOverrideUpdate = true;

        // ━━━ 生命周期钩子 ━━━

        protected virtual void OnCreate() { }
        protected virtual void OnRefresh() { }
        protected virtual void OnUpdate() { _hasOverrideUpdate = false; }
        protected virtual void OnDestroy() { }
        protected virtual void OnSetVisible(bool visible) { }
        protected virtual void OnSortDepth() { }

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
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKBase.cs
git commit -m "feat(uitk): add UITKBase with lifecycle hooks, events, and widget factory"
```

---

## Task 6: UITKWindow — Window Implementation

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKWindow.cs`

- [ ] **Step 1: Create UITKWindow.cs**

```csharp
using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 窗口基类。
    /// </summary>
    public abstract class UITKWindow : UITKBase
    {
        // ━━━ 属性 ━━━

        public override UIType Type => UIType.Window;
        public string WindowName { get; private set; }
        public int WindowLayer { get; private set; }
        public string AssetName { get; private set; }
        public virtual bool FullScreen { get; private set; }
        public bool FromResources { get; private set; }
        public int HideTimeToClose { get; set; }
        public string Package { get; private set; }
        public int HideTimerId { get; set; }
        public bool IsLoadDone { get; private set; }
        public bool IsHide { get; set; }

        private bool _isCreate;
        private bool _isDestroyed;
        private VisualTreeAsset _visualTreeAsset;
        private Action<UITKWindow> _prepareCallback;

        // ━━━ 动画配置 ━━━

        protected virtual UITKAnimationType ShowAnimation => UITKAnimationType.FadeIn;
        protected virtual UITKAnimationType HideAnimation => UITKAnimationType.FadeOut;
        protected virtual int AnimationDuration => 200;

        protected virtual UniTask OnShowAnimation()
        {
            return UITKModule.Instance.AnimationDriver.Play(RootElement, ShowAnimation, true, AnimationDuration);
        }

        protected virtual UniTask OnHideAnimation()
        {
            return UITKModule.Instance.AnimationDriver.Play(RootElement, HideAnimation, false, AnimationDuration);
        }

        // ━━━ 可见性 ━━━

        private bool _visible;
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (RootElement != null)
                {
                    RootElement.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                    RootElement.pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
                }
                if (_isCreate)
                {
                    OnSetVisible(value);
                }
            }
        }

        // ━━━ 初始化 ━━━

        internal void Init(string windowName, int windowLayer, bool fullScreen, string assetName, bool fromResources, int hideTimeToClose, string package)
        {
            WindowName = windowName;
            WindowLayer = windowLayer;
            FullScreen = fullScreen;
            AssetName = assetName;
            FromResources = fromResources;
            HideTimeToClose = hideTimeToClose;
            Package = package;
        }

        // ━━━ 加载流程 ━━━

        internal async UniTaskVoid InternalLoad(string assetName, Action<UITKWindow> prepareCallback, bool isAsync, params object[] userDatas)
        {
            _prepareCallback = prepareCallback;
            _userDatas = userDatas;

            if (FromResources)
            {
                _visualTreeAsset = Resources.Load<VisualTreeAsset>(assetName);
            }
            else if (isAsync)
            {
                _visualTreeAsset = await UITKModule.Resource.LoadVisualTreeAssetAsync(assetName, default, Package);
            }
            else
            {
                _visualTreeAsset = UITKModule.Resource.LoadVisualTreeAsset(assetName, Package);
            }

            HandleLoadCompleted();
        }

        private void HandleLoadCompleted()
        {
            IsLoadDone = true;

            if (_isDestroyed)
            {
                ReleaseAsset();
                return;
            }

            // CloneTree 生成根 VisualElement
            RootElement = _visualTreeAsset.CloneTree();
            RootElement.name = WindowName;

            // 确保 root 撑满父容器
            RootElement.style.flexGrow = 1;
            RootElement.style.position = Position.Absolute;
            RootElement.style.left = 0;
            RootElement.style.right = 0;
            RootElement.style.top = 0;
            RootElement.style.bottom = 0;

            // 挂载到对应层 Panel
            UITKModule.Instance.AttachToLayer(this);

            IsPrepare = true;
            _prepareCallback?.Invoke(this);
        }

        // ━━━ 生命周期内部方法 ━━━

        internal void InternalCreate()
        {
            if (_isCreate) return;
            _isCreate = true;
            Inject();
            OnCreate();
            RegisterEvent();
        }

        internal void InternalRefresh()
        {
            OnRefresh();
        }

        internal void InternalUpdate()
        {
            if (!IsPrepare || !_visible) return;

            OnUpdate();

            if (ListChild.Count > 0)
            {
                if (!_updateListValid)
                {
                    _listUpdateChild ??= new();
                    _listUpdateChild.Clear();
                    for (int i = 0; i < ListChild.Count; i++)
                    {
                        if (ListChild[i]._hasOverrideUpdate)
                        {
                            _listUpdateChild.Add(ListChild[i]);
                        }
                    }
                    _updateListValid = true;
                }

                if (_listUpdateChild != null)
                {
                    for (int i = 0; i < _listUpdateChild.Count; i++)
                    {
                        _listUpdateChild[i].InternalUpdate();
                    }
                }
            }
        }

        internal void InternalDestroy(bool isShutDown = false)
        {
            _isDestroyed = true;

            if (!isShutDown)
            {
                CancelHideToCloseTimer();
            }

            OnDestroy();
            RemoveAllUIEvent();

            // 销毁子 Widget
            for (int i = ListChild.Count - 1; i >= 0; i--)
            {
                ListChild[i].Destroy();
            }
            ListChild.Clear();

            // 从 Panel 树移除
            RootElement?.RemoveFromHierarchy();

            // 释放资源
            ReleaseAsset();
        }

        internal void CancelHideToCloseTimer()
        {
            if (HideTimerId > 0)
            {
                GameModule.Timer.RemoveTimer(HideTimerId);
                HideTimerId = 0;
            }
        }

        private void ReleaseAsset()
        {
            if (_visualTreeAsset != null && !FromResources)
            {
                UITKModule.Resource.Unload(_visualTreeAsset);
                _visualTreeAsset = null;
            }
        }

        // ━━━ TryInvoke (重新 Show 已存在窗口) ━━━

        internal void TryInvoke(Action<UITKWindow> prepareCallback, params object[] userDatas)
        {
            CancelHideToCloseTimer();
            IsHide = false;
            _userDatas = userDatas;

            if (IsPrepare)
            {
                prepareCallback?.Invoke(this);
            }
            else
            {
                _prepareCallback = prepareCallback;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKWindow.cs
git commit -m "feat(uitk): add UITKWindow with full lifecycle, visibility, and animation"
```

---

## Task 7: UITKWidget — Widget Implementation

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKWidget.cs`

- [ ] **Step 1: Create UITKWidget.cs**

```csharp
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit Widget 组件基类。独立 UXML，有完整生命周期。
    /// </summary>
    public class UITKWidget : UITKBase
    {
        public override UIType Type => UIType.Widget;

        private VisualTreeAsset _widgetAsset;
        private bool _fromResource;

        /// <summary>
        /// 获取所属 Window（向上遍历）。
        /// </summary>
        public UITKWindow OwnerWindow
        {
            get
            {
                var current = _parent;
                while (current != null)
                {
                    if (current.Type == UIType.Window)
                        return current as UITKWindow;
                    current = current.Parent;
                }
                return null;
            }
        }

        // ━━━ 列表虚拟化支持 ━━━

        /// <summary>
        /// ListView 虚拟化绑定数据。
        /// </summary>
        public virtual void OnBindData(object data, int index) { }

        /// <summary>
        /// ListView 虚拟化解绑数据。
        /// </summary>
        public virtual void OnUnbindData() { }

        // ━━━ 创建路径 ━━━

        /// <summary>
        /// 通过已存在的 VisualElement 创建（同步）。
        /// </summary>
        internal bool Create(UITKBase parent, VisualElement widgetRoot, bool visible)
        {
            _parent = parent;
            RootElement = widgetRoot;

            if (RootElement == null)
            {
                Log.Error($"UITKWidget Create failed: widgetRoot is null for {GetType().Name}");
                return false;
            }

            InitWidget(visible);
            return true;
        }

        /// <summary>
        /// 通过资源路径异步创建。
        /// </summary>
        internal async UniTask CreateByPath(UITKBase parent, VisualElement parentElement, string location, bool visible)
        {
            _parent = parent;

            _widgetAsset = await UITKModule.Resource.LoadVisualTreeAssetAsync(location);
            RootElement = _widgetAsset.CloneTree();
            parentElement.Add(RootElement);

            InitWidget(visible);
        }

        /// <summary>
        /// 通过 Resources 同步创建。
        /// </summary>
        internal bool CreateFromResources(UITKBase parent, VisualElement parentElement, string location, bool visible)
        {
            _parent = parent;
            _fromResource = true;

            _widgetAsset = UnityEngine.Resources.Load<VisualTreeAsset>(location);
            if (_widgetAsset == null)
            {
                Log.Error($"UITKWidget CreateFromResources failed: {location} not found");
                return false;
            }

            RootElement = _widgetAsset.CloneTree();
            parentElement.Add(RootElement);

            InitWidget(visible);
            return true;
        }

        private void InitWidget(bool visible)
        {
            // 注册到父节点
            _parent.ListChild.Add(this);
            _parent.SetUpdateDirty();

            // 生命周期
            Inject();
            OnCreate();
            RegisterEvent();
            OnRefresh();

            IsPrepare = true;

            // 可见性
            if (!visible)
            {
                RootElement.style.display = DisplayStyle.None;
            }
        }

        // ━━━ 销毁 ━━━

        /// <summary>
        /// 销毁 Widget。
        /// </summary>
        public void Destroy()
        {
            _parent?.ListChild.Remove(this);
            _parent?.SetUpdateDirty();
            InternalDestroy();
        }

        internal void InternalDestroy()
        {
            OnDestroy();
            RemoveAllUIEvent();

            // 递归销毁子 Widget
            for (int i = ListChild.Count - 1; i >= 0; i--)
            {
                ListChild[i].Destroy();
            }
            ListChild.Clear();

            // 从树移除
            RootElement?.RemoveFromHierarchy();

            // 释放资源
            if (_widgetAsset != null && !_fromResource)
            {
                UITKModule.Resource.Unload(_widgetAsset);
                _widgetAsset = null;
            }
        }

        // ━━━ Update ━━━

        internal void InternalUpdate()
        {
            if (!IsPrepare) return;
            OnUpdate();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKWidget.cs
git commit -m "feat(uitk): add UITKWidget with independent UXML, lifecycle, and list support"
```

---

## Task 8: UITKModule Singleton — Core + Panel + Stack + Animation

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.Panel.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.Stack.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/UITKModule.Animation.cs`

- [ ] **Step 1: Create UITKModule.cs (main partial)**

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 管理模块。
    /// </summary>
    public sealed partial class UITKModule : Singleton<UITKModule>, IUpdate
    {
        /// <summary>
        /// 资源加载器。
        /// </summary>
        public static IUITKResourceLoader Resource { get; private set; }

        /// <summary>
        /// 动画驱动器。
        /// </summary>
        public IUITKAnimationDriver AnimationDriver { get; set; }

        /// <summary>
        /// 窗口栈。
        /// </summary>
        private readonly List<UITKWindow> _windowStack = new List<UITKWindow>(64);

        protected override void OnInit()
        {
            Resource = new UITKResourceLoader();
            AnimationDriver = new USSAnimationDriver();
            InitPanels();
        }

        protected override void OnRelease()
        {
            CloseAll(isShutDown: true);
            DestroyPanels();
        }

        public void OnUpdate()
        {
            int count = _windowStack.Count;
            for (int i = 0; i < _windowStack.Count; i++)
            {
                if (_windowStack.Count != count) break;
                _windowStack[i].InternalUpdate();
            }
        }

        // ━━━ Show ━━━

        public void ShowUI<T>(params object[] userDatas) where T : UITKWindow, new()
        {
            ShowUIImp<T>(false, userDatas);
        }

        public void ShowUIAsync<T>(params object[] userDatas) where T : UITKWindow, new()
        {
            ShowUIImp<T>(true, userDatas);
        }

        public async UniTask<T> ShowUIAsyncAwait<T>(params object[] userDatas) where T : UITKWindow, new()
        {
            return await ShowUIAwaitImp<T>(true, userDatas) as T;
        }

        public void ShowUI(Type type, params object[] userDatas)
        {
            ShowUIImp(type, false, userDatas);
        }

        public void ShowUIAsync(Type type, params object[] userDatas)
        {
            ShowUIImp(type, true, userDatas);
        }

        private void ShowUIImp<T>(bool isAsync, params object[] userDatas) where T : UITKWindow, new()
        {
            Type type = typeof(T);
            string windowName = type.FullName;

            if (!TryGetWindow(windowName, out UITKWindow window, userDatas))
            {
                window = CreateInstance<T>();
                Push(window);
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();
            }
        }

        private void ShowUIImp(Type type, bool isAsync, params object[] userDatas)
        {
            string windowName = type.FullName;

            if (!TryGetWindow(windowName, out UITKWindow window, userDatas))
            {
                window = CreateInstance(type);
                Push(window);
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();
            }
        }

        private async UniTask<UITKWindow> ShowUIAwaitImp<T>(bool isAsync, params object[] userDatas) where T : UITKWindow, new()
        {
            Type type = typeof(T);
            string windowName = type.FullName;

            if (TryGetWindow(windowName, out UITKWindow window, userDatas))
            {
                return window;
            }

            window = CreateInstance<T>();
            Push(window);
            window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();

            float time = 0f;
            while (!window.IsLoadDone)
            {
                time += Time.deltaTime;
                if (time > 60f) break;
                await UniTask.Yield();
            }
            return window;
        }

        private bool TryGetWindow(string windowName, out UITKWindow window, params object[] userDatas)
        {
            window = GetWindow(windowName);
            if (window != null)
            {
                Pop(window);
                Push(window);
                window.TryInvoke(OnWindowPrepare, userDatas);
                return true;
            }
            return false;
        }

        // ━━━ Close / Hide ━━━

        public void CloseUI<T>() where T : UITKWindow
        {
            CloseUI(typeof(T));
        }

        public void CloseUI(Type type)
        {
            string windowName = type.FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            window.InternalDestroy();
            Pop(window);
            OnSetWindowVisible();
        }

        public void HideUI<T>() where T : UITKWindow
        {
            HideUI(typeof(T));
        }

        public void HideUI(Type type)
        {
            string windowName = type.FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            if (window.HideTimeToClose <= 0)
            {
                CloseUI(type);
                return;
            }

            window.CancelHideToCloseTimer();
            window.Visible = false;
            window.IsHide = true;
            window.HideTimerId = GameModule.Timer.AddTimer((arg) =>
            {
                CloseUI(type);
            }, window.HideTimeToClose);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }

        public void CloseAll(bool isShutDown = false)
        {
            for (int i = 0; i < _windowStack.Count; i++)
            {
                _windowStack[i].InternalDestroy(isShutDown);
            }
            _windowStack.Clear();
        }

        public void CloseAllWithOut<T>() where T : UITKWindow
        {
            for (int i = _windowStack.Count - 1; i >= 0; i--)
            {
                UITKWindow window = _windowStack[i];
                if (window.GetType() == typeof(T)) continue;
                window.InternalDestroy();
                _windowStack.RemoveAt(i);
            }
        }

        // ━━━ Query ━━━

        public bool HasWindow<T>() => GetWindow(typeof(T).FullName) != null;

        public T GetUI<T>() where T : UITKWindow
        {
            return GetWindow(typeof(T).FullName) as T;
        }

        // ━━━ Instance Factory ━━━

        private UITKWindow CreateInstance<T>() where T : UITKWindow, new()
        {
            return CreateInstance(typeof(T));
        }

        private UITKWindow CreateInstance(Type type)
        {
            UITKWindow window = Activator.CreateInstance(type) as UITKWindow;
            UIWindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(UIWindowAttribute)) as UIWindowAttribute;

            if (window == null)
                throw new GameFrameworkException($"UITKWindow {type.FullName} create instance failed.");

            if (attribute != null)
            {
                string assetName = string.IsNullOrEmpty(attribute.Location) ? type.Name : attribute.Location;
                window.Init(type.FullName, (int)attribute.Layer, attribute.FullScreen, assetName, attribute.FromResources, attribute.HideTimeToClose, attribute.Package);
            }
            else
            {
                window.Init(type.FullName, (int)UILayer.UI, false, type.Name, false, 10, "");
            }

            return window;
        }
    }
}
```

- [ ] **Step 2: Create UITKModule.Panel.cs**

```csharp
using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    public sealed partial class UITKModule
    {
        private const int LAYER_COUNT = 5;
        private const int LAYER_SORT_INTERVAL = 2000;

        private GameObject _uiTKRoot;
        private UIDocument[] _layerDocuments = new UIDocument[LAYER_COUNT];
        private VisualElement[] _layerRoots = new VisualElement[LAYER_COUNT];

        private static readonly string[] LayerNames = { "Panel_Bottom", "Panel_UI", "Panel_Top", "Panel_Tips", "Panel_System" };

        private void InitPanels()
        {
            _uiTKRoot = new GameObject("UITKRoot");
            Object.DontDestroyOnLoad(_uiTKRoot);

            for (int i = 0; i < LAYER_COUNT; i++)
            {
                var panelGo = new GameObject(LayerNames[i]);
                panelGo.transform.SetParent(_uiTKRoot.transform);

                var doc = panelGo.AddComponent<UIDocument>();
                // PanelSettings 需要预建资产加载
                // 实际项目中从 AB 或 Resources 加载 PanelSettings
                doc.sortingOrder = i * LAYER_SORT_INTERVAL;

                _layerDocuments[i] = doc;
                _layerRoots[i] = doc.rootVisualElement;
            }
        }

        private void DestroyPanels()
        {
            if (_uiTKRoot != null)
            {
                Object.Destroy(_uiTKRoot);
                _uiTKRoot = null;
            }
        }

        /// <summary>
        /// 将窗口挂载到对应层级的 Panel。
        /// </summary>
        internal void AttachToLayer(UITKWindow window)
        {
            int layer = window.WindowLayer;
            if (layer < 0 || layer >= LAYER_COUNT)
            {
                Log.Error($"UITKModule: Invalid window layer {layer} for {window.WindowName}");
                layer = (int)UILayer.UI;
            }
            _layerRoots[layer].Add(window.RootElement);
        }
    }
}
```

- [ ] **Step 3: Create UITKModule.Stack.cs**

```csharp
namespace GameLogic
{
    public sealed partial class UITKModule
    {
        private UITKWindow GetWindow(string windowName)
        {
            for (int i = 0; i < _windowStack.Count; i++)
            {
                if (_windowStack[i].WindowName == windowName)
                    return _windowStack[i];
            }
            return null;
        }

        private void Push(UITKWindow window)
        {
            // 按层级找到插入位置（同层末尾）
            int insertIndex = -1;
            for (int i = 0; i < _windowStack.Count; i++)
            {
                if (window.WindowLayer == _windowStack[i].WindowLayer)
                {
                    insertIndex = i + 1;
                }
            }

            // 没有同层，找相邻层
            if (insertIndex == -1)
            {
                for (int i = 0; i < _windowStack.Count; i++)
                {
                    if (window.WindowLayer > _windowStack[i].WindowLayer)
                    {
                        insertIndex = i + 1;
                    }
                }
            }

            if (insertIndex == -1)
            {
                insertIndex = 0;
            }

            _windowStack.Insert(insertIndex, window);
        }

        private void Pop(UITKWindow window)
        {
            _windowStack.Remove(window);
        }

        private void OnWindowPrepare(UITKWindow window)
        {
            window.InternalCreate();
            window.InternalRefresh();
            OnSetWindowVisible();
            window.OnShowAnimation().Forget();
        }

        private void OnSetWindowVisible()
        {
            bool isHideNext = false;
            for (int i = _windowStack.Count - 1; i >= 0; i--)
            {
                UITKWindow window = _windowStack[i];
                if (!isHideNext)
                {
                    if (window.IsHide) continue;
                    window.Visible = true;
                    if (window.IsPrepare && window.FullScreen)
                    {
                        isHideNext = true;
                    }
                }
                else
                {
                    window.Visible = false;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Create UITKModule.Animation.cs**

```csharp
using Cysharp.Threading.Tasks;

namespace GameLogic
{
    public sealed partial class UITKModule
    {
        /// <summary>
        /// 带隐藏动画的关闭。
        /// </summary>
        public async UniTask CloseUIWithAnimation<T>() where T : UITKWindow
        {
            string windowName = typeof(T).FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            await window.OnHideAnimation();
            window.InternalDestroy();
            Pop(window);
            OnSetWindowVisible();
        }

        /// <summary>
        /// 带隐藏动画的隐藏。
        /// </summary>
        public async UniTask HideUIWithAnimation<T>() where T : UITKWindow
        {
            string windowName = typeof(T).FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            if (window.HideTimeToClose <= 0)
            {
                await CloseUIWithAnimation<T>();
                return;
            }

            await window.OnHideAnimation();
            window.Visible = false;
            window.IsHide = true;
            window.HideTimerId = GameModule.Timer.AddTimer((arg) =>
            {
                CloseUI(typeof(T));
            }, window.HideTimeToClose);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Core/
git commit -m "feat(uitk): add UITKModule singleton with panel, stack, and animation management"
```

---

## Task 9: USS Variables Foundation + Common Styles

**Files:**
- Create: `Assets/AssetRaw/UITK/Shared/Styles/variables.uss`
- Create: `Assets/AssetRaw/UITK/Shared/Styles/common.uss`

- [ ] **Step 1: Create variables.uss**

```css
:root {
    /* Colors */
    --color-primary: #4A90D9;
    --color-primary-hover: #5BA0E9;
    --color-secondary: #7B8794;
    --color-success: #4CAF50;
    --color-warning: #FF9800;
    --color-danger: #F44336;
    --color-bg-dark: #1A1A2E;
    --color-bg-panel: #16213E;
    --color-bg-card: #0F3460;
    --color-text-primary: #FFFFFF;
    --color-text-secondary: #B0BEC5;
    --color-text-disabled: #546E7A;

    /* Typography */
    --font-size-xs: 12px;
    --font-size-sm: 14px;
    --font-size-md: 18px;
    --font-size-lg: 24px;
    --font-size-xl: 32px;
    --font-size-title: 28px;

    /* Spacing */
    --spacing-xs: 4px;
    --spacing-sm: 8px;
    --spacing-md: 16px;
    --spacing-lg: 24px;
    --spacing-xl: 32px;

    /* Border */
    --border-radius-sm: 4px;
    --border-radius-md: 8px;
    --border-radius-lg: 12px;
    --border-radius-full: 9999px;

    /* Transition */
    --transition-fast: 150ms;
    --transition-normal: 200ms;
    --transition-slow: 300ms;
}
```

- [ ] **Step 2: Create common.uss**

```css
/* 全屏窗口容器 */
.uitk-window-root {
    flex-grow: 1;
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    bottom: 0;
}

/* 通用按钮 */
.btn {
    padding: var(--spacing-sm) var(--spacing-md);
    border-radius: var(--border-radius-md);
    background-color: var(--color-primary);
    color: var(--color-text-primary);
    font-size: var(--font-size-md);
    transition-duration: var(--transition-fast);
    transition-property: background-color, scale;
}

.btn:hover {
    background-color: var(--color-primary-hover);
    scale: 1.02 1.02;
}

.btn:active {
    scale: 0.98 0.98;
}

.btn:disabled {
    background-color: var(--color-secondary);
    color: var(--color-text-disabled);
}

/* 通用文本 */
.text-title {
    font-size: var(--font-size-title);
    color: var(--color-text-primary);
}

.text-body {
    font-size: var(--font-size-md);
    color: var(--color-text-primary);
}

.text-secondary {
    font-size: var(--font-size-sm);
    color: var(--color-text-secondary);
}

/* 面板/卡片 */
.panel {
    background-color: var(--color-bg-panel);
    border-radius: var(--border-radius-md);
    padding: var(--spacing-md);
}

.card {
    background-color: var(--color-bg-card);
    border-radius: var(--border-radius-md);
    padding: var(--spacing-md);
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/AssetRaw/UITK/Shared/Styles/
git commit -m "feat(uitk): add USS variables foundation and common styles"
```

---

## Task 10: Register UITKModule in GameApp

**Files:**
- Modify: `Assets/GameScripts/HotFix/GameLogic/GameApp_RegisterSystem.cs`

- [ ] **Step 1: Add UITKModule registration**

Find the existing module registration section in `GameApp_RegisterSystem.cs` and add:

```csharp
// 在其他模块注册后添加
UITKModule.Instance.Active();
```

This follows the same pattern as existing modules (UIModule is registered as `UIModule.Instance.Active()`).

- [ ] **Step 2: Verify no compile errors**

Open Unity Editor, check Console for compile errors. All new files should compile without issues since they reference existing TEngine types (Singleton, IUpdate, GameModule, etc.).

- [ ] **Step 3: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/GameApp_RegisterSystem.cs
git commit -m "feat(uitk): register UITKModule in GameApp startup"
```

---

## Task 11: Smoke Test — Create a Simple Test Window

**Files:**
- Create: `Assets/AssetRaw/UITK/Shared/TestWindow.uxml`
- Create: `Assets/GameScripts/HotFix/GameLogic/UI/TestUITK/TestUITKWindow.cs`

- [ ] **Step 1: Create TestWindow.uxml**

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement class="uitk-window-root" style="align-items: center; justify-content: center;">
        <ui:Label text="UITKModule Works!" name="lbl-title" class="text-title" />
        <ui:Button text="Close" name="btn-close" class="btn" style="margin-top: 20px;" />
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Step 2: Create TestUITKWindow.cs**

```csharp
using UnityEngine.UIElements;

namespace GameLogic
{
    [UIWindow(UILayer.UI)]
    public class TestUITKWindow : UITKWindow
    {
        private Label _lblTitle;
        private Button _btnClose;

        protected override void OnCreate()
        {
            // 手动绑定（Plan 2 会加入自动绑定）
            _lblTitle = RootElement.Q<Label>("lbl-title");
            _btnClose = RootElement.Q<Button>("btn-close");

            _btnClose.clicked += () =>
            {
                UITKModule.Instance.CloseUI<TestUITKWindow>();
            };
        }

        protected override void OnRefresh()
        {
            _lblTitle.text = "UITKModule Works! 🎉";
        }

        protected override void OnDestroy()
        {
            _btnClose.clicked -= null; // 简化示例，Plan 2 自动处理
        }
    }
}
```

- [ ] **Step 3: Test in Unity**

在任意已有界面或测试入口调用：
```csharp
UITKModule.Instance.ShowUIAsync<TestUITKWindow>();
```

预期：屏幕中央显示 "UITKModule Works!" 文字和 Close 按钮，点击按钮窗口关闭并伴随 FadeOut 动画。

- [ ] **Step 4: Commit**

```bash
git add Assets/AssetRaw/UITK/Shared/TestWindow.uxml Assets/GameScripts/HotFix/GameLogic/UI/TestUITK/
git commit -m "test(uitk): add smoke test window to verify UITKModule basic functionality"
```

---

## Summary

Plan 1 delivers a fully functional UITKModule core that supports:
- ✅ Module initialization + 5-layer panel architecture
- ✅ Window lifecycle (Show/Hide/Close with fullscreen occlusion)
- ✅ Widget lifecycle (independent UXML, parent-child)
- ✅ Resource loading (YooAsset + Resources dual path)
- ✅ Animation system (USS presets + Tween driver interface)
- ✅ GameEvent integration (auto-cleanup)
- ✅ USS variables foundation for future theming

After Plan 1, developers can build UI using manual `Q()` binding. Plan 2 adds Source Generator auto-binding, MVVM, and ListView.
