using System;

namespace TEngine
{
    /// <summary>
    /// 流程管理器接口。
    /// </summary>
    public interface IProcedureModule
    {
        /// <summary>
        /// 获取当前流程。
        /// </summary>
        ProcedureBase CurrentProcedure
        {
            get;
        }

        /// <summary>
        /// 获取当前流程持续时间。
        /// </summary>
        float CurrentProcedureTime
        {
            get;
        }

        /// <summary>
        /// 初始化流程管理器。
        /// </summary>
        /// <param name="fsmModule">有限状态机管理器。</param>
        /// <param name="procedures">流程管理器包含的流程。</param>
        void Initialize(IFsmModule fsmModule, params ProcedureBase[] procedures);

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <typeparam name="T">要开始的流程类型。</typeparam>
        void StartProcedure<T>() where T : ProcedureBase;

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        void StartProcedure(Type procedureType);

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <typeparam name="T">要检查的流程类型。</typeparam>
        /// <returns>是否存在流程。</returns>
        bool HasProcedure<T>() where T : ProcedureBase;

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        bool HasProcedure(Type procedureType);

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <typeparam name="T">要获取的流程类型。</typeparam>
        /// <returns>要获取的流程。</returns>
        ProcedureBase GetProcedure<T>() where T : ProcedureBase;

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程。</returns>
        ProcedureBase GetProcedure(Type procedureType);

        /// <summary>
        /// 重启流程。
        /// <remarks>默认使用第一个流程作为启动流程。</remarks>
        /// </summary>
        /// <param name="procedures">新的的流程。</param>
        /// <returns>是否重启成功。</returns>
        /// <exception cref="GameFrameworkException">重启异常。</exception>
        bool RestartProcedure(params ProcedureBase[] procedures);
    }
}
