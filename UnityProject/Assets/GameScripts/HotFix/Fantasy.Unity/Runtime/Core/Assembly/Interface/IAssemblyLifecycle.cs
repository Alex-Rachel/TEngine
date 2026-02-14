using System;
using Fantasy.Async;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 程序集生命周期回调接口
    /// 实现此接口的类型可以接收程序集的加载、卸载、重载事件通知
    /// 通过 <see cref="AssemblyLifecycle.Add"/> 注册后，在程序集状态变化时会自动调用对应的生命周期方法。
    /// </summary>
    public interface IAssemblyLifecycle
    {
        /// <summary>
        /// 程序集加载或重载时调用
        /// 当新的程序集被加载到框架中时触发此回调，重新加载已存在的程序集时也会调用
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        /// <returns>异步任务</returns>
        FTask OnLoad(AssemblyManifest assemblyManifest);

        /// <summary>
        /// 程序集卸载时调用
        /// 当程序集从框架中卸载时触发此回调，应在此方法中清理该程序集相关的资源
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        /// <returns>异步任务</returns>
        FTask OnUnload(AssemblyManifest assemblyManifest);
    }
}