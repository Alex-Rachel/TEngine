# 事件系统

源码：[GameEvent](../../../../Assets/TEngine/Runtime/Core/GameEvent/GameEvent.cs)、
[GameEventMgr](../../../../Assets/TEngine/Runtime/Core/GameEvent/GameEventMgr.cs)、
[EventDelegateData](../../../../Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs)、
[UIBase](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs)。

- `GameEvent` 支持 int/string ID。发送和监听的参数类型、顺序必须完全一致。
  分发按委托类型匹配，类型不一致可能直接没有回调，不保证给出诊断错误。
- `AddUIEvent` 只接收 **int** ID，支持 0..4 个泛型参数；没有 string 重载。
- 在 `RegisterEvent()` 内注册 UI 事件。框架销毁路径清理监听；实例仍存活的隐藏阶段不会解绑。
- 非 UI 对象用 `GameEventMgr.AddEvent` 和实例 `Clear()` 成对管理，或保存原委托调用 `RemoveEventListener`。
  通过 MemoryPool 获取的 GameEventMgr 交还内存池时会 Clear，不要重复释放。
- `GameEvent.Shutdown()` 是全局生命周期操作，不能用于关闭单个窗口。
- 接口事件使用项目 `[EventInterface(...)]` 和实际生成的事件 ID，不手写生成类。
  当前事件组在 [EventInterfaceAttribute](../../../../Assets/TEngine/Runtime/Core/GameEvent/EventInterfaceAttribute.cs) 中定义为 GroupUI/GroupLogic。
- `GameEventHelper.Init()` 在 [GameApp.Entrance(object[])](../../../../Assets/GameScripts/HotFix/GameLogic/GameApp.cs) 中执行。
  接口辅助 API 来自生成器，新增调用前检查编译生成结果，不能仅凭接口名推导方法或事件 ID。

框架调用片段，`eventId` 与回调由业务定义：

```csharp
protected override void RegisterEvent()
{
    AddUIEvent<int>(eventId, OnValueChanged);
}
private void OnValueChanged(int value) { /* 更新本窗口 */ }
```

字符串事件确有需求时先 `RuntimeId.ToRuntimeId(name)`，再给 UI 注册；不要把字符串直接传入 `AddUIEvent`。
`GameEvent` 没有 `UnRegisterAll`、`RegisterListener`、`ClearAll`。
委托的所有权由最短有效生命周期持有；不要在 OnRefresh 重复注册。
销毁顺序与回归场景见 [event-antipatterns.md](event-antipatterns.md)。
