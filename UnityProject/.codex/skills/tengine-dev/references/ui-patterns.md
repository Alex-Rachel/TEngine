# Widget 与列表

创建 API 以 [UIBase](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs) 为准，
生命周期以 [UIWidget](../../../../Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs) 为准。
所有创建泛型要求 `where T : UIWidget, new()`。

| 来源 | 调用 |
|---|---|
| 已有子节点 | `CreateWidget<T>(goPath)`、`CreateWidget<T>(parentTrans, goPath)` |
| 已有对象 | `CreateWidget<T>(goRoot)` |
| 已确认资源地址 | `CreateWidgetByPath<T>(parent, location)` 或 await `CreateWidgetByPathAsync<T>(parent, location)` |
| Prefab 模板 | `CreateWidgetByPrefab<T>(prefab, parent)` |
| 类型名等于资源地址 | `CreateWidgetByType<T>`、`CreateWidgetByTypeAsync<T>` |

创建完成执行 `Inject → ScriptGenerator → BindMemberProperty → RegisterEvent → OnCreate → OnRefresh`，
然后设置 `IsPrepare` 和初始激活状态。不要重复包装同一个 GameObject。
`CreateWidgetByPathAsync` 使用宿主 `gameObject.GetCancellationTokenOnDestroy()`，没有业务取消参数；
返回后仍要处理 null 和请求已过期的情况，取消边界见 [资源模式](resource-patterns.md)。

## 列表更新

先由调用方创建并保存列表。传 null 时方法内新建 List 不会传回调用方。

| API | 使用边界 |
|---|---|
| `AdjustIconNum(list, number, parent, prefab)` | 同步复用/增删；参数名是 number；声明的 assetPath 当前未使用 |
| 同步且未传 prefab | 通过类型名作为地址创建，不会使用 assetPath |
| `AsyncAdjustIconNum` | 返回 void，内部 Forget，不能 await；无 prefab 时实际使用 assetPath |

`AsyncAdjustIconNum` 没有业务取消或并发互斥；`maxNumPerFrame` 使用正数。
需要可等待、取消或避免重叠替换时，使用业务拥有的串行更新流程，不反复启动同一列表的异步调整。
增删后按业务数据刷新；加载失败可能产生 null 项，不能假定每个元素创建成功。

## 显隐与销毁

`widget.Visible` 调用 SetActive 和 `OnSetVisible`；当前 `InternalUpdate` 只检查 `IsPrepare`，
父窗口仍更新时不能假定隐藏 Widget 的 OnUpdate 自动停止。

主动销毁调用公开的 `widget.Destroy()`：从父列表移除 → 自身 `OnDestroy` → 内部清事件 →
递归调用子项 `OnDestroy`/内部清理 → 销毁 GameObject。
窗口关闭则先清窗口事件，再对各子 Widget 执行其销毁链，最后执行窗口 `OnDestroy`。
因此 Widget 的 `OnDestroy` 可能在自身框架事件清理前执行，回调中不能假定已解绑。

不要手调 `OnDestroyWidget`、用 `list.Clear()` 代替销毁、复用已销毁 Widget 或重复 Destroy。
Widget 的业务任务和额外订阅由其自身生命周期持有并释放。

节点查找用 `FindChild`/`FindChildComponent<T>`，按钮用项目注册辅助方法。
Prefab 绑定路径、组件类型、命名前缀必须与实际对象一致；Editor 验证交给 [unity-cli](../../unity-cli/SKILL.md)。
