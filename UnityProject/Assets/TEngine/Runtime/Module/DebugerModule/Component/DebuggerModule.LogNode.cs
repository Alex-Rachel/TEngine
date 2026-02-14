using System;
using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        /// <summary>
        /// 日志记录结点。
        /// </summary>
        public sealed class LogNode : IMemory
        {
            private DateTime _logTime;
            private int _logFrameCount;
            private LogType _logType;
            private string _logMessage;
            private string _stackTrack;

            /// <summary>
            /// 初始化日志记录结点的新实例。
            /// </summary>
            public LogNode()
            {
                _logTime = default(DateTime);
                _logFrameCount = 0;
                _logType = LogType.Error;
                _logMessage = null;
                _stackTrack = null;
            }

            /// <summary>
            /// 获取日志时间。
            /// </summary>
            public DateTime LogTime
            {
                get
                {
                    return _logTime;
                }
            }

            /// <summary>
            /// 获取日志帧计数。
            /// </summary>
            public int LogFrameCount
            {
                get
                {
                    return _logFrameCount;
                }
            }

            /// <summary>
            /// 获取日志类型。
            /// </summary>
            public LogType LogType
            {
                get
                {
                    return _logType;
                }
            }

            /// <summary>
            /// 获取日志内容。
            /// </summary>
            public string LogMessage
            {
                get
                {
                    return _logMessage;
                }
            }

            /// <summary>
            /// 获取日志堆栈信息。
            /// </summary>
            public string StackTrack
            {
                get
                {
                    return _stackTrack;
                }
            }

            /// <summary>
            /// 创建日志记录结点。
            /// </summary>
            /// <param name="logType">日志类型。</param>
            /// <param name="logMessage">日志内容。</param>
            /// <param name="stackTrack">日志堆栈信息。</param>
            /// <returns>创建的日志记录结点。</returns>
            public static LogNode Create(LogType logType, string logMessage, string stackTrack)
            {
                LogNode logNode = MemoryPool.Acquire<LogNode>();
                logNode._logTime = DateTime.UtcNow;
                logNode._logFrameCount = Time.frameCount;
                logNode._logType = logType;
                logNode._logMessage = logMessage;
                logNode._stackTrack = stackTrack;
                return logNode;
            }

            /// <summary>
            /// 清理日志记录结点。
            /// </summary>
            public void Clear()
            {
                _logTime = default(DateTime);
                _logFrameCount = 0;
                _logType = LogType.Error;
                _logMessage = null;
                _stackTrack = null;
            }
        }
    }
}
