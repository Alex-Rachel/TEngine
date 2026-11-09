# 连接、发现与执行

所有示例从 Unity 项目根执行；`<...>` 是需要替换的值。
包装器实现见 [workflow.py](../../../scripts/workflow.py)、
[unity.py](../../../scripts/workflow_lib/unity.py)。本地和 CI 使用相同入口，不自动安装或启动 Editor。

## 目标绑定

1. 读 `git status`、`ProjectVersion.txt`、包元数据和本次任务，保留已有修改。
2. `doctor` 记录环境与连接。CLI 的 `status --project-path` 使用子串筛选；
   工作流还会规范化并逐字核对返回的项目路径，拒绝多实例和相似路径。
3. 新编译、测试或写入还须读取 `editor status`，检查编译、重载、导入和 Play 状态是否允许该操作。
   Server `ready` 只是服务可接收请求，不代表后续编译已经结束。
4. 同项目编译、测试、导入和写入串行。状态轮询允许观察忙状态；每次重连仍绑定同一工程。

2022.3 没有完整的模态弹窗镜像。请求无响应可能需要维护者处理弹窗，不能自动点击保存、重置或关闭项目。
不读取或打印 `Library/Pipeline` 描述文件；CLI 负责发现、token 和传输选择。

## 发现与参数

```powershell
python .codex/scripts/workflow.py unity list
python .codex/scripts/workflow.py unity describe "component get_fields"
python .codex/scripts/workflow.py unity query "editor status"
python .codex/scripts/workflow.py unity query "scene list_open"
python .codex/scripts/workflow.py unity query "settings.player get"
```

`unity list` 返回 canonical 命令全名；`unity describe` 是项目包装器功能，
从 `pipeline list_commands` 的完整目录中精确选择 schema，并不是包内名为 describe 的命令。
首次按组探索时，可用原生 CLI 缩小目录：

```powershell
& ./Tools/unity.exe --json --non-interactive --no-log-proxy command --project-path . pipeline list_commands --detail groups
& ./Tools/unity.exe --json --non-interactive --no-log-proxy command --project-path . pipeline list_commands --detail compact --group prefab
& ./Tools/unity.exe --json --non-interactive --no-log-proxy command --project-path . pipeline list_commands --detail full --query "prefab save_contents"
```

`query` 匹配名称、说明和标签的子串，返回结果还需按 `command` 精确选择。
完整 schema 比复制的参数表可靠。`animation.animator` 是一个 group，不能拆成三个单词。
项目自定义 `tengine_bake_ugui`、`tengine_validate_assets` 位于 `global` 组，其短名是显式支持的项目便捷名。

包装器参数放入 JSON 文件，经 `--params-file` 传入。例如读取已确认 Prefab 的字段：

```json
{"target":{"path":"Assets/Actual.prefab"},"field":"m_Name","detail":"value"}
```

```powershell
python .codex/scripts/workflow.py unity query "component get_fields" --params-file request.json
```

示例资源需替换为实际查到的路径。按 schema 原样使用 `filter-type`、`include-explicit` 等键；
不要把项目命令的 `json_path` 和 `dry_run` 自动改成连字符。执行器的 `format/json/fields/verbose/omit-nulls`
以及目标选择参数不应作为业务参数混入。

## 两种执行入口

包装器提供已审核的查询与写入策略，不是任意命令透传器。被 query 拒绝表示尚未纳入只读策略，
不代表 Pipeline 没有这个能力。

未封装能力先查包文档、schema、实现及副作用。一次性任务可在完成同样的目标绑定、预览和实际授权后使用原生 CLI，
把准确 argv、结果和产物记入任务证据；要长期自动化的能力再加入策略和契约测试。
不能借原生 CLI、菜单、eval 或 shell 绕过授权或直接编辑 Scene/Prefab YAML。

列表命令可能输出 JSON 对象、数组或逐行 JSON；终端格式化可能让空列表没有输出。
空 stdout 不足以证明资源或用例存在。必须检查退出码、外层传输状态和内层业务结果。

## 预览与授权

```powershell
python .codex/scripts/workflow.py unity preview "settings.player set" --params-file request.json
python .codex/scripts/workflow.py approve --plan <preview.json> --by <approver> --reason <actual-confirmation> --high-risk
python .codex/scripts/workflow.py unity apply --plan <preview.json> --approval <approval.json>
```

先展示项目、目标、输入、覆盖/删除、保存范围和可恢复性。沿用会话中覆盖这些具体操作的有效授权；
缺少对应确认时才等待用户。approve 记录真实确认，不代替确认。
无 dry-run 的命令只能先做静态范围预览，并明确哪些检查没有运行。
包的新增对象命令未必带 confirm；没有参数门禁不代表获得了用户许可。

包装器审批绑定工作树、项目、命令、输入哈希，1 小时有效、一次尝试。
范围或输入变化需新预览；超时也消耗本次尝试。CI 的确认来自真实的人工批准阶段。
记录用于防误操作，不是抗恶意本地修改的权限系统。
部分命令调用全局 `AssetDatabase.SaveAssets`，确认范围必须包含可能保存的其他脏资源。

## 中断恢复

| 现象 | 下一步 |
|---|---|
| 本工程零实例、错误工程或多实例 | 记录 blocked，维护者打开/选定本工程，不借用其他项目 |
| 导入、编译、域重载 | 有限重试只读观察，核对项目与 Editor 身份，不重发起始操作 |
| 命令已发送后超时 | 结果未知；查对应状态、目标资源、GUID 和文件，再决定新操作 |
| 内层 error/failed/canceled 或不匹配结果 | 失败；保留证据，不能仅凭退出码 0 通过 |
| 发现零匹配测试 | 核对程序集和筛选器，不改跑无关测试 |
| 残留 editor.lock | 核对持有进程和在途操作，由维护者处理，不自动删锁 |

编译/测试详见 [验证与证据](verification.md)，内容写入见 [内容操作](authoring.md)。
