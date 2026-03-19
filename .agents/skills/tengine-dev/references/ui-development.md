# TEngine UI 开发指南

## 目录

- [UILayer 层级](#uilayer-层级)
- [WindowAttribute 窗口标记](#windowattribute-窗口标记)
- [UIWindow 生命周期](#uiwindow-生命周期)
- [节点绑定](#节点绑定)
- [UIWidget 子组件](#uiwidget-子组件)
- [UIModule 核心 API](#uimodule-核心-api)
- [UI 内部事件](#ui-内部事件)
- [UI 脚本生成器](#ui-脚本生成器)
- [完整示例](#完整示例)

---

## UILayer 层级

```csharp
public enum UILayer : int
{
    Bottom = 0,  // 底层（世界空间 UI、背景）
    UI     = 1,  // 普通 UI 层（主要界面）
    Top    = 2,  // 顶层（弹窗、全屏遮罩）
    Tips   = 3,  // 提示层（Toast、飘字）
    System = 4,  // 系统层（加载中、异常提示）
}
```

层级越高渲染越靠前。系统层覆盖所有游戏 UI。

---

## WindowAttribute 窗口标记

每个 `UIWindow` 子类必须标记 `[Window]` 特性：

```csharp
// 基础用法
[Window(UILayer.UI, "BattleMainUI")]
public class BattleMainUI : UIWindow { }

// 完整参数
[Window(
    layer: UILayer.Top,          // UI 层级
    location: "LoginUI",         // Prefab 的 YooAsset location（文件名）
    fullScreen: true,            // 是否全屏（自动关闭下层）
    hideTimeToClose: 10f         // 隐藏后自动关闭的秒数（0=不自动关闭）
)]
public class LoginUI : UIWindow { }
```

**location** 即 Prefab 的资源地址，等于 `AssetRaw/UI/Prefabs/` 下的文件名（不含扩展名）。

---

## UIWindow 生命周期

```
ShowUI<T>()
    │
    ▼
ScriptGenerator()     ← 首次加载：绑定 UI 节点引用（仅执行一次）
    │
    ▼
BindMemberProperty()  ← 首次加载：绑定数据绑定属性
    │
    ▼
RegisterEvent()       ← 首次加载：注册 UI 事件（随窗口销毁自动清理）
    │
    ▼
OnCreate()            ← 首次加载：窗口创建初始化（仅执行一次）
    │
    ▼
OnRefresh()           ← 每次 ShowUI 都执行（刷新显示数据）
    │
    ▼
OnUpdate()            ← 每帧更新（尽量避免，改用 Timer）
    │
    ▼
HideUI() / CloseUI()
    │
    ├── 隐藏：窗口不销毁，超时后触发 Close
    └── 关闭：
            ▼
        OnDestroy()   ← 销毁前清理（事件、Timer 在这里移除）
```

**关键规则**：
- `ScriptGenerator` / `RegisterEvent` / `OnCreate` 只执行一次
- `OnRefresh` 每次 Show 都执行，用于刷新动态数据
- 禁止在 `OnUpdate` 中做高频计算，改用 `GameModule.Timer`

---

## 节点绑定

**UIScriptGenerator 工具**自动生成 `ScriptGenerator` 代码，节点命名前缀决定绑定类型（来自 `ScriptGeneratorSetting.asset`）：

| Prefab 节点名前缀 | 绑定类型 | 说明 |
|-----------------|---------|------|
| `m_go_xxx` | `GameObject` | 普通 GameObject |
| `m_item_xxx` | `UIWidget`（子类） | Widget 子组件（`isUIWidget=true`）|
| `m_tf_xxx` | `Transform` | Transform |
| `m_rect_xxx` | `RectTransform` | RectTransform |
| `m_text_xxx` | `Text` | UGUI Text |
| `m_richText_xxx` | `RichTextItem` | 富文本 |
| `m_btn_xxx` | `Button` | 按钮（自动注册点击事件）|
| `m_img_xxx` | `Image` | Image |
| `m_rimg_xxx` | `RawImage` | RawImage |
| `m_scroll_xxx` | `ScrollRect` | 滚动视图 |
| `m_scrollBar_xxx` | `Scrollbar` | 滚动条 |
| `m_input_xxx` | `InputField` | 输入框（UGUI）|
| `m_tInput_xxx` | `TMP_InputField` | 输入框（TextMeshPro）|
| `m_grid_xxx` | `GridLayoutGroup` | 网格布局 |
| `m_hlay_xxx` | `HorizontalLayoutGroup` | 水平布局 |
| `m_vlay_xxx` | `VerticalLayoutGroup` | 垂直布局 |
| `m_slider_xxx` | `Slider` | 滑动条（自动注册 onValueChanged）|
| `m_toggle_xxx` | `Toggle` | 开关（自动注册 onValueChanged）|
| `m_group_xxx` | `ToggleGroup` | Toggle 组 |
| `m_tmp_xxx` | `TextMeshProUGUI` | TextMeshPro 文本 |
| `m_canvasGroup_xxx` | `CanvasGroup` | CanvasGroup |
| `m_curve_xxx` | `AnimationCurve` | 动画曲线 |

**生成路径配置**（`ScriptGeneratorSetting.asset`）：
- 生成代码（绑定部分）：`Assets/GameScripts/HotFix/GameLogic/UI/Gen/`
- 实现代码（业务逻辑）：`Assets/GameScripts/HotFix/GameLogic/UI/`
- 命名空间：`GameLogic`

手动绑定 API：

```csharp
protected override void ScriptGenerator()
{
    // 查找子节点 Transform
    var trans = FindChild("m_tf_Container");
    
    // 查找子节点组件
    var btn  = FindChildComponent<Button>("m_btn_Start");
    var rect = FindChildComponent<RectTransform>("m_rect_Panel");
    var txt  = FindChildComponent<Text>("m_text_Title");
    var tmp  = FindChildComponent<TextMeshProUGUI>("m_tmp_Name");
    
    // 注册按钮点击（自动随窗口销毁）
    RegisterButtonClick(btn, OnStartClicked);
}
```

---

## UIWidget 子组件

`UIWidget` 用于封装可复用的 UI 子组件（列表 Item、图标槽、角色卡片等）。

### 创建方式

```csharp
// 方式1：节点已在 Prefab 中（路径）
var widget = CreateWidget<ItemWidget>("path/to/node");

// 方式2：通过 GameObject 引用
var widget = CreateWidget<ItemWidget>(someGameObject);

// 方式3：从 AssetRaw 动态加载 Prefab
var widget = CreateWidgetByPath<ItemWidget>(parentTrans, "ItemWidgetPrefab");

// 方式4：异步加载
var widget = await CreateWidgetByPathAsync<ItemWidget>(parentTrans, "ItemWidgetPrefab");

// 方式5：通过 Prefab 克隆（列表 Item 常用）
var widget = CreateWidgetByPrefab<ItemWidget>(prefab, parentTrans);
```

### 列表数量自动调整

```csharp
// 自动增删 Widget 使数量匹配 count（复用已有实例）
AdjustIconNum<ItemWidget>(listIcon, count: items.Count, parentTrans, prefab);

// 刷新每个 Widget 数据
for (int i = 0; i < items.Count; i++)
{
    listIcon[i].SetData(items[i]);
}
```

### UIWidget 生命周期

与 `UIWindow` 相同：`ScriptGenerator → RegisterEvent → OnCreate → OnRefresh → OnDestroy`

```csharp
public class ItemWidget : UIWidget
{
    private Text  _textName;
    private Image _imgIcon;

    protected override void ScriptGenerator()
    {
        _textName = FindChildComponent<Text>("m_text_Name");
        _imgIcon  = FindChildComponent<Image>("m_img_Icon");
    }

    public void SetData(ItemConfig cfg)
    {
        _textName.text = cfg.Name;
        // 使用 SetSprite 扩展方法加载图标（无需手动释放）
        _imgIcon.SetSprite(cfg.IconPath);
    }
}
```

---

## UIModule 核心 API

### 打开窗口

```csharp
// 异步打开（fire and forget，最常用）
GameModule.UI.ShowUIAsync<BattleMainUI>();

// 异步打开并等待实例
var win = await GameModule.UI.ShowUIAsyncAwait<BattleMainUI>();

// 携带用户数据（在 OnCreate/OnRefresh 中通过 UserData 访问）
GameModule.UI.ShowUIAsync<ItemDetailUI>(itemId, extraData);

// 同步打开（慎用，Prefab 必须已缓存在内存中）
GameModule.UI.ShowUI<BattleMainUI>();
```

### 关闭 / 隐藏窗口

```csharp
GameModule.UI.CloseUI<BattleMainUI>();           // 关闭并销毁
GameModule.UI.HideUI<BattleMainUI>();            // 隐藏（超时后自动关闭）
GameModule.UI.CloseAll();                        // 关闭所有窗口
GameModule.UI.CloseAllWithOut<BattleMainUI>();   // 保留指定窗口，关闭其余

// 在窗口内部关闭自身
protected void OnCloseClicked() => GameModule.UI.CloseUI(GetType());
```

### 查询状态

```csharp
bool exists   = GameModule.UI.HasWindow<BattleMainUI>();
bool loading  = GameModule.UI.IsAnyLoading();
string topWin = GameModule.UI.GetTopWindow();    // 当前顶层窗口名

// 获取已打开窗口实例
var win = await GameModule.UI.GetUIAsyncAwait<BattleMainUI>();
GameModule.UI.GetUIAsync<BattleMainUI>(win => { /* 回调 */ });
```

---

## UI 内部事件

在 `UIWindow` / `UIWidget` 内使用，事件监听随窗口/组件销毁**自动清理**：

```csharp
protected override void RegisterEvent()
{
    // 无参事件
    AddUIEvent(GameEventDef.OnGoldChanged, OnGoldChanged);
    
    // 1个参数
    AddUIEvent<int>(GameEventDef.OnHpChanged, OnHpChanged);
    
    // 2个参数
    AddUIEvent<int, int>(GameEventDef.OnDamage, OnDamage);
}

private void OnHpChanged(int newHp)
{
    _txtHp.text = newHp.ToString();
}
```

**禁止**在 `RegisterEvent` 外使用 `GameEvent.AddEventListener`，因为退出窗口时不会自动清理，会导致内存泄漏。

---

## UI 脚本生成器

`Assets/Editor/UIScriptGenerator` 工具：

1. 在 Prefab 中按命名规范为节点命名（`m_btn_xxx`、`m_txt_xxx` 等）
2. 选中 Prefab 根节点，在 Inspector 点击 "Generate UI Script"
3. 自动生成 `ScriptGenerator()` 方法代码，复制到 UIWindow 类中

**节点命名完整规范**见 [conventions.md](conventions.md#ui-节点命名规范)。

---

## 完整示例

```csharp
// Assets/GameScripts/HotFix/GameLogic/UI/Battle/BattleMainUI.cs

[Window(UILayer.UI, "BattleMainUI", fullScreen: true)]
public class BattleMainUI : UIWindow
{
    // --- 节点引用（UIScriptGenerator 自动生成）---
    private Button    _btnBack;
    private Text      _textHp;
    private Text      _textGold;
    private Transform _tfSkillPanel;

    // --- 子组件列表 ---
    private readonly List<SkillSlotWidget> _skillSlots = new();

    // ---- 生命周期 ----

    protected override void ScriptGenerator()
    {
        _btnBack       = FindChildComponent<Button>("m_btn_Back");
        _textHp        = FindChildComponent<Text>("m_text_Hp");
        _textGold      = FindChildComponent<Text>("m_text_Gold");
        _tfSkillPanel  = FindChild("m_tf_SkillPanel");

        RegisterButtonClick(_btnBack, OnBackClicked);
    }

    protected override void RegisterEvent()
    {
        AddUIEvent<int>(IBattleEvent_Event.OnHpChanged, RefreshHp);
        AddUIEvent<int>(IBattleEvent_Event.OnGoldChanged, RefreshGold);
    }

    protected override void OnCreate()
    {
        // 技能槽（固定4个，Prefab 中已存在节点）
        for (int i = 0; i < 4; i++)
        {
            var slot = CreateWidget<SkillSlotWidget>($"m_trans_SkillPanel/Slot_{i}");
            _skillSlots.Add(slot);
        }
    }

    protected override void OnRefresh()
    {
        // 每次打开时刷新数据
        RefreshHp(PlayerData.Hp);
        RefreshGold(PlayerData.Gold);

        for (int i = 0; i < _skillSlots.Count; i++)
            _skillSlots[i].SetData(PlayerData.Skills[i]);
    }

    protected override void OnDestroy()
    {
        // 计时器在这里移除（事件框架自动清理 AddUIEvent）
    }

    // ---- 事件回调 ----

    private void RefreshHp(int hp)    => _textHp.text   = $"HP: {hp}";
    private void RefreshGold(int gold) => _textGold.text = $"Gold: {gold}";

    private void OnBackClicked()
    {
        GameModule.UI.CloseUI<BattleMainUI>();
        GameModule.UI.ShowUIAsync<MainMenuUI>();
    }
}
```
