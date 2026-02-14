// ReSharper disable UseCollectionExpression
using System;
using System.Collections.Generic;
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 动态合并多个类型映射段到单个FrozenDictionary的合并器
    /// </summary>
    /// <typeparam name="TValue">映射的值类型</typeparam>
    public sealed class UInt32MergerFrozenDictionary<TValue>
    {
        private int _count;

        private UInt32FrozenDictionary<TValue> _mergedDictionary = new(new uint[1] { 0 }, new TValue[1] { default });

        private readonly Dictionary<long, MergeSegment<uint, TValue>> _segments = new();

        /// <summary>
        /// 添加或更新指定来源的类型映射
        /// </summary>
        public void Add(long key, uint[] uintArray, TValue[] tValueArray)
        {
            var length = uintArray.Length;

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
                _segments[key] = new MergeSegment<uint, TValue>(length, uintArray, tValueArray);
            }
            else
            {
                _segments.Add(key, new MergeSegment<uint, TValue>(length, uintArray, tValueArray));
            }

            _count += length;
            RebuildDictionary();
        }

        /// <summary>
        /// 批量添加多个来源的类型映射，减少重建次数
        /// </summary>
        public void AddRange(IEnumerable<(long key, uint[] uintArray, TValue[] tValueArray)> items)
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
                    _segments[key] = new MergeSegment<uint, TValue>(length, uintArray, tValueArray);
                }
                else
                {
                    _segments.Add(key, new MergeSegment<uint, TValue>(length, uintArray, tValueArray));
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
        public void Remove(long key)
        {
            if (!_segments.Remove(key, out var segment))
            {
                return;
            }

            _count -= segment.Count;
            RebuildDictionary();
        }

        /// <summary>
        /// 重建合并后的FrozenDictionary
        /// </summary>
        private void RebuildDictionary()
        {
            if (_count == 0)
            {
                _mergedDictionary = new UInt32FrozenDictionary<TValue>(new uint[1] { 0 }, new TValue[1] { default });
                return;
            }

            var index = 0;
            var keys = new uint[_count];
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

            _mergedDictionary = new UInt32FrozenDictionary<TValue>(keys, tValueArray);
        }

        /// <summary>
        /// 获取合并后的FrozenDictionary
        /// </summary>
        public UInt32FrozenDictionary<TValue> GetFrozenDictionary() => _mergedDictionary;
    }
}