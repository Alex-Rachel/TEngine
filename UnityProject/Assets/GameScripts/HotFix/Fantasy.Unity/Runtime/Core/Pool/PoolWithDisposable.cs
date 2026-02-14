using System;
using System.Collections.Generic;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy.Pool
{
    /// <summary>
    /// 静态通用对象池，用于存储实现了 IDisposable 接口的对象。
    /// </summary>
    /// <typeparam name="T">要存储在对象池中的对象类型，必须实现 IDisposable 接口。</typeparam>
    public abstract class PoolWithDisposable<T> : IDisposable where T : IPool, IDisposable, new()
    {
        private int _poolCount;
        private readonly int _maxCapacity;
        private readonly Queue<T> _poolQueue = new Queue<T>();
        /// <summary>
        /// 池子里可用的数量
        /// </summary>
        public int Count => _poolQueue.Count;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="maxCapacity">初始的容量</param>
        protected PoolWithDisposable(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
        }
        
        /// <summary>
        /// 租借
        /// </summary>
        /// <returns></returns>
        public T Rent()
        {
            T dequeue;
            if (_poolQueue.Count == 0)
            {
                dequeue = new T();
            }
            else
            {
                _poolCount--;
                dequeue = _poolQueue.Dequeue();
            }
            
            dequeue.SetIsPool(true);
            return dequeue;
        }
        
        /// <summary>
        /// 租借
        /// </summary>
        /// <param name="generator"></param>
        /// <returns></returns>
        public T Rent(Func<T> generator)
        {
            T dequeue;
            
            if (_poolQueue.Count == 0)
            {
                dequeue = generator();
            }
            else
            {
                _poolCount--;
                dequeue = _poolQueue.Dequeue();
            }
            
            dequeue.SetIsPool(true);
            return dequeue;
        }
        
        /// <summary>
        /// 返还
        /// </summary>
        /// <param name="t"></param>
        public void Return(T t)
        {
            if (t == null)
            {
                return;
            }

            if (!t.IsPool())
            {
                return;
            }

            if (_poolCount >= _maxCapacity)
            {
                return;
            }

            _poolCount++;
            t.SetIsPool(true);
            _poolQueue.Enqueue(t);
            t.Dispose();
        }
        
        /// <summary>
        /// 返还
        /// </summary>
        /// <param name="t"></param>
        /// <param name="reset"></param>
        public void Return(T t, Action<T> reset)
        {
            if (t == null)
            {
                return;
            }
            
            if (!t.IsPool())
            {
                reset(t);
                return;
            }
            
            if (_poolCount >= _maxCapacity)
            {
                return;
            }

            reset(t);
            _poolCount++;
            t.SetIsPool(false);
            _poolQueue.Enqueue(t);
            t.Dispose();
        }
        
        /// <summary>
        /// 销毁方法
        /// </summary>
        public virtual void Dispose()
        {
            _poolCount = 0;
            _poolQueue.Clear();
        }
    }
}