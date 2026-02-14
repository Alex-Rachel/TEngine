using System;
using System.Collections.Generic;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 实体系统注册器接口
    /// 由 Source Generator 自动生成实现类，用于在编译时注册实体的生命周期系统
    /// 替代运行时反射，提供零反射开销的实体系统管理，完全支持 Native AOT 编译
    /// 管理实体从创建、更新到销毁的完整生命周期
    /// </summary>
    public interface IEntitySystemRegistrar
    {
        /// <summary>
        /// 获取所有注册了 Awake 系统的实体类型句柄数组
        /// 与 AwakeHandles() 一一对应，用于建立实体类型到 Awake 处理器的映射关系
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个拥有 AwakeSystem 的实体类型</returns>
        RuntimeTypeHandle[] AwakeTypeHandles();

        /// <summary>
        /// 获取所有 Awake 系统的委托数组
        /// Awake 系统在实体创建后立即调用，用于初始化实体状态、设置默认值、建立关联关系等
        /// 这是实体生命周期的第一个阶段，类似于构造函数但支持访问 Scene 上下文
        /// </summary>
        /// <returns>Action 委托数组，每个委托接收 Entity 参数，执行实体的初始化逻辑</returns>
        Action<Entity>[] AwakeHandles();

        /// <summary>
        /// 获取所有注册了 Update 系统的实体类型句柄数组
        /// 与 UpdateHandles() 一一对应，用于建立实体类型到 Update 处理器的映射关系
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个拥有 UpdateSystem 的实体类型</returns>
        RuntimeTypeHandle[] UpdateTypeHandles();

        /// <summary>
        /// 获取所有 Update 系统的委托数组
        /// Update 系统在每帧调用，用于实现实体的逻辑更新、状态检查、定时任务等
        /// 通过 Scene 的调度系统定期执行，支持 MainThread、MultiThread、ThreadPool 等不同的调度模式
        /// </summary>
        /// <returns>Action 委托数组，每个委托接收 Entity 参数，执行实体的更新逻辑</returns>
        Action<Entity>[] UpdateHandles();

        /// <summary>
        /// 获取所有注册了 Destroy 系统的实体类型句柄数组
        /// 与 DestroyHandles() 一一对应，用于建立实体类型到 Destroy 处理器的映射关系
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个拥有 DestroySystem 的实体类型</returns>
        RuntimeTypeHandle[] DestroyTypeHandles();

        /// <summary>
        /// 获取所有 Destroy 系统的委托数组
        /// Destroy 系统在实体销毁时调用，用于清理资源、断开关联、释放引用等
        /// 这是实体生命周期的最后阶段，确保实体销毁时不会产生内存泄漏
        /// </summary>
        /// <returns>Action 委托数组，每个委托接收 Entity 参数，执行实体的清理逻辑</returns>
        Action<Entity>[] DestroyHandles();

        /// <summary>
        /// 获取所有注册了 Deserialize 系统的实体类型句柄数组
        /// 与 DeserializeHandles() 一一对应，用于建立实体类型到 Deserialize 处理器的映射关系
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个拥有 DeserializeSystem 的实体类型</returns>
        RuntimeTypeHandle[] DeserializeTypeHandles();

        /// <summary>
        /// 获取所有 Deserialize 系统的委托数组
        /// Deserialize 系统在实体从数据库或网络反序列化后调用，用于重建运行时状态
        /// 处理无法序列化的字段（如缓存、事件订阅、临时引用）的重新初始化
        /// </summary>
        /// <returns>Action 委托数组，每个委托接收 Entity 参数，执行反序列化后的初始化逻辑</returns>
        Action<Entity>[] DeserializeHandles();
#if FANTASY_UNITY
        /// <summary>
        /// 获取所有注册了 LateUpdate 系统的实体类型句柄数组（仅 Unity 平台）
        /// 与 LateUpdateHandles() 一一对应，用于建立实体类型到 LateUpdate 处理器的映射关系
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个拥有 LateUpdateSystem 的实体类型</returns>
        RuntimeTypeHandle[] LateUpdateTypeHandles();

        /// <summary>
        /// 获取所有 LateUpdate 系统的委托数组（仅 Unity 平台）
        /// LateUpdate 系统在 Update 之后调用，用于处理依赖其他实体更新结果的逻辑
        /// 常用于相机跟随、UI 同步、后处理等需要在所有 Update 完成后执行的操作
        /// </summary>
        /// <returns>Action 委托数组，每个委托接收 Entity 参数，执行延迟更新逻辑</returns>
        Action<Entity>[] LateUpdateHandles();

#endif
    }
}