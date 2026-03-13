# TEngine 事件系统

## 目录

- [两种事件模式对比](#两种事件模式对比)
- [模式一：int 事件（传统发布订阅）](#模式一int-事件)
- [模式二：接口事件（Source Generator）](#模式二接口事件)
- [UI 内部事件（自动生命周期）](#ui-内部事件)
- [GameEventMgr（非 UI 类的事件管理）](#gameeventmgr)
- [事件分组（EEventGroup）](#事件分组)
- [事件定义规范](#事件定义规范)
- [常见错误与避坑](#常见错误与避坑)

---

## 两种事件模式对比

| 特性 | int 事件（底层） | 接口事件（推荐） |
|------|---------|---------|
| 事件 ID 来源 | Source Generator 从接口自动生成 `static readonly int` | 同左，接口即定义 |
| 类型安全 | 弱（参数需手动匹配） | 强（编译期检查） |
| 发送方式 | `GameEvent.Send<T>(ILoginUI_Event.ShowLoginUI, data)` | `GameEvent.Get<ILoginUI>().ShowLoginUI()` |
| 收听方式 | `GameEvent.AddEventListener<T>(ILoginUI_Event.ShowLoginUI, cb)` | 无需手动收听，实现接口即可 |
| 适用场景 | UI 内部事件绑定（`AddUIEvent`）、精细订阅控制 | 模块间通信（推荐默认选择） |

**关键认知**：**两种模式共用同一套事件 ID**。接口事件 `[EventInterface]` 标记的接口，由 Source Generator 自动为每个方法生成 `static readonly int` 事件 ID（如 `ILoginUI_Event.ShowLoginUI`）。无需手动维护任何 int 常量文件。

**推荐**：新功能优先使用**接口事件**（类型安全，调用直观）；UI 内部简单响应用 `AddUIEvent` + 生成的事件 ID。

---

## 模式一：int 事件（底层）

### 事件 ID 的来源

**无需手动定义 int 常量。** 事件 ID 由 Source Generator 从 `[EventInterface]` 接口自动生成：

```csharp
// 1. 定义接口（开发者只需写这个）
[EventInterface(EEventGroup.GroupUI)]
public interface ILoginUI
{
    void ShowLoginUI();
    void CloseLoginUI();
}

// 2. Source Generator 自动生成（编译期，不存在实体 .cs 文件）：
// ILoginUI_Event.g.cs
public partial class ILoginUI_Event
{
    public static readonly int ShowLoginUI  = RuntimeId.ToRuntimeId("ILoginUI_Event.ShowLoginUI");
    public static readonly int CloseLoginUI = RuntimeId.ToRuntimeId("ILoginUI_Event.CloseLoginUI");
}
```

直接使用生成的事件 ID：`ILoginUI_Event.ShowLoginUI`（int 类型）

### 注册监听

```csharp
// 无参
GameEvent.AddEventListener(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);

// 1个参数
GameEvent.AddEventListener<int>(IBattleEvent_Event.OnHpChanged, OnHpChanged);

// 2个参数
GameEvent.AddEventListener<int, string>(IXxx_Event.OnXxx, OnXxx);

// 最多支持6个参数
```

### 发送事件

```csharp
GameEvent.Send(ILoginUI_Event.ShowLoginUI);
GameEvent.Send<int>(IBattleEvent_Event.OnHpChanged, newHp);
GameEvent.Send<int, string>(IXxx_Event.OnXxx, arg1, arg2);
```

### 移除监听

```csharp
GameEvent.RemoveEventListener<int>(IBattleEvent_Event.OnHpChanged, OnHpChanged);
```

**注意**：非 UIWindow/UIWidget 类中注册的监听必须在对象销毁时手动 `RemoveEventListener`，否则内存泄漏。

---

## 模式二：接口事件（推荐）

### 定义事件接口

```csharp
// GameScripts/HotFix/GameLogic/IEvent/ILoginUI.cs
[EventInterface(EEventGroup.GroupUI)]
public interface ILoginUI
{
    void ShowLoginUI();
    void CloseLoginUI();
    void RefreshLoginState(int state, string message);
}
```

Source Generator 自动生成：
- `ILoginUI_Event.g.cs` — 每个方法对应一个 `static readonly int` 事件 ID
- `ILoginUI_Gen.g.cs` — 实现 `ILoginUI`，方法体内调用 `dispatcher.Send(事件ID)`
- `GameEventHelper.g.cs` — `Init()` 中实例化所有 `_Gen` 类并注册

### 触发事件（发送方）

```csharp
// 任意位置调用，等同于发送对应的 int 事件
GameEvent.Get<ILoginUI>().ShowLoginUI();
GameEvent.Get<ILoginUI>().RefreshLoginState(1, "登录成功");
```

### 监听事件（接收方）

接口事件的**接收**仍然通过 `AddEventListener` 订阅对应的 int 事件 ID：

```csharp
// 在 UIWindow.RegisterEvent() 中（推荐）
protected override void RegisterEvent()
{
    AddUIEvent(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
    AddUIEvent<int, string>(ILoginUI_Event.RefreshLoginState, OnRefreshState);
}

// 或在普通类中
_eventMgr.AddEvent(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
_eventMgr.AddEvent<int, string>(ILoginUI_Event.RefreshLoginState, OnRefreshState);
```

### 初始化（必须调用）

```csharp
// GameApp.Entrance() 中，必须最先调用
GameEventHelper.Init();  // 实例化所有 _Gen 代理，否则 GameEvent.Get<T>() 全部无响应
```

---

## UI 内部事件

在 `UIWindow` / `UIWidget` 的 `RegisterEvent()` 方法中注册，随窗口/组件销毁**自动清理**，无需手动移除。使用 Source Generator 生成的事件 ID：

```csharp
protected override void RegisterEvent()
{
    // 无参（使用接口生成的事件 ID）
    AddUIEvent(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
    
    // 1个参数
    AddUIEvent<int>(IBattleEvent_Event.OnHpChanged, OnHpChanged);
    
    // 2个参数
    AddUIEvent<int, string>(IXxx_Event.OnItemGet, OnItemGet);
}

private void OnHpChanged(int hp) { _txtHp.text = hp.ToString(); }
private void OnItemGet(int itemId, string itemName) { ShowGetTip(itemName); }
```

**原则**：
- 只在 `RegisterEvent()` 内调用 `AddUIEvent`
- 禁止在 `RegisterEvent` 外调用 `GameEvent.AddEventListener`（不会自动清理）
- 框架在 `UIWindow.InternalDestroy` 时自动调用 `RemoveAllUIEvent()`

---

## GameEventMgr

用于普通 C# 类（非 UIWindow/UIWidget）管理多个事件监听，统一生命周期：

```csharp
public class PlayerSystem
{
    private readonly GameEventMgr _eventMgr = new GameEventMgr();

    public void Initialize()
    {
        // 使用 Source Generator 生成的事件 ID
        _eventMgr.AddEvent(IPlayerEvent_Event.OnPlayerDead, OnPlayerDead);
        _eventMgr.AddEvent<int>(IBattleEvent_Event.OnHpChanged, OnHpChanged);
    }

    public void Dispose()
    {
        _eventMgr.Clear();  // 批量移除所有已注册监听
    }

    private void OnPlayerDead() { }
    private void OnHpChanged(int hp) { }
}
```

---

## 事件分组（EEventGroup）

框架内置两种分组（`EventInterfaceAttribute.cs` 定义）：

```csharp
public enum EEventGroup
{
    GroupUI,     // UI 相关的交互事件
    GroupLogic,  // 逻辑层内部相关的交互事件
}
```

```csharp
[EventInterface(EEventGroup.GroupUI)]    // UI 模块间通信
public interface ILoginUI { ... }

[EventInterface(EEventGroup.GroupLogic)] // 逻辑层事件
public interface IBattleEvent { ... }
```

---

## 事件定义规范

### 核心规则：只定义接口，不手写 ID

```
❌ 不要创建  GameEventDef.cs  手动维护 const int 常量
✅ 只需定义  [EventInterface] 接口，Source Generator 自动生成所有 ID
```

### 接口命名规范

```
I{UIName}           → UI 相关（ILoginUI, IMainMenuUI, IBattleUI）
I{Domain}Event      → 逻辑层事件（IBattleEvent, IPlayerEvent）
```

### 文件组织

```
GameLogic/IEvent/
├── ILoginUI.cs          # 登录 UI 相关接口（GroupUI）
├── IMainMenuUI.cs       # 主菜单 UI 接口（GroupUI）
├── IBattleUI.cs         # 战斗 UI 接口（GroupUI）
├── IBattleEvent.cs      # 战斗逻辑事件（GroupLogic）
└── IPlayerEvent.cs      # 玩家逻辑事件（GroupLogic）
```

### 添加新事件的完整流程

```
1. 在 IEvent/ 下新建接口文件
2. 标记 [EventInterface(EEventGroup.GroupUI/GroupLogic)]
3. 定义方法签名
4. 重新编译 → Source Generator 自动生成 IXxx_Event.g.cs / IXxx_Gen.g.cs / 更新 GameEventHelper.g.cs
5. 使用：发送 GameEvent.Get<IXxx>().Method()，接收 AddUIEvent(IXxx_Event.Method, cb)
```

---

## 常见错误与避坑

**1. 手动定义 int 常量（错误做法）**

```csharp
// ❌ 错误：手动维护事件 ID，容易冲突且无类型保障
public static class GameEventDef
{
    public const int OnHpChanged = 10001; // 不要这样做！
}

// ✅ 正确：定义接口，让 Source Generator 生成 ID
[EventInterface(EEventGroup.GroupLogic)]
public interface IBattleEvent
{
    void OnHpChanged(int hp);
}
// 直接使用：IBattleEvent_Event.OnHpChanged（自动生成的 int ID）
```

**2. 忘记移除监听导致内存泄漏**

```csharp
// ❌ 错误：在普通类中注册但不清理
public class MySystem
{
    public void Init()
    {
        GameEvent.AddEventListener<int>(IBattleEvent_Event.OnHpChanged, OnHp); // 泄漏！
    }
}

// ✅ 正确：使用 GameEventMgr 统一清理
public class MySystem
{
    private readonly GameEventMgr _mgr = new();
    public void Init()    => _mgr.AddEvent<int>(IBattleEvent_Event.OnHpChanged, OnHp);
    public void Dispose() => _mgr.Clear();
}
```

**3. 在 UI 中绕开 AddUIEvent**

```csharp
// ❌ 错误：不会自动清理，窗口关闭后仍触发
protected override void OnCreate()
{
    GameEvent.AddEventListener<int>(IBattleEvent_Event.OnHpChanged, OnHp);
}

// ✅ 正确：在 RegisterEvent 中用 AddUIEvent
protected override void RegisterEvent()
{
    AddUIEvent<int>(IBattleEvent_Event.OnHpChanged, OnHp);
}
```

**4. 接口事件未调用 Init**

```csharp
// ❌ 错误：忘记初始化，所有 GameEvent.Get<T>().Method() 调用均无响应
public static void Entrance(Assembly[] assemblies) { /* ... */ }

// ✅ 正确：必须在 Entrance 第一行调用
public static void Entrance(Assembly[] assemblies)
{
    GameEventHelper.Init();  // 必须！Source Generator 生成，自动包含所有接口
    // ...
}
```

**5. 发送与监听的参数类型不匹配**

```csharp
// ❌ 错误：接口定义参数 int，但订阅时用 float
GameEvent.Get<IBattleEvent>().OnHpChanged(100); // 接口方法参数是 int
GameEvent.AddEventListener<float>(IBattleEvent_Event.OnHpChanged, cb); // 类型不匹配！

// ✅ 正确：订阅参数类型与接口方法参数一致
GameEvent.AddEventListener<int>(IBattleEvent_Event.OnHpChanged, cb);
```
