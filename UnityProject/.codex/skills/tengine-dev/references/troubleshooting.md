# 故障定位

| 现象 | 最小诊断证据 |
|---|---|
| 编译失败 | 完整解决方案输出、实际编译输入、Editor 编译终态；新增文件是否已导入 |
| Editor 不可连接 | doctor 报告、明确项目路径和实例状态；不读取鉴权描述文件内容 |
| UI 空白 | 资源地址、节点组件、Canvas/RectTransform、字体和图片引用、截图 |
| await 返回后 UI 不可用 | IsPrepare、面板对象、实际加载错误；不能把 ShowUIAsyncAwait 返回当成功 |
| 事件不触发 | 注册/发送 ID 与参数类型、Init、窗口隐藏/销毁状态和回调次数 |
| 内存增长 | 成功获取/归还次数、刷新竞争、池引用和实际 Profiler 数据；先区分托管缓存和资源句柄 |
| 配置失败 | 表注册、字段类型、引用、导出返回码和产物模式 |
| 真机热更失败 | 主包/热更版本配对、目标平台、AOT/裁剪、完整堆栈 |

源码入口见 [窗口生命周期](ui-lifecycle.md)、[资源 API](resource-api.md)、[事件系统](event-system.md)
和 [配置集成](luban-config.md)；优先沿实际失败调用链定位，不以旧文档中的方法名作为存在证明。

保留失败现场；不要默认删除 Library、persistentDataPath、缓存、包锁或重置用户场景。
没有连接时静态定位可继续，必需 Editor 验收标 blocked。Unity Pipeline 是当前项目 Editor/Player 证据入口，
连接、能力发现、异步任务和诊断操作按 [unity-cli](../../unity-cli/SKILL.md) 及其 [操作规程](../../unity-cli/references/operations.md) 执行。

查询不到的状态不能推断为正常；写入超时先核对实际结果，不自动重放。
全局强制卸载、缓存清空、平台切换和真实构建需单独说明目标并取得授权。
多阶段诊断写入当前任务的一份记录；小改交付摘要，不另建长期记忆体系。

修复后重现原触发条件，核对用户可观察结果，并按 [verification.md](verification.md) 选择受影响门禁。
只有截图、无 Console 错误或出现某个方法名，都不能独立证明缺陷已解决。
