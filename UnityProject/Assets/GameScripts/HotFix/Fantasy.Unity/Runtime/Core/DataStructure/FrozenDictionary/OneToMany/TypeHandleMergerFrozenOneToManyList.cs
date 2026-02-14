using System;
using System.Collections.Generic;
using Fantasy.DataStructure.Dictionary;
// ReSharper disable UseCollectionExpression
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 支持多来源合并的 TypeHandle -> List&lt;TValue&gt; 冻结字典（一对多）
    /// 每个来源(segmentKey)可以独立添加/移除，相同 RuntimeTypeHandle 的值会合并到 List 中
    /// </summary>
    public sealed class TypeHandleMergerFrozenOneToManyList<TValue>
    {
        private RuntimeTypeHandleFrozenDictionary<List<TValue>> _mergedDictionary =
            new(new RuntimeTypeHandle[1] { typeof(FrozenHashTable).TypeHandle },
                new List<TValue>[1] { new List<TValue>() });
        private readonly Dictionary<long, (RuntimeTypeHandle[] Keys, TValue[] Values)> _segments = new();

        /// <summary>
        /// 添加或更新指定来源的类型映射
        /// </summary>
        public void Add(long segmentKey, RuntimeTypeHandle[] handles, TValue[] values)
        {
            if (handles.Length != values.Length)
            {
                throw new ArgumentException("handles and values must have the same length");
            }

            if (handles.Length == 0)
            {
                return;
            }

            _segments[segmentKey] = (handles, values);
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
                _mergedDictionary = new RuntimeTypeHandleFrozenDictionary<List<TValue>>(
                    new RuntimeTypeHandle[1] { typeof(FrozenHashTable).TypeHandle },
                    new List<TValue>[1] { new List<TValue>() });
                return;
            }

            using var dictionaryPool = DictionaryPool<RuntimeTypeHandle, List<TValue>>.Create();

            foreach (var (_, (runtimeTypeHandles, values)) in _segments)
            {
                for (var i = 0; i < runtimeTypeHandles.Length; i++)
                {
                    var runtimeTypeHandle = runtimeTypeHandles[i];
                    if (!dictionaryPool.TryGetValue(runtimeTypeHandle, out var list))
                    {
                        list = new List<TValue>();
                        dictionaryPool.Add(runtimeTypeHandle, list);
                    }
                    list.Add(values[i]);
                }
            }

            var index = 0;
            var keyArray = new RuntimeTypeHandle[dictionaryPool.Count];
            var valueArray = new List<TValue>[dictionaryPool.Count];

            foreach (var (runtimeTypeHandles, values) in dictionaryPool)
            {
                keyArray[index] = runtimeTypeHandles;
                valueArray[index] = values;
                index++;
            }

            _mergedDictionary = new RuntimeTypeHandleFrozenDictionary<List<TValue>>(keyArray, valueArray);
        }
        
        /// <summary>
        /// 获取合并后的FrozenDictionary
        /// </summary>
        public RuntimeTypeHandleFrozenDictionary<List<TValue>> GetFrozenDictionary() => _mergedDictionary;
    }
}