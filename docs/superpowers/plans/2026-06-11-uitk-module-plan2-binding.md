# UITKModule Plan 2: Binding + MVVM + ListView + Editor Code Generator

> **状态**: ✅ 已完成

**Goal:** Add Editor pre-generation auto-binding, MVVM data binding, UITKListView, and click sound system.

**Architecture:** Editor 脚本扫描 [Q]/[OnClick]/[OnChange] 标记的 partial class，生成 `.bindgen.cs`。MVVM 使用 BindableProperty/Command/List。音效通过 Panel 级全局 ClickEvent 拦截统一处理。

---

## 完成状态

| Task | 状态 | 内容 |
|------|------|------|
| Binding Attributes | ✅ | QAttribute, OnClickAttribute, OnChangeAttribute, BindAttribute, BindCommandAttribute, BindingMode |
| MVVM Core | ✅ | BindableProperty, BindableCommand, BindableList, ViewModelBase, IValueConverter + 内置转换器 |
| UITKListView | ✅ | 虚拟化列表 + Widget 生命周期 + 对象池 |
| BindContext/UnbindContext | ✅ | UITKBase 中的 MVVM 绑定入口 |
| Editor Generator | ✅ | UITKBindingGenerator + NamingHelper + UXMLValidator |
| Auto-bind 生命周期集成 | ✅ | InternalCreate 自动调用 __UITKAutoBind + __UITKAutoBindEvents |
| Click Sound | ✅ | IUITKClickSoundHandler + Panel 全局拦截 + no-sound class |
| 测试窗口 | ✅ | TestAutoBindWindow, TestFullFeatureWindow, TestMainWindow, TestTipsPopup, TestSystemDialog, TestSubPage |

---

## 关键设计决策记录

### 自动绑定方案
- **最终方案**: Editor 脚本预生成 `.bindgen.cs`
- **弃用方案**: Roslyn Source Generator DLL（需要外部构建，与 HybridCLR 兼容性不确定）
- **弃用方案**: 运行时反射（性能问题）

### 自动绑定调用时机
- 框架在 `InternalCreate()` / `InitWidget()` 中自动调用 `__UITKAutoBind` + `__UITKAutoBindEvents`
- 框架在 `InternalDestroy()` 中自动调用 `__UITKAutoUnbindEvents`
- 开发者完全无感，不需要手动调用任何绑定方法

### 按钮音效
- **最终方案**: Panel 级 ClickEvent 冒泡全局拦截 + IUITKClickSoundHandler 接口
- 生成代码不碰音效（职责单一）
- 业务层 Handler 根据 button.name/class 决定策略
- `no-sound` CSS class 标记静音
- 弃用：[OnClick] sound 参数（过度设计）、class 携带音效名（滥用 class 语义）

### 资源定位
- YooAsset `AddressByFileName` 规则：文件名即 Address
- 不需要传路径/Group，只要文件名唯一即可
- [UIWindow(Location = "xxx")] 可覆盖默认类名定位
- [UIWindow(Package = "xxx")] 指定 YooAsset 包名

---

## 使用指南

### 1. 创建新窗口

```csharp
[UIWindow(UILayer.UI, FullScreen = true)]
public partial class BagWindow : UITKWindow
{
    [Q] Button btnClose;
    [Q] Label lblTitle;

    [OnClick] void OnBtnClose() { UITKModule.Instance.CloseUI<BagWindow>(); }

    protected override void OnCreate() { lblTitle.text = "背包"; }
}
```

### 2. 生成绑定代码

Unity 菜单: `TEngine → UITK → Generate All Bindings`

### 3. 打开窗口

```csharp
GameModule.UITK.ShowUIAsync<BagWindow>();
```

### 4. 配置音效

```csharp
GameModule.UITK.ClickSoundHandler = new GameClickSoundHandler();
```
