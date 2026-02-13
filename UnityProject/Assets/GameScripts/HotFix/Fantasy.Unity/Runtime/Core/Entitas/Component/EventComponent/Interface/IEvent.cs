using System;
using Fantasy.Async;

namespace Fantasy.Event
{
    /// <summary>
    /// 事件的接口
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// 用于指定事件的Type
        /// </summary>
        /// <returns></returns>
        Type EventType();
    }

    /// <summary>
    /// 事件的泛型接口
    /// </summary>
    /// <typeparam name="T">事件数据类型</typeparam>
    public interface IEvent<in T> : IEvent
    {
        /// <summary>
        /// 事件内部使用的入口
        /// </summary>
        /// <param name="self">事件数据</param>
        void Invoke(T self);
    }

    /// <summary>
    /// 异步事件的泛型接口
    /// </summary>
    /// <typeparam name="T">事件数据类型</typeparam>
    public interface IAsyncEvent<in T> : IEvent
    {
        /// <summary>
        /// 异步事件调用入口
        /// </summary>
        /// <param name="self">事件数据</param>
        FTask InvokeAsync(T self);
    }

    /// <summary>
    /// 领域事件的泛型接口
    /// </summary>
    /// <typeparam name="T">事件数据类型</typeparam>
    public interface ISphereEvent<in T> : IEvent
    {
        /// <summary>
        /// 领域事件调用入口
        /// </summary>
        /// <param name="self">事件数据</param>
        FTask Invoke(T self);
    }
    
    /// <summary>
    /// 事件的抽象类，要使用事件必须要继承这个抽象接口。
    /// 同时实现泛型和非泛型接口，支持零装箱调用
    /// </summary>
    /// <typeparam name="T">要监听的事件泛型类型</typeparam>
    public abstract class EventSystem<T> : IEvent<T>
    {
        private readonly Type _selfType = typeof(T);
        /// <summary>
        /// <see cref="IEvent.EventType"/>
        /// </summary>
        /// <returns></returns>
        public Type EventType()
        {
            return _selfType;
        }

        /// <summary>
        /// 事件调用的方法，要在这个方法里编写事件发生的逻辑
        /// </summary>
        /// <param name="self"></param>
        protected abstract void Handler(T self);

        /// <summary>
        /// 泛型调用入口
        /// </summary>
        /// <param name="self">事件数据</param>
        public void Invoke(T self)
        {
            try
            {
                Handler(self);
            }
            catch (Exception e)
            {
                Log.Error($"{_selfType.Name} Error {e}");
            }
        }
    }
    /// <summary>
    /// 异步事件的抽象类，要使用事件必须要继承这个抽象接口。
    /// 同时实现泛型和非泛型接口，支持零装箱调用
    /// </summary>
    /// <typeparam name="T">要监听的事件泛型类型</typeparam>
    public abstract class AsyncEventSystem<T> : IAsyncEvent<T>
    {
        private readonly Type _selfType = typeof(T);
        /// <summary>
        /// <see cref="IEvent.EventType"/>
        /// </summary>
        /// <returns></returns>
        public Type EventType()
        {
            return _selfType;
        }
        /// <summary>
        /// 事件调用的方法，要在这个方法里编写事件发生的逻辑
        /// </summary>
        /// <param name="self"></param>
        protected abstract FTask Handler(T self);

        /// <summary>
        /// 泛型异步调用入口
        /// </summary>
        /// <param name="self">事件数据</param>
        public async FTask InvokeAsync(T self)
        {
            try
            {
                await Handler(self);
            }
            catch (Exception e)
            {
                Log.Error($"{_selfType.Name} Error {e}");
            }
        }
    }
}