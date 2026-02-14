using System;
using System.Collections.Generic;
using System.Linq;
using Fantasy.Pool;

#pragma warning disable CS8603

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 基于排序字典实现的一对多映射列表的对象池包装类，继承自 <see cref="SortedOneToManyList{TKey, TValue}"/> 类，
    /// 同时实现了 <see cref="IDisposable"/> 接口，以支持对象的重用和释放。
    /// </summary>
    /// <typeparam name="TKey">字典中键的类型。</typeparam>
    /// <typeparam name="TValue">列表中值的类型。</typeparam>
    public class SortedOneToManyListPool<TKey, TValue> : SortedOneToManyList<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="SortedOneToManyListPool{TKey, TValue}"/> 实例。
        /// </summary>
        /// <returns>新创建的实例。</returns>
        public static SortedOneToManyListPool<TKey, TValue> Create()
        {
            var a = Pool<SortedOneToManyListPool<TKey, TValue>>.Rent();
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
            Pool<SortedOneToManyListPool<TKey, TValue>>.Return(this);
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
    /// 基于排序字典实现的一对多关系的映射列表类，将唯一键映射到包含多个值的列表。
    /// 用于在多个值与一个键关联的情况下进行管理和存储。
    /// </summary>
    /// <typeparam name="TKey">字典中键的类型。</typeparam>
    /// <typeparam name="TValue">列表中值的类型。</typeparam>
    public class SortedOneToManyList<TKey, TValue> : SortedDictionary<TKey, List<TValue>> where TKey : notnull
    {
        private readonly Queue<List<TValue>> _queue = new Queue<List<TValue>>();
        private readonly int _recyclingLimit;

        /// <summary>
        /// 创建一个新的 <see cref="SortedOneToManyList{TKey, TValue}"/> 实例。
        /// </summary>
        public SortedOneToManyList()
        {
        }

        /// <summary>
        /// 创建一个新的 <see cref="SortedOneToManyList{TKey, TValue}"/> 实例，设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public SortedOneToManyList(int recyclingLimit = 0)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 判断列表中是否包含指定的键值对。
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
        /// 将指定值添加到给定键关联的列表中。
        /// </summary>
        /// <param name="key">要添加值的键。</param>
        /// <param name="value">要添加的值。</param>
        public void Add(TKey key, TValue value)
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

        /// <summary>
        /// 获取指定键关联的列表中的第一个值。
        /// </summary>
        /// <param name="key">要查找值的键。</param>
        /// <returns>指定键关联的列表中的第一个值，如果列表为空则返回默认值。</returns>
        public TValue First(TKey key)
        {
            return !TryGetValue(key, out var list) ? default : list.FirstOrDefault();
        }

        /// <summary>
        /// 从指定键关联的列表中移除特定值。
        /// </summary>
        /// <param name="key">要移除值的键。</param>
        /// <param name="value">要移除的值。</param>

        public void RemoveValue(TKey key, TValue value)
        {
            if (!TryGetValue(key, out var list))
            {
                return;
            }

            list.Remove(value);

            if (list.Count == 0)
            {
                RemoveKey(key);
            }
        }

        /// <summary>
        /// 从字典中移除指定键以及关联的列表，并将列表进行回收。
        /// </summary>
        /// <param name="key">要移除的键。</param>

        public void RemoveKey(TKey key)
        {
            if (!TryGetValue(key, out var list))
            {
                return;
            }

            Remove(key);
            Recycle(list);
        }

        /// <summary>
        /// 获取一个空的或回收的列表。
        /// </summary>
        /// <returns>获取的列表实例。</returns>
        private List<TValue> Fetch()
        {
            return _queue.Count <= 0 ? new List<TValue>() : _queue.Dequeue();
        }

        /// <summary>
        /// 回收一个列表，将其清空并放入回收队列中。如果缓存数量超过限制，则丢弃列表而不进行回收
        /// </summary>
        /// <param name="list">要回收的列表。</param>
        private void Recycle(List<TValue> list)
        {
            list.Clear();

            if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit)
            {
                return;
            }

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