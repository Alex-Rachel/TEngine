---
name: tengine-dev
description: 在本仓库实现、排查或审查 TEngine 模块、UIWindow/UIWidget、GameEvent、YooAsset 资源及 HybridCLR 热更代码。配置源与导出使用 luban-dev，实际 Editor/Player 操作使用项目 unity-cli；纯文案和独立算法无需加载。
---

# TEngine 开发

先用 `git status` 和相关实现确定当前基线，沿调用方、接口、实现、asmdef 和测试追踪行为。
本技能提供框架决策与源码导航；Editor/Player 的执行层是
[`com.unity.pipeline`](../../../Packages/com.unity.pipeline/package.json)，通过项目
[unity-cli](../unity-cli/SKILL.md) 操作。参考中的签名与示例必须再次对照当前源码。

## 开发闭环

1. 明确要改变的可观察行为、入口和验收场景。小改直接实施；跨模块或多阶段任务用[任务模板](../../templates/task.md)。
2. 按下表只读相关主题，确认主包/热更边界、事件订阅方、资源持有方和异步结束条件。
3. 使用代码编辑工具实施，沿用相邻代码的模块与异步模式；配置生成代码回溯源模板。
4. 依据[验证选择](references/verification.md)执行门禁。涉及序列化、资源和运行状态时加载项目 unity-cli，
   先发现本工程 Pipeline 命令与参数，再选择验证。
5. 交付变更、实际证据、兼容性影响和未完成项。缺少 Editor 或匹配测试时说明具体阻塞。

## 按任务读取

| 任务 | 首读 | 有相应问题时再读 |
|---|---|---|
| 窗口显示、刷新、隐藏、关闭 | [UI 生命周期](references/ui-lifecycle.md) | [Widget 与列表](references/ui-patterns.md) |
| 事件订阅、接口事件 | [事件系统](references/event-system.md) | [事件生命周期检查](references/event-antipatterns.md) |
| 图片、实例、普通 Asset | [资源 API 与所有权](references/resource-api.md) | [刷新竞争与取消](references/resource-patterns.md) |
| 计时器、场景、音频、FSM | [模块导航](references/modules.md) | 对应接口和调用方 |
| 热更、主包与程序集依赖 | [程序集与启动](references/architecture.md) | [热更开发](references/hotfix-workflow.md) |
| 绑定生成和命名 | [命名与绑定](references/naming-rules.md) | 生成器、配置资源和实际 Prefab |
| 游戏配置接入 | [配置集成路由](references/luban-config.md) | [luban-dev](../luban-dev/SKILL.md) |
| 失败定位 | [故障定位](references/troubleshooting.md) | 对应领域参考及 Pipeline 诊断 |

## 实现时必须确定

- 业务通过 `GameModule` 访问模块；启动层和底层程序集遵守其现有初始化与依赖方式。
- 业务异步使用 UniTask，确定取消、失败、重复刷新和销毁后的结果归属；框架已有同步 API 可以按场景使用。
- `UIWindow` 是普通 C# 对象。Hide 是否保留实例取决于 `HideTimeToClose`；窗口与 Widget 的销毁顺序不同。
- UI 监听交给 `AddUIEvent`，非 UI 订阅由持有者释放；全局 Shutdown 不能替代局部清理。
- 图片优先 `SetSprite`，实例优先 `LoadGameObjectAsync`，普通 Asset 成功取得后配对 `UnloadAsset`。
  散图、子图、对象池和原始句柄的契约不同，具体限制见资源参考。
- `GameEntry`、`Procedure`、`Launcher` 属于主包；热更业务在 `GameScripts/HotFix`，以 asmdef 和当前加载链为准。

普通源码任务不自动授权资产覆盖、包/项目设置、平台切换、HybridCLR 生成、真实构建或发布。
Unity 写入先预览具体目标和影响，沿用会话中已有的有效授权；不要重复索取相同范围的确认。
