using UnityEditor;

namespace TEngine.Editor
{
    /// <summary>
    /// Profiler分析器宏定义操作类。
    /// </summary>
    public class ProfilerDefineSymbols
    {
        private const string ENABLE_FIRST_PROFILER = "FIRST_PROFILER";
        private const string ENABLE_DIN_PROFILER = "T_PROFILER";
        
        private static readonly string[] AllProfilerDefineSymbols = new string[]
        {
            ENABLE_FIRST_PROFILER,
            ENABLE_DIN_PROFILER,
        };
        
        /// <summary>
        /// 禁用所有日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Profiler Define Symbols/Disable All Profiler", false, 30)]
        public static void DisableAllLogs()
        {
            foreach (string aboveLogScriptingDefineSymbol in AllProfilerDefineSymbols)
            {
                ScriptingDefineSymbols.RemoveScriptingDefineSymbol(aboveLogScriptingDefineSymbol);
            }
        }

        /// <summary>
        /// 开启所有日志脚本宏定义。
        /// </summary>
        [MenuItem("TEngine/Profiler Define Symbols/Enable All Profiler", false, 31)]
        public static void EnableAllLogs()
        {
            DisableAllLogs();
            foreach (string aboveLogScriptingDefineSymbol in AllProfilerDefineSymbols)
            {
                ScriptingDefineSymbols.AddScriptingDefineSymbol(aboveLogScriptingDefineSymbol);
            }
        }
    }
}