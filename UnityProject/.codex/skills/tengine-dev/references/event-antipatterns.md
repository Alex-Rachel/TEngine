# 事件生命周期检查

与 [事件 API](event-system.md) 配合使用；不凭“有 Remove”判断生命周期正确。

| 场景 | 必查行为 |
|---|---|
| 重复 Show/Refresh | 不重复注册；首次 RegisterEvent 与后续刷新分离 |
| Hide | 存活且定时隐藏时监听仍有效；HideTimeToClose<=0 会立即关闭 |
| Close | 窗口先清理事件，再销毁子 Widget，最后自身 OnDestroy |
| Widget.Destroy | 先自身 OnDestroy，再清自身事件并递归销毁子项；OnDestroy 不能假定已解绑 |
| Lambda | 保存同一委托或交给局部 EventMgr；新 Lambda 无法解绑旧实例 |
| 接口无响应 | 查 Init、事件组、生成程序集和发送/监听类型 |
| 回调递归 | 更新状态与广播有去重条件，不在同类回调内无条件重发 |
| 高频通知 | 依据 Profiler 节流；计时器也要在销毁时释放 |

调用链依据 [UIWindow.InternalDestroy](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs)、
[UIWidget.Destroy/OnDestroyWidget](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs)；
`RemoveAllUIEvent` 已把管理器交还内存池，业务不再手动重复调用。

测试按改动选择重复开关、隐藏期间广播、销毁后广播或多次初始化，观察回调次数和实际状态变化。
没有源码证据时不能把隐藏、解绑、销毁当成同一件事；验证路由见 [verification.md](verification.md)。
