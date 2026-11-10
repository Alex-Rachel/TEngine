# Pipeline 契约与维护

## 权威顺序

1. 当前任务的用户约束和两级 AGENTS 决定范围与授权。
2. 目标实例的实时命令目录、schema、实际结果决定已注册与可用能力。
3. 本地包源码决定副作用、线程、错误和生命周期；包内文档辅助定位。
4. 外部官方文档补充设计背景，不能覆盖本地版本事实。

不要用更新版本的在线示例解释当前包，也不要把包 changelog 当成参数或退出码保证。

## 当前磁盘基线

2026-11-09 核对：Unity `2022.3.62f1`、CLI `1.0.0-beta.3`、Pipeline `0.10.0-exp.1`。
这是本次维护记录，不是后续任务的固定要求。每次从以下文件和 `doctor` 重新确认：

- [ProjectVersion](../../../../ProjectSettings/ProjectVersion.txt)。
- [包元数据](../../../../Packages/com.unity.pipeline/package.json)。
- [包命令索引](../../../../Packages/com.unity.pipeline/Documentation~/command-reference.md)。
- [命令注册与参数约定](../../../../Packages/com.unity.pipeline/Documentation~/creating-commands.md)。
- [连接及输出契约](../../../../Packages/com.unity.pipeline/Documentation~/connectivity.md)。

| 能力 | 当前限制 |
|---|---|
| 普通 Editor 对象、Prefab、资源、编译和测试 | 按实际注册 schema 和平台条件使用 |
| Build Profile | Unity 6 功能，2022.3 可返回 `feature_unavailable` |
| Multiplayer Play Mode 场景命令 | Unity 6000.3+ 且安装相应包才注册 |
| Multiplayer Roles | Unity 6000.6+ |
| UI Toolkit 元素 capture、模态弹窗镜像 | Unity 6000.7+；普通 Game/Scene/window capture 另行判断 |
| Runtime Pipeline | 实验性桌面 Player 流程，不能据描述文件路径推断移动端正式支持 |
| HTTP detached jobs | 包支持，不保证当前 CLI 提供 `--detach` 或顶层 `unity job`；不跨域重载 |

功能未注册、返回 feature_unavailable 或目标条件不足时，说明具体限制，不能自动升级。

## 已核实的迁移差异

当前全名是 `<group> <name>`：`editor status`、`scene list_open`、`script recompile`、`test run`。
旧 `editor_status/list_tests/run_tests` 等通常给改名提示，不能作为兼容入口。
目录项用 `command` 保留全名，不能只读 `name`，否则多个组的 list/get/status 会合并。

参数按 schema 大小写及连字符原样发送。截图保存参数是 `output-path`，
读取字段的精简形式是 `detail=value`；包 changelog 中旧 `save_path` 等字样不能直接复制。

服务返回既有 lean `{success,result}` envelope，也有领域 response；
测试的 summary/results 不能被当作 envelope 错误剥离。Pipeline 协议的格式化结果可能为 NDJSON，
不能假定每次都有相同的 CLI 外壳。

`MainThreadRequired=false` 只说明不必在 Unity 主线程执行。
立即旁路还由内部 ImmediateCommand/队列机制决定，不对所有后台命令承诺瞬时响应。
Server ready 与 Editor ready 也不是同一条件，见 [操作规程](operations.md)。

## 升级后的维护顺序

包或 CLI 版本变化后，先记录变更范围，运行 help 和本工程只读发现；不自动升级来修工作流。
比较本次要用的 command、参数、响应模型、终态和版本条件，修改适配器及对应测试。
执行 `check`、相关隔离回归和必要的真实 Editor 验证，再更新技能中的版本记录与限制。

参考源码导航：

| 主题 | 源码目录 |
|---|---|
| canonical 名称和旧名提示 | `Packages/com.unity.pipeline/Runtime/Common`、`Editor/ObsoleteCommandNames.json` |
| 命令目录和 schema | `Runtime/Commands/PipelineCommands.cs`、命令注册实现 |
| 服务与 Editor 状态 | `Editor/EditorPipelineServer.cs`、`Editor/Commands/EditorStatusCommand.cs` |
| 编译与测试 | `Editor/Commands/RecompileCommand.cs`、`Editor/Testing/PipelineTestRunner.cs` |
| 路径、对象、Undo | `Editor/Authoring` |
| 批处理与等待 | `Editor/Commands/Batch`、对应命令实现 |

包源码不因工作流适配需要而直接修改。维护项目命令和包装器时按[内容操作](authoring.md)扩展。
