// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted for Fantasy Framework

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 提供专用于 <see cref="string"/> 键的冻结字典（不可变只读字典）。
    /// 相比普通的 Dictionary，具有更好的读取性能和更低的内存占用。
    /// </summary>
    /// <typeparam name="TValue">字典中值的类型。</typeparam>
    /// <remarks>
    /// <para>此字典类型专门为 string 键优化，适用于配置键、名称映射等场景。</para>
    /// <para>性能特点：</para>
    /// <list type="bullet">
    /// <item>读取速度比 Dictionary 快 30-38%</item>
    /// <item>内存占用更少</item>
    /// <item>支持自定义字符串比较器（如忽略大小写）</item>
    /// <item>不支持修改操作（不可变）</item>
    /// <item>适合编译期生成的静态数据</item>
    /// <item>极简实现，无虚方法开销</item>
    /// </list>
    /// </remarks>
    public sealed class StringFrozenDictionary<TValue>
    {
        private readonly FrozenHashTable _hashTable;
        private readonly TValue[] _values;
        private readonly string[] _keys;
        private readonly IEqualityComparer<string> _comparer;

        /// <summary>
        /// 初始化 <see cref="StringFrozenDictionary{TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keys">键数组，必须保证无重复。</param>
        /// <param name="values">值数组，必须与键数组长度相同。</param>
        /// <param name="comparer">字符串比较器，如果为 null 则使用默认比较器。</param>
        /// <exception cref="ArgumentNullException">当 keys 或 values 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">当数组长度不一致或为空时抛出。</exception>
        public StringFrozenDictionary(string[] keys, TValue[] values, IEqualityComparer<string>? comparer = null)
        {
            if (keys == null)
                throw new ArgumentNullException(nameof(keys));
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (keys.Length != values.Length)
                throw new ArgumentException("Keys and values arrays must have the same length");
            if (keys.Length == 0)
                throw new ArgumentException("Arrays cannot be empty");

            _comparer = comparer ?? EqualityComparer<string>.Default;

            int[] arrayPoolHashCodes = ArrayPool<int>.Shared.Rent(keys.Length);
            Span<int> hashCodes = arrayPoolHashCodes.AsSpan(0, keys.Length);

            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == null)
                    throw new ArgumentNullException(nameof(keys), "Keys array contains null value");
                hashCodes[i] = _comparer.GetHashCode(keys[i]);
            }

            _hashTable = FrozenHashTable.Create(hashCodes, hashCodesAreUnique: false);

            _keys = new string[keys.Length];
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
        /// 初始化 <see cref="StringFrozenDictionary{TValue}"/> 类的新实例（从 Dictionary 创建）。
        /// </summary>
        /// <param name="source">源字典。</param>
        /// <param name="comparer">字符串比较器，如果为 null 则使用源字典的比较器。</param>
        /// <exception cref="ArgumentNullException">当 source 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">当 source 为空时抛出。</exception>
        public StringFrozenDictionary(Dictionary<string, TValue> source, IEqualityComparer<string>? comparer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.Count == 0)
                throw new ArgumentException("Source dictionary cannot be empty", nameof(source));

            _comparer = comparer ?? source.Comparer;

            KeyValuePair<string, TValue>[] entries = new KeyValuePair<string, TValue>[source.Count];
            ((ICollection<KeyValuePair<string, TValue>>)source).CopyTo(entries, 0);

            _keys = new string[entries.Length];
            _values = new TValue[entries.Length];

            int[] arrayPoolHashCodes = ArrayPool<int>.Shared.Rent(entries.Length);
            Span<int> hashCodes = arrayPoolHashCodes.AsSpan(0, entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Key == null)
                    throw new ArgumentNullException(nameof(source), "Source dictionary contains null key");
                hashCodes[i] = _comparer.GetHashCode(entries[i].Key);
            }

            _hashTable = FrozenHashTable.Create(hashCodes, hashCodesAreUnique: false);

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
        /// 获取字符串比较器。
        /// </summary>
        public IEqualityComparer<string> Comparer => _comparer;

        /// <summary>
        /// 获取与指定键关联的值。
        /// </summary>
        /// <param name="key">要获取值的键。</param>
        /// <returns>与指定键关联的值。</returns>
        /// <exception cref="KeyNotFoundException">如果键不存在则抛出。</exception>
        public TValue this[string key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ref readonly TValue value = ref GetValueRefOrNullRef(key);
                if (Unsafe.IsNullRef(ref Unsafe.AsRef(in value)))
                {
                    throw new KeyNotFoundException($"Key '{key}' not found in dictionary");
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
        public bool ContainsKey(string key)
        {
            if (key == null)
                return false;

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
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
        {
            if (key == null)
            {
                value = default;
                return false;
            }

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
        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            string[] keys = _keys;
            TValue[] values = _values;

            for (int i = 0; i < keys.Length; i++)
            {
                yield return new KeyValuePair<string, TValue>(keys[i], values[i]);
            }
        }

        /// <summary>
        /// 获取与指定键关联的值引用，如果键不存在则返回 null 引用。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref readonly TValue GetValueRefOrNullRef(string key)
        {
            int hashCode = _comparer.GetHashCode(key);
            _hashTable.FindMatchingEntries(hashCode, out int index, out int endIndex);

            string[] keys = _keys;
            while (index <= endIndex)
            {
                if (_comparer.Equals(key, keys[index]))
                {
                    return ref _values[index];
                }

                index++;
            }

            return ref Unsafe.NullRef<TValue>();
        }
    }

    /// <summary>
    /// 提供 StringFrozenDictionary 的扩展方法。
    /// </summary>
    public static class StringFrozenDictionaryExtensions
    {
        /// <summary>
        /// 将 Dictionary&lt;string, TValue&gt; 转换为 StringFrozenDictionary&lt;TValue&gt;。
        /// </summary>
        public static StringFrozenDictionary<TValue> ToStringFrozenDictionary<TValue>(
            this Dictionary<string, TValue> source,
            IEqualityComparer<string>? comparer = null)
        {
            return new StringFrozenDictionary<TValue>(source, comparer);
        }

        /// <summary>
        /// 根据指定的键选择器从序列创建 StringFrozenDictionary。
        /// </summary>
        public static StringFrozenDictionary<TSource> ToStringFrozenDictionary<TSource>(
            this IEnumerable<TSource> source,
            Func<TSource, string> keySelector,
            IEqualityComparer<string>? comparer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (keySelector == null)
                throw new ArgumentNullException(nameof(keySelector));

            comparer ??= EqualityComparer<string>.Default;
            var dict = new Dictionary<string, TSource>(comparer);
            foreach (var item in source)
            {
                dict[keySelector(item)] = item;
            }

            return new StringFrozenDictionary<TSource>(dict, comparer);
        }

        /// <summary>
        /// 根据指定的键选择器和元素选择器从序列创建 StringFrozenDictionary。
        /// </summary>
        public static StringFrozenDictionary<TValue> ToStringFrozenDictionary<TSource, TValue>(
            this IEnumerable<TSource> source,
            Func<TSource, string> keySelector,
            Func<TSource, TValue> valueSelector,
            IEqualityComparer<string>? comparer = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (keySelector == null)
                throw new ArgumentNullException(nameof(keySelector));
            if (valueSelector == null)
                throw new ArgumentNullException(nameof(valueSelector));

            comparer ??= EqualityComparer<string>.Default;
            var dict = new Dictionary<string, TValue>(comparer);
            foreach (var item in source)
            {
                dict[keySelector(item)] = valueSelector(item);
            }

            return new StringFrozenDictionary<TValue>(dict, comparer);
        }
    }
}
