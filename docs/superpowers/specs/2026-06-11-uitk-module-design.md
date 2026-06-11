# UITKModule 设计文档

> **日期**: 2026-06-11
> **状态**: 设计确认
> **目标版本**: Unity 2022.3.63f2
> **与现有系统关系**: 并行共存，现有 UIModule (UGUI) 保留不动

---

## 1. 概述

为 TEngine 框架新增基于 Unity UI Toolkit 的完整 UI 模块（UITKModule），全面替代 UGUI 用于新 UI 开发。核心特性：

- 纯 C# 驱动，去 MonoBehaviour
- Source Generator 编译期自动绑定 + UXML 校验
- 完整 MVVM 数据绑定
- 分层 Panel 架构
- 与现有 GameEvent / Timer / Resource 系统完全复用
- 热更新支持（UXML/USS 走 AB，代码走 HybridCLR）

---

## 2. 整体架构

```
┌────────────────────────────────────────────────────────────┐
│                    共享层 (不修改)                           │
├────────────────────────────────────────────────────────────┤
│  GameEvent + GameEventMgr + MemoryPool (事件自动清理)       │
│  GameModule.Resource / IResourceModule (YooAsset 加载管道)  │
│  GameModule.Timer (HideTimeToClose 定时关闭)               │
│  WindowAttribute (UILayer, Location, FullScreen, etc.)     │
└────────────────────────────────────────────────────────────┘
                            │
            ┌───────────────┴───────────────┐
            ▼                               ▼
┌─────────────────────┐         ┌──────────────────────────┐
│  UIModule (UGUI)    │         │  UITKModule (UIToolkit)   │
│  保持现有，不动      │         │  新增模块                 │
└─────────────────────┘         └──────────────────────────┘
```

### 模块内部目录结构

```
UITKModule/
├── Core/                          # 模块核心
│   ├── UITKModule.cs              # 模块单例，IUpdate 驱动
│   ├── UITKModule.Panel.cs        # 分层 Panel 管理 (5层)
│   ├── UITKModule.Stack.cs        # 窗口栈管理 (Push/Pop/Sort/Visible)
│   └── UITKModule.Animation.cs    # 内置开关动画预设
│
├── Base/                          # 基类
│   ├── UITKBase.cs                # 基类 (事件、子Widget列表、Update脏标记)
│   ├── UITKWindow.cs              # 窗口 (生命周期、深度、可见性)
│   └── UITKWidget.cs              # 组件 (独立UXML、父子关系)
│
├── Binding/                       # 绑定系统
│   ├── QAttribute.cs              # [Q] [Q("name")] 元素查询标记
│   ├── OnClickAttribute.cs        # [OnClick] 点击事件标记
│   ├── OnChangeAttribute.cs       # [OnChange] 值变化事件标记
│   ├── BindAttribute.cs           # [Bind] MVVM 数据绑定标记
│   └── BindCommandAttribute.cs    # [BindCommand] 命令绑定标记
│
├── MVVM/                          # MVVM 支持
│   ├── BindableProperty.cs        # 可绑定属性
│   ├── BindableCommand.cs         # 可绑定命令
│   ├── BindableList.cs            # 可绑定集合
│   ├── ViewModelBase.cs           # ViewModel 基类
│   ├── BindingMode.cs             # 绑定方向枚举
│   └── IValueConverter.cs         # 类型转换器接口 + 内置转换器
│
├── Resource/                      # 资源加载
│   ├── IUITKResourceLoader.cs     # 接口 (VisualTreeAsset/StyleSheet)
│   └── UITKResourceLoader.cs      # 默认实现 (YooAsset + Resources 双路径)
│
├── Animation/                     # 动画系统
│   ├── IUITKAnimationDriver.cs    # 动画驱动器接口
│   ├── USSAnimationDriver.cs      # 默认 USS Transition 实现
│   └── UITKAnimationType.cs       # 预设动画枚举
│
├── ListView/                      # 列表封装
│   └── UITKListView.cs            # 虚拟化列表 + Widget 生命周期
│
└── SourceGenerator/               # 编译期代码生成 (独立程序集)
    ├── UITKBindingGenerator.cs    # 生成 __AutoBind / __AutoBindEvents / __AutoBindViewModel
    ├── UXMLParser.cs              # 轻量 UXML 解析器
    ├── NamingConventions.cs       # camelCase → kebab-case 转换
    └── DiagnosticDescriptors.cs   # 编译错误/警告定义
```

---

## 3. 分层 Panel 架构

### Panel 布局

```
Scene
└── UITKRoot (GameObject, DontDestroyOnLoad)
    ├── Panel_Bottom   (UIDocument + PanelSettings, sortingOrder=0)
    ├── Panel_UI       (UIDocument + PanelSettings, sortingOrder=2000)
    ├── Panel_Top      (UIDocument + PanelSettings, sortingOrder=4000)
    ├── Panel_Tips     (UIDocument + PanelSettings, sortingOrder=6000)
    └── Panel_System   (UIDocument + PanelSettings, sortingOrder=8000)
```

### UILayer 枚举

```csharp
public enum UILayer
{
    Bottom = 0,
    UI = 1,
    Top = 2,
    Tips = 3,
    System = 4,
}
```

### 同层内窗口排序

不需要数值排序，纯靠 VisualElement 子节点顺序：
- Push：`_layerRoots[layer].Add(window.RootElement)` → 最上层
- 重新 Show：`window.RootElement.BringToFront()`
- Pop：`window.RootElement.RemoveFromHierarchy()`

### 可见性控制

| 操作 | 实现 |
|------|------|
| 显示 | `style.display = DisplayStyle.Flex` + `pickingMode = Position` |
| 隐藏 | `style.display = DisplayStyle.None` + `pickingMode = Ignore` |

### 全屏遮挡

从栈顶向下遍历，遇到第一个 `IsPrepare && FullScreen` 的窗口后，后续窗口全部 `Visible = false`。与现有 UIModule 算法一致。

---

## 4. 生命周期

### Window 完整流程

```
ShowUIAsync<T>(userData)
├─ CreateInstance<T>() → 读取 [UIWindow] Attribute
├─ Push(window) → 按层级插入栈
├─ InternalLoad(assetName, isAsync)
│   ├─ LoadVisualTreeAssetAsync / Resources.Load
│   ├─ asset.CloneTree() → rootElement
│   ├─ 挂载到对应层 Panel
│   └─ IsLoadDone = true
├─ OnWindowPrepare(window)
│   ├─ InternalCreate() [仅首次]
│   │   ├─ __UITKAutoBind(root)           ← Generator
│   │   ├─ __UITKAutoBindEvents()         ← Generator
│   │   ├─ Inject()
│   │   ├─ OnCreate()
│   │   └─ RegisterEvent()
│   ├─ InternalRefresh()
│   │   └─ OnRefresh()                    ← 每次 Show 执行
│   ├─ OnSortWindowDepth(layer)
│   ├─ OnSetWindowVisible()
│   └─ PlayShowAnimation()
└─ 运行态: OnUpdate() [每帧, 仅 Visible && IsPrepare]
```

### Window 关闭

```
CloseUI<T>()
├─ await OnHideAnimation()
├─ InternalDestroy()
│   ├─ OnDestroy()
│   ├─ __UITKAutoUnbindEvents()
│   ├─ __UITKAutoUnbindViewModel()
│   ├─ RemoveAllUIEvent()
│   ├─ 递归销毁子 Widget
│   ├─ rootElement.RemoveFromHierarchy()
│   └─ 释放资源句柄
├─ Pop(window)
├─ OnSortWindowDepth(layer)
└─ OnSetWindowVisible()
```

### Window 隐藏

```
HideUI<T>()
├─ HideTimeToClose <= 0 → 直接 CloseUI
├─ await OnHideAnimation()
├─ Visible = false
├─ IsHide = true
├─ Timer → 到期自动 CloseUI
└─ 若 FullScreen → OnSetWindowVisible()
```

### Widget 生命周期

```
创建: parent.CreateWidget<T>(parentElement)
├─ LoadVisualTreeAssetAsync(typeof(T).Name)
├─ asset.CloneTree() → widgetRoot
├─ parentElement.Add(widgetRoot)
├─ __UITKAutoBind / __UITKAutoBindEvents
├─ Inject() → OnCreate() → RegisterEvent() → OnRefresh()
└─ Parent.ListChild.Add(this)

销毁: widget.Destroy()
├─ OnDestroy()
├─ __UITKAutoUnbindEvents / __UITKAutoUnbindViewModel
├─ RemoveAllUIEvent()
├─ 递归销毁子 Widget
├─ widgetRoot.RemoveFromHierarchy()
├─ 释放资源句柄
└─ Parent.ListChild.Remove(this)
```

---

## 5. 自动绑定系统

### Attribute 定义

```csharp
// 元素查询
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class QAttribute : Attribute
{
    public string Name { get; }
    public QAttribute() { }
    public QAttribute(string name) => Name = name;
}

// 点击事件
[AttributeUsage(AttributeTargets.Method)]
public class OnClickAttribute : Attribute
{
    public string Target { get; }
    public OnClickAttribute() { }
    public OnClickAttribute(string target) => Target = target;
}

// 值变化事件
[AttributeUsage(AttributeTargets.Method)]
public class OnChangeAttribute : Attribute
{
    public string Target { get; }
    public OnChangeAttribute(string target) => Target = target;
}
```

### 命名转换规则

```
C# camelCase    →    UXML kebab-case
btnLogin        →    btn-login
lblPlayerHP     →    lbl-player-hp
inputAccount    →    input-account
imgAvatarBg     →    img-avatar-bg
```

### 事件方法名推导

```
方法名                →  目标
OnBtnLogin()         →  btn-login.clicked
OnBtnClose()         →  btn-close.clicked
```

### Source Generator 编译期校验

1. 按 `类名.uxml` 或 `[UIWindow(location="xxx")]` 定位 UXML 文件
2. 解析 UXML 提取所有 `name` + 类型
3. 校验 `[Q] Button btnLogin` → UXML 中必须有 `name="btn-login"` 且类型为 Button
4. 不匹配报编译错误：`UITK001: Element 'btn-login' not found in LoginWindow.uxml`

### 使用示例

```csharp
[UIWindow(UILayer.UI)]
public partial class LoginWindow : UITKWindow
{
    [Q] Label lblTitle;
    [Q] Button btnLogin;
    [Q] TextField inputAccount;
    [Q("server-list")] DropdownField serverDropdown;

    [OnClick] void OnBtnLogin() { /* ... */ }
    [OnChange("server-list")] void OnServerChanged(ChangeEvent<string> evt) { /* ... */ }
}
```

---

## 6. MVVM 数据绑定

### 核心组件

```csharp
// 可绑定属性
public class BindableProperty<T>
{
    private T _value;
    public event Action<T> OnValueChanged;
    public T Value { get; set; } // set 时触发 OnValueChanged
    public static implicit operator T(BindableProperty<T> prop) => prop.Value;
}

// 可绑定命令
public class BindableCommand
{
    public bool CanExecute();
    public void Execute();
    public event Action CanExecuteChanged;
}

// 可绑定集合
public class BindableList<T> : IList<T>
{
    public event Action OnListChanged;
    public event Action<int, T> OnItemAdded;
    public event Action<int, T> OnItemRemoved;
    public event Action<int, T, T> OnItemChanged;
}

// ViewModel 基类
public abstract class ViewModelBase
{
    public virtual void Dispose() { }
}

// 绑定方向
public enum BindingMode { OneWay, TwoWay, OneWayToSource }

// 类型转换器
public interface IValueConverter<TSource, TTarget>
{
    TTarget Convert(TSource value);
    TSource ConvertBack(TTarget value);
}
```

### Bind Attribute

```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class BindAttribute : Attribute
{
    public string Path { get; }
    public BindingMode Mode { get; }
    public string Format { get; }
    public Type Converter { get; }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class BindCommandAttribute : Attribute
{
    public string CommandName { get; }
}
```

### 使用示例

```csharp
// ViewModel
public class ShopViewModel : ViewModelBase
{
    public BindableProperty<int> Gold { get; } = new(0);
    public BindableProperty<string> PlayerName { get; } = new("");
    public BindableList<ShopItemData> Items { get; } = new();
    public BindableCommand BuyCommand { get; }
    public BindableCommand RefreshCommand { get; }
}

// View
[UIWindow(UILayer.UI, package: "UITK_Shop")]
public partial class ShopWindow : UITKWindow
{
    [Q] [Bind("Gold", format: "{0:N0}")] Label lblGold;
    [Q] [Bind("PlayerName")] Label lblName;
    [Q] [BindCommand("BuyCommand")] Button btnBuy;
    [Q] [BindCommand("RefreshCommand")] Button btnRefresh;
    [Q] [Bind("Items")] UITKListView<ShopItemWidget> listItems;

    private ShopViewModel _vm;

    protected override void OnCreate()
    {
        _vm = new ShopViewModel();
        BindContext(_vm);   // 一行完成所有绑定
    }

    protected override void OnDestroy()
    {
        UnbindContext();    // 自动解绑
        _vm.Dispose();
    }
}
```

### 双向绑定 (TwoWay)

```csharp
[Q] [Bind("Account", BindingMode.TwoWay)] TextField inputAccount;
// Generator 生成：ViewModel → View + View → ViewModel 双向监听
```

### 内置类型转换器

- `IntToString` / `FloatToString` — 数值 → 文本
- `BoolToDisplay` — bool → DisplayStyle.Flex/None
- `BoolToOpacity` — bool → 1/0
- `SpriteToBackground` — Sprite → BackgroundImage

### MVVM 与 GameEvent 的关系

- `GameEvent`：模块间通信（跨系统）
- `MVVM Bind`：窗口内 ViewModel ↔ View 同步

ViewModel 可监听 GameEvent 更新自身数据，两者互补不冲突。

---

## 7. 动画系统

### 预设动画

```csharp
public enum UITKAnimationType
{
    None, FadeIn, FadeOut,
    SlideFromBottom, SlideToBottom,
    SlideFromRight, SlideToRight,
    ScaleIn, ScaleOut,
}
```

### Window 动画接口

```csharp
protected virtual UITKAnimationType ShowAnimation => UITKAnimationType.FadeIn;
protected virtual UITKAnimationType HideAnimation => UITKAnimationType.FadeOut;
protected virtual int AnimationDuration => 200;
protected virtual UniTask OnShowAnimation();   // 可完全自定义
protected virtual UniTask OnHideAnimation();   // 可完全自定义
```

### 动画驱动器接口（预留 Tween）

```csharp
public interface IUITKAnimationDriver
{
    UniTask Play(VisualElement target, UITKAnimationType type, bool isShow, int durationMs);
}

// 默认实现
public class USSAnimationDriver : IUITKAnimationDriver { ... }

// 后续可选
// public class DOTweenAnimationDriver : IUITKAnimationDriver { ... }
```

通过 `UITKModule.AnimationDriver` 注入，一行切换。

---

## 8. UITKListView 封装

### 核心设计

- 包装 Unity 原生 `ListView` 虚拟化引擎
- 列表项为完整 UITKWidget（享有自动绑定 + 生命周期）
- 内置对象池（回收而非销毁）
- 支持 BindableList 自动监听增删改刷新

### Widget 列表项接口

```csharp
public virtual void OnBindData(object data, int index) { }
public virtual void OnUnbindData() { }
```

### 使用方式

```csharp
// MVVM 绑定
[Q] [Bind("Items")] UITKListView<ShopItemWidget> listItems;

// 手动设置
listItems.SetData(dataList);

// 固定高度优化
gridItems.FixedItemHeight = 80;
```

---

## 9. 资源加载管道

### 接口

```csharp
public interface IUITKResourceLoader
{
    VisualTreeAsset LoadVisualTreeAsset(string location, string packageName = "");
    UniTask<VisualTreeAsset> LoadVisualTreeAssetAsync(string location, CancellationToken ct = default, string packageName = "");
    StyleSheet LoadStyleSheet(string location, string packageName = "");
    UniTask<StyleSheet> LoadStyleSheetAsync(string location, CancellationToken ct = default, string packageName = "");
    PanelSettings LoadPanelSettings(string location, string packageName = "");
    void Unload(UnityEngine.Object asset);
}
```

### FromResources 支持

WindowAttribute 保留 `FromResources` 字段，加载器内部分支：
- `FromResources = true` → `Resources.Load<VisualTreeAsset>(location)`
- `FromResources = false` → `GameModule.Resource.LoadAssetAsync<VisualTreeAsset>(location, packageName)`

### 按业务模块分包

```
AssetRaw/UITK/
├── Shared/        # 公共 (首包)：variables.uss, common.uss, CommonDialog.uxml, PanelSettings
├── Login/         # 登录模块 (首包)
├── Lobby/         # 大厅模块
├── Bag/           # 背包模块
├── Battle/        # 战斗模块
└── Shop/          # 商城模块
```

WindowAttribute 可选 `package` 字段指定 YooAsset 资源包名：

```csharp
[UIWindow(UILayer.UI, package: "UITK_Bag")]
public partial class BagWindow : UITKWindow { }
```

### 释放策略

- Window 关闭：`RemoveFromHierarchy()` + `Unload(visualTreeAsset)`
- Widget 销毁：`RemoveFromHierarchy()` + `Unload(widgetAsset)`

---

## 10. 主题换肤（基础设施）

### 当前版本

- 所有 USS 使用 CSS 自定义变量定义颜色/字号/间距
- PanelSettings 预留 `themeStyleSheet` 加载/切换接口
- 不实现具体主题，不写第二套 USS

### 后续扩展路径

- 新增 TSS 文件覆盖变量值 → 零代码换肤
- 运行时 `PanelSettings.themeStyleSheet = newTSS` 切换

### USS 变量示例

```css
:root {
    --color-primary: #4A90D9;
    --color-secondary: #7B8794;
    --color-bg: #1A1A2E;
    --font-size-title: 28px;
    --font-size-body: 18px;
    --spacing-sm: 8px;
    --spacing-md: 16px;
    --spacing-lg: 24px;
}
```

---

## 11. 与现有系统共存

| 方面 | 说明 |
|------|------|
| 调用方式 | `UIModule.Instance.ShowUI<T>()` (UGUI) vs `UITKModule.Instance.ShowUI<T>()` (UIToolkit) |
| 事件通信 | 共享 GameEvent，两套 UI 可互相通信 |
| 同屏共存 | 通过 sortingOrder 控制 UIToolkit Panel 与 UGUI Canvas 的叠加层级 |
| 迁移策略 | 新界面用 UITKModule，旧界面按需逐步迁移 |

---

## 12. Source Generator 项目结构

```
UITKSourceGenerator/                    # 独立 .csproj (netstandard2.0)
├── UITKBindingGenerator.cs            # 主 Generator
│   ├── 扫描 [Q] → 生成 __UITKAutoBind
│   ├── 扫描 [OnClick]/[OnChange] → 生成 __UITKAutoBindEvents / Unbind
│   └── 扫描 [Bind]/[BindCommand] → 生成 __UITKAutoBindViewModel / Unbind
├── UXMLParser.cs                      # XML 解析 UXML name + type
├── NamingConventions.cs               # camelCase → kebab-case
├── DiagnosticDescriptors.cs           # 编译错误定义
│   ├── UITK001: Element not found
│   ├── UITK002: Type mismatch
│   └── UITK003: ViewModel property not found
└── UITKSourceGenerator.csproj
```

Unity 项目通过 `.asmdef` 的 Analyzers 引用此 Generator DLL。

---

## 13. 性能设计要点

| 要点 | 措施 |
|------|------|
| 零运行时反射 | Source Generator 编译期完成所有绑定代码生成 |
| Q() 查询仅一次 | OnCreate 时缓存，后续直接用字段 |
| 同层合批 | 分层 Panel 内自动合批，额外仅 5 个 Panel 基底开销 |
| 列表虚拟化 | UITKListView 内置对象池 + Unity ListView 虚拟化引擎 |
| 隐藏停止渲染 | display:none 停止布局+渲染，非 opacity |
| USS 变量动画 | 优先 transform 属性 + usageHints，避免触发布局重算 |
| 事件自动清理 | GameEventMgr 池化 + Generator 解绑代码，零泄漏 |

---

## 14. 限制与已知约束

| 约束 | 说明 |
|------|------|
| Unity 2022.3 无官方 DataBinding | 使用自研 MVVM，不依赖 Unity 6 特性 |
| 无世界空间 UI | 2022.3 不支持 UIToolkit 世界空间，需此类需求仍用 UGUI |
| USS 动画有限 | 复杂序列动画需通过 IUITKAnimationDriver 接入 Tween 库 |
| Source Generator 需 UXML 路径约定 | 类名.uxml 或 [UIWindow(location)] 显式指定 |
| PanelSettings 必须预建 | 不支持运行时 new PanelSettings()，需 Editor 预建资产 |
