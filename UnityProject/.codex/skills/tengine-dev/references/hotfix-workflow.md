# 热更开发与验证

先读 [程序集边界](architecture.md)、
[UpdateSetting](../../../../Assets/TEngine/Runtime/Core/UpdateSetting.cs)、
[ProcedureLoadAssembly](../../../../Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs)，
并核对 [HybridCLRSettings](../../../../ProjectSettings/HybridCLRSettings.asset) 与当前资源收集规则。
序列化值、定义符和实际平台通过 [unity-cli](../../unity-cli/SKILL.md) 核实，不把源码默认值当成当前设置。

## 开发决策

1. 确定代码属于主包还是热更程序集，检查 asmdef 引用和条件编译；主包启动不能依赖尚未加载的业务类型。
2. 修改入口或加载器时保持 `GameApp.Entrance(object[])` 调用约定，接口事件初始化先于接口使用。
3. 修改生成/保护流程时先读 [BuildDLLCommand](../../../../Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs) 及其实际调用方。
   工作区已有混淆/加密改动须保留，不按旧文档覆盖或重新生成。
4. 按 [验证路由](verification.md) 取得 .NET、Editor 编译及相关测试证据。
   涉及泛型、反射、裁剪、序列化或原生接口时，明确记录目标平台还需要验证什么。

## 运行证据与发布边界

下载流程需逐步核实版本请求、清单、下载器的状态和错误；等待 Task 完成不等于成功。
[ResourceModule](../../../../Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs) 和 Procedure 的实际失败分支
才是诊断依据，不能从一条入口成功日志推断所有程序集和资源已验证。

HybridCLR 生成、平台切换、资源打包、Player Build、签名和部署均需对应的额外授权。
普通代码任务不包含清缓存、升级 HybridCLR 或全量重导入；CI 预检成功也不产生发布许可。

AOT/IL2CPP 支持范围以本项目安装包和目标平台为准，不笼统宣称某类 C# 特性永远不支持。
Editor 测试不能替代真机热更。交付记录主包/热更代码与资源版本配对、目标平台和实际运行结果，
没有设备或构建授权时把对应检查列为未执行。
