#if !FANTASY_WEBGL
using System;
using System.Collections.Generic;
using System.Linq;
using Fantasy.Pool;

#pragma warning disable CS8603

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 基于排序字典和并发集合实现的一对多映射列表的对象池包装类，继承自 <see cref="SortedConcurrentOneToManyList{TKey, TValue}"/> 类，
    /// 同时实现了 <see cref="IDisposable"/> 接口，以支持对象的重用和释放。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class SortedConcurrentOneToManyListPool<TKey, TValue> : SortedConcurrentOneToManyList<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个新的 <see cref="SortedConcurrentOneToManyListPool{TKey, TValue}"/> 实例，使用默认的参数设置。
        /// </summary>
        /// <returns>新创建的 <see cref="SortedConcurrentOneToManyListPool{TKey, TValue}"/> 实例。</returns>
        public static SortedConcurrentOneToManyListPool<TKey, TValue> Create()
        {
            var a = Pool<SortedConcurrentOneToManyListPool<TKey, TValue>>.Rent();
            a._isDispose = false;
            a._isPool = true;
            return a;
        }

        /// <summary>
        /// 释放当前对象池实例，将其返回到对象池以供重用。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            Clear();
            Pool<SortedConcurrentOneToManyListPool<TKey, TValue>>.Return(this);
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
    /// 基于排序字典和并发集合实现的一多对映射列表类，继承自 <see cref="SortedDictionary{TKey, TValue}"/> 类，
    /// 用于在多个值与一个键关联的情况下进行管理和存储。该类支持并发操作，适用于多线程环境。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class SortedConcurrentOneToManyList<TKey, TValue> : SortedDictionary<TKey, List<TValue>> where TKey : notnull
    {
        /// 用于同步操作的锁对象，它确保在多线程环境下对数据的安全访问。
        private readonly object _lockObject = new object();
        /// 用于存储缓存的队列。
        private readonly Queue<List<TValue>> _queue = new Queue<List<TValue>>();
        /// 控制缓存回收的限制。当缓存的数量超过此限制时，旧的缓存将会被回收。
        private readonly int _recyclingLimit;

        /// <summary>
        /// 初始化一个新的 <see cref="SortedConcurrentOneToManyList{TKey, TValue}"/> 类的实例，使用默认的参数设置。
        /// </summary>
        public SortedConcurrentOneToManyList()
        {
        }

        /// <summary>
        /// 初始化一个新的 <see cref="SortedConcurrentOneToManyList{TKey, TValue}"/> 类的实例，指定最大缓存数量。
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public SortedConcurrentOneToManyList(int recyclingLimit = 0)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 检查指定的键和值是否存在于映射列表中。
        /// </summary>
        /// <param name="key">要检查的键。</param>
        /// <param name="value">要检查的值。</param>
        /// <returns>如果存在，则为 true；否则为 false。</returns>
        public bool Contains(TKey key, TValue value)
        {
            lock (_lockObject)
            {
                TryGetValue(key, out var list);

                return list != null && list.Contains(value);
            }
        }

        /// <summary>
        /// 将指定的值添加到与指定键关联的列表中。
        /// </summary>
        /// <param name="key">要关联值的键。</param>
        /// <param name="value">要添加到列表的值。</param>
        public void Add(TKey key, TValue value)
        {
            lock (_lockObject)
            {
                if (!TryGetValue(key, out var list))
                {
                    list = Fetch();
                    list.Add(value);
                    base[key] = list;
                    return;
                }

                list.Add(value);
            }
        }

        /// <summary>
        /// 获取与指定键关联的列表中的第一个值。
        /// 如果列表不存在或为空，则返回默认值。
        /// </summary>
        /// <param name="key">要获取第一个值的键。</param>
        /// <returns>第一个值，或默认值。</returns>
        public TValue First(TKey key)
        {
            lock (_lockObject)
            {
                return !TryGetValue(key, out var list) ? default : list.FirstOrDefault();
            }
        }

        /// <summary>
        /// 从与指定键关联的列表中移除指定的值。
        /// 如果列表不存在或值不存在于列表中，则不执行任何操作。
        /// </summary>
        /// <param name="key">要移除值的键。</param>
        /// <param name="value">要移除的值。</param>
        public void RemoveValue(TKey key, TValue value)
        {
            lock (_lockObject)
            {
                if (!TryGetValue(key, out var list)) return;

                list.Remove(value);

                if (list.Count == 0) RemoveKey(key);
            }
        }

        /// <summary>
        /// 从映射列表中移除指定的键及其关联的列表。
        /// 如果键不存在于映射列表中，则不执行任何操作。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveKey(TKey key)
        {
            lock (_lockObject)
            {
                if (!TryGetValue(key, out var list)) return;

                Remove(key);

                Recycle(list);
            }
        }

        /// <summary>
        /// 从缓存中获取一个可重用的列表。如果缓存中不存在列表，则创建一个新的列表并返回。
        /// </summary>
        /// <returns>可重用的列表。</returns>
        private List<TValue> Fetch()
        {
            lock (_lockObject)
            {
                return _queue.Count <= 0 ? new List<TValue>() : _queue.Dequeue();
            }
        }

        /// <summary>
        /// 将不再使用的列表回收到缓存中，以便重复利用。如果缓存数量超过限制，则丢弃列表而不进行回收。
        /// </summary>
        /// <param name="list">要回收的列表。</param>
        private void Recycle(List<TValue> list)
        {
            lock (_lockObject)
            {
                list.Clear();

                if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit) return;

                _queue.Enqueue(list);
            }
        }

        /// <summary>
        /// 清空映射列表以及队列。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _queue.Clear();
        }
    }
}
#endif