using System;
using Fantasy.Event;
using Fantasy.Pool;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace Fantasy.Async
{
    internal sealed class WaitCoroutineLockPool : PoolCore<WaitCoroutineLock>
    {
        private readonly Scene _scene;
        private readonly CoroutineLockComponent _coroutineLockComponent;

        public WaitCoroutineLockPool(CoroutineLockComponent coroutineLockComponent) : base(2000)
        {
            _scene = coroutineLockComponent.Scene;
            _coroutineLockComponent = coroutineLockComponent;
        }

        public WaitCoroutineLock Rent(ICoroutineLock coroutineLock, ref long coroutineLockQueueKey, string tag = null, int timeOut = 30000)
        {
            var timerId = 0L;
            var lockId = _coroutineLockComponent.LockId;
            var waitCoroutineLock = _coroutineLockComponent.WaitCoroutineLockPool.Rent();

            if (timeOut > 0)
            {
                timerId = _scene.TimerComponent.Net.OnceTimer(timeOut, new CoroutineLockTimeout(ref lockId, waitCoroutineLock));
            }

            waitCoroutineLock.Initialize(coroutineLock, this, ref coroutineLockQueueKey, ref timerId, ref lockId, tag);
            return waitCoroutineLock;
        }
    }

    internal struct CoroutineLockTimeout
    {
        public readonly long LockId;
        public readonly WaitCoroutineLock WaitCoroutineLock;

        public CoroutineLockTimeout(ref long lockId, WaitCoroutineLock waitCoroutineLock)
        {
            LockId = lockId;
            WaitCoroutineLock = waitCoroutineLock;
        }
    }

    internal sealed class OnCoroutineLockTimeout : EventSystem<CoroutineLockTimeout>
    {
        protected override void Handler(CoroutineLockTimeout self)
        {
            var selfWaitCoroutineLock = self.WaitCoroutineLock;

            if (self.LockId != selfWaitCoroutineLock.LockId)
            {
                return;
            }

            Log.Error($"coroutine lock timeout LockDuty:{selfWaitCoroutineLock.CoroutineLock.LockDuty} Key:{selfWaitCoroutineLock.CoroutineLockQueueKey} Tag:{selfWaitCoroutineLock.Tag}");
        }
    }

    /// <summary>
    /// 一个协程锁的等待器, 用户通过这里释放锁。(通常使用 using 语句)
    /// </summary>
    public sealed class WaitCoroutineLock : IPool, IDisposable
    {
        private bool _isPool;
        internal string Tag { get; private set; }
        internal long LockId { get; private set; }
        internal long TimerId { get; private set; }
        internal long CoroutineLockQueueKey { get; private set; }
        internal ICoroutineLock CoroutineLock { get; private set; }

        private bool _isSetResult;
        private FTask<WaitCoroutineLock> _tcs;
        private WaitCoroutineLockPool _waitCoroutineLockPool;
        internal void Initialize(ICoroutineLock coroutineLock, WaitCoroutineLockPool waitCoroutineLockPool, ref long coroutineLockQueueKey, ref long timerId, ref long lockId, string tag)
        {
            Tag = tag;
            LockId = lockId;
            TimerId = timerId;
            CoroutineLock = coroutineLock;
            CoroutineLockQueueKey = coroutineLockQueueKey;
            _waitCoroutineLockPool = waitCoroutineLockPool;
        }
        /// <summary>
        /// 释放协程锁
        /// </summary>
        public void Dispose()
        {
            if (LockId == 0)
            {
                Log.Error("WaitCoroutineLock is already disposed");
                return;
            }
            
            CoroutineLock.Release(CoroutineLockQueueKey);
            
            _tcs = null;
            Tag = null;
            LockId = 0;
            TimerId = 0;
            _isSetResult = false;
            CoroutineLockQueueKey = 0;
            _waitCoroutineLockPool.Return(this);
            CoroutineLock = null;
            _waitCoroutineLockPool = null;
        }
        
        internal FTask<WaitCoroutineLock> Tcs
        {
            get { return _tcs ??= FTask<WaitCoroutineLock>.Create(); }
        }

        internal void SetResult()
        {
            if (_isSetResult)
            {
                Log.Error("WaitCoroutineLock is already SetResult");
                return;
            }
            
            _isSetResult = true;
            Tcs.SetResult(this);
        }

        /// <summary>
        /// 获取一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <returns></returns>
        public bool IsPool()
        {
            return _isPool;
        }

        /// <summary>
        /// 设置一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <param name="isPool"></param>
        public void SetIsPool(bool isPool)
        {
            _isPool = isPool;
        }
    }
}