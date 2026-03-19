# TEngine 模块 API 速查

## 目录

- [GameModule 统一访问入口](#gamemodule-统一访问入口)
- [TimerModule 计时器](#timermodule-计时器)
- [SceneModule 场景管理](#scenemodule-场景管理)
- [AudioModule 音频](#audiomodule-音频)
- [FsmModule 有限状态机](#fsmmodule-有限状态机)
- [MemoryPool 内存池](#memorypool-内存池)
- [ObjectPool 对象池](#objectpool-对象池)
- [Log 日志系统](#log-日志系统)
- [LocalizationModule 本地化](#localizationmodule-本地化)

---

## GameModule 统一访问入口

所有模块通过 `GameModule` 静态类访问，内部已缓存，禁止重复 `ModuleSystem.GetModule<T>()`：

```csharp
GameModule.Resource      // IResourceModule  — 资源加载
GameModule.UI            // UIModule         — UI 管理
GameModule.Audio         // IAudioModule     — 音频
GameModule.Timer         // ITimerModule     — 计时器
GameModule.Scene         // ISceneModule     — 场景
GameModule.Fsm           // IFsmModule       — 有限状态机
GameModule.Procedure     // IProcedureModule — 流程
GameModule.Localization  // ILocalizationModule — 本地化
```

---

## TimerModule 计时器

### 添加计时器

```csharp
// 单次计时器（3秒后触发一次）
int tid = GameModule.Timer.AddTimer(OnTimerTick, time: 3f);

// 循环计时器（每1秒触发）
int tid = GameModule.Timer.AddTimer(OnTimerTick, time: 1f, isLoop: true);

// 非缩放时间（不受 Time.timeScale 影响）
int tid = GameModule.Timer.AddTimer(OnTimerTick, time: 5f, isLoop: false, isUnscaled: true);
```

### 控制计时器

```csharp
GameModule.Timer.Stop(timerId);         // 暂停
GameModule.Timer.Resume(timerId);       // 恢复
GameModule.Timer.Restart(timerId);      // 重置并重新开始
GameModule.Timer.RemoveTimer(timerId);  // 移除（销毁对象时必须调用）
GameModule.Timer.RemoveAllTimer();      // 清除所有计时器
```

### 查询

```csharp
float left   = GameModule.Timer.GetLeftTime(timerId);   // 剩余时间
bool running = GameModule.Timer.IsRunning(timerId);     // 是否运行中
```

**最佳实践**：在对象销毁（`OnDestroy` 或 `UIWindow.OnDestroy`）时调用 `RemoveTimer(tid)`，防止空引用。

---

## SceneModule 场景管理

### 加载场景

```csharp
// 单场景加载（替换当前场景）
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName");

// 叠加加载
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName", LoadSceneMode.Additive);

// 带进度回调
Scene scene = await GameModule.Scene.LoadSceneAsync(
    "SceneName",
    LoadSceneMode.Single,
    progressCallBack: p => { /* p: 0~1 */ }
);
```

### 卸载 / 激活

```csharp
bool ok = await GameModule.Scene.UnloadAsync("SceneName");
GameModule.Scene.ActivateScene("SceneName");  // 设为活动场景
bool has = GameModule.Scene.IsContainScene("SceneName");
```

**注意**：场景资产必须放在 `AssetRaw/Scenes/` 并由 YooAsset 管理，通过 location 字符串寻址，禁止 `SceneManager.LoadScene()`。

---

## AudioModule 音频

### 播放

```csharp
// 背景音乐（循环）
AudioAgent agent = GameModule.Audio.Play(AudioType.Music, "path/to/bgm", bLoop: true);

// 音效（单次）
AudioAgent agent = GameModule.Audio.Play(AudioType.Sound, "path/to/sfx");

// UI 音效（异步加载，fire and forget）
AudioAgent agent = GameModule.Audio.Play(AudioType.UISound, "ui_click", bAsync: true);
```

### 停止

```csharp
GameModule.Audio.Stop(AudioType.Music, fadeout: true);  // 渐出停止
GameModule.Audio.Stop(AudioType.Sound);
GameModule.Audio.StopAll(fadeout: false);
```

### 音量控制

```csharp
GameModule.Audio.Volume      = 1.0f;   // 全局音量 (0~1)
GameModule.Audio.MusicVolume = 0.8f;   // 音乐音量
GameModule.Audio.SoundVolume = 1.0f;   // 音效音量
GameModule.Audio.MusicEnable = true;   // 音乐开关
GameModule.Audio.SoundEnable = true;   // 音效开关
```

---

## FsmModule 有限状态机

### 创建并启动状态机

```csharp
// 定义状态
public class IdleState : FsmState<MyOwner>
{
    protected override void OnEnter(IFsm<MyOwner> fsm)  { }
    protected override void OnUpdate(IFsm<MyOwner> fsm, float elapseSeconds, float realElapseSeconds) { }
    protected override void OnLeave(IFsm<MyOwner> fsm, bool isShutdown) { }
}

// 创建状态机
IFsm<MyOwner> fsm = GameModule.Fsm.CreateFsm<MyOwner>(
    "FsmName",
    owner,
    new IdleState(),
    new RunState(),
    new AttackState()
);
fsm.Start<IdleState>();
```

### 状态切换

```csharp
// 在状态内部切换
fsm.ChangeState<RunState>();

// 数据传递
fsm.SetData<int>("Key", value);
int val = fsm.GetData<int>("Key");
```

### 销毁

```csharp
GameModule.Fsm.DestroyFsm<MyOwner>("FsmName");
```

---

## MemoryPool 内存池

用于频繁创建/销毁的纯 C# 对象，避免 GC 分配：

```csharp
// 对象需实现 IMemory 接口
public class DamageInfo : IMemory
{
    public int Damage;
    public int TargetId;
    
    public void Clear()  // 归还池时重置数据
    {
        Damage = 0;
        TargetId = 0;
    }
}

// 获取
var info = MemoryPool.Acquire<DamageInfo>();
info.Damage = 100;

// 归还（不要再使用此引用）
MemoryPool.Release(info);
```

**规则**：`Release` 后禁止再访问该对象；不能 `Release` 同一对象两次。

---

## ObjectPool 对象池

用于 GameObject 的复用，减少 Instantiate/Destroy 开销：

```csharp
// 框架内置 ObjectPoolModule，通过 YooAsset 管理 Prefab
// 生成对象（从池中取或新建）
GameObject go = GameModule.ObjectPool.Spawn("PrefabLocation", parent);

// 回收对象（不要 Destroy，而是归还池）
GameModule.ObjectPool.Unspawn(go);

// 预热（提前创建若干实例）
GameModule.ObjectPool.WarmUp("PrefabLocation", count: 10);
```

---

## Log 日志系统

```csharp
Log.Debug("调试信息，仅 Development Build 输出");
Log.Info("普通信息");
Log.Warning("警告信息");
Log.Error("错误信息");
Log.Fatal("严重错误，通常需要停止游戏");

// 条件断言
Log.Assert(condition, "条件不满足时的提示");

// 格式化
Log.Info($"玩家 {playerId} 死亡，位置 {pos}");
```

**发布规则**：`Log.Debug` 在非 Development 包中会被自动剥离；`Log.Error` 和 `Log.Fatal` 始终保留。

---

## LocalizationModule 本地化

```csharp
// 获取本地化字符串
string text = GameModule.Localization.GetString("key");

// 带参数
string text = GameModule.Localization.GetString("key", arg1, arg2);

// 切换语言
GameModule.Localization.Language = Language.ChineseSimplified;

// 查询当前语言
Language lang = GameModule.Localization.Language;
```

**集成说明**：TEngine 集成 I2Localization 编辑器工具（`Assets/Editor/I2Localization/`），本地化数据需在 I2Localization 面板中配置，并正确标记资源供 YooAsset 管理。
