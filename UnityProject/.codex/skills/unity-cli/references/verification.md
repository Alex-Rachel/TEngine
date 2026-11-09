# 验证与证据

## 按影响选择

```powershell
python .codex/scripts/workflow.py check
python .codex/scripts/workflow.py verify --profile docs
python .codex/scripts/workflow.py verify --profile code
python .codex/scripts/workflow.py verify --profile unity --test-filter <relevant-assembly> --filter-type assembly
python .codex/scripts/workflow.py verify --profile full --asset <Assets/Actual.prefab>
```

| 改动 | 需要的证据 |
|---|---|
| 文档、技能、Python | docs/check；Python 逻辑有对应行为测试 |
| C# | 完整解决方案构建、Editor 编译和相关测试 |
| Prefab、组件、导入、资源绑定 | 对应资产检查、Editor 测试、相关运行场景；视觉变更另留截图 |
| HTML/配置工具链 | 对应隔离 browser/Luban 回归；生成后仍执行 C#/资源门禁 |
| 热更、IL2CPP、目标平台 | 相应构建/设备证据另行授权，不能由 Editor 测试代替 |

`code` 包含 docs 与 .NET 构建；`unity` 包含发现、编译、指定资产及两种模式测试；
`full` 组合它们和领域隔离回归。默认筛选器 `TEngine.Workflow` 仅为工作流测试，
不代表完整游戏回归。Profile 是阶段集合，不是质量认证。
当前不存在的能力不能以 skipped 变为必需阶段通过。

新增 C#/asmdef 先让 Unity 导入并更新生成工程。旧 csproj 成功不能证明新文件已参与编译。
不要手造解决方案或借用其他项目解决 .NET/Editor 验证。

## 编译

先发现 `script recompile` 与 `script recompile_status`，检查 Editor 没有在导入、编译或重载。
起始命令只发送一次；确认启动响应，再轮询 `completed/up_to_date`。
检查 `failed/errors/compilationFailed` 及当前控制台，而不是仅判断 status 字符串。
成功重载后重新发现命令。编译失败可能仍保留旧程序集，后续能查询对象不能证明新代码生效。

服务状态与 Editor 状态分开判断。轮询不能要求空闲；重载期间允许有限的连接重试，
持续断开或轮询超时记 blocked，并保留当前状态。控制台旧日志不能替代本次编译结果。

## Unity 测试

包内契约见 [build-and-compilation](../../../../Packages/com.unity.pipeline/Documentation~/commands/build-and-compilation.md)，
实现见 [PipelineTestRunner](../../../../Packages/com.unity.pipeline/Editor/Testing/PipelineTestRunner.cs)。

1. 分别用 `test list --mode editor/playmode` 发现叶用例；按 testName/assembly/category 选择明确集合，
   默认不包含 Explicit。无匹配或全不可运行时阻塞。
2. 查询 `test status`，有活跃测试时停止启动新的测试；本包新 test run 会取消前一轮。
3. 分别启动 editor 和 playmode 的 `test run`，参数为 `filter-type`、`async-tests=true`。
   PlayMode 同步不支持，`mode=all` 加 async 也不支持。
4. 保存成功启动响应，再串行轮询终态。当前异步实现没有可依赖的服务端总超时，
   客户端必须有期限；超时后测试可能继续，不能自动重跑或擅自取消他人测试。
5. 核对 summary、逐用例结果、身份集合和数量。零用例、全跳过、失败、inconclusive、取消、
   结果与筛选集合不符均不能通过。部分跳过须列出及说明覆盖缺口。

当前异步结果没有 runId/mode/filter 等关联字段。关联依据是本次启动响应、串行控制、
发现集合与实际用例结果；不能声称服务端已经用 runId 绑定。
锁只约束本工作流，外部操作仍可能干扰，存在干扰证据时本次结果无效。

## 资产与视觉

使用明确、可重复的 `--asset`；未提供目标时资产阶段为可选 skipped，不代表扫过全工程。
项目 `tengine_validate_assets` 的[当前实现](../../../../Assets/TEngine/Extension/HtmlToUGUI/Editor/TEngineWorkflowCommands.cs)：

- Prefab/GameObject：递归检查 Missing Script 与可识别的断引用，并检查主资源/importer。
- 其他普通资产：检查主资源、可识别断引用及 importer。
- SceneAsset：内部遍历当前被注释，仅检查主资源/importer；不能证明场景内部正确。

因此场景内容验收需要额外的实际场景检查和相关测试；`verify --asset *.unity` 不得以该命令直接全绿。
这些结构检查也不覆盖所有空引用的业务必填规则、YooAsset 地址、字体字形或运行布局。
视觉证据按[诊断规程](diagnostics.md)获取，记录实际分辨率、相机/窗口来源、Play 状态及人工观察。

## 构建和报告

构建预检优先读取 build get_settings/target list。`build run --dry-run` 只验证受支持的输入；
必须检查 `status=dry_run`、`valid=true` 且 `validationErrors=[]`，外层命令成功不能代替输入有效。
当前预检不返回解析后的平台、路径和场景，工作流要求显式固定 `target`、`output-path` 和非空 `scenes`，
避免审批范围随 Editor 默认设置变化。不构建、不验证 AOT/热更发布。
真实构建需明确授权，保存起始 buildId，轮询 build status，
核对同一 buildId、`result=Succeeded`、平台、输出、错误和文件。
平台切换是另一项需单独确认的长操作；不能为了预检擅自切平台。

`.codex/runs/<run-id>` 保存脱敏 JSON/Markdown、命令、退出码与产物。
工作流退出码为 `0=passed`、`1=failed`、`2=blocked`，与底层 CLI 退出码分别记录。
`report --run <directory>` 只重排已有证据，不能补写成功；中断报告保持 blocked。
交付列明实际筛选与覆盖、剩余阻塞，不把结构检查或截图等同发布验收。
