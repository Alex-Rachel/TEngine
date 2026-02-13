using System.Collections.Generic;
#if FANTASY_UNITY || FANTASY_NET || !FANTASY_WEBGL
using System.Threading;
#endif
#if FANTASY_NET
using Fantasy.Platform.Net;
#endif
namespace Fantasy
{
    internal sealed class MainScheduler : ISceneScheduler
    {
        private readonly Queue<Scene> _queue = new Queue<Scene>();
        public readonly ThreadSynchronizationContext ThreadSynchronizationContext;
        
        public MainScheduler()
        {
            ThreadSynchronizationContext = new ThreadSynchronizationContext();
#if !FANTASY_WEBGL && !UNITY_EDITOR
            SynchronizationContext.SetSynchronizationContext(ThreadSynchronizationContext);
#endif
        }
        public void Dispose()
        {
            _queue.Clear();
        }

        public void Add(Scene scene)
        {
            ThreadSynchronizationContext.Post(() =>
            {
                if (scene.IsDisposed)
                {
                    return;
                }
                
                _queue.Enqueue(scene);
            });
        }

        public void Remove(Scene scene)
        {
            ThreadSynchronizationContext.Post(() =>
            {
                if (scene.IsDisposed)
                {
                    return;
                }
                
                var initialCount = _queue.Count;
                for (var i = 0; i < initialCount; i++)
                {
                    var currentScene = _queue.Dequeue();
                    if (currentScene != scene)
                    {
                        _queue.Enqueue(currentScene);
                    }
                }
            });
        }

        public void Update()
        {
            ThreadSynchronizationContext.Update();
            var initialCount = _queue.Count;
            
            while (initialCount-- > 0)
            {
                if(!_queue.TryDequeue(out var scene))
                {
                    continue;
                }
            
                if (scene.IsDisposed)
                {
                    continue;
                }
                
                scene.Update();
                _queue.Enqueue(scene);
            }
        }
#if FANTASY_UNITY
        public void LateUpdate()
        {
            var initialCount = _queue.Count;
            
            while (initialCount-- > 0)
            {
                if(!_queue.TryDequeue(out var scene))
                {
                    continue;
                }
            
                if (scene.IsDisposed)
                {
                    continue;
                }
                
                scene.LateUpdate();
                _queue.Enqueue(scene);
            }
        }
#endif
    }
}