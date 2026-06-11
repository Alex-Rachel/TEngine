# UITKModule 设计文档

> **日期**: 2026-06-11
> **状态**: 实现中
> **目标版本**: Unity 2022.3.63f2
> **与现有系统关系**: 并行共存，现有 UIModule (UGUI) 保留不动

---

## 1. 概述

为 TEngine 框架新增基于 Unity UI Toolkit 的完整 UI 模块（UITKModule），全面替代 UGUI 用于新 UI 开发。核心特性：

- 纯 C# 驱动，去 MonoBehaviour
- Editor 预生成自动绑定代码（.bindgen.cs）+ UXML 校验
- 完整 MVVM 数据绑定（BindableProperty / Command / List）
- 分层 Panel 架构（5 层）
- 统一按钮点击音效（IUITKClickSoundHandler 全局拦截）
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
├── Animation/                     # 动画系统
│   ├── UITKAnimationType.cs       # 预设动画枚举
│   ├── IUITKAnimationDriver.cs    # 动画驱动器接口（预留 Tween）
│   └── USSAnimationDriver.cs      # 默认 USS Transition 实现
├── Base/                          # 基类
│   ├── UITKBase.cs                # 基类 (事件、Widget工厂、MVVM绑定钩子)
│   ├── UITKWindow.cs              # 窗口 (生命周期、可见性、动画)
│   ├── UITKWidget.cs              # 组件 (独立UXML、父子关系)
│   └── UIWindowAttribute.cs       # [UIWindow] 窗口声明特性
├── Binding/                       # 绑定 Attribute 定义
│   ├── QAttribute.cs              # [Q] 元素查询
│   ├── OnClickAttribute.cs        # [OnClick] 点击事件
│   ├── OnChangeAttribute.cs       # [OnChange] 值变化事件
│   ├── BindAttribute.cs           # [Bind] MVVM 数据绑定
│   ├── BindCommandAttribute.cs    # [BindCommand] 命令绑定
│   └── BindingMode.cs             # 绑定方向枚举
├── Core/                          # 模块核心
│   ├── UITKModule.cs              # 单例主文件 (Show/Close/Hide)
│   ├── UITKModule.Panel.cs        # 分层 Panel + 全局点击音效拦截
│   ├── UITKModule.Stack.cs        # 窗口栈 (Push/Pop/全屏遮挡)
│   ├── UITKModule.Animation.cs    # 带动画的关闭/隐藏
│   └── IUITKClickSoundHandler.cs  # 按钮音效处理器接口
├── MVVM/                          # MVVM 支持
│   ├── BindableProperty.cs        # 可绑定属性
│   ├── BindableCommand.cs         # 可绑定命令
│   ├── BindableList.cs            # 可绑定集合
│   ├── ViewModelBase.cs           # ViewModel 基类
│   └── IValueConverter.cs         # 类型转换器 + 内置转换器
├── ListView/                      # 列表封装
│   └── UITKListView.cs            # 虚拟化列表 + Widget 生命周期
└── Resource/                      # 资源加载
    ├── IUITKResourceLoader.cs     # 接口
    └── UITKResourceLoader.cs      # YooAsset + Resources 双路径实现

Editor/UITKBindingGenerator/       # Editor 代码生成工具
├── UITKBindingGenerator.cs        # 主生成器
├── UITKNamingHelper.cs            # camelCase → kebab-case
└── UITKUXMLValidator.cs           # UXML 解析 + 校验
```

---

## 3. 分层 Panel 架构

```
Scene
└── UITKRoot (GameObject, DontDestroyOnLoad)
    ├── Panel_Bottom   (UIDocument + PanelSettings, sortingOrder=0)
    ├── Panel_UI       (UIDocument + PanelSettings, sortingOrder=2000)
    ├── Panel_Top      (UIDocument + PanelSettings, sortingOrder=4000)
    ├── Panel_Tips     (UIDocument + PanelSettings, sortingOrder=6000)
    └── Panel_System   (UIDocument + PanelSettings, sortingOrder=8000)
```

- 5 层共享一个 PanelSettings 资产（Editor 预建）
- 同层内窗口深度通过 VisualElement 子节点顺序控制
- 可见性通过 `style.display = none/flex` 控制
- 全局按钮点击音效通过 Panel root 的 ClickEvent 冒泡拦截

---

## 4. 生命周期

### Window 流程

```
ShowUIAsync<T>(userData)
├─ CreateInstance<T>() → 读取 [UIWindow] Attribute
├─ Push(window) → 按层级插入栈
├─ InternalLoad → 加载 UXML → CloneTree → 挂载到 Panel
├─ InternalCreate() [仅首次]
│   ├─ __UITKAutoBind(root)        ← 自动绑定（框架调用，开发者无感）
│   ├─ __UITKAutoBindEvents()      ← 自动绑定事件
│   ├─ Inject()
│   ├─ OnCreate()                  ← 用户代码
│   └─ RegisterEvent()
├─ InternalRefresh() → OnRefresh() ← 每次 Show 执行
├─ OnSetWindowVisible()            ← 全屏遮挡
└─ OnShowAnimation()               ← 显示动画
```

### Window 关闭

```
CloseUI<T>()
├─ OnDestroy()
├─ __UITKAutoUnbindEvents()        ← 自动解绑（框架调用）
├─ RemoveAllUIEvent()              ← GameEvent 自动清理
├─ 递归销毁子 Widget
├─ RemoveFromHierarchy + 释放资源
└─ OnSetWindowVisible()
```

---

## 5. 自动绑定系统

### 方案：Editor 预生成

- 通过 Unity Editor 菜单 `TEngine/UITK/Generate All Bindings` 触发
- 扫描所有继承 UITKWindow/UITKWidget 的 partial class
- 解析 [Q]、[OnClick]、[OnChange] Attribute
- 生成 `.bindgen.cs` 到源文件同目录
- 生成的代码是普通 C#，正常参与 HybridCLR 热更新
- 零运行时反射

### Attribute 定义

```csharp
[Q]                     // 自动查询，字段名 camelCase → UXML name kebab-case
[Q("explicit-name")]    // 显式指定 UXML name
[OnClick]               // 方法名推导目标：OnBtnLogin → btn-login
[OnClick("target")]     // 显式指定目标
[OnChange("target")]    // 值变化事件
```

### 命名转换

```
btnLogin     → btn-login
lblPlayerHP  → lbl-player-hp
inputAccount → input-account
```

### 使用示例

```csharp
[UIWindow(UILayer.UI)]
public partial class LoginWindow : UITKWindow
{
    [Q] Button btnLogin;
    [Q] Label lblTitle;
    [Q] TextField inputAccount;

    [OnClick] void OnBtnLogin() { /* 业务逻辑 */ }
    [OnChange("input-account")] void OnAccountChanged(ChangeEvent<string> evt) { }

    protected override void OnCreate()
    {
        // 字段已自动填充，事件已自动绑定，直接使用
        lblTitle.text = "Welcome";
    }
    // OnDestroy 不需要手动解绑
}
```

---

## 6. MVVM 数据绑定

### 核心组件

- `BindableProperty<T>` — 值变化通知
- `BindableCommand` — 命令 + CanExecute
- `BindableList<T>` — 可观察集合
- `ViewModelBase` — ViewModel 基类

### 使用方式（当前为手动绑定，后续 Generator 支持 [Bind]）

```csharp
protected override void OnCreate()
{
    _vm = new ShopViewModel();
    _vm.Gold.OnValueChanged += v => lblGold.text = v.ToString();
    _vm.BuyCommand.CanExecuteChanged += () => btnBuy.SetEnabled(_vm.BuyCommand.CanExecute());
    btnBuy.clicked += _vm.BuyCommand.Execute;
}
```

---

## 7. 动画系统

### 预设

```csharp
None, FadeIn, FadeOut, SlideFromBottom, SlideToBottom,
SlideFromRight, SlideToRight, ScaleIn, ScaleOut
```

### Window 配置

```csharp
protected override UITKAnimationType ShowAnimation => UITKAnimationType.ScaleIn;
protected override UITKAnimationType HideAnimation => UITKAnimationType.FadeOut;
protected override int AnimationDuration => 300;
```

### 驱动器接口（预留 Tween）

```csharp
public interface IUITKAnimationDriver
{
    UniTask Play(VisualElement target, UITKAnimationType type, bool isShow, int durationMs);
}
// UITKModule.AnimationDriver = new DOTweenAnimationDriver(); // 后续切换
```

---

## 8. 按钮点击音效

### 设计

- Panel 级全局 ClickEvent 冒泡拦截
- 所有 Button 点击统一触发 `IUITKClickSoundHandler`
- `no-sound` CSS class 标记静音按钮
- 业务层 Handler 根据 button.name 或 class 决定音效策略

### 接口

```csharp
public interface IUITKClickSoundHandler
{
    void OnButtonClick(Button button);
}
```

### 业务层实现

```csharp
public class GameClickSoundHandler : IUITKClickSoundHandler
{
    public void OnButtonClick(Button button)
    {
        if (button.name.Contains("confirm"))
            GameModule.Audio.PlayUISound("ui_confirm");
        else
            GameModule.Audio.PlayUISound("ui_click");
    }
}

// 初始化
GameModule.UITK.ClickSoundHandler = new GameClickSoundHandler();
```

### 静音按钮

```xml
<ui:Button text="静音" class="btn no-sound" />
```

---

## 9. 资源加载

### 接口

```csharp
public interface IUITKResourceLoader
{
    VisualTreeAsset LoadVisualTreeAsset(string location, string packageName = "");
    UniTask<VisualTreeAsset> LoadVisualTreeAssetAsync(string location, ...);
    StyleSheet LoadStyleSheet(string location, string packageName = "");
    UniTask<StyleSheet> LoadStyleSheetAsync(string location, ...);
    PanelSettings LoadPanelSettings(string location, string packageName = "");
    void Unload(Object asset);
}
```

### 资源定位

- YooAsset `AddressByFileName` 规则：文件名即 Address
- `[UIWindow]` 不指定 Location → 用类名作为 Address
- `[UIWindow(Location = "path")]` → 显式指定

### 按业务模块分包

```
AssetRaw/UITK/
├── Shared/     # 公共（首包）：PanelSettings、通用样式
├── Login/      # 登录模块
├── Lobby/      # 大厅
├── Bag/        # 背包
├── Battle/     # 战斗
└── Shop/       # 商城
```

`[UIWindow(UILayer.UI, Package = "UITK_Bag")]` 指定 YooAsset 包名。

---

## 10. 主题换肤（基础设施）

- 所有 USS 使用 CSS 自定义变量 (`--color-primary` 等)
- PanelSettings 预留 themeStyleSheet 切换接口
- 后续新增 TSS 文件覆盖变量值即可换肤

---

## 11. 与现有系统共存

- `GameModule.UI.ShowUI<T>()` → UGUI
- `GameModule.UITK.ShowUI<T>()` → UIToolkit
- 共享 GameEvent 通信
- 可同屏共存（通过 sortingOrder 控制叠加层级）

---

## 12. 性能设计要点

| 要点 | 措施 |
|------|------|
| 零运行时反射 | Editor 预生成绑定代码，运行时直接调用 |
| Q() 查询仅一次 | InternalCreate 时缓存，后续直接用字段 |
| 同层合批 | 分层 Panel 内自动合批，仅 5 个 Panel 基底开销 |
| 列表虚拟化 | UITKListView 内置对象池 + Unity ListView |
| 隐藏停止渲染 | display:none 停布局+渲染 |
| 事件自动清理 | GameEventMgr 池化 + 生成解绑代码 |
| 音效零侵入 | Panel 级冒泡拦截，不在每个按钮上注册 |

---

## 13. 限制与已知约束

| 约束 | 说明 |
|------|------|
| Unity 2022.3 无官方 DataBinding | 使用自研 MVVM |
| 无世界空间 UI | 需此类需求仍用 UGUI |
| USS 动画有限 | 复杂动画需接入 Tween 库 |
| 代码生成需手动触发 | Editor 菜单 Generate Bindings |
| PanelSettings 必须预建 | Editor 创建资产 |
| 资源文件名需唯一 | AddressByFileName 规则限制 |
