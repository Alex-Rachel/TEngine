using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 运行时Id。
    /// <remarks>提供给事件分发的运行时Id。</remarks>
    /// <example> public static readonly int ExampleEventId = RuntimeId.ToRuntimeId("ExampleEvent.ExampleEventId"); </example>
    /// </summary>
    public static class RuntimeId
    {
        /// <summary>
        /// Key->字符串 | Value->RuntimeIdTable)
        /// </summary>
        private static readonly Dictionary<string, int> _eventString2RuntimeMap = new Dictionary<string, int>();

        /// <summary>
        /// Key->RuntimeId    | Value->字符串 (Table)
        /// </summary>
        private static readonly Dictionary<int, string> _eventRuntimeToStringMap = new Dictionary<int, string>();

        /// <summary>
        /// 当前运行时Id。
        /// </summary>
        private static int _currentRuntimeId = 0;

        /// <summary>
        /// 字符串转RuntimeId。
        /// </summary>
        /// <param name="value">字符串Value。</param>
        /// <returns>RuntimeId。</returns>
        public static int ToRuntimeId(string value)
        {
            if (_eventString2RuntimeMap.TryGetValue(value, out var runtimeId))
            {
                return runtimeId;
            }

            runtimeId = ++_currentRuntimeId;
            _eventString2RuntimeMap[value] = runtimeId;
            _eventRuntimeToStringMap[runtimeId] = value;

            return runtimeId;
        }

        /// <summary>
        /// RuntimeId转字符串。
        /// </summary>
        /// <param name="runtimeId">RuntimeId。</param>
        /// <returns>字符串。</returns>
        public static string ToString(int runtimeId)
        {
            return _eventRuntimeToStringMap.TryGetValue(runtimeId, out var value) ? value : string.Empty;
        }
    }
}