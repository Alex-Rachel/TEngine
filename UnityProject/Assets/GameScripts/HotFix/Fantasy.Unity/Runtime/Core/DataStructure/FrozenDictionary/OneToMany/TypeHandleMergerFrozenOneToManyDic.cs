using System;
using System.Collections.Generic;
using Fantasy.DataStructure.Dictionary;
// ReSharper disable UseCollectionExpression
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 支持多来源合并的 RuntimeTypeHandle -> Dictionary&lt;TKey, TValue&gt; 冻结字典合并器（一对多）。
    /// </summary>
    /// <typeparam name="TKey">字典键的类型，必须是非空类型。</typeparam>
    /// <typeparam name="TValue">字典值的类型。</typeparam>
    /// <remarks>
    /// <para>此类用于管理多个数据源（如多个程序集）的类型映射，并将它们合并到一个统一的冻结字典中。</para>
    /// <para>每个数据源（由 segmentKey 标识）可以独立添加或移除，相同 RuntimeTypeHandle 的键值对会合并到同一个内部字典中。</para>
    /// <para>主要特性：</para>
    /// <list type="bullet">
    /// <item><description>支持热重载场景：可以动态添加/移除程序集的类型映射</description></item>
    /// <item><description>高性能查找：使用冻结字典提供无分配的快速查找</description></item>
    /// <item><description>自动合并：相同 RuntimeTypeHandle 的多个键值对自动合并到一个字典中</description></item>
    /// <item><description>线程安全重建：通过对象池减少 GC 压力</description></item>
    /// </list>
    /// </remarks>
    public class TypeHandleMergerFrozenOneToManyDic<TKey, TValue> where TKey : notnull
    {
        /// <summary>
        /// 合并后的冻结字典，外层 key 为 RuntimeTypeHandle，value 为该类型对应的所有键值对的字典。
        /// </summary>
        private RuntimeTypeHandleFrozenDictionary<Dictionary<TKey, TValue>> _mergedDictionary =
            new(new RuntimeTypeHandle[1] { typeof(FrozenHashTable).TypeHandle },
                new Dictionary<TKey, TValue>[1] { new Dictionary<TKey, TValue>() });

        /// <summary>
        /// 存储各个数据源的原始数据段，key 为数据源标识符（如程序集 ID），value 为该数据源的类型句柄、键和值数组。
        /// </summary>
        private readonly Dictionary<long, (RuntimeTypeHandle[] Keys, TKey[] keys, TValue[] Values)> _segments = new();
        
        /// <summary>
        /// 添加或更新指定来源的类型映射
        /// </summary>
        /// <param name="segmentKey">来源标识符，用于区分不同的数据源（如程序集 ID）。</param>
        /// <param name="handles">RuntimeTypeHandle 数组，表示类型句柄。</param>
        /// <param name="keys">键数组，与 handles 和 values 一一对应。</param>
        /// <param name="values">值数组，与 handles 和 keys 一一对应。</param>
        /// <exception cref="ArgumentNullException">当 handles、keys 或 values 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">当 handles、keys 和 values 的长度不一致时抛出。</exception>
        public void Add(long segmentKey, RuntimeTypeHandle[] handles, TKey[] keys, TValue[] values)
        {
            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles), "handles cannot be null");
            }

            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys), "keys cannot be null");
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values), "values cannot be null");
            }

            if (handles.Length != keys.Length)
            {
                throw new ArgumentException($"handles and keys must have the same length. handles: {handles.Length}, keys: {keys.Length}");
            }

            if (handles.Length != values.Length)
            {
                throw new ArgumentException($"handles and values must have the same length. handles: {handles.Length}, values: {values.Length}");
            }

            if (handles.Length == 0)
            {
                return;
            }

            _segments[segmentKey] = (handles, keys, values);
            RebuildDictionary();
        }

        /// <summary>
        /// 移除指定来源的类型映射。
        /// </summary>
        /// <param name="segmentKey">要移除的数据源标识符（如程序集 ID）。</param>
        /// <remarks>
        /// <para>移除指定数据源的所有类型映射，并重新构建合并后的冻结字典。</para>
        /// <para>如果指定的 segmentKey 不存在，则不执行任何操作。</para>
        /// <para>此方法常用于程序集卸载或热重载场景。</para>
        /// </remarks>
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
        /// 重建合并后的冻结字典。
        /// </summary>
        /// <remarks>
        /// <para>此方法会遍历所有已注册的数据段，将相同 RuntimeTypeHandle 的键值对合并到同一个字典中。</para>
        /// <para>性能优化：</para>
        /// <list type="bullet">
        /// <item><description>使用对象池创建临时字典，减少 GC 压力</description></item>
        /// <item><description>重用数组，避免不必要的内存分配</description></item>
        /// <item><description>最终生成不可变的冻结字典，提供最佳查询性能</description></item>
        /// </list>
        /// <para>如果所有数据源都被移除（_segments 为空），则生成一个空的冻结字典。</para>
        /// </remarks>
        private void RebuildDictionary()
        {
            if (_segments.Count == 0)
            {
                _mergedDictionary = new RuntimeTypeHandleFrozenDictionary<Dictionary<TKey, TValue>>(
                    new RuntimeTypeHandle[1] { typeof(FrozenHashTable).TypeHandle },
                    new Dictionary<TKey, TValue>[1] { new Dictionary<TKey, TValue>() });
                return;
            }

            using var dictionaryPool = DictionaryPool<RuntimeTypeHandle, (List<TKey>, List<TValue>)>.Create();

            foreach (var (_, (runtimeTypeHandles,keys, values)) in _segments)
            {
                for (var i = 0; i < runtimeTypeHandles.Length; i++)
                {
                    var runtimeTypeHandle = runtimeTypeHandles[i];
                    if (!dictionaryPool.TryGetValue(runtimeTypeHandle, out var list))
                    {
                        list = (new List<TKey>(), new List<TValue>());
                        dictionaryPool.Add(runtimeTypeHandle, list);
                    }
                    list.Item1.Add(keys[i]);
                    list.Item2.Add(values[i]);
                }
            }

            var index = 0;
            var typeHandleArray = new RuntimeTypeHandle[dictionaryPool.Count];
            var dictionaryArray = new Dictionary<TKey, TValue>[dictionaryPool.Count];

            foreach (var (runtimeTypeHandles, values) in dictionaryPool)
            {
                var dictionary = new Dictionary<TKey, TValue>();
                typeHandleArray[index] = runtimeTypeHandles;
                dictionaryArray[index] = dictionary;
                for (var i = 0; i < values.Item1.Count; i++)
                {
                    dictionary.Add(values.Item1[i], values.Item2[i]);
                }
                index++;
            }

            _mergedDictionary = new RuntimeTypeHandleFrozenDictionary<Dictionary<TKey, TValue>>(typeHandleArray, dictionaryArray);
        }

        /// <summary>
        /// 获取合并后的冻结字典。
        /// </summary>
        /// <returns>
        /// 合并后的冻结字典，外层 key 为 RuntimeTypeHandle，value 为该类型对应的所有键值对的字典。
        /// </returns>
        /// <remarks>
        /// <para>返回的冻结字典是不可变的，提供高性能的无分配查找。</para>
        /// <para>每次调用 <see cref="Add"/> 或 <see cref="Remove"/> 后，都会重新构建冻结字典，因此返回的引用可能会变化。</para>
        /// </remarks>
        public RuntimeTypeHandleFrozenDictionary<Dictionary<TKey, TValue>> GetFrozenDictionary() => _mergedDictionary;
    }
}