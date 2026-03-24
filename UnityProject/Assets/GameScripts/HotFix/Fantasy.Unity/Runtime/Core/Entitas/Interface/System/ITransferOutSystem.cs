using System;
using Fantasy.Async;

namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// 实体的TransferOut事件的接口
    /// </summary>
    public interface ITransferOutSystem : IEntitySystem { }
    /// <summary>
    /// 实体的TransferOut事件的抽象接口
    /// </summary>
    /// <typeparam name="T">实体的泛型类型</typeparam>
    public abstract class TransferOutSystem<T> : ITransferOutSystem where T : Entity
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
        protected abstract FTask Out(T self);
        /// <summary>
        /// 框架内部调用的触发Out的方法。
        /// </summary>
        /// <param name="self">触发事件的实体实例</param>
        public void Invoke(Entity self)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// 框架内部调用的触发Out的异步方法。
        /// </summary>
        /// <param name="self">触发事件的实体实例</param>
        /// <returns></returns>
        public FTask InvokeAsync(Entity self)
        {
            return Out((T) self);
        }
    }
}
