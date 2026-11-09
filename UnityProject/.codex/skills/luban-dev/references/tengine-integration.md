# TEngine 集成与导出

权威输入：[luban.conf](../../../../../Configs/GameConfig/luban.conf)、
[加载器模板](../../../../../Configs/GameConfig/CustomTemplate/ConfigSystem.cs)、
[懒加载模板](../../../../../Configs/GameConfig/CustomTemplate/CustomTemplate_Client_LazyLoad/cs-bin/tables.sbn)。
路径从 Unity 项目出发为 `../Configs/GameConfig`。

| 模式 | 现有脚本 | 产物 |
|---|---|---|
| lazyload | gen_code_bin_to_project_lazyload.bat/.sh | cs-bin + bin，Tables 属性首次访问时加载整表 |
| standard | gen_code_bin_to_project.bat/.sh | cs-bin + bin，标准 Tables |

两者都写 `Assets/GameScripts/HotFix/GameProto` 与 `Assets/AssetRaw/Configs/bytes`。
生产导出会复制加载器模板并更新生成目录，必须预览写入范围后取得覆盖授权。
普通源数据修改沿用用户已明确的目标和输入；只有范围不清、删除或兼容性选择未定时补充确认。

## 校验与生产写入

从 Unity 项目执行：

```powershell
python .codex/scripts/workflow.py luban validate
python .codex/scripts/workflow.py luban test
python .codex/scripts/workflow.py luban preview --mode lazyload
```

`validate` 按默认 lazyload 模式对当前配置源在 runs 隔离目录生成，`test` 使用最小 fixture；二者都不写生产产物。
当前脚本按 `../../Tools/Luban/Luban.dll` 寻址，会重设 `LUBAN_DLL`，仅传环境变量不能替代该目录。
隔离运行器须复制当前本地 `Tools/Luban` 工具快照，保持 `Configs/GameConfig`、`Tools/Luban`、
`UnityProject/Assets` 的相对结构；不得重新构建、下载或升级工具。执行目录和返回码以实际报告为准。
Windows 使用受控脚本名及参数数组调用 `cmd.exe /d /c`，不要套用 Git Bash 的 `cmd //c`。

审查 preview 的具体输入与目标，取得真实确认后，使用其返回的计划路径执行：

```powershell
python .codex/scripts/workflow.py approve --plan <preview.json> --by <reviewer> --reason <confirmation> --high-risk
python .codex/scripts/workflow.py luban apply --plan <preview.json> --approval <approval.json>
```

尖括号表示实际输出或确认记录，不能原样执行。只要求查表或隔离校验的任务在对应阶段结束。
apply 后检查生成文件、模板复制、与本次隔离结果的差异，再验证 .NET 编译和相关 Unity 加载行为。

## 分片能力边界

当前项目脚本和懒加载模板未实现分片导出及分片缓存 API。不要调用不存在的
`LoadTbXPartition`、`ReleaseTbXPartition` 或 `ClearPartitions`，也不要把懒加载解释为按分片加载。
[fixture](../examples/regression/Defines/tables.xml) 中的 `partition=field/range/count` 标签
保留了待实现需求；标签存在或生成器接受输入，不证明生成了分片、路由索引或释放接口。

任务确实要求分片时，先核实本地生成器、exporter、模板和运行时集成，再单独界定实施与兼容性验收。
不能以现有普通 bin 产物宣称分片通过，也不能为完成验证临时切换生产模式。
cs-bin 按生成代码布局读行；新增字段也不能无条件宣称兼容旧客户端。
代码、bytes 和模板必须作为同版本产物验证。当前加载器的资源归属限制见 [运行时配置](runtime.md)。
