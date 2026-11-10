# 诊断、截图与 Player

## 日志和状态

优先 `editor status`、`console get`、`pipeline progress` 和对应操作的 status。
按 schema 限定日志级别、会话和数量，记录实际异常及其调用栈；不先删除 Library 或清控制台来制造干净结果。
性能问题先获取可复现步骤与目标身份，再决定采样；一次查询不证明峰值或长期泄漏。
查询命令也应审核实际副作用，未知命令不能靠 get/status 前缀放行。

## 图像证据

先读当前 [capture 文档](../../../../Packages/com.unity.pipeline/Documentation~/commands/capture.md)和 schema。
当前 `capture game_view` 默认 `source=screen`，已能读取 Game view 的画面；
不能沿用旧版“所有截图都只是相机 RenderTexture”的断言。

| 来源 | 解释证据时必须记录 |
|---|---|
| game_view source=screen | 实际 Game/Simulator 画面；Play Mode 可包括 Overlay UI。无窗口时可能打开 Game view，首帧未就绪可能回退相机 |
| game_view source=camera | 相机离屏渲染；Overlay 另依赖 SRP、overlay 参数与版本，Built-in 有限制 |
| scene_view source=camera | 场景相机画面，无窗口工具栏和 gizmos |
| scene_view source=screen | 窗口实际画面，尺寸取窗口，batchmode 不支持 |
| editor_window | 已打开的匹配窗口；可能临时切前后台 tab，不能当纯无副作用读取 |
| UITK element | Unity 6000.7+，不能要求本项目 2022.3 提供 |

检查返回的 source/message/savedPath/尺寸；如果本次依赖 Overlay UI，明确选择 screen 并确认处于正确 Play 状态，
不能接受悄悄回退的相机图。输出 width/height 可能只是缩放，不证明相应屏幕尺寸的真实布局。
窗口和相机/Play 状态修改也在预览授权范围内。

保存参数为 `output-path`。优先 `.codex/runs/<run-id>/` 下明确路径；
写入 Assets 会触发导入和 meta，不应为了证据污染资源目录。
指定 output-path 默认只返回路径，按需 include-inline-image；文件应实际存在且图像可查看。
截图未加入包装器 query 白名单；使用原生 CLI 时先按[操作规程](operations.md)审核与记录。
浏览器、Unity 和设备截图分别留证；字体字形、安全区、触摸与人工视觉判断不能互相替代。

## Profiler 与内存

命令和字段见 [profiling-and-memory](../../../../Packages/com.unity.pipeline/Documentation~/commands/profiling-and-memory.md)。
`diagnostics profiler_targets` 先确认当前目标；Profiler 连接可以指向其他 Player，不能只根据当前 Editor 推断采样对象。
切换连接、清录制历史、录制、GC 与快照会改变临时状态或写大文件，先说明目标、参数、成本和输出范围。

录制按启动 captureId、target、path 关联状态；快照按 snapshotId 和实际响应字段关联。
分别轮询 profiler_record_status/memory_snapshot_status，核对 completed、文件大小和实际产物。
failed/interrupted 不通过；域重载会中断。录制 framesRecorded 与 framesSaved 可能不同，不能声称文件保留全部帧。
快照超时后底层操作可能仍在运行，先观察，不能立即启动第二轮。
不自动修改 Deep Profile、后台运行、auto tick 或项目设置来凑结果。

## Player 连接

先读 [Runtime setup](../../../../Packages/com.unity.pipeline/Documentation~/runtime-setup.md)。
连接已存在的桌面 Development Player 不等于获准启用 Runtime Pipeline 或构建 Player；
开关会改变编译 define，发布版本默认不得附带开启。

```powershell
python .codex/scripts/workflow.py unity runtime-status --runtime-path <absolute-player-directory>
& ./Tools/unity.exe --json --non-interactive --no-log-proxy command --runtime-path <absolute-player-directory> runtime status
```

Windows/Linux 传可执行文件旁目录；macOS 传 `.app` 根。目标选择参数放在 command 后、group 前。
包装器记录显式路径与实际 RuntimeStatusResponse 身份；当前 response 不包含 workingDirectory，
路径绑定来自 CLI `--runtime-path`，不能伪称已核对不存在的 Player 自报字段。
CLI 顶层 `status` 当前是 Editor 列表，不支持 `--runtime-path`。
Player 测试不代替 Editor 资源/编译验证，不读取或分享运行时鉴权描述文件。

## eval、run 与 MethodReload

优先已注册的查询/内容命令。确需代码执行时先审核具体代码、输入、调用、状态及写入范围，
源码仍用编辑工具写到可审查文件，不把长段脚本塞入 shell 字符串。
[`script eval/run`](../../../../Packages/com.unity.pipeline/Documentation~/commands/scripts.md)
不是受限查询；dry-run 主要证明编译，不证明运行正确或写入可撤销。
超时后异步脚本可能继续生效；不能自动重发。当前 CLI help 未证明支持的 `--detach/unity job` 不直接照抄。

[`MethodReload`](../../../../Packages/com.unity.pipeline/Documentation~/method-reload.md)
只替换已标记并织入的方法；不保证新增字段、程序集/资源/序列化结构已更新。
Assembly.Load 和解释器后端约束不同；watcher 会改变 Auto Refresh 并可能将下一次保存推送给连接的 Player。
先确认 backend、目标与 watcher 状态，不能为一般 TEngine 异步业务强加不支持其语言特性的标记。
方法重载成功不等于完整 Editor 编译、HybridCLR/AOT 或发布验收。
