using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fantasy.Async
{
    #region TODO 冷冻态的协程锁. 基于 .NET8的 FrozenDictionary, 效果应该会不错, 没完成, 之后有时间再做
    ///// <summary>
    ///// 冷冻态协程锁, 用到了 FrozenDictionary, 寻锁速度更快; 
    ///// 那么, 代价是什么呢? 答曰:无法池化, 无手动销毁, 因为它不能Clear()也不能Dispose (),
    ///// 只适用于确定要为哪些Key上锁的场合 (通常是框架级的)。
    ///// </summary>
    //public class CoroutineLockFrozen
    //{
    //    private Scene _scene;
    //    private CoroutineLockComponent _coroutineLockComponent;
    //    private readonly FrozenDictionary<long, CoroutineLockQueue> _queue;

    //    /// <summary>
    //    /// 协程锁的类型
    //    /// </summary>
    //    public long CoroutineLockType { get; private set; }

    //    internal void Initialize(CoroutineLockComponent coroutineLockComponent, ref long coroutineLockType)
    //    {
    //        _scene = coroutineLockComponent.Scene;
    //        CoroutineLockType = coroutineLockType;
    //        _coroutineLockComponent = coroutineLockComponent;
    //    }
    //    /// <summary>
    //    /// 把所有协程等待释放掉
    //    /// </summary>
    //    public virtual void ReleaseAllWaiters() {
    //        foreach (var (_, coroutineLockQueue) in _queue)
    //        {
    //            while (TryCoroutineLockQueueDequeue(coroutineLockQueue)) { }
    //        }
    //    }
    //    /// <summary>
    //    /// 等待上一个任务完成
    //    /// </summary>
    //    /// <param name="coroutineLockQueueKey">需要等待的Id</param>
    //    /// <param name="tag">用于查询协程锁的标记，可不传入，只有在超时的时候排查是哪个锁超时时使用</param>
    //    /// <param name="timeOut">等待多久会超时，当到达设定的时候会把当前锁给按照超时处理</param>
    //    /// <returns></returns>
    //    public async FTask<WaitCoroutineLock> Wait(long coroutineLockQueueKey, string tag = null, int timeOut = 30000)
    //    {
    //        var waitCoroutineLock = _coroutineLockComponent.WaitCoroutineLockPool.Rent(this, ref coroutineLockQueueKey, tag, timeOut);

    //        if (!_queue.TryGetValue(coroutineLockQueueKey, out var queue))
    //        {
    //            queue = _coroutineLockComponent.CoroutineLockQueuePool.Rent();
    //            _queue.Add(coroutineLockQueueKey, queue);
    //            return waitCoroutineLock;
    //        }

    //        queue.Enqueue(waitCoroutineLock);
    //        return await waitCoroutineLock.Tcs;
    //    }
    //    /// <summary>
    //    /// 按照先入先出的顺序，释放最早的一个协程锁
    //    /// </summary>
    //    /// <param name="coroutineLockQueueKey"></param>
    //    public void Release(long coroutineLockQueueKey)
    //    {
    //        if (!_queue.TryGetValue(coroutineLockQueueKey, out var coroutineLockQueue))
    //        {
    //            return;
    //        }

    //        if (!TryCoroutineLockQueueDequeue(coroutineLockQueue))
    //        {
    //            _queue.Remove(coroutineLockQueueKey);
    //        }
    //    }

    //    private bool TryCoroutineLockQueueDequeue(CoroutineLockQueue coroutineLockQueue)
    //    {
    //        if (!coroutineLockQueue.TryDequeue(out var waitCoroutineLock))
    //        {
    //            _coroutineLockComponent.CoroutineLockQueuePool.Return(coroutineLockQueue);
    //            return false;
    //        }

    //        if (waitCoroutineLock.TimerId != 0)
    //        {
    //            _scene.TimerComponent.Net.Remove(waitCoroutineLock.TimerId);
    //        }

    //        try
    //        {
    //            // 放到下一帧执行,如果不这样会导致逻辑的顺序不正常。
    //            _scene.ThreadSynchronizationContext.Post(waitCoroutineLock.SetResult);
    //        }
    //        catch (Exception e)
    //        {
    //            Log.Error($"Error in disposing CoroutineLock: {e}");
    //        }

    //        return true;
    //    }

    //    /// <summary>
    //    /// 获取一个值，该值指示当前实例是否为对象池中的实例。
    //    /// </summary>
    //    /// <returns></returns>
    //    public bool IsPool()
    //    {
    //        return _isPool;
    //    }

    //    /// <summary>
    //    /// 设置一个值，该值指示当前实例是否为对象池中的实例。
    //    /// </summary>
    //    /// <param name="isPool"></param>
    //    public void SetIsPool(bool isPool)
    //    {
    //        _isPool = isPool;
    //    }
    //}
    #endregion
}
