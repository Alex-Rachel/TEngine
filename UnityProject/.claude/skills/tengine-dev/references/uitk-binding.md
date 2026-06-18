# UITKModule 自动绑定

## 机制

Editor 预生成 `.bindgen.cs` 文件，运行时零反射。

**触发方式**：Unity 菜单 `TEngine → UITK → Generate All Bindings`

## Attribute

### [Q] — 元素查询绑定

```csharp
[Q] Button btnLogin;          // → 查询 name="btn-login"
[Q] Label lblTitle;           // → 查询 name="lbl-title"
[Q("explicit-name")] DropdownField serverList;  // → 查询 name="explicit-name"
```

**命名规则**: camelCase → kebab-case
```
btnLogin     → btn-login
lblPlayerHP  → lbl-player-hp
inputAccount → input-account
imgAvatarBg  → img-avatar-bg
```

### [OnClick] — 点击事件绑定

```csharp
[OnClick] void OnBtnLogin() { }       // → 绑定 btn-login.clicked
[OnClick] void OnBtnClose() { }       // → 绑定 btn-close.clicked
[OnClick("custom-btn")] void HandleClick() { }  // → 显式指定目标
```

**推导规则**: 去掉 "On" 前缀，剩余部分转 kebab-case
```
OnBtnLogin   → btn-login
OnBtnClose   → btn-close
```

### [OnChange] — 值变化事件

```csharp
[OnChange("input-name")]
void OnNameChanged(ChangeEvent<string> evt) { }

[OnChange("slider-value")]
void OnSliderChanged(ChangeEvent<float> evt) { }
```

### [Bind] / [BindCommand] — MVVM 自动绑定

将 UI 控件直接绑定到 ViewModel 的 `BindableProperty<T>` / `BindableCommand`，**无需手动 += / -=**：

```csharp
[Bind("Gold", format: "{0:N0}")]          Label lblGold;     // OneWay + 格式化
[Bind("PlayerName", BindingMode.TwoWay)]  TextField inputName; // 双向
[Bind("Hp", BindingMode.TwoWay, converter: typeof(IntToStringConverter))] TextField inputHp; // 异类型经转换器
[BindCommand("BuyCommand")]               Button btnBuy;     // 命令 + CanExecute

private ShopViewModel _vm;   // 类内恰一个 ViewModelBase 字段，生成器据此解析
```

- `[Bind]/[BindCommand]` 字段会被自动查询赋值（隐含 `[Q]`，字段名 → kebab-case），无需再加 `[Q]`。
- 框架在 `OnCreate` 之后自动绑定、`OnDestroy` 之前自动解绑（同一委托实例，无泄漏）。
- 详见 [uitk-mvvm.md](uitk-mvvm.md)「自动绑定」。

## 完整示例

```csharp
[UIWindow(UILayer.UI)]
public partial class ShopWindow : UITKWindow
{
    [Q] Label lblGold;
    [Q] Label lblTitle;
    [Q] Button btnBuy;
    [Q] Button btnClose;
    [Q] TextField inputSearch;

    [OnClick] void OnBtnBuy()
    {
        // 购买逻辑
    }

    [OnClick] void OnBtnClose()
    {
        UITKModule.Instance.CloseUI<ShopWindow>();
    }

    [OnChange("input-search")]
    void OnSearchChanged(ChangeEvent<string> evt)
    {
        // 搜索过滤
    }

    protected override void OnCreate()
    {
        // [Q] 字段已自动填充，直接使用
        lblTitle.text = "商店";
    }
}
```

## 生成文件

输入: `ShopWindow.cs` → 输出: `ShopWindow.bindgen.cs`（同目录）

生成内容:
```csharp
partial class ShopWindow
{
    protected override void __UITKAutoBind(VisualElement root)
    {
        lblGold = root.Q<Label>("lbl-gold");
        lblTitle = root.Q<Label>("lbl-title");
        btnBuy = root.Q<Button>("btn-buy");
        btnClose = root.Q<Button>("btn-close");
        inputSearch = root.Q<TextField>("input-search");
    }

    protected override void __UITKAutoBindEvents()
    {
        btnBuy.clicked += OnBtnBuy;
        btnClose.clicked += OnBtnClose;
        inputSearch.RegisterValueChangedCallback(OnSearchChanged);
    }

    protected override void __UITKAutoUnbindEvents()
    {
        btnBuy.clicked -= OnBtnBuy;
        btnClose.clicked -= OnBtnClose;
        inputSearch.UnregisterValueChangedCallback(OnSearchChanged);
    }
}
```

## 注意事项

- 类必须声明为 `partial class`
- 修改 [Q]/[OnClick]/[OnChange] 后需重新 Generate
- UXML 中对应元素的 `name` 属性必须匹配
- `.bindgen.cs` 文件不要手动编辑（会被覆盖）
- 生成的代码参与 HybridCLR 热更新
