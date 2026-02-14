#if FANTASY_NET
using CommandLine;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace Fantasy.Platform.Net;

/// <summary>
/// Process运行模式
/// </summary>
public enum ProcessMode
{
    /// <summary>
    /// 默认
    /// </summary>
    None =0,
    /// <summary>
    /// 开发模式
    /// </summary>
    Develop = 1,
    /// <summary>
    /// 发布模式
    /// </summary>
    Release = 2
}

internal sealed class CommandLineOptions
{
    /// <summary>
    /// 用于启动指定的进程，该进程的 ID 与 ProcessConfig 的 ID 相关联。此参数只能传递单个 ID，不支持传递多个 ID。
    /// </summary>
    [Option("pid", Required = false, Default = (uint)0, HelpText = "Enter an ProcessIdId such as 1")]
    public uint ProcessId { get; set; }
    /// <summary>
    /// Process类型，获取或设置应用程序的类型。
    /// Game - 游戏服务器Process
    /// Robot - 机器人（暂未支持该功能）
    /// </summary>
    [Option('a', "ProcessType", Required = false, Default = "Game", HelpText = "Game")]
    public string ProcessType { get; set; }
    /// <summary>
    /// 服务器运行模式，获取或设置服务器的运行模式。
    /// Develop - 开发模式（启动Process配置表中的所有Process）
    /// Release - 发布模式（根据ProcessId启动Process）
    /// </summary>
    [Option('m', "RuntimeMode", Required = true, Default = "Release", HelpText = "Develop:启动Process配置表中的所有Process,\nRelease:根据ProcessId启动Process")]
    public string RuntimeMode { get; set; }
    /// <summary>
    /// 启动组。
    /// </summary>
    [Option('g', "StartupGroup", Required = false, Default = 0, HelpText = "Used to start a group of Process")]
    public int StartupGroup { get; set; }
}
#endif