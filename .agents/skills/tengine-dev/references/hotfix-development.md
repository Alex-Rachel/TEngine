# TEngine 热更代码开发规范

## 目录

- [程序集划分与职责](#程序集划分与职责)
- [热更入口 GameApp](#热更入口-gameapp)
- [系统注册 GameApp_RegisterSystem](#系统注册)
- [热更流程状态机](#热更流程状态机)
- [HybridCLR 注意事项](#hybridclr-注意事项)
- [热更开发工作流](#热更开发工作流)
- [AOT 泛型补充](#aot-泛型补充)

---

## 程序集划分与职责

```
GameScripts/HotFix/
├── GameProto/          # Luban 生成的配置表代码（自动生成，不要手改）
│   ├── LubanLib/       # ByteBuf 等序列化库
│   ├── GameConfig/     # Tables + 各配置类（由 Luban 生成）
│   └── ConfigSystem.cs # 配置加载器
│
└── GameLogic/          # 业务逻辑（主开发区域）
    ├── GameApp.cs                  # 热更主入口
    ├── GameApp_RegisterSystem.cs   # 系统注册
    ├── GameModule.cs               # 模块统一访问入口
    ├── IEvent/                     # 事件接口定义
    ├── Module/
    │   └── UIModule/               # UI 管理器实现
    ├── System/                     # 业务系统（登录/主城/战斗等）
    ├── UI/                         # UI 窗口代码
    │   ├── Login/
    │   ├── MainMenu/
    │   └── Battle/
    └── Data/                       # 玩家数据、运行时缓存
```

**依赖规则**（严格遵守，不可逆向）：
```
GameLogic → GameProto
GameLogic → TEngine.Runtime
```

---

## 热更入口 GameApp

`GameApp.cs` 是整个热更域的主入口，由主包的 `ProcedureStartGame` 通过反射调用。

```csharp
// GameScripts/HotFix/GameLogic/GameApp.cs
public class GameApp
{
    /// <summary>
    /// 热更主入口，由主包 ProcedureStartGame 调用
    /// </summary>
    /// <param name="assemblies">已加载的热更程序集列表</param>
    public static void Entrance(Assembly[] assemblies)
    {
        // 1. 初始化接口事件代理（必须最先调用）
        GameEventHelper.Init();

        // 2. 注册所有业务系统
        GameApp_RegisterSystem.Register();

        // 3. 启动游戏主流程
        var procedure = ModuleSystem.GetModule<IProcedureModule>();
        procedure.ChangeState<ProcedureLogin>();
    }
}
```

**注意**：`GameEventHelper.Init()` 必须在任何 `GameEvent.Get<T>()` 调用之前执行。

---

## 系统注册

```csharp
// GameScripts/HotFix/GameLogic/GameApp_RegisterSystem.cs
public static class GameApp_RegisterSystem
{
    public static void Register()
    {
        // 注册各业务系统（单例系统、服务等）
        RegisterLoginSystem();
        RegisterBattleSystem();
        // ...
    }

    private static void RegisterLoginSystem()
    {
        // 注册事件监听实现
        GameEventHelper.RegisterListener<ILoginUI>(LoginSystem.Instance);
        // 初始化系统
        LoginSystem.Instance.Initialize();
    }
}
```

---

## 热更流程状态机

热更域内的流程通过 `ProcedureBase`（热更版，与主包的同名但不同程序集）实现：

```csharp
// 登录流程示例
public class ProcedureLogin : ProcedureBase
{
    protected override void OnEnter(ProcedureOwner owner)
    {
        // 打开登录 UI
        GameModule.UI.ShowUIAsync<LoginUI>();
    }

    protected override void OnUpdate(ProcedureOwner owner, float elapsed, float realElapsed)
    {
        // 通常不在 Update 轮询，改用事件驱动
    }

    protected override void OnLeave(ProcedureOwner owner, bool isShutdown)
    {
        GameModule.UI.CloseUI<LoginUI>();
    }

    // 外部调用此方法切换到下一个流程
    public void OnLoginSuccess()
    {
        ChangeState<ProcedureSelectRole>(owner);
    }
}
```

### 流程切换

```csharp
// 在 Procedure 内部切换
ChangeState<ProcedureMain>(procedureOwner);

// 通过 GameModule.Procedure 外部切换（较少使用）
GameModule.Procedure.ChangeState<ProcedureMain>();
```

---

## HybridCLR 注意事项

### 1. AOT 泛型需预先声明

IL2CPP + HybridCLR 中，泛型类型必须有 AOT 实例才能在热更代码中使用：

```csharp
// AOTGenericReferences.cs（主包，由 HybridCLR 工具自动生成）
// 如果热更代码中使用了新的泛型组合，需要重新生成此文件
// 工具：HybridCLR → Generate → AOT Generic References
```

如果出现 `ExecutionEngineException: Attempting to call method xxx on type xxx for which no ahead of time (AOT) code was generated.`，需要将该泛型组合添加到 AOT 引用并重新生成。

### 2. 热更代码不能引用主包 internal 类型

```csharp
// ❌ 错误：热更代码引用主包的 internal 类
// GameScripts/Main/ 中的 internal class
internal class BootstrapHelper { }

// HotFix 中不能引用
var h = new BootstrapHelper(); // 编译不通过（设计上的正确约束）
```

### 3. 反射在热更中的限制

- 避免在热更代码中大量使用反射，性能差且 AOT 限制多
- 框架内部使用的反射（Source Generator、事件系统）已处理好兼容性

### 4. 不支持的 C# 特性

- `dynamic` 类型
- `Expression<T>` 编译
- 部分 `Emit` 操作
- `Marshal`/P/Invoke（平台限制）

### 5. iOS AOT 模式要求

iOS 平台必须使用 AOT 模式，所有代码最终需要 AOT 编译：
- 热更代码在 iOS 上由 HybridCLR 解释执行（性能略低于 JIT）
- 建议核心战斗逻辑使用确定性定点数，避免浮点问题

---

## 热更开发工作流

### 日常开发步骤

```
1. 在 GameScripts/HotFix/ 下修改/添加代码

2. Unity Editor 模式（热更 DLL 直接编译进 Assets）：
   - 直接 Play 即可，不需要打 AssetBundle

3. 模拟热更模式（测试热更流程）：
   HybridCLR → Build → Generate All
   → 打资源包（YooAsset AssetBundleCollector → Build）
   → 将 DLL 拷贝到资源包输出目录
   → 模拟从 CDN 下载更新

4. 真机测试：
   → 出包（iOS/Android）
   → 部署热更资源到 CDN
   → 启动游戏触发热更流程
```

### 新功能开发流程

```
1. 在 IEvent/ 定义事件接口（如需跨模块通信）
2. 在 UI/ 下创建 UIWindow 子类（添加 [Window] 特性）
3. 在 System/ 下实现业务系统（处理数据和逻辑）
4. 在 GameApp_RegisterSystem.cs 中注册新系统
5. 在流程 Procedure 中连接 UI 打开和系统初始化
```

### 添加新 UI 步骤

```
1. 在 Unity 中创建 UI Prefab，放到 Assets/AssetRaw/UI/Prefabs/
2. 按命名规范命名节点（m_btn_xxx, m_txt_xxx 等）
3. 使用 UIScriptGenerator 工具生成脚本模板
4. 创建 [Window(UILayer.UI, "PrefabName")] 的 UIWindow 子类
5. 将生成的节点绑定代码粘贴到 ScriptGenerator() 中
6. 实现 RegisterEvent() / OnCreate() / OnRefresh() 方法
```

---

## AOT 泛型补充

HybridCLR 工具链会在打包时分析热更代码，自动收集需要 AOT 补充的泛型。手动管理步骤：

```
Unity 菜单：HybridCLR → Generate → AOT Generic References
→ 生成 Assets/HybridCLRData/AOTGenericReferences.cs
→ 包含主包需要预先 AOT 编译的泛型实例

如出现运行时 AOT 错误：
1. 查看错误信息中的类型/方法名
2. 在 AOTGenericReferences.cs 中手动添加对应类型的使用
3. 重新打包主包
```

常见需要 AOT 补充的场景：
- `List<自定义类型>`、`Dictionary<K,V>` 新组合
- `UniTask<自定义类型>`
- `Func<自定义类型>`、`Action<自定义类型>`
