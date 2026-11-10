# TEngine AI 开发工作流

维护核对：2026-11-09。版本以当前源码、CLI help、实时命令目录和 doctor 输出为准。
入口是[仓库 AGENTS](../AGENTS.md)及[Unity 项目 AGENTS](../UnityProject/AGENTS.md)。
本地与 CI 可共用 Python 入口。Unity 自动化以项目中的
[`com.unity.pipeline`](../UnityProject/Packages/com.unity.pipeline/package.json) 为执行基础，
不依赖其他 Unity 连接服务或用户级技能安装。

## 职责与架构

| 层 | 负责什么 | 维护位置 |
|---|---|---|
| 工作约定 | 范围、授权、已有修改、交付边界 | 两级 AGENTS |
| 技能 | 任务路由、源码导航、领域决策、按需规程 | `.codex/skills` |
| 项目工作流 | 目标绑定、已审核策略、审批记录、验证阶段和脱敏证据 | `.codex/scripts/workflow.py`、`workflow_lib` |
| Unity CLI | 实例发现、鉴权、请求与协议传输 | `Tools/unity.exe` |
| Unity Pipeline | 命令注册/schema、线程调度、对象/资产操作、编译测试与状态 | `Packages/com.unity.pipeline` |
| 实际工程 | 业务代码、资源、配置源、Editor/Player 和设备行为 | 当前工作树及目标实例 |

技能不维护第二套 API，工作流不重写 Pipeline 服务，也不把有限白名单等同于包的全部能力。
源码用代码编辑工具；Unity 对象和序列化通过 CLI/Pipeline；配置生成由现有 Luban 工具完成。
先取得目标实例 schema，再按本次需要读取包文档和源码，避免把整份命令手册塞进每次上下文。

## 研发闭环

需求确认 -> 读取源码与技能 -> 实现 -> 自动验证 -> 审查 -> 证据交付。

小修改直接实施并交付摘要。跨模块、高风险或多阶段任务使用[一份任务模板](../UnityProject/.codex/templates/task.md)，记录目标、验收、决策、授权和证据；不创建多文档审批体系。
先检查工作区已有修改；不恢复用户已删除文件，不修改第三方 Pipeline 包来迎合工作流。

| 任务 | 技能 |
|---|---|
| 框架、生命周期、事件、资源、热更 | [tengine-dev](../UnityProject/.codex/skills/tengine-dev/SKILL.md) |
| Editor/Player、场景、资源、测试和构建预检 | [unity-cli](../UnityProject/.codex/skills/unity-cli/SKILL.md) |
| 配置结构、数据、普通/整表懒加载及分片能力核对 | [luban-dev](../UnityProject/.codex/skills/luban-dev/SKILL.md) |
| HTML/UI-DSL 到 UGUI Prefab | [html-to-ugui](../UnityProject/.codex/skills/html-to-ugui/SKILL.md) |

`.codex/skills` 是唯一维护源，通过 AGENTS 按任务显式读取，不假设所有客户端原生发现该目录。
跨技能只组合必要内容，例如配置驱动 UI 涉及 Luban、框架和 Editor 验证；普通文案不触发 Unity 检查。
查源码、查表、仅 HTML/JSON 烘焙不自动升级为对象写入、生产导出或真实构建。
参考文档仅导航；源码、程序集和实时 Editor 结果有优先权。

## 环境与预检

本次磁盘基线为 Unity `2022.3.62f1`、CLI `1.0.0-beta.3`、Pipeline `0.10.0-exp.1`，不要求升级。
需要 Python 3.11+、Git、项目对应 .NET SDK、Unity 生成的完整解决方案。
[requirements.txt](../UnityProject/.codex/scripts/requirements.txt) 描述可选领域依赖；缺工具时报告阻塞，由维护者决定安装。
浏览器转换另需已安装的 Playwright Chromium；脚本不会自动下载。

以下命令从 `UnityProject` 执行，也可以从任意目录调用脚本绝对路径：

```powershell
python .codex/scripts/workflow.py doctor
python .codex/scripts/workflow.py check
python .codex/scripts/workflow.py unity list
python .codex/scripts/workflow.py unity describe "scene list_open"
python .codex/scripts/workflow.py unity query "scene list_open"
```

CLI 位于 `Tools/unity.exe`，不是 Unity Editor 的同名二进制。
每次 Editor 操作绑定规范化项目路径。CLI status 的项目路径是子串过滤，工作流还会核对规范化绝对路径与唯一实例。
服务 ready 不等于 Editor 空闲：新编译、测试、写入还要核对 `editor status`，排除编译、重载、
导入及不适合该操作的 Play 状态。忙时可以发现命令和读取已审核状态；
轮询持续确认工程身份，错误项目、多实例或持续断开均阻塞。
不通过读取/打印 `Library/Pipeline` 描述文件诊断鉴权，不自动启动其他项目代替。
Linux/macOS CI 可做离线检查；Editor 验收需要可运行该项目 CLI 的受支持 runner，当前仓库自带 Windows 可执行文件。

## Pipeline 能力发现

当前命令使用 `<group> <name>` 全名，例如 `script recompile`、`test list/run/status`。
旧 `editor_status/list_tests` 等不能作为可用接口。包装器的 `unity describe` 返回精确 schema；
它是工作流功能，不是包的 describe 命令。

需要按组探索时，用原生 CLI 查询包的目录：

```powershell
& ./Tools/unity.exe --json --non-interactive --no-log-proxy command --project-path . pipeline list_commands --detail groups
& ./Tools/unity.exe --json --non-interactive --no-log-proxy command --project-path . pipeline list_commands --detail full --query "component get_fields"
```

查询子串匹配的结果须按 `command` 精确选择。参数按 schema 原样发送，包通常使用 `filter-type/dry-run/output-path`，
项目两个 global 命令仍有 `dry_run/json_path`；不做统一猜测转换。
命令目录、参数、线程、响应和功能版本条件见
[Pipeline 契约](../UnityProject/.codex/skills/unity-cli/references/pipeline-contract.md)。

2022.3 的 Build Profile、MPPM、UITK 元素截图和弹窗镜像能力有具体版本限制。
包内文档介绍的 HTTP detached jobs 不保证当前 CLI 有对应顶层子命令；以 help 和实际注册结果为准。
不自动安装升级或修改包来获得不存在的能力。

## 验证矩阵

```powershell
python .codex/scripts/workflow.py verify --profile docs
python .codex/scripts/workflow.py verify --profile code
python .codex/scripts/workflow.py verify --profile unity --test-filter <relevant-assembly> --filter-type assembly
python .codex/scripts/workflow.py verify --profile full --asset Assets/TEngine/Extension/HtmlToUGUI/HtmlToUGUIConfig.asset
```

| Profile | 必需阶段 |
|---|---|
| docs | 四技能结构、引用、退役依赖检查、Python 行为与契约测试 |
| code | docs 检查加完整解决方案构建；生成工程需包含项目源码 |
| unity | 实际能力发现、Editor 编译、明确筛选的 EditMode/PlayMode 测试；给定资产的验证 |
| full | 上述全部，加隔离的浏览器、Luban 普通/整表懒加载回归和当前配置源生成校验 |

默认测试筛选器仅选本次工作流测试，**不代表全部游戏测试**。改业务时显式选择相关程序集、分类或名称。
没有资产目标时资产阶段 skipped，不代表检查过整个工程；资源任务必须传明确的 `--asset`。
当前 `tengine_validate_assets` 对 Prefab 可遍历 Missing Script/断引用，但 SceneAsset 的内部遍历已被注释，
只证明主资产/importer 存在。场景内容不能因此通过，工作流将所需的额外场景内容验证标为 blocked。
新 asmdef/C# 文件需要 Unity 导入并重新生成工程。旧 csproj 的绿色结果不能证明新文件已编译。
异步启动必须确认收到响应，持续轮询最终结果，核对用例身份、数量、每项结果和汇总。
零用例、全跳过、失败、过期结果、编译错误不能通过。

测试分别选择 editor/playmode 并异步运行：PlayMode 同步不支持，all+async 不支持；
新 test run 会取消前一轮，不能并行启动。本包异步结果没有 runId/mode/filter，
当前也没有可依赖的服务端测试总超时。工作流用客户端期限、成功启动响应、串行控制和用例集合建立证据，
超时报告未知状态，不重跑掩盖。详细规则见
[验证与证据](../UnityProject/.codex/skills/unity-cli/references/verification.md)。

## 写入与授权

查询和预览不等于授权。写入前展示具体项目、操作、参数、输入和影响路径；范围变化重新确认。
沿用会话中覆盖具体目标和范围的有效授权；范围变化才重新确认。
不自动运行任意菜单、eval、文本写入或名称像查询的未知命令。

```powershell
python .codex/scripts/workflow.py ui preview --json input.json --config Assets/TEngine/Extension/HtmlToUGUI/HtmlToUGUIConfig.asset --prefab Assets/AssetRaw/UI/Example.prefab
python .codex/scripts/workflow.py approve --plan <preview.json> --by <reviewer> --reason <actual-confirmation> --high-risk
python .codex/scripts/workflow.py ui apply --plan <preview.json> --approval <approval.json>
```

`<...>` 为上一步输出/真实确认引用，不能原样执行。approve 只能在用户或 CI 人工批准后调用，不允许代理自行写“已同意”。
审批绑定工作树、项目、命令、输入哈希，1 小时有效、一次尝试。审批记录防误操作，不是抗本地恶意修改的密码学权限系统。
同项目工作流使用锁串行；锁不阻止用户或其他客户端操作 Editor。不要删除有效锁；
异常退出留下锁时核实进程和在途操作后由维护者处理。
查询/轮询可以有限重试；写入超时视作未知结果，先查目标/状态，不自动重放。
删除、覆盖、项目/包设置、平台切换、真实构建必须单独批准。不自动安装、升级、提交、推送、签名、上传或发布。

未封装能力不是不存在：审核包文档、schema、源码和副作用后，可按同样的预览/授权规则使用原生 CLI，
将准确调用与实际结果记录到任务；反复复用的能力再加入包装器和契约测试。
`build run` 的 dry-run 必须有 `valid=true` 和空 `validationErrors`；本工作流还要求显式提供平台、输出路径和非空场景集合，
因为包的预检结果没有展开 Editor 默认构建范围。审批前固定这些输入，不能仅凭 `status=dry_run` 判定通过。
`confirm/dry-run` 由每个命令自行实现，新增对象可能没有门禁参数；
authoring root 不约束所有截图/脚本/构建路径，Undo 不普遍恢复资产写盘与设置。
对象和 Prefab 操作见[内容规程](../UnityProject/.codex/skills/unity-cli/references/authoring.md)。

## 领域流程

Luban：先按查表、源修改、隔离校验、生产导出确定终点。
需要更新生产产物时：结构与引用预检 -> 源数据修改 -> validate -> preview/approve/apply -> 产物检查 -> 编译和 Editor 验证。
`validate` 不写生产产物，而在 runs 内对当前配置实际生成；旧 helper 对多行 Bean 表头的诊断仅供辅助。
当前两个脚本都使用 `-d bin`；`lazyload` 额外选择整表懒加载模板，不是分片。
不存在经当前源码验证的 partition API，不恢复被删除的旧说明，也不把 fixture tags 当实现。
两模式写同一产物目录，不能在生产工作区随便对比；隔离运行器复制当前配置与现有 `Tools/Luban` 工具快照，
适配脚本固定的仓库相对路径，不安装或重建工具。
`luban test` 在 runs 内运行这两种真实导表；apply 先取得本次源数据的隔离导出，再逐文件比对生产产物的路径与哈希，
不接受旧文件、错误模式或不完整产物冒充成功。
二进制兼容性要验证代码/数据组合。托管表缓存与 YooAsset 引用有不同归属；当前加载器模板缺少 TextAsset 配对归还，
不因文档整理而声称已修复。

HTML：本地被动 UI-DSL -> 浏览器烘焙（等待字体和图片）-> JSON v1/v2 -> CLI 预检 -> 授权 -> PreviewScene 生成 Prefab -> 引用/序列化/布局测试。
仅要求 HTML/JSON 时到烘焙阶段交付，不强制连接 Editor。图片必须处于已允许的源目录或项目资产范围。
图像导入可能修改 importer；同路径重跑保持 GUID，但会覆盖 Prefab 内容。失败报告可能写入路径，不自动全局清理。
`ui test` 验证浏览器三端尺寸、字体/图片加载、零值及重复生成确定性。默认 TMP 模式需要已配置字体资源；
明确选择 UGUI Text 时使用 `ui preview --legacy-text`，不自动改用另一种文字系统。
Prefab 不包含临时 Canvas，按返回的 canvasIntegration 设置宿主 CanvasScaler。`safeArea` 只是布局意图，不会生成安全区组件。
Unity 字体、PC/mobile/pad 布局、安全区、触摸与视觉仍需实际 Editor/设备验收，不能用浏览器截图代替。

## 诊断与性能

优先注册的日志、状态和进度查询。截图、Profiler、内存快照、Player 和 MethodReload 有各自的状态与副作用，
详见[诊断与运行时](../UnityProject/.codex/skills/unity-cli/references/diagnostics.md)。
`capture game_view` 当前默认可读取 screen，包括运行时 Overlay UI；需检查 source/message，
不能用回退相机截图证明 HUD 已显示。输出尺寸也不等于真实多分辨率布局。
Profiler 先确认其当前连接目标；录制/快照核对 ID、target、path、终态及文件，
避免把其他 Player 的采样当成本工程。MethodReload 不替代完整编译或 HybridCLR 发布验证。

## CI 与证据

仓库当前未配置由本工作流管理的 CI 作业。接入 CI 时调用相同非交互命令并保留退出码；
不要使用 `|| true` 或空测试集使门禁变绿。本文描述可复用入口，不声称已经部署 CI。
运行目录 `.codex/runs/<run-id>` 已忽略提交，保存脱敏 JSON/Markdown、命令输出及截图。
将所需运行目录作为 CI 受控 artifact 保留即可，工作流不会上传。分享前仍检查个人路径与业务数据。

| 状态 | 含义 | 退出码 |
|---|---|---|
| passed | 本次选定必需阶段都有成功证据 | 0 |
| failed | 命令/业务/测试或结构检查失败 | 1 |
| blocked | 环境、授权、连接或必需阶段缺失 | 2 |
| skipped | 未运行的步骤，必须有原因 | 必需步骤跳过时整体 2 |

```powershell
python .codex/scripts/workflow.py report --run <run-directory>
```

report 仅重排已执行证据，不补写成功。中断报告保持 blocked。
构建、测试通过也不等于视觉/发布验收完成；交付列明实际门禁、未运行项、用户既有变更是否保留。
技能行为回归见[场景集](../UnityProject/.codex/evals/scenarios.json)：检查真实工具选择和产物，不能用关键词命中代替正确性。
评估操作和判定见[评估规程](../UnityProject/.codex/evals/evaluation.md)。

## 维护验收

技能保持精简入口和按任务加载的 references，框架生命周期、Pipeline 参数和业务 API 分别维护在各自权威位置。
修改后至少执行 check；Python 适配器用可观察结果测试，复杂技能再做独立、只读的真实任务评估。
版本变化时重新发现本次所需命令并更新契约，不积累另一份过时命令大全。
在线资料可以辅助设计；未能联网或资料不匹配版本时如实记录，不把未获取的页面列为已核验依据。

本次已读取 [OpenAI 官方技能编写指南](https://learn.chatgpt.com/docs/build-skills)（2026-11-09）：
技能描述明确触发范围，入口保持聚焦，详细规程按需加载，并用真实任务检查触发与行为。
这里只采用技能组织原则；该页面不作为本项目 `.codex/skills` 自动发现、Unity API 或 Pipeline 功能的保证。
