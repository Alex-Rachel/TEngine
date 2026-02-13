namespace Fantasy.Timer
{
    /// <summary>
    /// 枚举对象TimerType
    /// </summary>
    public enum TimerType
    {
        /// <summary>
        /// None
        /// </summary>
        None,
        /// <summary>
        /// 一次等待定时器
        /// </summary>
        OnceWaitTimer,
        /// <summary>
        /// 一次性定时器
        /// </summary>
        OnceTimer,
        /// <summary>
        /// 重复定时器
        /// </summary>
        RepeatedTimer
    }
}