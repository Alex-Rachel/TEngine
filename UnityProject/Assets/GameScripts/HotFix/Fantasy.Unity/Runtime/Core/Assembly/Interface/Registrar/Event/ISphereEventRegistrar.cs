#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.Sphere;

namespace Fantasy.Assembly;

/// <summary>
/// 跨服域事件系统注册器接口
/// 由 Source Generator 自动生成实现类，用于在程序集加载时注册跨服域事件系统
/// SphereEvent 用于支持分布式服务器之间的事件订阅和发布机制
/// </summary>
public interface ISphereEventRegistrar
{
    /// <summary>
    /// 获取所有跨服域事件类型的哈希码数组
    /// 哈希码用于在 SphereEventComponent 中建立事件类型到处理器的映射关系
    /// 支持跨服务器的事件路由和分发
    /// </summary>
    /// <returns>long 数组，每个元素对应一个 SphereEventSystem&lt;T&gt; 处理的事件类型哈希码</returns>
    long[] TypeHashCodes();

    /// <summary>
    /// 获取所有跨服域事件处理委托数组
    /// 这些委托会被注册到 SphereEventComponent 中，用于处理来自远程服务器的事件
    /// 每个委托对应一个 SphereEventSystem 的 Invoke 方法
    /// </summary>
    /// <returns>Func 委托数组，接收 Scene 和 SphereEventArgs 参数，返回异步任务 FTask</returns>
    Func<Scene, SphereEventArgs, FTask>[] SphereEvent();
}
#endif