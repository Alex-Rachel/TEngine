using System;
using Fantasy.Async;
using Fantasy.Entitas;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy
{
    /// <summary>
/// 实体超时销毁组件，挂载到实体上后可设置一个延迟销毁定时器。
/// 超时到期后会自动销毁父实体，支持在销毁前执行自定义异步任务。
/// </summary>
public sealed class EntityTimeoutComponent : Entity
{
    /// <summary>
    /// 用于记录时间计划任务的ID，后面可以通过这个ID随时取消这个任务
    /// </summary>
    public long TimerId;

    /// <summary>
    /// 设置父实体的延迟销毁定时器。
    /// 若已存在定时器，会先取消旧的再重新注册，实现"刷新超时"效果。
    /// </summary>
    /// <param name="timeout">超时时间（毫秒），默认 3000ms</param>
    /// <param name="task">超时触发后、销毁父实体前执行的可选异步任务</param>
#if FANTASY_UNITY
    public void SetDestroyTimeout(int timeout = 3000, Func<FTask> task = null)
#endif
#if FANTASY_NET
    public void SetDestroyTimeout(int timeout = 3000, Func<FTask>? task = null)
#endif
    {
        var parentRunTimeId = Parent.RuntimeId;

        if (TimerId != 0)
        {
            Scene.TimerComponent.Net.Remove(ref TimerId);
        }
        
        TimerId = Scene.TimerComponent.Net.OnceTimer(timeout, () =>
        {
            Handler(parentRunTimeId, task).Coroutine();
        });
    }

    /// <summary>
    /// 取消当前的延迟销毁定时器。
    /// </summary>
    public void CancelDestroyTimeout()
    {
        if (TimerId == 0)
        {
            return;
        }
        
        Scene.TimerComponent.Net.Remove(ref TimerId);
    }

    /// <summary>
    /// 销毁组件时同步取消尚未触发的定时器，防止资源泄漏。
    /// </summary>
    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        CancelDestroyTimeout();
        base.Dispose();
    }

    /// <summary>
    /// 定时器触发后的核心处理逻辑：
    /// 1. 校验父实体是否仍然有效（RuntimeId 一致）；
    /// 2. 若存在自定义任务则先 await 执行；
    /// 3. 再次校验父实体有效性后执行 Dispose，销毁父实体。
    /// </summary>
    /// <param name="parentRunTimeId">触发定时器时父实体的 RuntimeId，用于防止实体已被替换的误操作</param>
    /// <param name="task">销毁前执行的可选异步任务</param>
#if FANTASY_UNITY
    private async FTask Handler(long parentRunTimeId, Func<FTask> task = null)
#endif
#if FANTASY_NET
    private async FTask Handler(long parentRunTimeId, Func<FTask>? task = null)
#endif
    {
        if (Parent == null || Parent.RuntimeId != parentRunTimeId)
        {
            return;
        }

        if (task != null)
        {
            await task();
        }
        
        TimerId = 0;

        if (Parent == null || Parent.RuntimeId != parentRunTimeId)
        {
            return;
        }
        
        Parent.Dispose();
    }
}
}
