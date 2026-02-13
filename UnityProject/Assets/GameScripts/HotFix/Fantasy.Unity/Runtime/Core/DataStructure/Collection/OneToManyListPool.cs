using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fantasy.Pool;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 可回收的、一对多关系的列表池。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public sealed class OneToManyListPool<TKey, TValue> : OneToManyList<TKey, TValue>, IDisposable, IPool where TKey : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个 <see cref="OneToManyListPool{TKey, TValue}"/> 一对多关系的列表池的实例。
        /// </summary>
        /// <returns>创建的实例。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OneToManyListPool<TKey, TValue> Create()
        {
            var list = Pool<OneToManyListPool<TKey, TValue>>.Rent();
            list._isDispose = false;
            list._isPool = true;
            return list;
        }

        /// <summary>
        /// 释放当前对象所占用的资源，并将对象回收到对象池中。
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
            Pool<OneToManyListPool<TKey, TValue>>.Return(this);
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
    /// 一对多关系的列表字典。
    /// </summary>
    /// <typeparam name="TKey">键的类型。</typeparam>
    /// <typeparam name="TValue">值的类型。</typeparam>
    public class OneToManyList<TKey, TValue> : Dictionary<TKey, List<TValue>> where TKey : notnull
    {
        /// 用于回收和重用的空闲值集合栈（Stack比Queue性能更好）。
        private readonly Stack<List<TValue>> _pool = new Stack<List<TValue>>();
        /// 设置最大回收限制，用于控制值集合的最大数量。
        private readonly int _recyclingLimit = 120;
        /// 一个空的、不包含任何元素的列表，用于在查找失败时返回。
        private static readonly List<TValue> _empty = new List<TValue>();

        /// <summary>
        /// 初始化一个新的 <see cref="OneToManyList{TKey, TValue}"/> 实例。
        /// </summary>
        public OneToManyList() { }

        /// <summary>
        /// 设置最大缓存数量
        /// </summary>
        /// <param name="recyclingLimit">
        /// 1:防止数据量过大、所以超过recyclingLimit的数据还是走GC.
        /// 2:设置成0不控制数量，全部缓存
        /// </param>
        public OneToManyList(int recyclingLimit)
        {
            _recyclingLimit = recyclingLimit;
        }

        /// <summary>
        /// 判断给定的键和值是否存在于列表中。
        /// </summary>
        /// <param name="key">要搜索的键。</param>
        /// <param name="value">要搜索的值。</param>
        /// <returns>如果存在则为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey key, TValue value)
        {
            return TryGetValue(key, out var list) && list.Contains(value);
        }

        /// <summary>
        /// 向列表中添加指定键和值。
        /// </summary>
        /// <param name="key">要添加值的键。</param>
        /// <param name="value">要添加的值。</param>
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
        /// 获取指定键对应的列表中的第一个值。
        /// </summary>
        /// <param name="key">要获取值的键。</param>
        /// <returns>键对应的列表中的第一个值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue First(TKey key)
        {
            if (!TryGetValue(key, out var list) || list.Count == 0)
            {
                return default;
            }

            return list[0];
        }

        /// <summary>
        /// 从列表中移除指定键和值。
        /// </summary>
        /// <param name="key">要移除值的键。</param>
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
        /// 从列表中移除指定键及其关联的所有值。
        /// </summary>
        /// <param name="key">要移除的键。</param>
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
        /// 获取指定键关联的所有值的列表。
        /// </summary>
        /// <param name="key">要获取值的键。</param>
        /// <returns>键关联的所有值的列表。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<TValue> GetValues(TKey key)
        {
            return TryGetValue(key, out var list) ? list : _empty;
        }

        /// <summary>
        /// 清空字典中的数据和对象池。
        /// </summary>
        protected new void Clear()
        {
            base.Clear();
            _pool.Clear();
        }

        /// <summary>
        /// 从栈中获取一个空闲的值集合，或者创建一个新的。
        /// </summary>
        /// <returns>值集合。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<TValue> Fetch()
        {
            return _pool.Count > 0 ? _pool.Pop() : new List<TValue>();
        }

        /// <summary>
        /// 回收值集合到栈中，以便重复利用。
        /// </summary>
        /// <param name="list">要回收的值集合。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Recycle(List<TValue> list)
        {
            list.Clear();

            // 优化：先检查是否需要限制，避免不必要的比较
            if (_recyclingLimit == 0 || _pool.Count < _recyclingLimit)
            {
                _pool.Push(list);
            }
        }
    }
}
