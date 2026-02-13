// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
using System.Collections.Generic;
using Fantasy.Pool;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
namespace Fantasy.Async
{
    internal sealed class CoroutineLockQueuePool : PoolCore<CoroutineLockQueue>
    {
        public CoroutineLockQueuePool() : base(2000) { }
    }
    
    internal sealed class CoroutineLockQueue : Queue<WaitCoroutineLock>, IPool
    {
        private bool _isPool;
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