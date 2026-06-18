# UITKModule 概览

> UIToolkit UI 框架模块，与现有 UIModule (UGUI) 并行共存。

## 访问方式

```csharp
GameModule.UITK.ShowUIAsync<T>(userDatas);   // 异步打开
GameModule.UITK.ShowUI<T>(userDatas);        // 同步打开
GameModule.UITK.CloseUI<T>();                // 关闭
GameModule.UITK.HideUI<T>();                 // 隐藏（定时关闭）
GameModule.UITK.CloseUIWithAnimation<T>();   // 带动画关闭
GameModule.UITK.HasWindow<T>();              // 是否存在
GameModule.UITK.GetUI<T>();                  // 获取实例
```

## 分层 Panel 架构

| 层级 | UILayer 枚举 | sortingOrder | 用途 |
|------|-------------|-------------|------|
| Bottom | 0 | 0 | 背景层 |
| UI | 1 | 2000 | 主要 UI |
| Top | 2 | 4000 | 浮动 UI |
| Tips | 3 | 6000 | 提示弹窗 |
| System | 4 | 8000 | 系统级（确认框等）|

- 同层内窗口深度靠 VisualElement 子节点顺序
- 可见性用 `style.display = none/flex`
- 全屏窗口 (`FullScreen = true`) 会遮挡同层及下层窗口

## 窗口声明

```csharp
[UIWindow(UILayer.UI)]                          // 基本
[UIWindow(UILayer.UI, FullScreen = true)]       // 全屏
[UIWindow(UILayer.Tips)]                        // Tips 层
[UIWindow(UILayer.UI, Location = "path")]       // 指定资源路径
[UIWindow(UILayer.UI, Package = "UITK_Bag")]    // 指定 AB 包名
[UIWindow(UILayer.UI, FromResources = true)]    // 从 Resources 加载
[UIWindow(UILayer.UI, HideTimeToClose = 5)]     // 隐藏后 5 秒自动关闭
```

## 资源加载

- YooAsset `AddressByFileName` 规则：文件名即 Address
- 不指定 Location → 类名作为 Address（如 `LoginWindow` → 找 `LoginWindow.uxml`）
- 资源目录：`Assets/AssetRaw/UITK/` 按业务模块划分子目录
- Window 关闭时自动释放 UXML 资产

## 音效

```csharp
// 初始化
GameModule.UITK.ClickSoundHandler = new GameClickSoundHandler();

// 静音按钮
// UXML 中加 class="no-sound"
```

## 动画

```csharp
// Window 中配置
protected override UITKAnimationType ShowAnimation => UITKAnimationType.ScaleIn;
protected override UITKAnimationType HideAnimation => UITKAnimationType.FadeOut;
protected override int AnimationDuration => 300;

// 预设：None, FadeIn, FadeOut, SlideFromBottom, SlideToBottom, SlideFromRight, SlideToRight, ScaleIn, ScaleOut
```

## 模块文件位置

```
Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/
```
