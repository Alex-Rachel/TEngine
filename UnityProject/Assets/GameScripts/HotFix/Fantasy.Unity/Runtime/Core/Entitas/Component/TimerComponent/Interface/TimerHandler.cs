using Fantasy.Event;

namespace Fantasy.Timer
{
    /// <summary>
    /// 计时器抽象类，提供了一个基础框架，用于创建处理计时器事件的具体类。
    /// </summary>
    /// <typeparam name="T">事件的类型参数</typeparam>
    public abstract class TimerHandler<T> : EventSystem<T> { }
}