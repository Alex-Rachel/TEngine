# 运行时配置

本项目使用二进制与 YooAsset，不安装另一套 Luban.Runtime 或添加 Resources.Load 示例。
模板位于 `Configs/GameConfig/CustomTemplate`，输出为 GameProto 中的加载器及 GameConfig 类。

`ConfigSystem.Tables` 首次访问会 Load；它依赖 ResourceModule 已初始化。不能凭目录名假定某个启动步骤已调用 Load。
普通生成会在构造中加载表；当前懒加载模板在 Tables 属性首次访问时加载整张表并 ResolveRef。
它不是分片加载器，未提供分片查询、路由索引或缓存释放接口，能力边界见 [集成](tengine-integration.md)。
`GameProto` 不依赖 GameLogic，因此加载器使用 ModuleSystem 获取资源接口，而不是业务 GameModule。

当前 ConfigSystem 模板调用 `LoadAsset<TextAsset>`，未配对 `UnloadAsset`，这是现有集成限制。
若任务修改此处，需要确认 ByteBuf 是否拷贝/持有 bytes、卸载后数据存活、重复 Load 和引用解析边界，
修改模板再授权导出，不直接修生成文件。
懒加载属性会触发表加载与 ResolveRef，不能将它视为无副作用的缓存查询；验证空引用、相互引用和实际访问顺序。
热更前验证代码与 bytes 同版本；发布和缓存淘汰需要独立授权。
