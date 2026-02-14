namespace TEngine
{
    /// <summary>
    /// 音频代理辅助器运行时状态枚举。
    /// </summary>
    public enum AudioAgentRuntimeState
    {
        /// <summary>
        /// 无状态。
        /// </summary>
        None,

        /// <summary>
        /// 加载中状态。
        /// </summary>
        Loading,

        /// <summary>
        /// 播放中状态。
        /// </summary>
        Playing,

        /// <summary>
        /// 渐渐消失状态。
        /// </summary>
        FadingOut,

        /// <summary>
        /// 结束状态。
        /// </summary>
        End,
    };
}