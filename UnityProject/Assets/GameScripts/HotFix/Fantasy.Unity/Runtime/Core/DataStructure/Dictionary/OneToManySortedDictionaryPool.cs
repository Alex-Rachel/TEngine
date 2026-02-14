using System;
using System.Collections.Generic;
using Fantasy.Pool;

#pragma warning disable CS8601

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 一对多映射关系的排序字典对象池。
    /// </summary>
    /// <typeparam name="TKey">外部字典中的键类型。</typeparam>
    /// <typeparam name="TSortedKey">内部字典中的排序键类型。</typeparam>
    /// <typeparam name="TValue">内部字典中的值类型。</typeparam>
    public class OneToManySortedDictionaryPool<TKey, TSortedKey, TValue> : OneToManySortedDictionary<TKey, TSortedKey, TValue>, IDisposable, IPool where TKey : notnull where TSortedKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="OneToManySortedDictionaryPool{TKey, TSortedKey, TValue}"/> 的实例。
        /// </summary>
        /// <returns>新创建的 OneToManySortedDictionaryPool 实例。</returns>
        public static OneToManySortedDictionaryPool<TKey, TSortedKey, TValue> Create()
        {
            var a = Pool<OneToManySortedDictionaryPool<TKey, TSortedKey, TValue>>.Rent();
            a._isDispose = false;
            a._isPool = true;
            return a;
        }

        /// <summary>
        /// 释放当前实例及其资源。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            Clear();
            Pool<OneToManySortedDictionaryPool<TKey, TSortedKey, TValue>>.Return(this);
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
    /// 一对多映射关系的排序字典。每个外部键映射到一个内部排序字典，该内部排序字典将排序键映射到相应的值。
    /// </summary>
    /// <typeparam name="TKey">外部字典中的键类型。</typeparam>
    /// <typeparam name="TSortedKey">内部字典中的排序键类型。</typeparam>
    /// <typeparam name="TValue">内部字典中的值类型。</typeparam>
    public class
        OneToManySortedDictionary<TKey, TSortedKey, TValue> : Dictionary<TKey, SortedDictionary<TSortedKey, TValue>>
        where TSortedKey : notnull where TKey : notnull
    {
        /// 缓存队列的回收限制
        private readonly int _recyclingLimit = 120;
        /// 缓存队列，用于存储已回收的内部排序字典
        private readonly Queue<SortedDictionary<TSortedKey, TValue>> _queue = new Queue<SortedDictionary<TSortedKey, TValue>>();

        /// <summary>
        /// 创建一个新的 <see cref="OneToManySortedDictionary{TKey, TSortedKey, TValue}"/> 实例。
        /// </summary>
        protected OneToManySortedDictionary() { }

        /// <summary>
        /// 创建一个新的 <see cref="OneToManySortedDictionary{TKey, TSortedKey, TValue}"/> 实例。设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public OneToManySortedDictionary(int recyclingLimit)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 检查字典是否包含指定的外部键。
        /// </summary>
        /// <param name="key">要检查的外部键。</param>
        /// <returns>如果字典包含指定的外部键，则为 true；否则为 false。</returns>
        public bool Contains(TKey key)
        {
            return this.ContainsKey(key);
        }

        /// <summary>
        /// 检查字典是否包含指定的外部键和排序键。
        /// </summary>
        /// <param name="key">要检查的外部键。</param>
        /// <param name="sortedKey">要检查的排序键。</param>
        /// <returns>如果字典包含指定的外部键和排序键，则为 true；否则为 false。</returns>
        public bool Contains(TKey key, TSortedKey sortedKey)
        {
            return TryGetValue(key, out var dic) && dic.ContainsKey(sortedKey);
        }

        /// <summary>
        /// 尝试从字典中获取指定外部键对应的内部排序字典。
        /// </summary>
        /// <param name="key">要获取内部排序字典的外部键。</param>
        /// <param name="dic">获取到的内部排序字典，如果找不到则为 null。</param>
        /// <returns>如果找到内部排序字典，则为 true；否则为 false。</returns>
        public new bool TryGetValue(TKey key, out SortedDictionary<TSortedKey, TValue> dic)
        {
            return base.TryGetValue(key, out dic);
        }

        /// <summary>
        /// 尝试从字典中获取指定外部键和排序键对应的值。
        /// </summary>
        /// <param name="key">要获取值的外部键。</param>
        /// <param name="sortedKey">要获取值的排序键。</param>
        /// <param name="value">获取到的值，如果找不到则为 default。</param>
        /// <returns>如果找到值，则为 true；否则为 false。</returns>
        public bool TryGetValueBySortedKey(TKey key, TSortedKey sortedKey, out TValue value)
        {
            if (base.TryGetValue(key, out var dic))
            {
                return dic.TryGetValue(sortedKey, out value);
            }

            value = default;
            return false;
        }

        /// <summary>
        /// 向字典中添加一个值，关联到指定的外部键和排序键。
        /// </summary>
        /// <param name="key">要关联值的外部键。</param>
        /// <param name="sortedKey">要关联值的排序键。</param>
        /// <param name="value">要添加的值。</param>
        public void Add(TKey key, TSortedKey sortedKey, TValue value)
        {
            if (!TryGetValue(key, out var dic))
            {
                dic = Fetch();
                dic.Add(sortedKey, value);
                Add(key, dic);

                return;
            }

            dic.Add(sortedKey, value);
        }

        /// <summary>
        /// 从字典中移除指定外部键和排序键关联的值。
        /// </summary>
        /// <param name="key">要移除值的外部键。</param>
        /// <param name="sortedKey">要移除值的排序键。</param>
        /// <returns>如果成功移除值，则为 true；否则为 false。</returns>
        public bool RemoveSortedKey(TKey key, TSortedKey sortedKey)
        {
            if (!TryGetValue(key, out var dic))
            {
                return false;
            }

            var isRemove = dic.Remove(sortedKey);

            if (dic.Count == 0)
            {
                isRemove = RemoveKey(key);
            }

            return isRemove;
        }

        /// <summary>
        /// 从字典中移除指定外部键及其关联的所有值。
        /// </summary>
        /// <param name="key">要移除的外部键。</param>
        /// <returns>如果成功移除外部键及其关联的所有值，则为 true；否则为 false。</returns>
        public bool RemoveKey(TKey key)
        {
            if (!TryGetValue(key, out var list))
            {
                return false;
            }

            Remove(key);
            Recycle(list);
            return true;
        }

        /// <summary>
        /// 从缓存队列中获取一个内部排序字典。
        /// </summary>
        /// <returns>一个内部排序字典。</returns>
        private SortedDictionary<TSortedKey, TValue> Fetch()
        {
            return _queue.Count <= 0 ? new SortedDictionary<TSortedKey, TValue>() : _queue.Dequeue();
        }

        /// <summary>
        /// 回收一个内部排序字典到缓存队列。
        /// </summary>
        /// <param name="dic">要回收的内部排序字典。</param>
        private void Recycle(SortedDictionary<TSortedKey, TValue> dic)
        {
            dic.Clear();

            if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit)
            {
                return;
            }

            _queue.Enqueue(dic);
        }

        /// <summary>
        /// 清空字典以及内部排序字典缓存队列，释放所有资源。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _queue.Clear();
        }
    }
}