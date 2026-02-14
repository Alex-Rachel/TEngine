using System;
using System.Collections.Generic;
using Fantasy.Pool;

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 基于排序字典实现的一对多关系的映射哈希集合的对象池包装类，将唯一键映射到多个值的哈希集合。
    /// 同时实现了 <see cref="IDisposable"/> 接口，以支持对象的重用和释放。
    /// </summary>
    /// <typeparam name="TKey">字典中键的类型。</typeparam>
    /// <typeparam name="TValue">哈希集合中值的类型。</typeparam>
    public class SortedOneToManyHashSetPool<TKey, TValue> : SortedOneToManyHashSet<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="SortedOneToManyHashSetPool{TKey, TValue}"/> 实例。
        /// </summary>
        /// <returns>新创建的实例。</returns>
        public static SortedOneToManyHashSetPool<TKey, TValue> Create()
        {
            var a = Pool<SortedOneToManyHashSetPool<TKey, TValue>>.Rent();
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
            Pool<SortedOneToManyHashSetPool<TKey, TValue>>.Return(this);
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
    /// 基于排序字典实现的一对多关系的映射哈希集合类，将唯一键映射到多个值的哈希集合。
    /// 用于在多个值与一个键关联的情况下进行管理和存储。
    /// </summary>
    /// <typeparam name="TKey">字典中键的类型。</typeparam>
    /// <typeparam name="TValue">集合中值的类型。</typeparam>
    public class SortedOneToManyHashSet<TKey, TValue> : SortedDictionary<TKey, HashSet<TValue>> where TKey : notnull
    {
        private readonly Queue<HashSet<TValue>> _queue = new Queue<HashSet<TValue>>();
        private readonly int _recyclingLimit = 120;

        /// <summary>
        /// 创建一个新的 <see cref="SortedOneToManyHashSet{TKey, TValue}"/> 实例。
        /// </summary>
        public SortedOneToManyHashSet() { }

        /// <summary>
        /// 创建一个新的 <see cref="SortedOneToManyHashSet{TKey, TValue}"/> 实例，设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public SortedOneToManyHashSet(int recyclingLimit)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 判断哈希集合中是否包含指定的键值对。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <param name="value">要查找的值。</param>
        /// <returns>如果键值对存在，则为 true；否则为 false。</returns>
        public bool Contains(TKey key, TValue value)
        {
            TryGetValue(key, out var list);

            return list != null && list.Contains(value);
        }

        /// <summary>
        /// 将指定值添加到给定键关联的哈希集合中。
        /// </summary>
        /// <param name="key">要添加值的键。</param>
        /// <param name="value">要添加的值。</param>
        public void Add(TKey key, TValue value)
        {
            if (!TryGetValue(key, out var list))
            {
                list = Fetch();
                list.Add(value);
                Add(key, list);

                return;
            }

            list.Add(value);
        }

        /// <summary>
        /// 从指定键关联的哈希集合中移除特定值。
        /// 如果哈希集合不存在或值不存在于集合中，则不执行任何操作。
        /// </summary>
        /// <param name="key">要移除值的键。</param>
        /// <param name="value">要移除的值。</param>
        public void RemoveValue(TKey key, TValue value)
        {
            if (!TryGetValue(key, out var list)) return;

            list.Remove(value);

            if (list.Count == 0) RemoveKey(key);
        }

        /// <summary>
        /// 从字典中移除指定键以及关联的哈希集合，并将集合进行回收。
        /// 如果键不存在于映射列表中，则不执行任何操作。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveKey(TKey key)
        {
            if (!TryGetValue(key, out var list)) return;

            Remove(key);

            Recycle(list);
        }

        /// <summary>
        /// 获取一个空的或回收的哈希集合。
        /// </summary>
        /// <returns>获取的哈希集合实例。</returns>
        private HashSet<TValue> Fetch()
        {
            return _queue.Count <= 0 ? new HashSet<TValue>() : _queue.Dequeue();
        }

        /// <summary>
        /// 回收一个哈希集合，将其清空并放入回收队列中。
        /// </summary>
        /// <param name="list">要回收的哈希集合。</param>
        private void Recycle(HashSet<TValue> list)
        {
            list.Clear();

            if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit) return;

            _queue.Enqueue(list);
        }

        /// <summary>
        /// 重写 Clear 方法，清空字典并清空回收队列。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _queue.Clear();
        }
    }
}