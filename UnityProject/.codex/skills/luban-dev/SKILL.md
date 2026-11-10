---
name: luban-dev
description: 维护本仓库 Luban 游戏配置的表、数据、枚举、Bean、模板、校验和普通/整表懒加载导出，核实分片需求的实际能力。普通应用设置、Unity ProjectSettings 或仅讨论游戏技能设计不触发。
---

# Luban 开发

## 定位与任务终点

先读 git status、表注册、字段、数据、引用和当前导出脚本，确定目标表/主键、
客户端/服务端分组、导出模式与兼容性。不要按字段关键词推断业务分组。
源位于仓库 `Configs/GameConfig`，从 Unity 项目出发为 `../Configs/GameConfig`；
命名空间为 `GameConfig`。修改生成 C#/bytes 时回溯源表或模板。

| 用户目标 | 执行到哪里 |
|---|---|
| 查表、解释结构或讨论方案 | 只读数据、实现和 helper 输出，不导出、不连接 Editor |
| 修改源记录/字段/模板 | 沿用明确的目标与授权编辑，检查引用和 diff，再隔离 validate |
| 更新生产生成产物 | 隔离 validate 后 preview，展示覆盖范围，取得确认再 approve/apply |
| 运行时加载问题 | 读真实加载器/生成类；组合 tengine-dev，实际运行验证再用 unity-cli |
| 按关卡/区间分片 | 先查 exporter、模板与生成 API，当前未实现，不能直接套 tags 或宣称完成 |

普通源修改不自动扩大到生产覆盖；删除/重命名需说明兼容性影响并取得对应确认。
不安装工具、不重建 Luban，不用普通导出掩盖分片要求。

## 校验与生产导出

1. 用 [helper](scripts/luban_helper.py) 的实际 `--help` 操作；多行 Bean 表头等复杂结构先核对实现，
   不使用会破坏格式的扁平 CRUD。缺依赖明确阻塞。
2. `python .codex/scripts/workflow.py luban validate` 在 runs 中复制配置与现有工具快照，
   用真实 Luban 校验并导出；helper 诊断仅为辅助，不写生产产物。
3. 需要生产导出时运行 `luban preview --mode lazyload` 或显式 `--mode standard`，
   展示配置输入、模板、生成 C# 和 bytes 的覆盖范围。
4. 在具体确认后记录 approve（high-risk），执行 `luban apply`；范围或输入改变则重新预览。
5. 核对实际产物、模板、模式和隔离输出哈希，随后按生成改动取得 .NET、Editor 及运行证据。

当前两个脚本均使用 `-d bin`。`lazyload` 增加整表懒加载模板，**不是分片导出**；
没有经源码证实的 `LoadTbXPartition/ReleaseTbXPartition/ClearPartitions` API。
分片是另一个需实现和验收的能力，详情见集成参考。
`luban test` 覆盖当前普通/整表懒加载的隔离导出，不代表分片或所有业务配置通过。

## 按需读取

| 任务 | 参考 |
|---|---|
| 项目导出、加载器和分片能力边界 | [tengine-integration.md](references/tengine-integration.md) |
| helper 参数、Excel 修改 | [operating-guide.md](references/operating-guide.md) |
| 类型和 Schema | [type-system.md](references/type-system.md)、[schema.md](references/schema.md) |
| 配置文件、生成参数 | [luban-conf.md](references/luban-conf.md)、[command-reference.md](references/command-reference.md) |
| 数据源 | [excel-format.md](references/excel-format.md)、[data-sources.md](references/data-sources.md) |
| 校验和运行时 | [validators.md](references/validators.md)、[runtime.md](references/runtime.md) |

配置源检查和隔离导出不必连接 Editor；实际生成 C#/资源后按项目
[unity-cli](../unity-cli/SKILL.md) 验证，框架接入读取 [tengine-dev](../tengine-dev/SKILL.md)。
交付数据/模板 diff、引用与兼容性、模式、日志、产物及实际验证范围。
失败保留输入和日志，不回滚用户已有数据，不将缺失产物或 Editor 离线写成通过。
