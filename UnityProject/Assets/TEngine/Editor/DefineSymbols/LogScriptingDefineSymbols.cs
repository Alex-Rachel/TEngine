using UnityEditor;

namespace TEngine.Editor
{
    /// <summary>
    /// 日志脚本宏定义操作类。
    /// </summary>
    public static class LogScriptingDefineSymbols
    {
        private const string ENABLE_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_LOG";
        private const string ENABLE_DEBUG_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_DEBUG_AND_ABOVE_LOG";
        private const string ENABLE_INFO_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_INFO_AND_ABOVE_LOG";
        private const string ENABLE_WARNING_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_WARNING_AND_ABOVE_LOG";
        private const string ENABLE_ERROR_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_ERROR_AND_ABOVE_LOG";
        private const string ENABLE_FATAL_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_FATAL_AND_ABOVE_LOG";
        private const string ENABLE_DEBUG_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_DEBUG_LOG";
        private const string ENABLE_INFO_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_INFO_LOG";
        private const string ENABLE_WARNING_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_WARNING_LOG";
        private const string ENABLE_ERROR_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_ERROR_LOG";
        private const string ENABLE_FATAL_LOG_SCRIPTING_DEFINE_SYMBOL = "ENABLE_FATAL_LOG";

        private static readonly string[] AboveLogScriptingDefineSymbols = new string[]
        {
            ENABLE_DEBUG_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_INFO_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_WARNING_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_ERROR_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_FATAL_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL
        };

        private static readonly string[] SpecifyLogScriptingDefineSymbols = new string[]
        {
            ENABLE_DEBUG_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_INFO_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_WARNING_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_ERROR_LOG_SCRIPTING_DEFINE_SYMBOL,
            ENABLE_FATAL_LOG_SCRIPTING_DEFINE_SYMBOL
        };

        /// <summary>
        /// 禁用所有日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Log Scripting Define Symbols/Disable All Logs", false, 30)]
        public static void DisableAllLogs()
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(ENABLE_LOG_SCRIPTING_DEFINE_SYMBOL);

            foreach (string specifyLogScriptingDefineSymbol in SpecifyLogScriptingDefineSymbols)
            {
                ScriptingDefineSymbols.RemoveScriptingDefineSymbol(specifyLogScriptingDefineSymbol);
            }

            foreach (string aboveLogScriptingDefineSymbol in AboveLogScriptingDefineSymbols)
            {
                ScriptingDefineSymbols.RemoveScriptingDefineSymbol(aboveLogScriptingDefineSymbol);
            }
        }

        /// <summary>
        /// 开启所有日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Log Scripting Define Symbols/Enable All Logs", false, 31)]
        public static void EnableAllLogs()
        {
            DisableAllLogs();
            ScriptingDefineSymbols.AddScriptingDefineSymbol(ENABLE_LOG_SCRIPTING_DEFINE_SYMBOL);
        }

        /// <summary>
        /// 开启调试及以上级别的日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Log Scripting Define Symbols/Enable Debug And Above Logs", false, 32)]
        public static void EnableDebugAndAboveLogs()
        {
            SetAboveLogScriptingDefineSymbol(ENABLE_DEBUG_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL);
        }

        /// <summary>
        /// 开启信息及以上级别的日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Log Scripting Define Symbols/Enable Info And Above Logs", false, 33)]
        public static void EnableInfoAndAboveLogs()
        {
            SetAboveLogScriptingDefineSymbol(ENABLE_INFO_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL);
        }

        /// <summary>
        /// 开启警告及以上级别的日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Log Scripting Define Symbols/Enable Warning And Above Logs", false, 34)]
        public static void EnableWarningAndAboveLogs()
        {
            SetAboveLogScriptingDefineSymbol(ENABLE_WARNING_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL);
        }

        /// <summary>
        /// 开启错误及以上级别的日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Log Scripting Define Symbols/Enable Error And Above Logs", false, 35)]
        public static void EnableErrorAndAboveLogs()
        {
            SetAboveLogScriptingDefineSymbol(ENABLE_ERROR_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL);
        }

        /// <summary>
        /// 开启严重错误及以上级别的日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Log Scripting Define Symbols/Enable Fatal And Above Logs", false, 36)]
        public static void EnableFatalAndAboveLogs()
        {
            SetAboveLogScriptingDefineSymbol(ENABLE_FATAL_AND_ABOVE_LOG_SCRIPTING_DEFINE_SYMBOL);
        }

        /// <summary>
        /// 设置日志脚本宏定义。
        /// </summary>
        /// <param name="aboveLogScriptingDefineSymbol">要设置的日志脚本宏定义。</param>
        public static void SetAboveLogScriptingDefineSymbol(string aboveLogScriptingDefineSymbol)
        {
            if (string.IsNullOrEmpty(aboveLogScriptingDefineSymbol))
            {
                return;
            }

            foreach (string i in AboveLogScriptingDefineSymbols)
            {
                if (i == aboveLogScriptingDefineSymbol)
                {
                    DisableAllLogs();
                    ScriptingDefineSymbols.AddScriptingDefineSymbol(aboveLogScriptingDefineSymbol);
                    return;
                }
            }
        }

        /// <summary>
        /// 设置日志脚本宏定义。
        /// </summary>
        /// <param name="specifyLogScriptingDefineSymbols">要设置的日志脚本宏定义。</param>
        public static void SetSpecifyLogScriptingDefineSymbols(string[] specifyLogScriptingDefineSymbols)
        {
            if (specifyLogScriptingDefineSymbols == null || specifyLogScriptingDefineSymbols.Length <= 0)
            {
                return;
            }

            bool removed = false;
            foreach (string specifyLogScriptingDefineSymbol in specifyLogScriptingDefineSymbols)
            {
                if (string.IsNullOrEmpty(specifyLogScriptingDefineSymbol))
                {
                    continue;
                }

                foreach (string i in SpecifyLogScriptingDefineSymbols)
                {
                    if (i == specifyLogScriptingDefineSymbol)
                    {
                        if (!removed)
                        {
                            removed = true;
                            DisableAllLogs();
                        }

                        ScriptingDefineSymbols.AddScriptingDefineSymbol(specifyLogScriptingDefineSymbol);
                        break;
                    }
                }
            }
        }
    }
}
