using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using System.Text;

namespace YooAsset.Editor
{
    internal static class BuildLogger
    {
        private const int MAX_LOG_BUFFER_SIZE = 1024 * 1024 * 2; //2MB

        private static bool _enableLog = true;
        private static string _logFilePath;

        private static readonly object _lockObj = new object();
        private static readonly StringBuilder _logBuilder = new StringBuilder(MAX_LOG_BUFFER_SIZE);

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        public static void InitLogger(bool enableLog, string logFilePath)
        {
            _enableLog = enableLog;
            _logFilePath = logFilePath;
            _logBuilder.Clear();

            if (_enableLog)
            {
                if (string.IsNullOrEmpty(_logFilePath))
                    throw new Exception("Log file path is null or empty !");

                Debug.Log($"Logger initialized at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
        }

        /// <summary>
        /// 关闭日志系统
        /// </summary>
        public static void Shuntdown()
        {
            if (_enableLog)
            {
                lock (_lockObj)
                {
                    try
                    {
                        if (File.Exists(_logFilePath))
                            File.Delete(_logFilePath);

                        FileUtility.CreateFileDirectory(_logFilePath);
                        File.WriteAllText(_logFilePath, _logBuilder.ToString(), Encoding.UTF8);
                        _logBuilder.Clear();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to write log file: {ex.Message}");
                    }
                }
            }
        }

        public static void Log(string message)
        {
            if (_enableLog)
            {
                WriteLog("INFO", message);
                Debug.Log(message);
            }
        }
        public static void Warning(string message)
        {
            if (_enableLog)
            {
                WriteLog("WARN", message);
                Debug.LogWarning(message);
            }
        }
        public static void Error(string message)
        {
            if (_enableLog)
            {
                WriteLog("ERROR", message);
                Debug.LogError(message);
            }
        }
        public static string GetErrorMessage(ErrorCode code, string message)
        {
            return $"[ErrorCode{(int)code}] {message}";
        }

        private static void WriteLog(string level, string message)
        {
            lock (_lockObj)
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                _logBuilder.AppendLine(logEntry);
            }
        }
    }
}