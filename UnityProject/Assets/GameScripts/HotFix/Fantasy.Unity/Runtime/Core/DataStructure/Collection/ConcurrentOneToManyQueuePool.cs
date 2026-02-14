#if !FANTASY_WEBGL
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Fantasy.Pool;

#pragma warning disable CS8603

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 表示一个并发的一对多队列池，用于维护具有相同键的多个值的关联关系，实现了 <see cref="IDisposable"/> 接口。
    /// </summary>
    /// <typeparam name="TKey">关键字的类型，不能为空。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class ConcurrentOneToManyQueuePool<TKey, TValue> : ConcurrentOneToManyQueue<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建并返回一个 <see cref="ConcurrentOneToManyQueuePool{TKey, TValue}"/> 的实例。
        /// </summary>
        /// <returns>创建的实例。</returns>
        public static ConcurrentOneToManyQueuePool<TKey, TValue> Create()
        {
            var a = Pool<ConcurrentOneToManyQueuePool<TKey, TValue>>.Rent();
            a._isDispose = false;
            a._isPool = true;
            return a;
        }

        /// <summary>
        /// 释放当前实例所占用的资源，并将实例返回到对象池中，以便重用。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            Clear();
            // 将实例返回到对象池中，以便重用
            Pool<ConcurrentOneToManyQueuePool<TKey, TValue>>.Return(this);
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
    /// 表示一个并发的一对多队列，用于维护具有相同键的多个值的关联关系。
    /// </summary>
    /// <typeparam name="TKey">关键字的类型，不能为空。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class ConcurrentOneToManyQueue<TKey, TValue> : ConcurrentDictionary<TKey, Queue<TValue>> where TKey : notnull
    {
        private readonly Queue<Queue<TValue>> _queue = new Queue<Queue<TValue>>();
        private readonly int _recyclingLimit;

        /// <summary>
        /// 设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public ConcurrentOneToManyQueue(int recyclingLimit = 0)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 判断指定键的队列是否包含指定值。
        /// </summary>
        /// <param name="key">要搜索的键。</param>
        /// <param name="value">要搜索的值。</param>
        /// <returns>如果队列包含值，则为 true；否则为 false。</returns>
        public bool Contains(TKey key, TValue value)
        {
            TryGetValue(key, out var list);

            return list != null && list.Contains(value);
        }

        /// <summary>
        /// 向指定键的队列中添加一个值。
        /// </summary>
        /// <param name="key">要添加值的键。</param>
        /// <param name="value">要添加的值。</param>
        public void Enqueue(TKey key, TValue value)
        {
            if (!TryGetValue(key, out var list))
            {
                list = Fetch();
                list.Enqueue(value);
                TryAdd(key, list);
                return;
            }

            list.Enqueue(value);
        }

        /// <summary>
        /// 从指定键的队列中出队并返回一个值。
        /// </summary>
        /// <param name="key">要出队的键。</param>
        /// <returns>出队的值，如果队列为空则为默认值。</returns>
        public TValue Dequeue(TKey key)
        {
            if (!TryGetValue(key, out var list) || list.Count == 0) return default;

            var value = list.Dequeue();

            if (list.Count == 0) RemoveKey(key);

            return value;
        }

        /// <summary>
        /// 尝试从指定键的队列中出队一个值。
        /// </summary>
        /// <param name="key">要出队的键。</param>
        /// <param name="value">出队的值，如果队列为空则为默认值。</param>
        /// <returns>如果成功出队，则为 true；否则为 false。</returns>
        public bool TryDequeue(TKey key, out TValue value)
        {
            value = Dequeue(key);

            return value != null;
        }

        /// <summary>
        /// 从字典中移除指定键以及其关联的队列。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveKey(TKey key)
        {
            if (!TryGetValue(key, out var list)) return;

            TryRemove(key, out _);
            Recycle(list);
        }

        /// <summary>
        /// 从队列中获取一个新的队列，如果队列为空则创建一个新的队列。
        /// </summary>
        /// <returns>获取的队列。</returns>
        private Queue<TValue> Fetch()
        {
            return _queue.Count <= 0 ? new Queue<TValue>() : _queue.Dequeue();
        }

        /// <summary>
        /// 将一个队列回收到队列池中。
        /// </summary>
        /// <param name="list">要回收的队列。</param>
        private void Recycle(Queue<TValue> list)
        {
            list.Clear();

            if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit) return;

            _queue.Enqueue(list);
        }

        /// <summary>
        /// 清空当前类的数据，包括从基类继承的键值对字典中的数据以及自定义的队列池。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _queue.Clear();
        }
    }
}
#endif