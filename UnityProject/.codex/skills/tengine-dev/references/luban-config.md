# 配置集成路由

维护配置数据、类型、表注册、模板和导出请读取 [luban-dev](../../luban-dev/SKILL.md)，不要在此维护第二套命令。
配置源在仓库 `Configs/GameConfig`；生成目录在 Unity 的 `Assets/GameScripts/HotFix/GameProto/GameConfig`。

## 追溯源和产物

| 内容 | 维护入口 |
|---|---|
| 配置注册与目标 | [luban.conf](../../../../../Configs/GameConfig/luban.conf) 和其引用的 Defines/Datas |
| 运行时加载器 | [CustomTemplate/ConfigSystem.cs](../../../../../Configs/GameConfig/CustomTemplate/ConfigSystem.cs) |
| 普通导出 | [gen_code_bin_to_project.bat](../../../../../Configs/GameConfig/gen_code_bin_to_project.bat) |
| 懒加载模板选择 | [gen_code_bin_to_project_lazyload.bat](../../../../../Configs/GameConfig/gen_code_bin_to_project_lazyload.bat) |
| 表级懒加载实现 | [tables.sbn](../../../../../Configs/GameConfig/CustomTemplate/CustomTemplate_Client_LazyLoad/cs-bin/tables.sbn) |

批处理还复制 ConfigSystem/ExternalTypeUtil 到 GameProto 根目录；这些属于模板产物，不直接修补生成文件。
当前源码树可能没有 ConfigSystem 或表类，不能宣称已经导出、初始化或存在某个表成员。
GameProto 不能反向依赖 GameLogic.GameModule，当前加载器模板使用 `ModuleSystem.GetModule<IResourceModule>()`。

## 当前源码限制

ConfigSystem 模板同步加载 TextAsset 后未调用 UnloadAsset；需要修改所有权时回溯模板，
验证 ByteBuf 对底层字节的使用和重复加载，不用“缓存自动释放”的文档表述掩盖未释放引用。

当前直接选用的 `tables.sbn` 按表延迟构造，不包含 `ClearPartitions`。
更细的分片 API、缓存清理和索引能力以实际生成产物及 Luban 实现为准，不能从 lazyload 名称推断。
即使生成器提供托管缓存清理，也不等于归还 YooAsset 引用。

二进制读取依赖生成代码布局。新增、删除、改名或重排字段均需匹配代码/数据版本，
不能宣称新增字段天然前向兼容。普通、懒加载或分片模式的 API 应逐一确认，
不编造尚未生成的表名、加载或清理方法。

仅修改业务消费逻辑时读真实生成类即可；改变表结构、模板或导出模式时再进入 Luban 技能，
并按 [验证路由](verification.md) 检查生产代码与数据组合。
