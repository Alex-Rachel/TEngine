# 窗口生命周期

先读 [UIBase](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs)、
[UIWindow](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs)、
[UIModule](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs) 的调用链；
属性声明见 [WindowAttribute](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/WindowAttribute.cs)。

## 创建与刷新

首次准备完成时执行 `Inject → ScriptGenerator → BindMemberProperty → RegisterEvent → OnCreate`，
随后 `OnRefresh`。已有窗口再次显示时更新参数并刷新，不重复创建和注册。
`UserData` 是首个参数，`UserDatas` 是完整数组；先做类型匹配。生命周期回调不接收 userData 参数，也没有 `OnClose`。

窗口通过 `[Window(UILayer.UI, location: "...")]` 声明层级与已确认的资源地址。
`UILayer` 为 Bottom/UI/Top/Tips/System；空 location 的回退规则见 `UIModule.CreateInstance`，不能凭类名推断收集器地址。
绑定节点和组件必须与实际 Prefab 一致，生成规则见 [命名与绑定](naming-rules.md)。

`ShowUIAsync<T>` 返回 void。`ShowUIAsyncAwait<T>` 返回 `UniTask<T>`，但当前实现有以下限制：

- 已存在且仍在加载的窗口可直接返回。
- 新窗口等待 `IsLoadDone`，超过按 `Time.deltaTime` 累计的 60 秒仍返回窗口；没有业务取消参数。
- `Handle_Completed` 在 Canvas 检查和 `OnCreate` 前设置 `IsLoadDone`，返回实例不等于创建成功。

`window != null && window.IsPrepare && window.gameObject != null` 只是必要保护，不能证明绑定和 `OnCreate` 成功：
`IsPrepare` 在准备回调前设置，`InternalCreate` 也先设置创建标记；初始化抛错后这些检查仍可能全部成立。
依赖控件的调用方还需有明确的初始化成功/失败信号；不能把三项 guard 当作该等待契约的修复。
`GetUIAsyncAwait<T>(token)` 也有上述加载标记和超时限制，不能当成成功保证。
需要严格可取消的打开协议时，在本次任务范围内设计并验证，不能只给现有调用加上 await。

## 显隐与关闭

| 操作 | 当前行为 |
|---|---|
| `HideUI<T>` 且 `HideTimeToClose > 0` | 改 Canvas layer 和 Raycaster，保留实例与监听，定时调用 Close |
| `HideUI<T>` 且 `HideTimeToClose <= 0` | 立即 Close |
| 窗口 `Visible` 变化 | 创建完成后调用 `OnSetVisible(bool)`；不通过 SetActive 隐藏整个窗口 |
| `CloseUI<T>` | 清窗口事件 → 子 Widget 的 OnDestroy 与内部清理 → 窗口 OnDestroy → 销毁面板 |

`hideTimeToClose` 默认 10 秒。定时关闭前重新显示会取消隐藏计时器；已有监听可能在隐藏期间收到事件。
窗口 `InternalUpdate` 只更新已准备且可见的窗口。隐藏时是否取消异步、暂停计时器由业务明确决定。

`UIWindow` 是普通 C# 对象，`this == null` 无法判断面板销毁。
在自己的 `OnDestroy` 中取消业务任务、归还持有资源；不要再手动调用内部销毁方法或重复清理框架事件池。
异步工作放在返回 `UniTask` 的方法内，调用方 await 或显式 Forget 并观察异常。

Widget 的创建和销毁顺序见 [Widget 与列表](ui-patterns.md)，事件规则见 [事件系统](event-system.md)，
按改动选择 [验证场景](verification.md)。
