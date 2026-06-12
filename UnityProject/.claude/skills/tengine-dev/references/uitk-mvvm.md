# UITKModule MVVM 数据绑定

## 核心组件

### BindableProperty<T>

```csharp
public class BindableProperty<T>
{
    public T Value { get; set; }            // set 时触发通知
    public event Action<T> OnValueChanged;
}

// 使用
var gold = new BindableProperty<int>(100);
gold.OnValueChanged += v => lblGold.text = v.ToString();
gold.Value = 200;  // 自动触发 UI 更新
```

### BindableCommand

```csharp
public class BindableCommand
{
    public void Execute();
    public bool CanExecute();
    public event Action CanExecuteChanged;
}

// 使用
var buyCmd = new BindableCommand(
    execute: () => Buy(),
    canExecute: () => gold.Value >= price
);
btnBuy.clicked += buyCmd.Execute;
buyCmd.CanExecuteChanged += () => btnBuy.SetEnabled(buyCmd.CanExecute());
```

### BindableList<T>

```csharp
public class BindableList<T> : IList<T>
{
    public event Action OnListChanged;
    public event Action<int, T> OnItemAdded;
    public event Action<int, T> OnItemRemoved;
    public event Action<int, T, T> OnItemChanged;
}

// 使用
var items = new BindableList<ItemData>();
items.OnListChanged += () => listView.Rebuild();
items.Add(newItem);  // 自动触发 UI 刷新
```

### ViewModelBase

```csharp
public abstract class ViewModelBase
{
    public virtual void Dispose() { }
}
```

## 完整示例

```csharp
// ━━━ ViewModel ━━━
public class BagViewModel : ViewModelBase
{
    public BindableProperty<int> Gold { get; } = new(0);
    public BindableProperty<string> PlayerName { get; } = new("");
    public BindableList<ItemData> Items { get; } = new();
    public BindableCommand SortCommand { get; }

    public BagViewModel()
    {
        SortCommand = new BindableCommand(() =>
        {
            // 排序逻辑
        });

        // 监听全局事件更新数据
        GameEvent.AddEventListener<int>(EventId.GoldChanged, v => Gold.Value = v);
    }

    public override void Dispose()
    {
        // 清理事件监听
    }
}

// ━━━ View ━━━
[UIWindow(UILayer.UI, FullScreen = true)]
public partial class BagWindow : UITKWindow
{
    [Q] Label lblGold;
    [Q] Label lblName;
    [Q] Button btnSort;
    [Q] Button btnClose;

    [OnClick] void OnBtnSort() { }
    [OnClick] void OnBtnClose() { UITKModule.Instance.CloseUI<BagWindow>(); }

    private BagViewModel _vm;

    protected override void OnCreate()
    {
        _vm = new BagViewModel();

        // 绑定数据
        _vm.Gold.OnValueChanged += v => lblGold.text = $"{v:N0}";
        _vm.PlayerName.OnValueChanged += v => lblName.text = v;

        // 绑定命令
        _vm.SortCommand.CanExecuteChanged += () => btnSort.SetEnabled(_vm.SortCommand.CanExecute());

        // 初始同步
        lblGold.text = $"{_vm.Gold.Value:N0}";
        lblName.text = _vm.PlayerName.Value;
    }

    protected override void OnDestroy()
    {
        _vm.Dispose();
    }
}
```

## 自动绑定（[Bind] / [BindCommand]）

除手动绑定外，可用特性声明 + 绑定生成器全自动绑定，**无需任何手动 += / -=**，框架在生命周期内自动绑定/解绑（无泄漏）。

### 前置约定

- View 声明为 `partial class`。
- 类内**恰有一个** `ViewModelBase` 派生字段（如 `private XxxViewModel _vm;`），生成器据此解析 VM 类型与字段名。
- VM 字段必须在 `OnCreate` 内赋值（自动绑定在 `OnCreate` 之后执行；为 null 时跳过）。
- 改特性后经菜单 `TEngine → UITK → Generate All Bindings` 重新生成 `.bindgen.cs`。
- `[Bind]/[BindCommand]` 字段会被**自动查询赋值**（等同隐含 `[Q]`），无需再加 `[Q]`。

### 特性

```csharp
// OneWay：BindableProperty → Label.text（converter 优先于 format，皆无则 ToString）
[Bind("Gold", format: "{0:N0}")] Label lblGold;
[Bind("Hp", converter: typeof(IntToStringConverter))] Label lblHp;

// 字段双向/单向（TextField/Slider/Toggle…），mode 默认 OneWay
[Bind("PlayerName", BindingMode.TwoWay)] TextField inputName;   // string ↔ string
[Bind("Volume", BindingMode.TwoWay)] Slider sliderVol;          // float ↔ float
[Bind("Hp", BindingMode.TwoWay, converter: typeof(IntToStringConverter))] TextField inputHp; // int ↔ string

// 命令：Button.clicked → Execute；CanExecuteChanged → SetEnabled
[BindCommand("BuyCommand")] Button btnBuy;
```

### 完整示例

```csharp
[UIWindow(UILayer.UI)]
public partial class BagWindow : UITKWindow
{
    [Bind("Gold", format: "{0:N0}")]          Label lblGold;
    [Bind("PlayerName", BindingMode.TwoWay)]  TextField inputName;
    [BindCommand("SortCommand")]              Button btnSort;

    private BagViewModel _vm;   // ← 生成器据此解析 VM

    protected override void OnCreate() => _vm = new BagViewModel();
    protected override void OnDestroy() => _vm.Dispose();
    // 无需手动绑定/解绑
}
```

### 类型匹配规则

| 控件 | 值类型 | 绑 int 属性 | 绑 string 属性 |
|------|--------|------------|---------------|
| Label | （text，恒 OneWay）| format/ToString | 直接 |
| TextField | string | 需 converter（如 IntToStringConverter）| 直接 TwoWay |
| Slider | float | 需 converter，或改用 SliderInt | — |
| SliderInt | int | 直接 TwoWay | — |
| Toggle | bool | — | — |

> 控件值类型与 VM 属性类型不一致且未给 converter → 生成器报错并跳过该字段（不影响其它绑定）。

### 手动 vs 自动

- 两者可共存：复杂/动态绑定继续用手动 `prop.OnValueChanged += ...`；规整的属性/命令用 `[Bind]/[BindCommand]`。
- 自动绑定使用 `UITKBase` 上的 `BindLabel/BindField/BindCommand` helper（订阅/解绑同一委托实例），无泄漏。

## UITKListView 使用

```csharp
// Widget 列表项
public class ItemSlotWidget : UITKWidget
{
    [Q] Label lblName;
    [Q] Label lblCount;

    public override void OnBindData(object data, int index)
    {
        var item = (ItemData)data;
        lblName.text = item.Name;
        lblCount.text = item.Count.ToString();
    }
}

// 在 Window 中使用
var listView = CreateWidget<UITKListView<ItemSlotWidget>>(containerElement);
listView.BindList(viewModel.Items);  // 自动监听增删改
```

## 内置类型转换器

| 转换器 | 方向 |
|--------|------|
| IntToStringConverter | int ↔ string |
| FloatToStringConverter | float ↔ string |
| BoolToDisplayConverter | bool ↔ DisplayStyle |
| BoolToOpacityConverter | bool ↔ float (0/1) |

## MVVM 与 GameEvent 的关系

- **GameEvent**: 跨模块通信（如 "金币变化"、"战斗结束"）
- **MVVM Bind**: 窗口内 ViewModel ↔ View 同步

ViewModel 监听 GameEvent 更新自身属性 → 属性变化自动推送到 UI。
