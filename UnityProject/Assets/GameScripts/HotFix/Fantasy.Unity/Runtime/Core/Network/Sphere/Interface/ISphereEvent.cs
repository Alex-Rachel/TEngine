#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.Entitas.Interface;
// ReSharper disable CheckNamespace

namespace Fantasy.Sphere;

/// <summary>
/// Sphere事件接口，定义跨进程事件处理的基础契约
/// Sphere event interface, defines the basic contract for cross-process event handling
/// </summary>
public interface ISphereEvent
{
    /// <summary>
    /// 类型哈希码，用于快速类型匹配
    /// Type hash code for fast type matching
    /// </summary>
    long TypeHashCode { get; }

    /// <summary>
    /// 异步调用事件处理方法
    /// Asynchronously invoke the event handler method
    /// </summary>
    /// <param name="scene">Scene</param>
    /// <param name="args">事件参数 / Event arguments</param>
    /// <returns>异步任务 / Asynchronous task</returns>
    FTask Invoke(Scene scene, SphereEventArgs args);
}

/// <summary>
/// Sphere事件系统的抽象基类，要使用Sphere事件必须要继承这个抽象类。
/// 同时实现泛型和非泛型接口，支持零装箱调用，提供跨进程事件分发能力。
/// Abstract base class for Sphere event system. To use Sphere events, you must inherit from this class.
/// Implements both generic and non-generic interfaces, supports zero-boxing calls, and provides cross-process event dispatching capability.
/// </summary>
/// <typeparam name="T">要监听的事件参数类型 / The event argument type to listen for</typeparam>
public abstract class SphereEventSystem<T> : ISphereEvent where T : SphereEventArgs
{
    /// <summary>
    /// 类型哈希码，用于快速识别事件类型
    /// Type hash code for fast event type identification
    /// </summary>
    public long TypeHashCode { get; }

    /// <summary>
    /// 自身类型的反射信息，用于错误日志输出
    /// Reflection info of self type, used for error logging
    /// </summary>
    private readonly Type _selfType = typeof(T);

    /// <summary>
    /// 构造函数，初始化类型哈希码
    /// Constructor, initializes the type hash code
    /// </summary>
    public SphereEventSystem()
    {
        TypeHashCode = TypeHashCache<T>.HashCode;
    }

    /// <summary>
    /// 事件处理方法，子类需要重写此方法来实现具体的事件处理逻辑
    /// Event handler method, subclasses need to override this method to implement specific event handling logic
    /// </summary>
    /// <param name="scene">Scene</param>
    /// <param name="args">事件参数 / Event arguments</param>
    /// <returns>异步任务 / Asynchronous task</returns>
    protected abstract FTask Handler(Scene scene, T args);

    /// <summary>
    /// 非泛型调用入口，实现ISphereEvent接口，支持零装箱调用
    /// Non-generic invocation entry, implements ISphereEvent interface, supports zero-boxing calls
    /// </summary>
    /// <param name="scene">Scene</param>
    /// <param name="args">事件参数 / Event arguments</param>
    /// <returns>异步任务 / Asynchronous task</returns>
    public async FTask Invoke(Scene scene, SphereEventArgs args)
    {
        try
        {
            await Handler(scene, (T)args);
        }
        catch (Exception e)
        {
            Log.Error($"{_selfType.Name} Error {e}");
        }
    }
}
#endif