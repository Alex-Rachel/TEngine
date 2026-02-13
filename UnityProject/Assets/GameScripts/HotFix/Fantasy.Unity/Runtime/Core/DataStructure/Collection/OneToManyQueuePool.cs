using System;
using System.Collections.Generic;
using Fantasy.Pool;

#pragma warning disable CS8603

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 支持一对多关系的队列池，用于存储具有相同键的值的队列集合。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class OneToManyQueuePool<TKey, TValue> : OneToManyQueue<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="OneToManyQueuePool{TKey, TValue}"/> 一对多关系的队列池的实例。
        /// </summary>
        /// <returns>创建的实例。</returns>
        public static OneToManyQueuePool<TKey, TValue> Create()
        {
            var a = Pool<OneToManyQueuePool<TKey, TValue>>.Rent();
            a._isDispose = false;
            a._isPool = true;
            return a;
        }

        /// <summary>
        /// 释放当前实例所占用的资源，并将实例回收到对象池中。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            Clear();
            Pool<OneToManyQueuePool<TKey, TValue>>.Return(this);
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

    /// <summary>
    /// 支持一对多关系的队列，用于存储具有相同键的值的队列集合。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class OneToManyQueue<TKey, TValue> : Dictionary<TKey, Queue<TValue>> where TKey : notnull
    {
        private readonly Queue<Queue<TValue>> _queue = new Queue<Queue<TValue>>();
        private readonly int _recyclingLimit;

        /// <summary>
        /// 创建一个 <see cref="OneToManyQueue{TKey, TValue}"/> 一对多关系的队列的实例。设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public OneToManyQueue(int recyclingLimit = 0)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 判断指定键的值队列是否包含指定的值。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <param name="value">要查找的值。</param>
        /// <returns>如果存在，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        public bool Contains(TKey key, TValue value)
        {
            TryGetValue(key, out var list);

            return list != null && list.Contains(value);
        }

        /// <summary>
        /// 将指定的值添加到指定键的值队列中。
        /// </summary>
        /// <param name="key">要添加值的键。</param>
        /// <param name="value">要添加的值。</param>
        public void Enqueue(TKey key, TValue value)
        {
            if (!TryGetValue(key, out var list))
            {
                list = Fetch();
                list.Enqueue(value);
                Add(key, list);
                return;
            }

            list.Enqueue(value);
        }

        /// <summary>
        /// 从指定键的值队列中出队一个值。
        /// </summary>
        /// <param name="key">要出队的键。</param>
        /// <returns>出队的值。</returns>
        public TValue Dequeue(TKey key)
        {
            if (!TryGetValue(key, out var list) || list.Count == 0)
            {
                return default;
            }

            var value = list.Dequeue();

            if (list.Count == 0)
            {
                RemoveKey(key);
            }

            return value;
        }

        /// <summary>
        /// 尝试从指定键的值队列中出队一个值。
        /// </summary>
        /// <param name="key">要出队的键。</param>
        /// <param name="value">出队的值。</param>
        /// <returns>如果成功出队，则为 <c>true</c>；否则为 <c>false</c>。</returns>
        public bool TryDequeue(TKey key, out TValue value)
        {
            value = Dequeue(key);

            return value != null;
        }

        /// <summary>
        /// 从字典中移除指定键及其对应的值队列。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveKey(TKey key)
        {
            if (!TryGetValue(key, out var list)) return;

            Remove(key);
            Recycle(list);
        }

        /// <summary>
        /// 从队列池中获取一个值队列。如果队列池为空，则创建一个新的值队列。
        /// </summary>
        /// <returns>获取的值队列。</returns>
        private Queue<TValue> Fetch()
        {
            return _queue.Count <= 0 ? new Queue<TValue>() : _queue.Dequeue();
        }

        /// <summary>
        /// 回收一个不再使用的值队列到队列池中，以便重用。
        /// </summary>
        /// <param name="list">要回收的值队列。</param>
        private void Recycle(Queue<TValue> list)
        {
            list.Clear();

            if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit) return;

            _queue.Enqueue(list);
        }

        /// <summary>
        /// 清空当前实例的数据，同时回收所有值队列。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _queue.Clear();
        }
    }
}