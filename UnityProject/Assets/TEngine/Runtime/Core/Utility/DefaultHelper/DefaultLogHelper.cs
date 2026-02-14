using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditorInternal;
#endif
using Debug = UnityEngine.Debug;

namespace TEngine
{
    /// <summary>
    /// 默认游戏框架日志辅助。
    /// </summary>
    public class DefaultLogHelper : GameFrameworkLog.ILogHelper
    {
        private enum ELogLevel
        {
            Info,
            Debug,
            Assert,
            Warning,
            Error,
            Exception,
        }

        private const ELogLevel FILTER_LEVEL = ELogLevel.Info;
        private static readonly StringBuilder _stringBuilder = new StringBuilder(1024);

        /// <summary>
        /// 打印游戏日志。
        /// </summary>
        /// <param name="level">游戏框架日志等级。</param>
        /// <param name="message">日志信息。</param>
        /// <exception cref="GameFrameworkException">游戏框架异常类。</exception>
        public void Log(GameFrameworkLogLevel level, object message)
        {
            switch (level)
            {
                case GameFrameworkLogLevel.Debug:
                    LogImp(ELogLevel.Debug, Utility.Text.Format("<color=#888888>{0}</color>", message));
                    break;

                case GameFrameworkLogLevel.Info:
                    LogImp(ELogLevel.Info, message.ToString());
                    break;

                case GameFrameworkLogLevel.Warning:
                    LogImp(ELogLevel.Warning, message.ToString());
                    break;

                case GameFrameworkLogLevel.Error:
                    LogImp(ELogLevel.Error, message.ToString());
                    break;

                case GameFrameworkLogLevel.Fatal:
                    LogImp(ELogLevel.Exception, message.ToString());
                    break;

                default:
                    throw new GameFrameworkException(message.ToString());
            }
        }

        /// <summary>
        /// 获取日志格式。
        /// </summary>
        /// <param name="eLogLevel">日志级别。</param>
        /// <param name="logString">日志字符。</param>
        /// <param name="bColor">是否使用颜色。</param>
        /// <returns>StringBuilder。</returns>
        private static StringBuilder GetFormatString(ELogLevel eLogLevel, string logString, bool bColor)
        {
            _stringBuilder.Clear();
            switch (eLogLevel)
            {
                case ELogLevel.Debug:
                    _stringBuilder.AppendFormat(
                        bColor
                            ? "<color=#CFCFCF><b>[Debug] ► </b></color> - <color=#00FF18>{0}</color>"
                            : "<color=#00FF18><b>[Debug] ► </b></color> - {0}",
                        logString);
                    break;
                case ELogLevel.Info:
                    _stringBuilder.AppendFormat(
                        bColor
                            ? "<color=#CFCFCF><b>[INFO] ► </b></color> - <color=#CFCFCF>{0}</color>"
                            : "<color=#CFCFCF><b>[INFO] ► </b></color> - {0}",
                        logString);
                    break;
                case ELogLevel.Assert:
                    _stringBuilder.AppendFormat(
                        bColor
                            ? "<color=#FF00BD><b>[ASSERT] ► </b></color> - <color=green>{0}</color>"
                            : "<color=#FF00BD><b>[ASSERT] ► </b></color> - {0}",
                        logString);
                    break;
                case ELogLevel.Warning:
                    _stringBuilder.AppendFormat(
                        bColor
                            ? "<color=#FF9400><b>[WARNING] ► </b></color> - <color=yellow>{0}</color>"
                            : "<color=#FF9400><b>[WARNING] ► </b></color> - {0}",
                        logString);
                    break;
                case ELogLevel.Error:
                    _stringBuilder.AppendFormat(
                        bColor
                            ? "<color=red><b>[ERROR] ► </b></color> - <color=red>{0}</color>"
                            : "<color=red><b>[ERROR] ► </b></color>- {0}",
                        logString);
                    break;
                case ELogLevel.Exception:
                    _stringBuilder.AppendFormat(
                        bColor
                            ? "<color=red><b>[EXCEPTION] ► </b></color> - <color=red>{0}</color>"
                            : "<color=red><b>[EXCEPTION] ► </b></color> - {0}",
                        logString);
                    break;
            }

            return _stringBuilder;
        }

        private static void LogImp(ELogLevel type, string logString)
        {
            if (type < FILTER_LEVEL)
            {
                return;
            }

            StringBuilder infoBuilder = GetFormatString(type, logString, true);
            string logStr = infoBuilder.ToString();

            //获取C#堆栈,Warning以上级别日志才获取堆栈
            if (type == ELogLevel.Error || type == ELogLevel.Warning || type == ELogLevel.Exception)
            {
                StackFrame[] stackFrames = new StackTrace().GetFrames();
                // ReSharper disable once PossibleNullReferenceException
                for (int i = 0; i < stackFrames.Length; i++)
                {
                    StackFrame frame = stackFrames[i];
                    // ReSharper disable once PossibleNullReferenceException
                    string declaringTypeName = frame.GetMethod().DeclaringType.FullName;
                    string methodName = stackFrames[i].GetMethod().Name;

                    infoBuilder.AppendFormat("[{0}::{1}\n", declaringTypeName, methodName);
                }
            }

            switch (type)
            {
                case ELogLevel.Info:
                case ELogLevel.Debug:
                    Debug.Log(logStr);
                    break;
                case ELogLevel.Warning:
                    Debug.LogWarning(logStr);
                    break;
                case ELogLevel.Assert:
                    Debug.LogAssertion(logStr);
                    break;
                case ELogLevel.Error:
                    Debug.LogError(logStr);
                    break;
                case ELogLevel.Exception:
                    throw new Exception(logStr);
            }
        }
    }
}

#if UNITY_EDITOR
namespace TEngine.Editor
{
    /// <summary>
    /// 日志重定向相关的实用函数。
    /// </summary>
    internal static class LogRedirection
    {
        [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            if (line <= 0)
            {
                return false;
            }
            // 获取资源路径
            string assetPath = AssetDatabase.GetAssetPath(instanceID);
            
            // 判断资源类型
            if (!assetPath.EndsWith(".cs"))
            {
                return false;
            }

            bool autoFirstMatch = assetPath.Contains("Logger.cs") ||
                                   assetPath.Contains("DefaultLogHelper.cs") ||
                                   assetPath.Contains("GameFrameworkLog.cs") ||
                                   assetPath.Contains("AssetsLogger.cs") ||
                                   assetPath.Contains("Log.cs");
            
            var stackTrace = GetStackTrace();
            if (!string.IsNullOrEmpty(stackTrace) && (stackTrace.Contains("[Debug]") || 
                                                      stackTrace.Contains("[INFO]") || 
                                                      stackTrace.Contains("[ASSERT]") ||
                                                      stackTrace.Contains("[WARNING]") ||
                                                      stackTrace.Contains("[ERROR]") ||
                                                      stackTrace.Contains("[EXCEPTION]")))
                                            
            {
                if (!autoFirstMatch)
                {
                    var fullPath = UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.LastIndexOf("Assets", StringComparison.Ordinal));
                    fullPath = $"{fullPath}{assetPath}";
                    // 跳转到目标代码的特定行
                    InternalEditorUtility.OpenFileAtLineExternal(fullPath.Replace('/', '\\'), line);
                    return true;
                }
                
                // 使用正则表达式匹配at的哪个脚本的哪一行
                var matches = Regex.Match(stackTrace, @"\(at (.+)\)",
                    RegexOptions.IgnoreCase);
                while (matches.Success)
                {
                    var pathLine = matches.Groups[1].Value;

                    if (!pathLine.Contains("Logger.cs") && 
                        !pathLine.Contains("DefaultLogHelper.cs") &&
                        !pathLine.Contains("GameFrameworkLog.cs") && 
                        !pathLine.Contains("AssetsLogger.cs") && 
                        !pathLine.Contains("Log.cs"))
                    {
                        var splitIndex = pathLine.LastIndexOf(":", StringComparison.Ordinal);
                        // 脚本路径
                        var path = pathLine.Substring(0, splitIndex);
                        // 行号
                        line = Convert.ToInt32(pathLine.Substring(splitIndex + 1));
                        var fullPath = UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.LastIndexOf("Assets", StringComparison.Ordinal));
                        fullPath = $"{fullPath}{path}";
                        // 跳转到目标代码的特定行
                        InternalEditorUtility.OpenFileAtLineExternal(fullPath.Replace('/', '\\'), line);
                        break;
                    }

                    matches = matches.NextMatch();
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取当前日志窗口选中的日志的堆栈信息。
        /// </summary>
        /// <returns>选中日志的堆栈信息实例。</returns>
        private static string GetStackTrace()
        {
            // 通过反射获取ConsoleWindow类
            var consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            // 获取窗口实例
            var fieldInfo = consoleWindowType.GetField("ms_ConsoleWindow",
                BindingFlags.Static |
                BindingFlags.NonPublic);
            if (fieldInfo != null)
            {
                var consoleInstance = fieldInfo.GetValue(null);
                if (consoleInstance != null)
                    if (EditorWindow.focusedWindow == (EditorWindow)consoleInstance)
                    {
                        // 获取m_ActiveText成员
                        fieldInfo = consoleWindowType.GetField("m_ActiveText",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);
                        // 获取m_ActiveText的值
                        if (fieldInfo != null)
                        {
                            var activeText = fieldInfo.GetValue(consoleInstance).ToString();
                            return activeText;
                        }
                    }
            }

            return null;
        }
    }
}
#endif