using System;
using System.Collections.Generic;
using Fantasy.DataStructure.Dictionary;
// ReSharper disable UseCollectionExpression
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 支持多来源合并的 TypeHandle -> List&lt;TValue&gt; 冻结字典（一对多）
    /// 每个来源(segmentKey)可以独立添加/移除，相同 RuntimeTypeHandle 的值会合并到 List 中
    /// </summary>
    public sealed class Int64MergerFrozenOneToManyList<TValue>
    {
        private Int64FrozenDictionary<List<TValue>> _mergedDictionary =
            new(new long[1] { 0 },
                new List<TValue>[1] { new List<TValue>() });
        private readonly Dictionary<long, (long[] Keys, TValue[] Values)> _segments = new();
        
        /// <summary>
        /// 添加或更新指定来源的类型映射
        /// </summary>
        public void Add(long segmentKey, long[] keys, TValue[] values)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (keys.Length != values.Length)
            {
                throw new ArgumentException("keys and values must have the same length");
            }

            if (keys.Length == 0)
            {
                return;
            }

            _segments[segmentKey] = (keys, values);
            RebuildDictionary();
        }
        
        /// <summary>
        /// 移除指定来源的类型映射
        /// </summary>
        public bool Remove(long segmentKey)
        {
            if (!_segments.Remove(segmentKey, out _))
            {
                return false;
            }

            RebuildDictionary();
            return true;
        }
        
        /// <summary>
        /// 重建合并后的FrozenDictionary
        /// </summary>
        private void RebuildDictionary()
        {
            if (_segments.Count == 0)
            {
                _mergedDictionary = new Int64FrozenDictionary<List<TValue>>( new long[1] { 0 }, new List<TValue>[1] { new List<TValue>() });
                return;
            }

            using var dictionaryPool = DictionaryPool<long, List<TValue>>.Create();

            foreach (var (_, (keys, values)) in _segments)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    var runtimeTypeHandle = keys[i];
                    if (!dictionaryPool.TryGetValue(runtimeTypeHandle, out var list))
                    {
                        list = new List<TValue>();
                        dictionaryPool.Add(runtimeTypeHandle, list);
                    }
                    list.Add(values[i]);
                }
            }

            var index = 0;
            var keyArray = new long[dictionaryPool.Count];
            var valueArray = new List<TValue>[dictionaryPool.Count];

            foreach (var (keys, values) in dictionaryPool)
            {
                keyArray[index] = keys;
                valueArray[index] = values;
                index++;
            }

            _mergedDictionary = new Int64FrozenDictionary<List<TValue>>(keyArray, valueArray);
        }
        
        /// <summary>
        /// 获取合并后的FrozenDictionary
        /// </summary>
        public Int64FrozenDictionary<List<TValue>> GetFrozenDictionary() => _mergedDictionary;
    }
}