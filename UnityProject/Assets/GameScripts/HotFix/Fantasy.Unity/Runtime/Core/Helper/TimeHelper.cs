using System;
#if FANTASY_UNITY
using UnityEngine;
#endif

namespace Fantasy.Helper
{
    /// <summary>
    /// 提供与时间相关的帮助方法。
    /// </summary>
    public static partial class TimeHelper
    {
        /// <summary>
        /// 一小时的毫秒值。
        /// </summary>
        public const long Hour = 3600000;
        /// <summary>
        /// 一分钟的毫秒值。
        /// </summary>
        public const long Minute = 60000;
        /// <summary>
        /// 一天的毫秒值。
        /// </summary>
        public const long OneDay = 86400000;
        // 1970年1月1日的Ticks
        private const long Epoch = 621355968000000000L;
        /// <summary>
        /// 获取当前时间的毫秒数，从1970年1月1日开始计算。
        /// </summary>
        public static long Now => (DateTime.UtcNow.Ticks - Epoch) / 10000;
#if FANTASY_UNITY || FANTASY_CONSOLE
        /// <summary>
        /// 与服务器时间的偏差。
        /// </summary>
        public static long TimeDiff;
        /// <summary>
        /// 获取当前服务器时间的毫秒数，加上与服务器时间的偏差。
        /// </summary>
        public static long ServerNow => Now + TimeDiff;
#if FANTASY_UNITY
        /// <summary>
        /// 获取当前Unity运行的总时间的毫秒数。
        /// </summary>
        public static long UnityNow => (long) (Time.time * 1000);
#endif
#endif
        /// <summary>
        /// 根据时间获取时间戳
        /// </summary>
        public static long Transition(this DateTime dateTime)
        {
            return (dateTime.ToUniversalTime().Ticks - Epoch) / 10000;
        }

        /// <summary>
        /// 根据时间获取 时间戳
        /// </summary>
        public static long TransitionToSeconds(this DateTime dateTime)
        {
            return (dateTime.ToUniversalTime().Ticks - Epoch) / 10000000;
        }

        /// <summary>
        /// 将毫秒数转换为日期时间。
        /// </summary>
        /// <param name="timeStamp">要转换的毫秒数。</param>
        /// <returns>转换后的日期时间。</returns>
        public static DateTime Transition(this long timeStamp)
        {
            return new DateTime(Epoch + timeStamp * 10000, DateTimeKind.Utc).ToUniversalTime();
        }

        /// <summary>
        /// 将毫秒数转换为本地时间的日期时间。
        /// </summary>
        /// <param name="timeStamp">要转换的毫秒数。</param>
        /// <returns>转换后的本地时间的日期时间。</returns>
        public static DateTime TransitionLocal(this long timeStamp)
        {
            return new DateTime(Epoch + timeStamp * 10000, DateTimeKind.Utc).ToLocalTime();
        }
    }
}