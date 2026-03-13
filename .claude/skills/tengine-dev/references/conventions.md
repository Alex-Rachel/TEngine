# TEngine 代码规范与设计模式

## 目录

- [命名规范](#命名规范)
- [UI 节点命名规范](#ui-节点命名规范)
- [异步编程规范](#异步编程规范)
- [禁止的代码模式](#禁止的代码模式)
- [推荐的代码模式](#推荐的代码模式)
- [模块设计规范](#模块设计规范)
- [代码审查清单](#代码审查清单)
- [Git 工作流](#git-工作流)

---

## 命名规范

### C# 类型命名

| 类型 | 规范 | 示例 |
|------|------|------|
| 模块接口 | `IXxxModule` | `IResourceModule` |
| 模块实现 | `XxxModule` | `ResourceModule` |
| 事件接口 | `IXxxEvent` / `IXxxUI` + `[EventInterface]` | `ILoginUI` |
| UIWindow 子类 | `XxxUI` / `XxxWindow` | `BattleMainUI` |
| UIWidget 子类 | `XxxWidget` / `XxxItem` | `SkillSlotWidget` |
| 流程状态 | `ProcedureXxx` | `ProcedureLogin` |
| 状态机状态 | `XxxState` | `IdleState` |
| 系统类 | `XxxSystem` | `LoginSystem` |
| 配置类（Luban 生成） | `TbXxx` / `Xxx`（行数据类） | `TbItem` / `Item` |
| 内存池对象 | 实现 `IMemory` | `DamageInfo : IMemory` |

### 字段命名

```csharp
public class BattleMainUI : UIWindow
{
    // 私有字段：小驼峰，下划线前缀（组件引用加类型前缀见下方UI规范）
    private int _currentHp;
    private Button _btnAttack;
    private Text _txtHp;
    
    // 私有静态：下划线前缀
    private static int _instanceCount;
    
    // 常量：全大写下划线
    private const int MAX_LEVEL = 100;
    
    // 公开属性：大驼峰
    public int CurrentHp => _currentHp;
    
    // 事件/委托字段：大驼峰
    public event Action OnDead;
}
```

### 方法命名

```csharp
// 异步方法：Async 后缀（返回 UniTask）
public async UniTask LoadDataAsync() { }

// 事件回调：On 前缀 + 事件名
private void OnHpChanged(int hp) { }
private void OnBtnAttackClicked() { }

// 初始化类：Init / Initialize / Setup
private void InitUI() { }

// 工厂/创建类：Create
private ItemWidget CreateItemWidget(ItemConfig cfg) { }
```

---

## UI 节点命名规范

Prefab 节点命名前缀决定 `UIScriptGenerator` 自动生成的绑定类型（来自 `ScriptGeneratorSetting.asset`）：

| 前缀 | 生成类型 | 示例节点名 |
|------|---------|----------|
| `m_go_` | `GameObject` | `m_go_Effect` |
| `m_item_` | `UIWidget`（子类）| `m_item_Slot` |
| `m_tf_` | `Transform` | `m_tf_Container` |
| `m_rect_` | `RectTransform` | `m_rect_Panel` |
| `m_text_` | `Text` | `m_text_Title` |
| `m_tmp_` | `TextMeshProUGUI` | `m_tmp_Name` |
| `m_richText_` | `RichTextItem` | `m_richText_Desc` |
| `m_btn_` | `Button` | `m_btn_Start` |
| `m_img_` | `Image` | `m_img_Icon` |
| `m_rimg_` | `RawImage` | `m_rimg_Avatar` |
| `m_toggle_` | `Toggle` | `m_toggle_Sound` |
| `m_group_` | `ToggleGroup` | `m_group_Tab` |
| `m_slider_` | `Slider` | `m_slider_Volume` |
| `m_input_` | `InputField` | `m_input_Name` |
| `m_tInput_` | `TMP_InputField` | `m_tInput_Search` |
| `m_scroll_` | `ScrollRect` | `m_scroll_List` |
| `m_scrollBar_` | `Scrollbar` | `m_scrollBar_Vert` |
| `m_grid_` | `GridLayoutGroup` | `m_grid_Items` |
| `m_hlay_` | `HorizontalLayoutGroup` | `m_hlay_Tabs` |
| `m_vlay_` | `VerticalLayoutGroup` | `m_vlay_List` |
| `m_canvasGroup_` | `CanvasGroup` | `m_canvasGroup_Fade` |
| `m_curve_` | `AnimationCurve` | `m_curve_Anim` |

不需要绑定的节点无需加前缀，UIScriptGenerator 会忽略。

---

## 异步编程规范

### 基本规则

```csharp
// ✅ 异步方法返回 UniTask（有返回值）
public async UniTask<int> GetPlayerLevelAsync()
{
    var data = await LoadPlayerDataAsync();
    return data.Level;
}

// ✅ 无返回值异步方法（fire and forget）：返回 UniTaskVoid
public async UniTaskVoid StartBattleAsync()
{
    await LoadBattleResourcesAsync();
    EnterBattle();
}

// ✅ 调用 UniTaskVoid 方法不需要 await，加 .Forget() 忽略警告
StartBattleAsync().Forget();

// ✅ 也可以直接调用有返回值的 UniTask（不关心结果时）
GameModule.UI.ShowUIAsync<LoginUI>(); // 不 await 也不会有警告（框架内部处理）
```

### CancellationToken 使用

```csharp
// ✅ 异步加载时传递 CancellationToken，防止对象销毁后回调执行
private CancellationTokenSource _cts = new CancellationTokenSource();

private async UniTaskVoid LoadAsync()
{
    try
    {
        var asset = await GameModule.Resource.LoadAssetAsync<Sprite>("icon", _cts.Token);
        _img.sprite = asset;  // 取消后这里不会执行
    }
    catch (OperationCanceledException)
    {
        // 正常取消，不报错
    }
}

protected override void OnDestroy()
{
    _cts.Cancel();
    _cts.Dispose();
}
```

### 并发加载

```csharp
// ✅ 多个资源并发加载（提速）
var (sprite, prefab, config) = await UniTask.WhenAll(
    GameModule.Resource.LoadAssetAsync<Sprite>("hero_icon"),
    GameModule.Resource.LoadGameObjectAsync("HeroModel"),
    GameModule.Resource.LoadAssetAsync<TextAsset>("hero_config")
);

// ✅ 批量加载
var tasks = locations.Select(loc => GameModule.Resource.LoadAssetAsync<Sprite>(loc));
var sprites = await UniTask.WhenAll(tasks);
```

### 禁止的异步模式

```csharp
// ❌ 禁止：Task（应使用 UniTask）
public async Task LoadAsync() { }

// ❌ 禁止：Coroutine（应使用 async/await）
IEnumerator LoadCoroutine() { yield return null; }

// ❌ 禁止：在 Update 中 await（错误用法）
void Update()
{
    await SomeAsync(); // 编译错误，但警惕逻辑错误
}

// ❌ 禁止：忽略 UniTask 警告（应加 .Forget() 或 await）
SomeUniTaskVoidMethod(); // 警告
```

---

## 禁止的代码模式

```csharp
// ❌ 禁止 Resources.Load
var sprite = Resources.Load<Sprite>("icon"); // 使用 GameModule.Resource

// ❌ 禁止直接 Instantiate Prefab
var go = Instantiate(heroPrefab); // 使用 GameModule.Resource.LoadGameObjectAsync

// ❌ 禁止 Object.FindObjectOfType（性能差）
var player = FindObjectOfType<Player>();

// ❌ 禁止在 Update 中频繁创建对象
void Update()
{
    var data = new DamageData(); // GC 压力，使用 MemoryPool
}

// ❌ 禁止直接持有跨模块的强引用（用事件替代）
public BattleSystem battleSystem; // 用 GameEvent.Get<IBattleEvent>()

// ❌ 禁止在 UI 外部直接访问 UI 组件
var ui = FindObjectOfType<BattleMainUI>(); // 用 GameModule.UI.GetUIAsync<T>
ui._btnAttack.onClick.AddListener(...);   // 禁止外部访问私有组件

// ❌ 禁止静态持有 Asset 引用
public static Sprite HeroIcon; // 导致无法卸载，内存泄漏

// ❌ 禁止忽略 async 返回值（会吞异常）
GameModule.Resource.LoadAssetAsync<Sprite>("icon"); // 要么 await，要么 .Forget() + 错误处理
```

---

## 推荐的代码模式

```csharp
// ✅ 对象池复用频繁创建的对象
var dmg = MemoryPool.Acquire<DamageInfo>();
dmg.Damage = 100;
ProcessDamage(dmg);
MemoryPool.Release(dmg);

// ✅ 事件驱动的模块间通信
GameEvent.Get<IBattleEvent>().OnPlayerDead(playerId);

// ✅ 通过 GameModule 访问模块（不直接持有引用）
GameModule.Audio.Play(AudioType.Sound, "attack_sfx");

// ✅ 读取配置表（单次获取，不重复 Get）
var cfg = ConfigSystem.Instance.Tables.TbSkill.Get(skillId);
if (cfg == null) { Log.Error($"找不到技能配置: {skillId}"); return; }

// ✅ 场景间数据传递通过数据管理类，不放全局变量
GameDataMgr.Instance.PlayerLevel = 10;
```

---

## 模块设计规范

### 新增业务模块（System）

```csharp
// ✅ 单例系统模板
public class LoginSystem
{
    private static LoginSystem _instance;
    public static LoginSystem Instance => _instance ??= new LoginSystem();

    private readonly GameEventMgr _eventMgr = new GameEventMgr();

    public void Initialize()
    {
        _eventMgr.AddEvent<string>(GameEventDef.OnAccountLogin, OnAccountLogin);
    }

    public void Dispose()
    {
        _eventMgr.Clear();
        _instance = null;
    }

    private void OnAccountLogin(string account) { /* 处理登录 */ }
}
```

### 新增热更模块（继承 TEngine 模块）

```csharp
// 仅在需要帧驱动（Update）的模块才继承 Module
public interface IMyCustomModule : IModule
{
    void DoSomething();
}

public class MyCustomModule : Module, IMyCustomModule
{
    public override void Update(float elapseSeconds, float realElapseSeconds)
    {
        // 每帧逻辑
    }

    public void DoSomething() { }
}

// 注册（在 GameApp 初始化时）
ModuleSystem.GetModule<IMyCustomModule>();
```

---

## 代码审查清单

提交代码前自查：

**资源管理**
- [ ] 每个 `LoadAssetAsync` 都有对应 `UnloadAsset`（或使用 `LoadGameObjectAsync`）
- [ ] 没有使用 `Resources.Load()` 或 `Instantiate()`
- [ ] 没有静态变量持有 Unity Asset 引用

**异步编程**
- [ ] 没有使用 `Task`（应使用 `UniTask`）
- [ ] 没有使用 `Coroutine`（应使用 `async/await`）
- [ ] 有异步操作的类都在 `OnDestroy` 中取消 `CancellationToken`

**事件系统**
- [ ] 非 UIWindow 类中注册的事件都有对应的 `RemoveEventListener` 或使用 `GameEventMgr`
- [ ] UI 内部事件都在 `RegisterEvent()` 中用 `AddUIEvent` 注册

**热更代码**
- [ ] 没有反向引用主包 internal 类型
- [ ] 新的泛型组合已添加到 AOT 引用（如有 HybridCLR AOT 错误）

**性能**
- [ ] 没有在 `Update` 中频繁创建对象（改用对象池）
- [ ] 没有在 `Update` 中做字符串拼接（改用 Timer + 缓存）
- [ ] 列表/滚动视图使用了 Widget 复用（`AdjustIconNum`）

---

## Git 工作流

```
main / master       ← 稳定版本，禁止直接提交
feature/xxx         ← 功能开发分支
fix/xxx             ← Bug 修复分支
hotfix/xxx          ← 线上紧急修复
```

**提交信息格式**：

```
feat: 添加登录界面 UI
fix: 修复资源加载后引用计数未减一的问题
refactor: 重构战斗系统事件通信方式
perf: 优化技能列表 Widget 复用逻辑
docs: 更新热更开发规范文档
```

**分支合并前检查**：
1. Unity 编译无错误无警告
2. 真机（Android/iOS）运行正常
3. 内存占用无明显增长（调试器模块监控）
4. 已自查代码审查清单
