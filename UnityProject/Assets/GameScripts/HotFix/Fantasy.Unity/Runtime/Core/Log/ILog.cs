#if FANTASY_NET
using Fantasy.Platform.Net;
#endif
namespace Fantasy
{
    /// <summary>
    /// 定义日志记录功能的接口。
    /// </summary>
    public partial interface ILog
    {
#if FANTASY_NET
        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="processMode"></param>
        void Initialize(ProcessMode processMode);
#endif
        /// <summary>
        /// 记录跟踪级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        void Trace(string message);
        /// <summary>
        /// 记录警告级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        void Warning(string message);
        /// <summary>
        /// 记录信息级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        void Info(string message);
        /// <summary>
        /// 记录调试级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        void Debug(string message);
        /// <summary>
        /// 记录错误级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息。</param>
        void Error(string message);
        /// <summary>
        /// 记录跟踪级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        void Trace(string message, params object[] args);
        /// <summary>
        /// 记录警告级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        void Warning(string message, params object[] args);
        /// <summary>
        /// 记录信息级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        void Info(string message, params object[] args);
        /// <summary>
        /// 记录调试级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        void Debug(string message, params object[] args);
        /// <summary>
        /// 记录错误级别的格式化日志消息。
        /// </summary>
        /// <param name="message">日志消息模板。</param>
        /// <param name="args">格式化参数。</param>
        void Error(string message, params object[] args);
    }
}