# 内容操作与项目命令

先读本次涉及的包内[资源](../../../../Packages/com.unity.pipeline/Documentation~/commands/assets-and-files.md)、
[对象组件](../../../../Packages/com.unity.pipeline/Documentation~/commands/gameobjects-and-components.md)、
[Prefab](../../../../Packages/com.unity.pipeline/Documentation~/commands/prefabs.md)或
[场景](../../../../Packages/com.unity.pipeline/Documentation~/commands/scenes.md)参考，再取得目标命令的实时 schema。
普通源码通过代码编辑工具修改，Unity 对象与序列化通过 Pipeline。

## 找到目标再写入

使用 `asset find`、`gameobject find`、`scene get_hierarchy`、`component get_fields` 等查询确认目标。
资产优先 path/GUID/globalId，子资产还需 fileId；场景对象可用 globalId 或本次取得的 instanceId。
instanceId 不跨重载和重新加载保留，hierarchyPath 也可能重名；目标变化后重新定位。
`ObjectRef` 提供多种输入形式不意味着多个条件会联合校验，发送一个已核实的明确标识。

预览至少列出对象/资产、字段与旧新值、父子关系、输入、写盘路径、覆盖与保存范围。
场景当前是否脏、Prefab 是否有 overrides、资产是否已有用户修改会影响写入范围。
未得到对应授权前保留查询与静态预览；不自动 SaveAll 或关闭用户场景。

| 需求 | Pipeline 路径及验收重点 |
|---|---|
| 对象、Transform、组件、SerializedProperty | create/set/add 后重新查询身份、层级和字段；核对 Undo 支持 |
| 资源导入、移动和 importer | 通过 AssetDatabase 命令，核对路径/GUID、importer 前后值和依赖；不手改 meta |
| Prefab 实例或变体 | 先确认源资产与实例，写后核对引用和 overrides |
| 编辑 Prefab 内容 | 优先 prefab save_contents 的隔离内容流程；不可把保存 Prefab 误解成保存当前场景 |
| 场景打开、保存、构建列表 | 分别确认是否更换场景、保存哪些脏内容、改哪条构建记录 |
| HTML/UI-DSL 生成 | 使用 [html-to-ugui](../../html-to-ugui/SKILL.md) 与项目专用预览，避免手搭重复转换器 |

写后验证实际序列化对象与持久化结果，不能只用返回的对象数量当验收。
当前场景资产校验覆盖有限，见[验证规程](verification.md)。

## dry-run、路径和 Undo

[`confirm/dry-run`](../../../../Packages/com.unity.pipeline/Documentation~/safety-and-mutations.md)
由每个命令实现，dispatcher 不统一强制。新增对象等 additive 命令可能直接生效。
先验证全部输入再写入；支持 dry-run 时核对它确实不产生目标写入。

`ProjectPaths.Resolve` 约束 authoring 资产路径，默认根为 Assets；
截图、脚本、构建及报告等文件接口可能接受工程外绝对路径。
authoring root 不是全系统安全沙箱，也不能替代明确目标路径。
更改 root 本身会影响后续操作，不能为了省事自动扩大。

场景对象修改只有在命令正确登记 Undo 后才可恢复。
AssetDatabase、Prefab 写盘、设置、包、构建和部分烘焙不能靠 Ctrl+Z 普遍撤销。
失败后先核对已写路径和真实状态，不能自动删除产物或回滚用户改动。

## 批量操作与等待

跨对象的重复 CRUD 可考虑 [`job batch`](../../../../Packages/com.unity.pipeline/Documentation~/commands/batch.md)，
先逐项审核所有 command/params 及跨操作引用，再预览和授权整个范围。
默认 transactional 仅覆盖可由 Undo 恢复的操作；资产写盘、保存和设置等受限制。
`on-error=continue` 会关闭事务。dry-run 校验命令、参数和引用拓扑，不等于已经验证目标存在与业务状态。
最终核对每项 success/error/skipped、applied/reverted 和真实对象，不只看批处理外层成功。

[`job wait`](../../../../Packages/com.unity.pipeline/Documentation~/commands/wait.md) 适合已明确字段的状态等待。
优先 async，保存 waitId 并核对终态；同步等待会占用命令队列，不能等待尚未发出的操作。
`on-met` 可以截图或暂停，因此带动作的 wait 不是纯查询。
`state=met` 仍需检查 onMetError。等待、detached job 和测试状态是不同契约，不能混用 ID 或终态。

## 扩展项目命令

已有命令足够时直接使用。重复需要的项目操作才新增薄适配器，业务逻辑放到可测试服务，
不要修改 `com.unity.pipeline` 或引入另一套连接服务。

先读[命令声明](../../../../Packages/com.unity.pipeline/Documentation~/creating-commands.md)和
[authoring API](../../../../Packages/com.unity.pipeline/Documentation~/authoring-commands.md)：

- 使用明确的项目组名与 `[CliGroupCommand]`，避免与包命令冲突；现有 global 命令按源码保留。
- asmdef 按实际使用引用 `Unity.Pipeline.Attributes` 或所需的其他程序集，核实平台与 defineConstraints。
- Unity API 默认主线程。后台状态读取必须线程安全；`MainThreadRequired=false` 不保证旁路队列。
- 输入先解析 ObjectRef、路径和 DTO。资产路径使用适当的 ProjectPaths API，不能字符串拼接绕过范围。
- destructive/overwrite 实现 confirm 与 dry-run；场景对象记录 Undo，写盘明确不可完全 Undo。
- 返回领域结果/AuthoringResult，失败抛异常或返回明确失败模型，不把内部失败包装成成功字符串。
- 新命令编译后重新发现 schema；测试包括参数绑定、真实行为和中断后状态，不能只查方法存在。

项目命令进入工作流白名单前补对应返回/轮询契约和测试。
`script eval/run`、菜单和 MethodReload 是代码执行或状态修改入口，不能作为任意透传兜底；
具体限制见[诊断与运行时](diagnostics.md)。
