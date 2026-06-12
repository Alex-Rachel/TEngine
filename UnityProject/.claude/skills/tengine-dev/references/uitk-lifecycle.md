# UITKModule 生命周期

## UITKWindow 生命周期

```
ShowUIAsync<T>()
  → InternalLoad()          加载 UXML (YooAsset/Resources)
  → CloneTree()             实例化 VisualElement
  → 挂载到对应层 Panel
  → InternalCreate() [仅首次]
      → __UITKAutoBind()        ← 自动填充 [Q]/[Bind]/[BindCommand] 字段（框架调用）
      → __UITKAutoBindEvents()  ← 自动注册 [OnClick]/[OnChange]（框架调用）
      → Inject()                ← DI 注入
      → OnCreate()              ← 用户代码（在此构造 ViewModel）
      → RegisterEvent()         ← 用户注册 GameEvent
      → __UITKAutoBindMVVM()    ← 自动绑定 [Bind]/[BindCommand]（框架调用，OnCreate 后 VM 已就绪）
  → InternalRefresh()
      → OnRefresh()             ← 每次 Show 都执行
  → OnSetWindowVisible()        ← 全屏遮挡处理
  → OnShowAnimation()           ← 显示动画
```

## 关闭流程

```
CloseUI<T>()
  → __UITKAutoUnbindMVVM()       ← 自动解绑 [Bind]/[BindCommand]（VM 仍存活时，框架调用）
  → OnDestroy()                  ← 用户代码（在此 Dispose ViewModel）
  → __UITKAutoUnbindEvents()     ← 自动解绑（框架调用）
  → RemoveAllUIEvent()           ← GameEvent 自动清理
  → 递归销毁子 Widget
  → RemoveFromHierarchy()
  → Unload(visualTreeAsset)      ← 释放资源
```

> 销毁幂等：`InternalDestroy` 重复调用安全（动画关闭期间不会二次销毁）；
> 加载未完成即关闭时跳过用户回调，避免对未初始化窗口跑代码导致 NPE。
> 资源加载失败时窗口自动弹栈回滚（不会卡在栈中），并打 Log.Error。

## 隐藏流程

```
HideUI<T>()
  → HideTimeToClose <= 0 → 直接 CloseUI
  → Visible = false (display:none)
  → IsHide = true
  → Timer → 到期自动 CloseUI
```

## UITKWidget 生命周期

```
CreateWidgetAsync<T>(parentElement)
  → LoadVisualTreeAssetAsync()
  → CloneTree() → parentElement.Add()
  → __UITKAutoBind + __UITKAutoBindEvents
  → Inject() → OnCreate() → RegisterEvent() → OnRefresh()

Destroy()
  → OnDestroy()
  → __UITKAutoUnbindEvents()
  → RemoveAllUIEvent()
  → 递归销毁子 Widget
  → RemoveFromHierarchy() + Unload
```

## 关键回调

| 回调 | 时机 | 频率 |
|------|------|------|
| OnCreate() | 首次创建完成 | 1 次 |
| OnRefresh() | 每次 Show/重新显示 | 多次 |
| OnUpdate() | 每帧（仅 Visible 时） | 每帧 |
| OnDestroy() | 关闭销毁 | 1 次 |
| OnSetVisible(bool) | 可见性变化（全屏遮挡等） | 多次 |
| RegisterEvent() | OnCreate 后注册 GameEvent | 1 次 |

## 注意事项

- `OnCreate` 中 `[Q]` 字段已被自动填充，可直接使用
- 事件绑定/解绑由框架自动管理，`OnDestroy` 中不需要手动处理
- `OnRefresh` 用于每次显示时刷新数据（如从其他页面返回）
- 不要在构造函数中操作 UI，等 `OnCreate` 才有 RootElement
