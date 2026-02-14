using System;

namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// Update事件的接口
    /// </summary>
    public interface IUpdateSystem : IEntitySystem { }
    /// <summary>
    /// Update事件的抽象接口
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class UpdateSystem<T> : IUpdateSystem where T : Entity
    {
        /// <summary>
        /// 实体的类型
        /// </summary>
        /// <returns></returns>
        public Type EntityType() => typeof(T);
        /// <summary>
        /// 事件的抽象方法，需要自己实现这个方法
        /// </summary>
        /// <param name="self">触发事件的实体实例</param>
        protected abstract void Update(T self);
        /// <summary>
        /// 框架内部调用的触发Update的方法
        /// </summary>
        /// <param name="self">触发事件的实体实例</param>
        public void Invoke(Entity self)
        {
            Update((T) self);
        }
    }
}