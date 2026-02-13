using System;
using Fantasy.Async;

namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// ECS事件系统的核心接口，任何事件都是要继承这个接口
    /// </summary>
    public interface IEntitySystem
    {
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
}