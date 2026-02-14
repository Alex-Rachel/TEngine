// ReSharper disable CheckNamespace
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
    public sealed class TypeHandleMergerFrozenDictionary<TValue>
    {
        private int _count;

        private RuntimeTypeHandleFrozenDictionary<TValue> _mergedDictionary =
            new(new RuntimeTypeHandle[1] { typeof(FrozenHashTable).TypeHandle },
                new TValue[1] { default(TValue) });

        private readonly Dictionary<long, MergeSegment<RuntimeTypeHandle, TValue>> _segments = new();

        /// <summary>
        /// 添加或更新指定来源的类型映射
        /// </summary>
        public void Add(long segmentKey, RuntimeTypeHandle[] runtimeTypeHandles, TValue[] tValueArray)
        {
            var length = runtimeTypeHandles.Length;

            if (length != tValueArray.Length)
            {
                throw new ArgumentException("runtimeTypeHandles and tValueArray must have the same length");
            }

            if (length == 0)
            {
                return;
            }
            
            if (_segments.TryGetValue(segmentKey, out var oldSegment))
            {
                _count -= oldSegment.Count;
                _segments[segmentKey] = new MergeSegment<RuntimeTypeHandle, TValue>(length, runtimeTypeHandles, tValueArray);
            }
            else
            {
                _segments.Add(segmentKey, new MergeSegment<RuntimeTypeHandle, TValue>(length, runtimeTypeHandles, tValueArray));
            }

            _count += length;
            RebuildDictionary();
        }

        /// <summary>
        /// 批量添加多个来源的类型映射，减少重建次数
        /// </summary>
        public void AddRange(IEnumerable<(long segmentKey, RuntimeTypeHandle[] runtimeTypeHandles, TValue[] tValueArray)> items)
        {
            var hasChanges = false;

            foreach (var (segmentKey, runtimeTypeHandles, tValueArray) in items)
            {
                var length = runtimeTypeHandles.Length;

                if (length != tValueArray.Length)
                {
                    throw new ArgumentException($"ID {segmentKey}: runtimeTypeHandles and tValueArray must have the same length");
                }

                if (length == 0)
                {
                    continue;
                }
                
                if (_segments.TryGetValue(segmentKey, out var oldSegment))
                {
                    _count -= oldSegment.Count;
                    _segments[segmentKey] = new MergeSegment<RuntimeTypeHandle, TValue>(length, runtimeTypeHandles, tValueArray);
                }
                else
                {
                    _segments.Add(segmentKey, new MergeSegment<RuntimeTypeHandle, TValue>(length, runtimeTypeHandles, tValueArray));
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
        public bool Remove(long segmentKey)
        {
            if (!_segments.Remove(segmentKey, out var segment))
            {
                return false;
            }

            _count -= segment.Count;
            RebuildDictionary();
            return true;
        }

        /// <summary>
        /// 重建合并后的FrozenDictionary
        /// </summary>
        private void RebuildDictionary()
        {
            if (_count == 0)
            {
                _mergedDictionary = new RuntimeTypeHandleFrozenDictionary<TValue>(
                    new RuntimeTypeHandle[1] { typeof(FrozenHashTable).TypeHandle },
                    new TValue[1] { default(TValue) });
                return;
            }

            var index = 0;
            var runtimeTypeHandles = new RuntimeTypeHandle[_count];
            var tValueArray = new TValue[_count];

            var keySpan = runtimeTypeHandles.AsSpan();
            var valueSpan = tValueArray.AsSpan();

            foreach (var segment in _segments.Values)
            {
                var count = segment.Count;
                segment.TArray.AsSpan(0, count).CopyTo(keySpan.Slice(index, count));
                segment.T1Array.AsSpan(0, count).CopyTo(valueSpan.Slice(index, count));
                index += count;
            }

            _mergedDictionary = new RuntimeTypeHandleFrozenDictionary<TValue>(runtimeTypeHandles, tValueArray);
        }

        /// <summary>
        /// 获取合并后的FrozenDictionary
        /// </summary>
        public RuntimeTypeHandleFrozenDictionary<TValue> GetFrozenDictionary() => _mergedDictionary;
    }
}