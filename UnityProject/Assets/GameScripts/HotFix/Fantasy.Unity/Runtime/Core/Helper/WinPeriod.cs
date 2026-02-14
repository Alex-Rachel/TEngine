using System.Runtime.InteropServices;

namespace Fantasy.Helper
{
    /// <summary>
    /// 精度设置
    /// </summary>
    public static partial class WinPeriod
    {
        // 一般默认的精度不止1毫秒（不同操作系统有所不同），需要调用timeBeginPeriod与timeEndPeriod来设置精度
        [DllImport("winmm")]
        private static extern void timeBeginPeriod(int t);
        /// <summary>
        /// 针对Windows平台设置精度
        /// </summary>
        public static void Initialize()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                timeBeginPeriod(1);
            }
        }
    }
}