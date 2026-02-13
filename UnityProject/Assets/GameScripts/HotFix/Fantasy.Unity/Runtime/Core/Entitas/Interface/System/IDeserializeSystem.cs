using System;
using Fantasy.Async;

namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// 实体的反序列化事件的接口
    /// </summary>
    public interface IDeserializeSystem : IEntitySystem { }
    /// <summary>
    /// 实体的反序列化事件的抽象接口
    /// </summary>
    /// <typeparam name="T">实体的泛型数据</typeparam>
    public abstract class DeserializeSystem<T> : IDeserializeSystem where T : Entity
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
        protected abstract void Deserialize(T self);
        /// <summary>
        /// 框架内部调用的触发Deserialize的方法
        /// </summary>
        /// <param name="self">触发事件的实体实例</param>
        public void Invoke(Entity self)
        {
            Deserialize((T) self);
        }
    }
}