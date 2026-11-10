# 生成命令契约

生产导出只走 [项目脚本](tengine-integration.md)。以下为核验线索，不是自动安装或自组生产命令的许可。
从 Unity 项目运行 `dotnet ../Tools/Luban/Luban.dll --help` 查询本地版本的真实参数；
help 的返回码与生产导出分开判断，实际导出必须同时检查退出码与产物。

| 参数 | 本项目用法 |
|---|---|
| --conf | luban.conf，路径相对受控 cwd |
| -t | client；server 需独立范围 |
| -c | cs-bin |
| -d | 两种现有项目脚本均为 bin |
| --customTemplateDir | lazyload 的自定义模板目录 |
| -x | 现有脚本使用 outputCodeDir、outputDataDir、code.lineEnding |

`bin-sharded` 和 `dataExporter=sharded` 不在当前项目脚本中，不能当成已支持的参数组合。
业务需要新 exporter、variant、时区或验证开关时，先查询本地 help 并在隔离输入上验证，不凭旧文档拼装。
不要将 `-o` 当作通用输出目录，先核实当前工具对该参数的含义。
不要把仅生成代码、零输出或 watcher 启动成功当导表完成。
参数或版本变化后先在隔离 fixture 验证，更新入口契约与测试，不改第三方工具包。
