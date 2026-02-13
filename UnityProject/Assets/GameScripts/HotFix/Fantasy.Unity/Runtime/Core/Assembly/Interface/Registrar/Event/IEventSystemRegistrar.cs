using System;
using Fantasy.Event;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 事件系统注册器接口
    /// 由 Source Generator 自动生成实现类，用于在程序集加载时注册事件系统
    /// 负责管理同步事件（EventSystem）和异步事件（AsyncEventSystem）的注册
    /// </summary>
    public interface IEventSystemRegistrar
    {
        /// <summary>
        /// 获取所有同步事件系统对应的事件类型句柄数组
        /// 这些句柄用于在 EventComponent 中建立事件类型到处理器的映射关系
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个事件类型（EventSystem&lt;T&gt; 或 TimerHandler&lt;T&gt; 中的 T）</returns>
        RuntimeTypeHandle[] EventTypeHandles();

        /// <summary>
        /// 获取所有同步事件系统实例数组
        /// 这些实例会被注册到 EventComponent 中，用于同步事件的分发和处理
        /// </summary>
        /// <returns>IEvent 数组，包含所有继承自 EventSystem&lt;T&gt; 的事件处理器实例（包括 TimerHandler&lt;T&gt;）</returns>
        IEvent[] Events();

        /// <summary>
        /// 获取所有异步事件系统对应的事件类型句柄数组
        /// 这些句柄用于在 EventComponent 中建立异步事件类型到处理器的映射关系
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个异步事件类型（AsyncEventSystem&lt;T&gt; 中的 T）</returns>
        RuntimeTypeHandle[] AsyncEventTypeHandles();

        /// <summary>
        /// 获取所有异步事件系统实例数组
        /// 这些实例会被注册到 EventComponent 中，用于异步事件的分发和处理
        /// </summary>
        /// <returns>IEvent 数组，包含所有继承自 AsyncEventSystem&lt;T&gt; 的异步事件处理器实例</returns>
        IEvent[] AsyncEvents();
    }
}