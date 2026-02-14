// ================================================================================
// Fantasy.Net 服务器应用程序入口
// ================================================================================
// 本文件是 Fantasy.Net 分布式游戏服务器的主入口点
//
// 初始化流程：
//   1. 强制加载引用程序集，触发 ModuleInitializer 执行
//   2. 配置日志基础设施（NLog）
//   3. 启动 Fantasy.Net 框架
// ================================================================================

using Fantasy;

try
{
    // 初始化引用的程序集，确保 ModuleInitializer 执行
    // .NET 采用延迟加载机制 - 仅当类型被引用时才加载程序集
    // 通过访问 AssemblyMarker 强制加载程序集并调用 ModuleInitializer
    // 注意：Native AOT 不存在延迟加载问题，所有程序集在编译时打包
    AssemblyHelper.Initialize();
    // 配置 NLog 日志基础设施
    var logger = new Fantasy.NLog("Server");
    // 使用 NLog 日志系统启动 Fantasy.Net 框架
    await Fantasy.Platform.Net.Entry.Start(logger);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"服务器初始化过程中发生致命错误：{ex}");
    Environment.Exit(1);
}
