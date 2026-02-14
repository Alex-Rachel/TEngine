// ReSharper disable UseCollectionExpression
using System;
using System.Collections.Generic;
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// Int64 键的可合并 FrozenDictionary 实现。
    /// 支持从多个数据源（通过 long 键标识）合并数据到单一的高性能 FrozenDictionary。
    /// </summary>
    /// <typeparam name="TValue">字典值的类型</typeparam>
    /// <remarks>
    /// 此类主要用于程序集热重载场景，可以动态地添加、移除或更新来自不同程序集的数据映射，
    /// 并自动重建合并后的 FrozenDictionary 以保持查询性能。
    /// 例如：Entity 类型序列化时，可以合并来自多个程序集的类型映射信息。
    /// </remarks>
    public sealed class Int64MergerFrozenDictionary<TValue>
    {
        /// <summary>
        /// 合并后字典中的总条目数
        /// </summary>
        private int _count;

        /// <summary>
        /// 合并后的 FrozenDictionary，提供高性能的只读查询
        /// </summary>
        private Int64FrozenDictionary<TValue> _mergedDictionary = new(new long[1] { 0 }, new TValue[1] { default });

        /// <summary>
        /// 按数据源（通过 long 键标识）存储的分段数据
        /// 键：数据源标识（如程序集 ID）
        /// 值：该数据源的 Int64 键数组和值数组
        /// </summary>
        private readonly Dictionary<long, MergeSegment<long, TValue>> _segments = new();

        /// <summary>
        /// 添加或更新指定来源的类型映射
        /// </summary>
        /// <param name="key">数据源标识（如程序集 ID）</param>
        /// <param name="int64Array">Int64 键数组</param>
        /// <param name="tValueArray">对应的值数组</param>
        /// <exception cref="ArgumentException">当 int64Array 和 tValueArray 长度不匹配时抛出</exception>
        /// <remarks>
        /// 如果指定的数据源已存在，将替换其原有数据。
        /// 每次调用都会触发字典重建以保持数据一致性。
        /// </remarks>
        public void Add(long key, long[] int64Array, TValue[] tValueArray)
        {
            var length = int64Array.Length;

            if (length != tValueArray.Length)
            {
                throw new ArgumentException("uintArray and tValueArray must have the same length");
            }

            if (length == 0)
            {
                return;
            }

            if (_segments.TryGetValue(key, out var oldSegment))
            {
                _count -= oldSegment.Count;
                _segments[key] = new MergeSegment<long, TValue>(length, int64Array, tValueArray);
            }
            else
            {
                _segments.Add(key, new MergeSegment<long, TValue>(length, int64Array, tValueArray));
            }

            _count += length;
            RebuildDictionary();
        }

        /// <summary>
        /// 批量添加多个来源的类型映射，减少重建次数
        /// </summary>
        /// <param name="items">包含数据源标识、Int64 键数组和值数组的元组集合</param>
        /// <exception cref="ArgumentException">当任何一项的 int64Array 和 tValueArray 长度不匹配时抛出</exception>
        /// <remarks>
        /// 相比多次调用 Add 方法，此方法只在所有数据添加完成后重建一次字典，提高批量操作性能。
        /// 适用于程序集初始化或批量热重载场景。
        /// </remarks>
        public void AddRange(IEnumerable<(long key, long[] int64Array, TValue[] tValueArray)> items)
        {
            var hasChanges = false;

            foreach (var (key, uintArray, tValueArray) in items)
            {
                var length = uintArray.Length;

                if (length != tValueArray.Length)
                {
                    throw new ArgumentException($"ID {key}: uintArray and tValueArray must have the same length");
                }

                if (length == 0)
                {
                    continue;
                }

                if (_segments.TryGetValue(key, out var oldSegment))
                {
                    _count -= oldSegment.Count;
                    _segments[key] = new MergeSegment<long, TValue>(length, uintArray, tValueArray);
                }
                else
                {
                    _segments.Add(key, new MergeSegment<long, TValue>(length, uintArray, tValueArray));
                }

                _count += length;
                hasChanges = true;
            }

            if (hasChanges)
            {
                RebuildDictionary();
            }
        }

        /// <summary>
        /// 移除指定来源的类型映射
        /// </summary>
        /// <param name="key">要移除的数据源标识（如程序集 ID）</param>
        /// <remarks>
        /// 如果指定的数据源不存在，此方法不执行任何操作。
        /// 移除成功后会触发字典重建。
        /// </remarks>
        public bool Remove(long key)
        {
            if (!_segments.Remove(key, out var segment))
            {
                return false;
            }

            _count -= segment.Count;
            RebuildDictionary();
            return true;
        }

        /// <summary>
        /// 重建合并后的 FrozenDictionary
        /// </summary>
        /// <remarks>
        /// 此方法将所有数据源的键值对合并到单一数组，然后创建新的 FrozenDictionary。
        /// 自动在 Add、AddRange 和 Remove 操作后调用以保持数据一致性。
        /// 如果总条目数为 0，将创建一个包含单个默认条目的字典以避免空字典。
        /// </remarks>
        private void RebuildDictionary()
        {
            if (_count == 0)
            {
                _mergedDictionary = new Int64FrozenDictionary<TValue>(new long[1] { 0 }, new TValue[1] { default });
                return;
            }

            var index = 0;
            var keys = new long[_count];
            var tValueArray = new TValue[_count];

            var keySpan = keys.AsSpan();
            var valueSpan = tValueArray.AsSpan();

            foreach (var segment in _segments.Values)
            {
                var count = segment.Count;
                segment.TArray.AsSpan(0, count).CopyTo(keySpan.Slice(index, count));
                segment.T1Array.AsSpan(0, count).CopyTo(valueSpan.Slice(index, count));
                index += count;
            }

            _mergedDictionary = new Int64FrozenDictionary<TValue>(keys, tValueArray);
        }

        /// <summary>
        /// 获取合并后的 FrozenDictionary
        /// </summary>
        /// <returns>包含所有数据源合并数据的高性能只读字典</returns>
        /// <remarks>
        /// 返回的字典是最近一次重建的结果，包含所有已添加且未移除的数据源的键值对。
        /// 此字典提供 O(1) 的查询性能，适合频繁查询场景。
        /// </remarks>
        public Int64FrozenDictionary<TValue> GetFrozenDictionary() => _mergedDictionary;
    }
}