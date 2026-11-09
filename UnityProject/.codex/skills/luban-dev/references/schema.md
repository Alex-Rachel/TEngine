# Schema 维护

本项目混用 Defines XML 与 `Datas/__tables__.xlsx`、`__beans__.xlsx`、`__enums__.xlsx`。
先确定类型是否已在另一来源定义，避免重复定义和命名空间漂移。

最小 XML 示例见 [tables.xml](../examples/regression/Defines/tables.xml)：
module -> bean/enum/table；bean 的 var 声明字段，table 指定 value/input/mode/index。
map 需要唯一主键；list 的复合索引以实际生成的访问 API 为准；不要把嵌套 Bean 内字段直接当顶层索引。
非 Excel 多记录输入必须表达多记录选择，JSON 示例为 `*@rows.json`（选择器在 @ 前）。
fixture 中的 partition 标签是待实现分片需求，当前项目脚本只生成普通 bin；详见 [集成边界](tengine-integration.md)。

Excel 定义列名和实际位置先从现有注册表读取，不按旧文档的 `read_mode`、`cfg.*` 表头重建。
继承、多态、mapper、refgroup 等高级规则先检查已安装版本和现有示例，再用隔离数据生成。
示例类型不是业务 API；生成后核对 namespace、字段名、构造器与 Tables 暴露成员。
