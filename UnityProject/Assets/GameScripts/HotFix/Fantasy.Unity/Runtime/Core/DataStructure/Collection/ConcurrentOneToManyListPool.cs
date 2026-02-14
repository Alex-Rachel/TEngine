#if !FANTASY_WEBGL
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Fantasy.Pool;

#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 并发的一对多列表池，用于维护具有相同键的多个值的关联关系，实现了 <see cref="IDisposable"/> 接口。
    /// </summary>
    /// <typeparam name="TKey">关键字的类型，不能为空。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class ConcurrentOneToManyListPool<TKey, TValue> : ConcurrentOneToManyList<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="ConcurrentOneToManyListPool{TKey, TValue}"/> 的实例。
        /// </summary>
        /// <returns>创建的实例。</returns>
        public static ConcurrentOneToManyListPool<TKey, TValue> Create()
        {
            var a = Pool<ConcurrentOneToManyListPool<TKey, TValue>>.Rent();
            a._isDispose = false;
            a._isPool = true;
            return a;
        }

        /// <summary>
        /// 释放实例占用的资源。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            // 清空实例的数据
            Clear();
            // 将实例返回到池中以便重用
            Pool<ConcurrentOneToManyListPool<TKey, TValue>>.Return(this);
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
    /// 并发的一对多列表，用于维护具有相同键的多个值的关联关系。
    /// </summary>
    /// <typeparam name="TKey">关键字的类型，不能为空。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class ConcurrentOneToManyList<TKey, TValue> : ConcurrentDictionary<TKey, List<TValue>> where TKey : notnull
    {
        private readonly Queue<List<TValue>> _queue = new Queue<List<TValue>>();
        private readonly int _recyclingLimit = 120;

        /// <summary>
        /// 初始化 <see cref="ConcurrentOneToManyList{TKey, TValue}"/> 类的新实例。
        /// </summary>
        public ConcurrentOneToManyList()
        {
        }

        /// <summary>
        /// 设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public ConcurrentOneToManyList(int recyclingLimit)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 判断指定键的列表是否包含指定值。
        /// </summary>
        /// <param name="key">要搜索的键。</param>
        /// <param name="value">要搜索的值。</param>
        /// <returns>如果列表包含值，则为 true；否则为 false。</returns>
        public bool Contains(TKey key, TValue value)
        {
            TryGetValue(key, out var list);

            return list != null && list.Contains(value);
        }

        /// <summary>
        /// 向指定键的列表中添加一个值。
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
        /// 获取指定键的列表中的第一个值。
        /// </summary>
        /// <param name="key">要获取第一个值的键。</param>
        /// <returns>指定键的列表中的第一个值，如果不存在则为默认值。</returns>
        public TValue First(TKey key)
        {
            return !TryGetValue(key, out var list) ? default : list.FirstOrDefault();
        }

        /// <summary>
        /// 从指定键的列表中移除一个值。
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
        /// 从字典中移除指定键以及其关联的列表。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveKey(TKey key)
        {
            if (!TryRemove(key, out var list)) return;

            Recycle(list);
        }

        /// <summary>
        /// 从队列中获取一个列表，如果队列为空则创建一个新的列表。
        /// </summary>
        /// <returns>获取的列表。</returns>
        private List<TValue> Fetch()
        {
            return _queue.Count <= 0 ? new List<TValue>() : _queue.Dequeue();
        }

        /// <summary>
        /// 将一个列表回收到队列中。
        /// </summary>
        /// <param name="list">要回收的列表。</param>
        private void Recycle(List<TValue> list)
        {
            list.Clear();

            if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit) return;

            _queue.Enqueue(list);
        }

        /// <summary>
        /// 清空当前类的数据，包括从基类继承的数据以及自定义的数据队列。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _queue.Clear();
        }
    }
}
#endif