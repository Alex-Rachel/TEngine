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
