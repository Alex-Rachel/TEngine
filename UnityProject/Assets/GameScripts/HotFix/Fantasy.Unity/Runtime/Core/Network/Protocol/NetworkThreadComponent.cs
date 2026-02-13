#if !FANTASY_WEBGL
using System;
using System.Collections.Generic;
using System.Threading;
using Fantasy.Entitas;
// ReSharper disable ForCanBeConvertedToForeach
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Network
{
    internal interface INetworkThreadUpdate
    {
        void Update();
    }
    
    /// <summary>
    /// 网络线程组件
    /// </summary>
    internal sealed class NetworkThreadComponent : Entity
    {
        private Thread _netWorkThread;
        internal ThreadSynchronizationContext SynchronizationContext { get; private set; }
        private readonly List<INetworkThreadUpdate> _updates = new List<INetworkThreadUpdate>();
        
        internal NetworkThreadComponent Initialize()
        {
            SynchronizationContext = new ThreadSynchronizationContext();
            _netWorkThread = new Thread(Update)
            {
                IsBackground = true
            };
            _netWorkThread.Start();
            return this;
        }

        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            
            SynchronizationContext.Post(() =>
            {
                _updates.Clear();
                _netWorkThread.Join();
                _netWorkThread = null;
                SynchronizationContext = null;
            });
            
            base.Dispose();
        }

        private void Update()
        {
            // 将同步上下文设置为网络线程的上下文，以确保操作在正确的线程上下文中执行。
            System.Threading.SynchronizationContext.SetSynchronizationContext(SynchronizationContext);
            // 循环执行
            while (!IsDisposed)
            {
                for (var i = 0; i < _updates.Count; i++)
                {
                    try
                    {
                        _updates[i].Update();
                    }
                    catch (Exception e)
                    {
                        Log.Error(e);
                    }
                }
                SynchronizationContext.Update();
                Thread.Sleep(1);
            }
        }
        
        internal void AddNetworkThreadUpdate(INetworkThreadUpdate update)
        {
            SynchronizationContext.Post(() =>
            {
                if (_updates.Contains(update))
                {
                    Log.Warning($"{update.GetType().FullName} Network thread update is already running");
                    return;
                }
                _updates.Add(update);
            });
        }

        internal void RemoveNetworkThreadUpdate(INetworkThreadUpdate update)
        {
            SynchronizationContext.Post(() =>
            {
                _updates.Remove(update);
            });
        }
    }
}
#endif