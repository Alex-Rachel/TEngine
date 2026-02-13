#if !FANTASY_WEBGL || !FANTASY_SINGLETHREAD
using System;
using System.Collections.Concurrent;
using System.Threading;
namespace Fantasy
{
    internal struct MultiThreadStruct : IDisposable
    {
        public readonly Thread Thread;
        public readonly CancellationTokenSource Cts;

        public MultiThreadStruct(Thread thread, CancellationTokenSource cts)
        {
            Thread = thread;
            Cts = cts;
        }

        public void Dispose()
        {
            Cts.Cancel();
            if (Thread.IsAlive)
            {
                Thread.Join();
            }
            Cts.Dispose();
        }
    }
    
    internal sealed class MultiThreadScheduler : ISceneScheduler
    {
        private bool _isDisposed;
        private readonly ConcurrentDictionary<long, MultiThreadStruct> _threads = new ConcurrentDictionary<long, MultiThreadStruct>();
        public int ThreadCount => _threads.Count;
        
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            
            foreach (var (_, multiThreadStruct) in _threads.ToArray())
            {
                multiThreadStruct.Dispose();
            }
            
            _threads.Clear();
        }

        public void Add(Scene scene)
        {
            var cts = new CancellationTokenSource();
            var thread = new Thread(() => Loop(scene, cts.Token));
            _threads.TryAdd(scene.RuntimeId, new MultiThreadStruct(thread, cts));
            thread.Start();
        }

        public void Remove(Scene scene)
        {
            if (_threads.TryRemove(scene.RuntimeId, out var multiThreadStruct))
            {
                multiThreadStruct.Dispose();
            }
        }

        public void Update()
        {
            throw new NotImplementedException();
        }

        private void Loop(Scene scene, CancellationToken cancellationToken)
        {
            var sceneThreadSynchronizationContext = scene.ThreadSynchronizationContext;
            SynchronizationContext.SetSynchronizationContext(sceneThreadSynchronizationContext);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (scene.IsDisposed)
                    {
                        Remove(scene);
                        return;
                    }

                    sceneThreadSynchronizationContext.Update();
                    scene.Update();
                }
                catch (Exception e)
                {
                    Log.Error($"Error in MultiThreadScheduler loop: {e.Message}");
                }
                finally
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
}
#endif
