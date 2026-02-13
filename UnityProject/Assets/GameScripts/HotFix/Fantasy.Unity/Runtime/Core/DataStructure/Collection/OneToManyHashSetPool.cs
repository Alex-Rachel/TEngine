using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fantasy.Pool;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 一对多哈希集合（OneToManyHashSet）对象池。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public sealed class OneToManyHashSetPool<TKey, TValue> : OneToManyHashSet<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="OneToManyHashSetPool{TKey, TValue}"/> 一对多哈希集合（OneToManyHashSet）对象池的实例。
        /// </summary>
        /// <returns>创建的实例。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OneToManyHashSetPool<TKey, TValue> Create()
        {
            var a = Pool<OneToManyHashSetPool<TKey, TValue>>.Rent();
            a._isDispose = false;
            a._isPool = true;
            return a;
        }

        /// <summary>
        /// 释放实例所占用的资源，并将实例返回到对象池中，以便重用。
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
            Pool<OneToManyHashSetPool<TKey, TValue>>.Return(this);
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
    /// 一对多哈希集合（OneToManyHashSet），用于创建和管理键对应多个值的集合。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class OneToManyHashSet<TKey, TValue> : Dictionary<TKey, HashSet<TValue>> where TKey : notnull
    {
        /// 用于回收和重用的空闲值集合栈（Stack比Queue性能更好）。
        private readonly Stack<HashSet<TValue>> _pool = new Stack<HashSet<TValue>>();
        /// 设置最大回收限制，用于控制值集合的最大数量。
        private readonly int _recyclingLimit = 120;
        /// 一个空的、不包含任何元素的哈希集合，用于在查找失败时返回。
        private static readonly HashSet<TValue> _empty = new HashSet<TValue>();

        /// <summary>
        /// 初始化 <see cref="OneToManyHashSet{TKey, TValue}"/> 类的新实例。
        /// </summary>
        public OneToManyHashSet() { }

        /// <summary>
        /// 设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        ///     1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        ///     2:设置成0不控制数量，全部缓存
        /// </param>
        public OneToManyHashSet(int recyclingLimit)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 判断指定的键值对是否存在于集合中。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        /// <returns>如果存在则为 true，否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey key, TValue value)
        {
            return TryGetValue(key, out var list) && list.Contains(value);
        }

        /// <summary>
        /// 添加指定的键值对到集合中。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TKey key, TValue value)
        {
            if (!TryGetValue(key, out var list))
            {
                list = Fetch();
                list.Add(value);
                base.Add(key, list);
                return;
            }

            list.Add(value);
        }

        /// <summary>
        /// 从集合中移除指定键对应的值。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">要移除的值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        /// 从集合中移除指定键及其对应的值集合。
        /// </summary>
        /// <param name="key">键。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        /// 获取指定键对应的值集合，如果不存在则返回一个空的哈希集合。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>对应的值集合或空的哈希集合。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HashSet<TValue> GetValue(TKey key)
        {
            return TryGetValue(key, out var value) ? value : _empty;
        }

        /// <summary>
        /// 从栈中获取一个空闲的值集合，或者创建一个新的。
        /// </summary>
        /// <returns>值集合。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private HashSet<TValue> Fetch()
        {
            return _pool.Count > 0 ? _pool.Pop() : new HashSet<TValue>();
        }

        /// <summary>
        /// 回收值集合到栈中，以便重复利用。
        /// </summary>
        /// <param name="list">要回收的值集合。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Recycle(HashSet<TValue> list)
        {
            list.Clear();

            // 优化：先检查是否需要限制，避免不必要的比较
            if (_recyclingLimit == 0 || _pool.Count < _recyclingLimit)
            {
                _pool.Push(list);
            }
        }

        /// <summary>
        /// 清空集合中的数据和对象池。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _pool.Clear();
        }
    }
}