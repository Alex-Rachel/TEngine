---
name: unity-cli
description: 通过本项目 com.unity.pipeline 和 Unity CLI 查询、操作及验证 Editor/Player，处理场景、Prefab、组件、资源数据库、编译、测试、截图和性能证据。涉及实际 Unity 状态时使用；纯文本或只读源码定位不必连接 Editor。不使用 Unity MCP。
---

# Unity CLI 与 Pipeline

`com.unity.pipeline` 是本项目 Unity 自动化的执行基础：注册命令、线程调度、对象与资源操作、异步状态和运行结果。
项目 Python 工作流负责绑定工程、执行已审核命令和保留证据；它只封装包的一部分能力。
包的[命令参考](../../../Packages/com.unity.pipeline/Documentation~/command-reference.md)和当前注册 schema 是操作依据，
不要把其他全局 Unity 技能或网上示例当作本项目接口。

## 起步

以下命令在 Unity 项目根运行，其他目录可调用脚本的绝对路径：

```powershell
python .codex/scripts/workflow.py doctor
python .codex/scripts/workflow.py unity list
python .codex/scripts/workflow.py unity describe "scene list_open"
python .codex/scripts/workflow.py unity query "scene list_open"
```

先确认规范化项目路径、唯一实例、CLI/包/Editor 版本与目标操作允许的 Editor 状态。
服务可连接与 Editor 空闲是两个条件。离线时仍可完成源码和文档工作，但实际 Editor 验证记为 blocked。
本地版本事实和更新检查见 [Pipeline 契约](references/pipeline-contract.md)。

## 按任务读取

| 任务 | 项目规程 | 包内权威入口 |
|---|---|---|
| 连接、发现、schema、参数、查询、故障恢复 | [操作规程](references/operations.md) | [connectivity](../../../Packages/com.unity.pipeline/Documentation~/connectivity.md)、[pipeline](../../../Packages/com.unity.pipeline/Documentation~/commands/pipeline.md) |
| 编译、EditMode/PlayMode、资源和构建证据 | [验证与证据](references/verification.md) | [build-and-compilation](../../../Packages/com.unity.pipeline/Documentation~/commands/build-and-compilation.md) |
| 场景、对象、组件、Prefab、批处理、项目命令 | [内容操作](references/authoring.md) | [authoring](../../../Packages/com.unity.pipeline/Documentation~/authoring-commands.md)、[safety](../../../Packages/com.unity.pipeline/Documentation~/safety-and-mutations.md) |
| 截图、Profiler、内存、Player、方法重载 | [诊断与运行时](references/diagnostics.md) | [capture](../../../Packages/com.unity.pipeline/Documentation~/commands/capture.md)、[runtime](../../../Packages/com.unity.pipeline/Documentation~/runtime-setup.md) |

只加载本次任务相关的规程和包文档，不复制整份命令目录进上下文。

## 执行边界

- 使用 `<group> <name>` 全名，如 `script recompile`、`test list`。包装器将全名作为一个带引号的参数接收；
  原生 CLI 的 group/name 是两个参数。旧扁平名只作为迁移线索。
- 包命令多用 `dry-run`、`filter-type`；本项目自定义命令仍有 `dry_run`。按 schema 原样传键，不统一替换命名。
- 查询不能凭名称判定。新增对象、捕获文件、切换选择、Profiler、Play Mode、脚本和菜单可能改变状态。
- 源码使用代码编辑工具。`eval`、`script run`、MethodReload、菜单及文本写入不能绕过代码审查和 Unity 授权。
- Unity 写入先展示具体项目、目标、参数、输入和保存范围，取得该范围的明确授权后执行；
  删除、覆盖、包/设置、平台切换、真实构建单独确认。`confirm` 是命令参数，不是用户授权。
- 编译、导入、测试和写入串行。状态观察不要求 Editor 空闲；超时只重试经过审核的只读观察，
  已发送的写入结果未知时先查真实状态，禁止自动重放。
- `dry-run`、Undo、authoring root 各有适用范围，不能据此承诺全工程事务回滚或安全沙箱。
- 缺连接、命令、匹配测试或必需依赖时报告具体阻塞，不自动安装升级或连接其他工程替代。

交付需包含命令与筛选器、目标身份、启动响应和终态、执行数量及产物路径。
区分结构检查、运行验证、截图观察和人工验收；外层 `success=true` 不能覆盖内层失败。
