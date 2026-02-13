// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted for Fantasy Framework

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 提供专用于 <see cref="uint"/> 键的冻结字典（不可变只读字典）。
    /// 相比普通的 Dictionary，具有更好的读取性能和更低的内存占用。
    /// </summary>
    /// <typeparam name="TValue">字典中值的类型。</typeparam>
    /// <remarks>
    /// <para>此字典类型专门为 uint 键优化，适用于无符号ID、标识符等场景。</para>
    /// <para>性能特点：</para>
    /// <list type="bullet">
    /// <item>读取速度比 Dictionary 快 34-42%</item>
    /// <item>uint 键可以直接转换为哈希码，性能极高</item>
    /// <item>内存占用更少</item>
    /// <item>不支持修改操作（不可变）</item>
    /// <item>适合编译期生成的静态数据</item>
    /// <item>极简实现，无虚方法开销</item>
    /// </list>
    /// </remarks>
    public sealed class UInt32FrozenDictionary<TValue>
    {
        private readonly FrozenHashTable _hashTable;
        private readonly TValue[] _values;
        private readonly uint[] _keys;

        /// <summary>
        /// 初始化 <see cref="UInt32FrozenDictionary{TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keys">键数组，必须保证无重复。</param>
        /// <param name="values">值数组，必须与键数组长度相同。</param>
        /// <exception cref="ArgumentNullException">当 keys 或 values 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">当数组长度不一致或为空时抛出。</exception>
        public UInt32FrozenDictionary(uint[] keys, TValue[] values)
        {
            if (keys == null)
                throw new ArgumentNullException(nameof(keys));
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (keys.Length != values.Length)
                throw new ArgumentException("Keys and values arrays must have the same length");
            if (keys.Length == 0)
                throw new ArgumentException("Arrays cannot be empty");

            int[] arrayPoolHashCodes = ArrayPool<int>.Shared.Rent(keys.Length);
            Span<int> hashCodes = arrayPoolHashCodes.AsSpan(0, keys.Length);

            for (int i = 0; i < keys.Length; i++)
            {
                hashCodes[i] = unchecked((int)keys[i]);
            }

            _hashTable = FrozenHashTable.Create(hashCodes, hashCodesAreUnique: true);

            _keys = new uint[keys.Length];
            _values = new TValue[keys.Length];

            for (int srcIndex = 0; srcIndex < hashCodes.Length; srcIndex++)
            {
                int destIndex = hashCodes[srcIndex];
                _keys[destIndex] = keys[srcIndex];
                _values[destIndex] = values[srcIndex];
            }

            ArrayPool<int>.Shared.Return(arrayPoolHashCodes);
        }

        /// <summary>
        /// 初始化 <see cref="UInt32FrozenDictionary{TValue}"/> 类的新实例（从 Dictionary 创建）。
        /// </summary>
        /// <param name="source">源字典，必须使用默认的相等比较器。</param>
        /// <exception cref="ArgumentNullException">当 source 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">当 source 为空或不使用默认比较器时抛出。</exception>
        public UInt32FrozenDictionary(Dictionary<uint, TValue> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (!ReferenceEquals(source.Comparer, EqualityComparer<uint>.Default))
                throw new ArgumentException("Source dictionary must use default comparer", nameof(source));

            if (source.Count == 0)
                throw new ArgumentException("Source dictionary cannot be empty", nameof(source));

            KeyValuePair<uint, TValue>[] entries = new KeyValuePair<uint, TValue>[source.Count];
            ((ICollection<KeyValuePair<uint, TValue>>)source).CopyTo(entries, 0);

            _keys = new uint[entries.Length];
            _values = new TValue[entries.Length];

            int[] arrayPoolHashCodes = ArrayPool<int>.Shared.Rent(entries.Length);
            Span<int> hashCodes = arrayPoolHashCodes.AsSpan(0, entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                hashCodes[i] = unchecked((int)entries[i].Key);
            }

            _hashTable = FrozenHashTable.Create(hashCodes, hashCodesAreUnique: true);

            for (int srcIndex = 0; srcIndex < hashCodes.Length; srcIndex++)
            {
                int destIndex = hashCodes[srcIndex];
                _keys[destIndex] = entries[srcIndex].Key;
                _values[destIndex] = entries[srcIndex].Value;
            }

            ArrayPool<int>.Shared.Return(arrayPoolHashCodes);
        }

        /// <summary>
        /// 获取字典中包含的元素数量。
        /// </summary>
        public int Count => _hashTable.Count;

        /// <summary>
        /// 获取与指定键关联的值。
        /// </summary>
        /// <param name="key">要获取值的键。</param>
        /// <returns>与指定键关联的值。</returns>
        /// <exception cref="KeyNotFoundException">如果键不存在则抛出。</exception>
        public TValue this[uint key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ref readonly TValue value = ref GetValueRefOrNullRef(key);
                if (Unsafe.IsNullRef(ref Unsafe.AsRef(in value)))
                {
                    throw new KeyNotFoundException($"Key {key} not found in dictionary");
                }
                return value;
            }
        }

        /// <summary>
        /// 确定字典是否包含指定的键。
        /// </summary>
        /// <param name="key">要在字典中定位的键。</param>
        /// <returns>如果字典包含具有指定键的元素，则为 true；否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(uint key)
        {
            return !Unsafe.IsNullRef(ref Unsafe.AsRef(in GetValueRefOrNullRef(key)));
        }

        /// <summary>
        /// 获取与指定键关联的值。
        /// </summary>
        /// <param name="key">要获取其值的键。</param>
        /// <param name="value">
        /// 当此方法返回时，如果找到指定键，则包含与该键关联的值；
        /// 否则包含值类型的默认值。
        /// </param>
        /// <returns>如果字典包含具有指定键的元素，则为 true；否则为 false。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(uint key, [MaybeNullWhen(false)] out TValue value)
        {
            ref readonly TValue valRef = ref GetValueRefOrNullRef(key);
            if (!Unsafe.IsNullRef(ref Unsafe.AsRef(in valRef)))
            {
                value = valRef;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// 返回循环访问字典的枚举器。
        /// </summary>
        /// <returns>用于循环访问字典的枚举器。</returns>
        public IEnumerator<KeyValuePair<uint, TValue>> GetEnumerator()
        {
            uint[] keys = _keys;
            TValue[] values = _values;

            for (int i = 0; i < keys.Length; i++)
            {
                yield return new KeyValuePair<uint, TValue>(keys[i], values[i]);
            }
        }

        /// <summary>
        /// 获取与指定键关联的值引用，如果键不存在则返回 null 引用。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref readonly TValue GetValueRefOrNullRef(uint key)
        {
            int hashCode = unchecked((int)key);
            _hashTable.FindMatchingEntries(hashCode, out int index, out int endIndex);

            uint[] keys = _keys;
            while (index <= endIndex)
            {
                if (key == keys[index])
                {
                    return ref _values[index];
                }

                index++;
            }

            return ref Unsafe.NullRef<TValue>();
        }
    }

    /// <summary>
    /// 提供 UInt32FrozenDictionary 的扩展方法。
    /// </summary>
    public static class UInt32FrozenDictionaryExtensions
    {
        /// <summary>
        /// 将 Dictionary&lt;uint, TValue&gt; 转换为 UInt32FrozenDictionary&lt;TValue&gt;。
        /// </summary>
        public static UInt32FrozenDictionary<TValue> ToUInt32FrozenDictionary<TValue>(
            this Dictionary<uint, TValue> source)
        {
            return new UInt32FrozenDictionary<TValue>(source);
        }

        /// <summary>
        /// 根据指定的键选择器从序列创建 UInt32FrozenDictionary。
        /// </summary>
        public static UInt32FrozenDictionary<TSource> ToUInt32FrozenDictionary<TSource>(
            this IEnumerable<TSource> source,
            Func<TSource, uint> keySelector)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (keySelector == null)
                throw new ArgumentNullException(nameof(keySelector));

            var dict = new Dictionary<uint, TSource>();
            foreach (var item in source)
            {
                dict[keySelector(item)] = item;
            }

            return new UInt32FrozenDictionary<TSource>(dict);
        }

        /// <summary>
        /// 根据指定的键选择器和元素选择器从序列创建 UInt32FrozenDictionary。
        /// </summary>
        public static UInt32FrozenDictionary<TValue> ToUInt32FrozenDictionary<TSource, TValue>(
            this IEnumerable<TSource> source,
            Func<TSource, uint> keySelector,
            Func<TSource, TValue> valueSelector)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (keySelector == null)
                throw new ArgumentNullException(nameof(keySelector));
            if (valueSelector == null)
                throw new ArgumentNullException(nameof(valueSelector));

            var dict = new Dictionary<uint, TValue>();
            foreach (var item in source)
            {
                dict[keySelector(item)] = valueSelector(item);
            }

            return new UInt32FrozenDictionary<TValue>(dict);
        }
    }
}
