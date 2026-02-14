using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 线程安全的哈希Set, 兼容新旧.NET版本
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class ConcurrentHashSet<T> : IEnumerable<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, byte> _dict;
        /// <summary>
        /// 构造方法
        /// </summary>
        public ConcurrentHashSet()
        {
            _dict = new ConcurrentDictionary<T, byte>();
        }
        /// <summary>
        /// 构造方法
        /// </summary>
        /// <param name="comparer"></param>
        public ConcurrentHashSet(IEqualityComparer<T> comparer)
        {
            _dict = new ConcurrentDictionary<T, byte>(comparer);
        }
        /// <summary>
        /// 尝试添加
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool TryAdd(T item) => _dict.TryAdd(item, 0);
        /// <summary>
        /// 移除
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool TryRemove(T item) => _dict.TryRemove(item, out _);
        /// <summary>
        /// 移除
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(T item) => _dict.Remove(item, out _);
        /// <summary>
        /// 检查是否包含
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Contains(T item) => _dict.ContainsKey(item);
        /// <summary>
        /// 数量
        /// </summary>
        public int Count => _dict.Count;
        /// <summary>
        /// 计数
        /// </summary>
        public void Clear() => _dict.Clear();
        /// <summary>
        /// 转为数组
        /// </summary>
        public T[] ToArray() => _dict.Keys.ToArray();

        /// <summary>
        /// 泛型枚举器
        /// </summary>
        public IEnumerator<T> GetEnumerator() => _dict.Keys.GetEnumerator();

        // 非泛型枚举器（必须返回 System.Collections.IEnumerator）
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_dict.Keys).GetEnumerator();
    }
}
