using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fantasy.Pool;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 提供一个双向映射字典对象池类，用于双向键值对映射。
    /// </summary>
    /// <typeparam name="TKey">字典中键的类型。</typeparam>
    /// <typeparam name="TValue">字典中值的类型。</typeparam>
    public class DoubleMapDictionaryPool<TKey, TValue> : DoubleMapDictionary<TKey, TValue>, IDisposable, IPool where TKey : notnull where TValue : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个新的 <see cref="DoubleMapDictionaryPool{TKey, TValue}"/> 实例。
        /// </summary>
        /// <returns>新创建的实例。</returns>
        public static DoubleMapDictionaryPool<TKey, TValue> Create()
        {
            var a = Pool<DoubleMapDictionaryPool<TKey, TValue>>.Rent();
            a._isDispose = false;
            a._isPool = true;
            return a;
        }

        /// <summary>
        /// 释放实例占用的资源。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            Clear();
            Pool<DoubleMapDictionaryPool<TKey, TValue>>.Return(this);
        }

        /// <summary>
        /// 获取一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsPool()
        {
            return _isPool;
        }

        /// <summary>
        /// 设置一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <param name="isPool"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIsPool(bool isPool)
        {
            _isPool = isPool;
        }
    }

    /// <summary>
    /// 可以实现双向映射的字典类，用于将键和值进行双向映射。
    /// </summary>
    /// <typeparam name="TK">键的类型，不能为 null。</typeparam>
    /// <typeparam name="TV">值的类型，不能为 null。</typeparam>
    public class DoubleMapDictionary<TK, TV> where TK : notnull where TV : notnull
    {
        private readonly Dictionary<TK, TV> _kv = new Dictionary<TK, TV>();
        private readonly Dictionary<TV, TK> _vk = new Dictionary<TV, TK>();

        /// <summary>
        /// 创建一个新的空的 <see cref="DoubleMapDictionary{TK, TV}"/> 实例。
        /// </summary>
        public DoubleMapDictionary() { }

        /// <summary>
        /// 创建一个新的具有指定初始容量的 <see cref="DoubleMapDictionary{TK, TV}"/> 实例。
        /// </summary>
        /// <param name="capacity">初始容量。</param>
        public DoubleMapDictionary(int capacity)
        {
            _kv = new Dictionary<TK, TV>(capacity);
            _vk = new Dictionary<TV, TK>(capacity);
        }

        /// <summary>
        /// 获取字典中键的集合。
        /// </summary>
        public Dictionary<TK, TV>.KeyCollection KeyCollection => _kv.Keys;

        /// <summary>
        /// 获取字典中值的集合。
        /// </summary>
        public Dictionary<TV, TK>.KeyCollection ValueCollection => _vk.Keys;

        /// <summary>
        /// 获取字典中的键值对数量。
        /// </summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _kv.Count;
        }

        /// <summary>
        /// 对字典中的每个键值对执行指定的操作。使用struct枚举器避免内存分配。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach(Action<TK, TV> action)
        {
            if (action == null)
            {
                return;
            }
            
            foreach (var kvp in _kv)
            {
                action(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// 将指定的键值对添加到字典中。
        /// </summary>
        /// <param name="key">要添加的键。</param>
        /// <param name="value">要添加的值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TK key, TV value)
        {
            if (_kv.TryAdd(key, value))
            {
                _vk.Add(value, key);
            }
        }

        /// <summary>
        /// 根据指定的键获取相应的值。
        /// </summary>
        /// <param name="key">要查找值的键。</param>
        /// <returns>与指定键关联的值,如果找不到键,则返回默认值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TV GetValueByKey(TK key)
        {
            _kv.TryGetValue(key, out var value);
            return value;
        }

        /// <summary>
        /// 尝试根据指定的键获取相应的值。
        /// </summary>
        /// <param name="key">要查找值的键。</param>
        /// <param name="value">如果找到，则为与指定键关联的值；否则为值的默认值。</param>
        /// <returns>如果找到键，则为 true；否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValueByKey(TK key, out TV value)
        {
            // 单次查找
            return _kv.TryGetValue(key, out value);
        }

        /// <summary>
        /// 根据指定的值获取相应的键。
        /// </summary>
        /// <param name="value">要查找键的值。</param>
        /// <returns>与指定值关联的键,如果找不到值,则返回默认键。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TK GetKeyByValue(TV value)
        {
            _vk.TryGetValue(value, out var key);
            return key;
        }

        /// <summary>
        /// 尝试根据指定的值获取相应的键。
        /// </summary>
        /// <param name="value">要查找键的值。</param>
        /// <param name="key">如果找到，则为与指定值关联的键；否则为键的默认值。</param>
        /// <returns>如果找到值，则为 true；否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetKeyByValue(TV value, out TK key)
        {
            return _vk.TryGetValue(value, out key);
        }

        /// <summary>
        /// 根据指定的键移除键值对。
        /// </summary>
        /// <param name="key">要移除的键。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveByKey(TK key)
        {
            // 单次查找并获取value，然后同时删除两个字典
            if (_kv.Remove(key, out var value))
            {
                _vk.Remove(value);
            }
        }

        /// <summary>
        /// 根据指定的值移除键值对。
        /// </summary>
        /// <param name="value">要移除的值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveByValue(TV value)
        {
            // 单次查找并获取key，然后同时删除两个字典
            if (_vk.Remove(value, out var key))
            {
                _kv.Remove(key);
            }
        }

        /// <summary>
        /// 清空字典中的所有键值对。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _kv.Clear();
            _vk.Clear();
        }

        /// <summary>
        /// 判断字典是否包含指定的键。
        /// </summary>
        /// <param name="key">要检查的键。</param>
        /// <returns>如果字典包含指定的键，则为 true；否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(TK key)
        {
            return _kv.ContainsKey(key);
        }

        /// <summary>
        /// 判断字典是否包含指定的值。
        /// </summary>
        /// <param name="value">要检查的值。</param>
        /// <returns>如果字典包含指定的值，则为 true；否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsValue(TV value)
        {
            return _vk.ContainsKey(value);
        }

        /// <summary>
        /// 判断字典是否包含指定的键值对。
        /// </summary>
        /// <param name="key">要检查的键。</param>
        /// <param name="value">要检查的值。</param>
        /// <returns>如果字典包含指定的键值对，则为 true；否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TK key, TV value)
        {
            // 先检查key存在且对应的value匹配，避免不必要的第二次查找
            return _kv.TryGetValue(key, out var foundValue) && EqualityComparer<TV>.Default.Equals(foundValue, value);
        }

        /// <summary>
        /// 添加或更新指定的键值对。如果键已存在，则更新其对应的值。
        /// </summary>
        /// <param name="key">要添加或更新的键。</param>
        /// <param name="value">要添加或更新的值。</param>
        /// <returns>如果是新增返回true，如果是更新返回false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AddOrUpdate(TK key, TV value)
        {
            // 如果key已存在，先移除旧的映射
            if (_kv.Remove(key, out var oldValue))
            {
                _vk.Remove(oldValue);
                _kv.Add(key, value);
                _vk.Add(value, key);
                return false;
            }

            // 新增
            _kv.Add(key, value);
            _vk.Add(value, key);
            return true;
        }

        /// <summary>
        /// 尝试添加或更新键值对。如果值已被其他键占用，返回false。
        /// </summary>
        /// <param name="key">要添加或更新的键。</param>
        /// <param name="value">要添加或更新的值。</param>
        /// <returns>操作成功返回true，失败返回false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAddOrUpdate(TK key, TV value)
        {
            // 检查value是否被其他key占用
            if (_vk.TryGetValue(value, out var existingKey) && !EqualityComparer<TK>.Default.Equals(existingKey, key))
            {
                return false;
            }

            // 如果key已存在，移除旧的value映射
            if (_kv.Remove(key, out var oldValue))
            {
                _vk.Remove(oldValue);
            }

            _kv[key] = value;
            _vk[value] = key;
            return true;
        }

        /// <summary>
        /// 获取字典的枚举器，用于高性能迭代。
        /// </summary>
        /// <returns>字典的结构化枚举器，避免装箱。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Dictionary<TK, TV>.Enumerator GetEnumerator()
        {
            return _kv.GetEnumerator();
        }

        /// <summary>
        /// 确保字典容量至少为指定值。用于预分配内存优化性能。
        /// </summary>
        /// <param name="capacity">最小容量。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int capacity)
        {
            _kv.EnsureCapacity(capacity);
            _vk.EnsureCapacity(capacity);
        }

        /// <summary>
        /// 将内部字典容量缩减到实际元素数量。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrimExcess()
        {
            _kv.TrimExcess();
            _vk.TrimExcess();
        }
    }
}