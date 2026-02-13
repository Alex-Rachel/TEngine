using System;

namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// 自定义组件事件系统接口
    /// 如果需要自定义组件事件系统，请继承此接口。
    /// 这个接口内部使用。不对外开放。
    /// </summary>
    internal interface ICustomEntitySystem
    {
        /// <summary>
        /// 事件类型
        /// 用于触发这个组件事件关键因素。
        /// </summary>
        int CustomEventType { get; }
        /// <summary>
        /// 实体的类型
        /// </summary>
        /// <returns></returns>
        Type EntityType();
        /// <summary>
        /// 框架内部调用的触发事件方法
        /// </summary>
        /// <param name="entity"></param>
        void Invoke(Entity entity);
    }

    /// <summary>
    /// 自定义组件事件系统抽象类
    /// 如果需要自定义组件事件系统，请继承此抽象类。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class CustomSystem<T> : ICustomEntitySystem where T : Entity
    {
        /// <summary>
        /// 这个1表示是一个自定义事件类型，执行这个事件是时候需要用到这个1.
        /// </summary>
        public abstract int CustomEventType { get; }
        /// <summary>
        /// 事件的抽象方法，需要自己实现这个方法
        /// </summary>
        /// <param name="self">触发事件的实体实例</param>
        protected abstract void Custom(T self);
        /// <summary>
        /// 实体的类型
        /// </summary>
        /// <returns></returns>
        public abstract Type EntityType();
        /// <summary>
        /// 框架内部调用的触发Awake的方法。
        /// </summary>
        /// <param name="self">触发事件的实体实例</param>
        public void Invoke(Entity self)
        {
            Custom((T) self);
        }
    }
}