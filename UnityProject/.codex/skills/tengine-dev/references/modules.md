# 模块导航

[GameModule](../../../../Assets/GameScripts/HotFix/GameLogic/GameModule.cs) 缓存业务模块访问；
UI 属性为 `UIModule`，不是虚构的 IUIModule。启动层和 GameProto 按自身依赖边界取模块。

| 任务 | 先读取的源码 |
|---|---|
| 计时器 | [ITimerModule](../../../../Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs) |
| 场景 | [ISceneModule](../../../../Assets/TEngine/Runtime/Module/SceneModule/ISceneModule.cs) |
| 音频 | [IAudioModule](../../../../Assets/TEngine/Runtime/Module/AudioModule/IAudioModule.cs) |
| FSM | [IFsm](../../../../Assets/TEngine/Runtime/Module/FsmModule/IFsm.cs)、[FsmState](../../../../Assets/TEngine/Runtime/Module/FsmModule/FsmState.cs) |
| 资源 | [资源 API](resource-api.md) |
| UI | [UI 生命周期](ui-lifecycle.md) |

## 调用与归属

- `Timer.AddTimer(callback, time, ...)` 回调在前；持有 timerId，在所属生命周期结束时 RemoveTimer。
  默认 isLoop/isUnscaled 均为 false；隐藏时是否暂停由业务决定。
- 场景加载进度参数当前拼作 `progressCallBack`。按 `ISceneModule` 核对激活、挂起和卸载语义，
  不绕过 YooAsset 资源管理改用默认 SceneManager 加载。
- Audio 模块已持有自己的资源，先查其接口、回调和停止机制，不在调用前重复加载同一 AudioClip。
- FSM 的状态切换位置、数据约束取实际接口，不照搬其他框架的 public ChangeState 示例。
- [MemoryPool](../../../../Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.cs) 归还时调用 IMemory.Clear；
  类型在 Clear 内重置状态，交还后不再访问、不重复交还。

`GameModule.Shutdown()` 当前仅清空静态模块缓存，不会自动关闭 UI、卸载资源、停止计时器或清理全局事件。
真正的全局生命周期沿 [GameApp](../../../../Assets/GameScripts/HotFix/GameLogic/GameApp.cs)、
[ModuleSystem](../../../../Assets/TEngine/Runtime/Core/ModuleSystem.cs) 和各模块实现核对。
不要用 Shutdown、RemoveAllTimer 或全局清理恢复一个局部任务的错误。

改动哪种模块就验证其实际行为和所有权；范围选择见 [verification.md](verification.md)。
