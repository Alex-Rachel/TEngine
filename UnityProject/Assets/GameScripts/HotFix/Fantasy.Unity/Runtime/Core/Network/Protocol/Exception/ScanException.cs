using System;

namespace Fantasy.Network
{
    /// <summary>
    /// 在扫描过程中发生的异常。
    /// </summary>
    public class ScanException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="ScanException"/> 类的新实例。
        /// </summary>
        public ScanException() { }

        /// <summary>
        /// 使用指定的错误消息初始化 <see cref="ScanException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">错误消息。</param>
        public ScanException(string msg) : base(msg) { }
    }
}