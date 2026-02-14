#if FANTASY_UNITY && !FANTASY_WEBGL
#pragma warning disable CS8765
#pragma warning disable CS8601
#pragma warning disable CS8618
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Fantasy
{
    public sealed class ThreadSynchronizationContext : SynchronizationContext
    {
        private Action _actionHandler;
        private readonly ConcurrentQueue<Action> _queue = new();
        
        public void Update()
        {
            while (_queue.TryDequeue(out _actionHandler))
            {
                try
                {
                    _actionHandler();
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
        }
        
        public override void Post(SendOrPostCallback callback, object state)
        {
            Post(() => callback(state));
        }
        
        public void Post(Action action)
        {
            _queue.Enqueue(action);
        }
    }
}
#endif
#if FANTASY_UNITY && FANTASY_WEBGL
using System;
using System.Collections.Generic;
using System.Threading;
using Fantasy;
using UnityEngine;
using Object = UnityEngine.Object;

public class WebGLSynchronizationContextUpdater : MonoBehaviour
{
    private ThreadSynchronizationContext _context;
    
    public void Initialize(ThreadSynchronizationContext context)
    {
        _context = context;
    }
    
    void Update()
    {
        _context.Update();
    }
}
public sealed class ThreadSynchronizationContext : SynchronizationContext
{
    private Action _actionHandler;
    private readonly Queue<Action> _queue = new();

    public static void Initialize()
    {
        var context = new ThreadSynchronizationContext();
        SetSynchronizationContext(context);
        var go = new GameObject("WebGLSynchronizationContextUpdater");
        go.AddComponent<WebGLSynchronizationContextUpdater>().Initialize(context);
        Object.DontDestroyOnLoad(go);
    }
        
    public void Update()
    {
        while (_queue.TryDequeue(out _actionHandler))
        {
            try
            {
                _actionHandler();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
    
    public override void Post(SendOrPostCallback callback, object state)
    {
        Post(() => callback(state));
    }
        
    public void Post(Action action)
    {
        _queue.Enqueue(action);
    }
}
#endif