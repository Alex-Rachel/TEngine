using Fantasy.Platform.Net;
using NLog;

namespace Fantasy
{
    /// <summary>
    /// 使用 NLog 实现的日志记录器。
    /// </summary>
    public class NLog : ILog
    {
        private readonly Logger _logger; // NLog 日志记录器实例

        /// <summary>
        /// 初始化 NLog 实例。
        /// </summary>
        /// <param name="name">日志记录器的名称。</param>
        public NLog(string name)
        {
            // 获取指定名称的 NLog 日志记录器
            _logger = LogManager.GetLogger(name);
        }

        /// <summary>
        /// 初始化方法
        /// </summary>
        /// <param name="processMode"></param>
        public void Initialize(ProcessMode processMode)
        {
            // 非Benchmark模式、根据不同的运行模式来选择日志的方式
            switch (processMode)
            {
                case ProcessMode.Develop:
                {
                    LogManager.Configuration.RemoveRuleByName("ServerDebug");
                    LogManager.Configuration.RemoveRuleByName("ServerTrace");
                    LogManager.Configuration.RemoveRuleByName("ServerInfo");
                    LogManager.Configuration.RemoveRuleByName("ServerWarn");
                    LogManager.Configuration.RemoveRuleByName("ServerError");
                    break;
                }
                case ProcessMode.Release:
                {
                    LogManager.Configuration.RemoveRuleByName("ConsoleTrace");
                    LogManager.Configuration.RemoveRuleByName("ConsoleDebug");
                    LogManager.Configuration.RemoveRuleByName("ConsoleInfo");
                    LogManager.Configuration.RemoveRuleByName("ConsoleWarn");
                    LogManager.Configuration.RemoveRuleByName("ConsoleError");
                    break;
                }
            }
        }

        /// <summary>
        /// 记录跟踪级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        public void Trace(string message)
        {
            _logger.Trace(message);
        }

        /// <summary>
        /// 记录警告级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        public void Warning(string message)
        {
            _logger.Warn(message);
        }

        /// <summary>
        /// 记录信息级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        public void Info(string message)
        {
            _logger.Info(message);
        }

        /// <summary>
        /// 记录调试级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        public void Debug(string message)
        {
            _logger.Debug(message);
        }

        /// <summary>
        /// 记录错误级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        public void Error(string message)
        {
            _logger.Error(message);
        }

        /// <summary>
        /// 记录严重错误级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        public void Fatal(string message)
        {
            _logger.Fatal(message);
        }

        /// <summary>
        /// 记录跟踪级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        public void Trace(string message, params object[] args)
        {
            _logger.Trace(message, args);
        }

        /// <summary>
        /// 记录警告级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        public void Warning(string message, params object[] args)
        {
            _logger.Warn(message, args);
        }

        /// <summary>
        /// 记录信息级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        public void Info(string message, params object[] args)
        {
            _logger.Info(message, args);
        }

        /// <summary>
        /// 记录调试级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        public void Debug(string message, params object[] args)
        {
            _logger.Debug(message, args);
        }

        /// <summary>
        /// 记录错误级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        public void Error(string message, params object[] args)
        {
            _logger.Error(message, args);
        }

        /// <summary>
        /// 记录严重错误级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        public void Fatal(string message, params object[] args)
        {
            _logger.Fatal(message, args);
        }
    }
}