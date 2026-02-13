#if FANTASY_NET
using System;
using Fantasy.Platform.Net;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy;

/// <summary>
/// 标准的控制台Log
/// </summary>
public sealed class ConsoleLog : ILog
{
    /// <summary>
    /// 初始化方法
    /// </summary>
    /// <param name="processMode"></param>
    public void Initialize(ProcessMode processMode) { }

    /// <summary>
    /// 记录跟踪级别的日志消息。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Trace(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
    }

    /// <summary>
    /// 记录警告级别的日志消息。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
    }

    /// <summary>
    /// 记录信息级别的日志消息。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message);
    }

    /// <summary>
    /// 记录调试级别的日志消息。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Debug(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine(message);
    }

    /// <summary>
    /// 记录错误级别的日志消息。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(message);
    }

    /// <summary>
    /// 记录严重错误级别的日志消息。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Fatal(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
    }

    /// <summary>
    /// 记录跟踪级别的格式化日志消息。
    /// </summary>
    /// <param name="message">日志消息模板。</param>
    /// <param name="args">格式化参数。</param>
    public void Trace(string message, params object[] args)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message, args);
    }

    /// <summary>
    /// 记录警告级别的格式化日志消息。
    /// </summary>
    /// <param name="message">日志消息模板。</param>
    /// <param name="args">格式化参数。</param>
    public void Warning(string message, params object[] args)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message, args);
    }

    /// <summary>
    /// 记录信息级别的格式化日志消息。
    /// </summary>
    /// <param name="message">日志消息模板。</param>
    /// <param name="args">格式化参数。</param>
    public void Info(string message, params object[] args)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message, args);
    }

    /// <summary>
    /// 记录调试级别的格式化日志消息。
    /// </summary>
    /// <param name="message">日志消息模板。</param>
    /// <param name="args">格式化参数。</param>
    public void Debug(string message, params object[] args)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine(message, args);
    }

    /// <summary>
    /// 记录错误级别的格式化日志消息。
    /// </summary>
    /// <param name="message">日志消息模板。</param>
    /// <param name="args">格式化参数。</param>
    public void Error(string message, params object[] args)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(message, args);
    }

    /// <summary>
    /// 记录严重错误级别的格式化日志消息。
    /// </summary>
    /// <param name="message">日志消息模板。</param>
    /// <param name="args">格式化参数。</param>
    public void Fatal(string message, params object[] args)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message, args);
    }
}
#endif