#if FANTASY_NET
using System;
using System.Collections.Concurrent;
using System.Threading;

#pragma warning disable CS8765
#pragma warning disable CS8601
#pragma warning disable CS8618

namespace Fantasy;

/// <summary>
/// 线程的同步上下文
/// </summary>
public sealed class ThreadSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<Action> _queue = new();
    /// <summary>
    /// 执行当前上下文投递过的逻辑
    /// </summary>
    public void Update()
    {
        while (_queue.TryDequeue(out var actionHandler))
        {
            try
            {
                actionHandler();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
        
    /// <summary>
    /// 投递一个逻辑到当前上下文
    /// </summary>
    /// <param name="callback"></param>
    /// <param name="state"></param>
    public override void Post(SendOrPostCallback callback, object state)
    {
        Post(() => callback(state));
    }
        
    /// <summary>
    /// 投递一个逻辑到当前上下文
    /// </summary>
    /// <param name="action"></param>
    public void Post(Action action)
    {
        _queue.Enqueue(action);
    }
}
#endif