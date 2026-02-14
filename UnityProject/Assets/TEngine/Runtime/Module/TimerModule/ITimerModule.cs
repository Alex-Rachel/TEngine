namespace TEngine
{
    public interface ITimerModule
    {
        /// <summary>
        /// 添加计时器。
        /// </summary>
        /// <param name="callback">计时器回调。</param>
        /// <param name="time">计时器间隔。</param>
        /// <param name="isLoop">是否循环。</param>
        /// <param name="isUnscaled">是否不收时间缩放影响。</param>
        /// <param name="args">传参。(避免闭包)</param>
        /// <returns>计时器Id。</returns>
        public int AddTimer(TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false, params object[] args);

        /// <summary>
        /// 暂停计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public void Stop(int timerId);

        /// <summary>
        /// 恢复计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public void Resume(int timerId);

        /// <summary>
        /// 计时器是否在运行中。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        /// <returns>否在运行中。</returns>
        public bool IsRunning(int timerId);

        /// <summary>
        /// 获得计时器剩余时间。
        /// </summary>
        public float GetLeftTime(int timerId);

        /// <summary>
        /// 重置计时器,恢复到开始状态。
        /// </summary>
        public void Restart(int timerId);

        /// <summary>
        /// 重置计时器。
        /// </summary>
        public void ResetTimer(int timerId, TimerHandler callback, float time, bool isLoop = false, bool isUnscaled = false);

        /// <summary>
        /// 重置计时器。
        /// </summary>
        public void ResetTimer(int timerId, float time, bool isLoop, bool isUnscaled);

        /// <summary>
        /// 移除计时器。
        /// </summary>
        /// <param name="timerId">计时器Id。</param>
        public void RemoveTimer(int timerId);

        /// <summary>
        /// 移除所有计时器。
        /// </summary>
        public void RemoveAllTimer();
    }
}