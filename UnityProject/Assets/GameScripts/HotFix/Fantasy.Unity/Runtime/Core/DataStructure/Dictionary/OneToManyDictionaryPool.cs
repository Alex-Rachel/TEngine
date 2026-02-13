using System;
using System.Collections.Generic;
using System.Linq;
using Fantasy.Pool;

#pragma warning disable CS8603
#pragma warning disable CS8601

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 一对多映射关系的字典对象池。
    /// </summary>
    /// <typeparam name="TKey">外部字典中的键类型。</typeparam>
    /// <typeparam name="TValueKey">内部字典中的键类型。</typeparam>
    /// <typeparam name="TValue">内部字典中的值类型。</typeparam>
    public class OneToManyDictionaryPool<TKey, TValueKey, TValue> : OneToManyDictionary<TKey, TValueKey, TValue>, IDisposable, IPool where TKey : notnull where TValueKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="OneToManyDictionaryPool{TKey, TValueKey, TValue}"/> 的实例。
        /// </summary>
        /// <returns>新创建的 OneToManyDictionaryPool 实例。</returns>
        public static OneToManyDictionaryPool<TKey, TValueKey, TValue> Create()
        {
            var a = Pool<OneToManyDictionaryPool<TKey, TValueKey, TValue>>.Rent();
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
            Pool<OneToManyDictionaryPool<TKey, TValueKey, TValue>>.Return(this);
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
    /// 一对多映射关系的字典。每个键都对应一个内部字典，该内部字典将键值映射到相应的值。
    /// </summary>
    /// <typeparam name="TKey">外部字典中的键类型。</typeparam>
    /// <typeparam name="TValueKey">内部字典中的键类型。</typeparam>
    /// <typeparam name="TValue">内部字典中的值类型。</typeparam>
    public class OneToManyDictionary<TKey, TValueKey, TValue> : Dictionary<TKey, Dictionary<TValueKey, TValue>>
        where TKey : notnull where TValueKey : notnull
    {
        private readonly Queue<Dictionary<TValueKey, TValue>> _queue = new Queue<Dictionary<TValueKey, TValue>>();
        private readonly int _recyclingLimit = 120;

        /// <summary>
        /// 创建一个新的 <see cref="OneToManyDictionary{TKey, TValueKey, TValue}"/> 实例。
        /// </summary>
        public OneToManyDictionary() { }

        /// <summary>
        /// 创建一个新的 <see cref="OneToManyDictionary{TKey, TValueKey, TValue}"/> 实例，并指定最大缓存数量。
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public OneToManyDictionary(int recyclingLimit = 0)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 检查是否包含指定的键值对。
        /// </summary>
        /// <param name="key">外部字典中的键。</param>
        /// <param name="valueKey">内部字典中的键。</param>
        /// <returns>如果包含指定的键值对，则为 true；否则为 false。</returns>
        public bool Contains(TKey key, TValueKey valueKey)
        {
            TryGetValue(key, out var dic);

            return dic != null && dic.ContainsKey(valueKey);
        }

        /// <summary>
        /// 尝试获取指定键值对的值。
        /// </summary>
        /// <param name="key">外部字典中的键。</param>
        /// <param name="valueKey">内部字典中的键。</param>
        /// <param name="value">获取的值，如果操作成功，则为值；否则为默认值。</param>
        /// <returns>如果操作成功，则为 true；否则为 false。</returns>
        public bool TryGetValue(TKey key, TValueKey valueKey, out TValue value)
        {
            value = default;
            return TryGetValue(key, out var dic) && dic.TryGetValue(valueKey, out value);
        }

        /// <summary>
        /// 获取指定键的第一个值。
        /// </summary>
        /// <param name="key">要获取第一个值的键。</param>
        public TValue First(TKey key)
        {
            return !TryGetValue(key, out var dic) ? default : dic.First().Value;
        }

        /// <summary>
        /// 向字典中添加指定的键值对。
        /// </summary>
        /// <param name="key">要添加键值对的键。</param>
        /// <param name="valueKey">要添加键值对的内部字典键。</param>
        /// <param name="value">要添加的值。</param>
        public void Add(TKey key, TValueKey valueKey, TValue value)
        {
            if (!TryGetValue(key, out var dic))
            {
                dic = Fetch();
                dic[valueKey] = value;
                // dic.Add(valueKey, value);
                Add(key, dic);

                return;
            }

            dic[valueKey] = value;
            // dic.Add(valueKey, value);
        }

        /// <summary>
        /// 从字典中移除指定的键值对。
        /// </summary>
        /// <param name="key">要移除键值对的键。</param>
        /// <param name="valueKey">要移除键值对的内部字典键。</param>
        /// <returns>如果成功移除键值对，则为 true；否则为 false。</returns>
        public bool Remove(TKey key, TValueKey valueKey)
        {
            if (!TryGetValue(key, out var dic)) return false;

            var result = dic.Remove(valueKey);

            if (dic.Count == 0) RemoveKey(key);

            return result;
        }

        /// <summary>
        /// 从字典中移除指定的键值对。
        /// </summary>
        /// <param name="key">要移除键值对的键。</param>
        /// <param name="valueKey">要移除键值对的内部字典键。</param>
        /// <param name="value">如果成功移除键值对，则为移除的值；否则为默认值。</param>
        /// <returns>如果成功移除键值对，则为 true；否则为 false。</returns>
        public bool Remove(TKey key, TValueKey valueKey, out TValue value)
        {
            if (!TryGetValue(key, out var dic))
            {
                value = default;
                return false;
            }

            var result = dic.TryGetValue(valueKey, out value);

            if (result) dic.Remove(valueKey);

            if (dic.Count == 0) RemoveKey(key);

            return result;
        }

        /// <summary>
        /// 移除字典中的指定键及其相关的所有键值对。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        public void RemoveKey(TKey key)
        {
            if (!TryGetValue(key, out var dic)) return;

            Remove(key);
            Recycle(dic);
        }

        /// <summary>
        /// 从对象池中获取一个内部字典实例，如果池中没有，则创建一个新实例。
        /// </summary>
        /// <returns>获取的内部字典实例。</returns>
        private Dictionary<TValueKey, TValue> Fetch()
        {
            return _queue.Count <= 0 ? new Dictionary<TValueKey, TValue>() : _queue.Dequeue();
        }

        /// <summary>
        /// 将不再使用的内部字典实例放回对象池中，以便后续重用。
        /// </summary>
        /// <param name="dic">要放回对象池的内部字典实例。</param>
        private void Recycle(Dictionary<TValueKey, TValue> dic)
        {
            dic.Clear();

            if (_recyclingLimit != 0 && _queue.Count > _recyclingLimit) return;

            _queue.Enqueue(dic);
        }

        /// <summary>
        /// 清空字典中的所有键值对，并将不再使用的内部字典实例放回对象池中。
        /// </summary>
        public new void Clear()
        {
            foreach (var keyValuePair in this) Recycle(keyValuePair.Value);

            base.Clear();
        }
    }
}