using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Fantasy.Helper;
#pragma warning disable CS8604 // Possible null reference argument.

namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// 实体类型哈希码缓存器。
    /// 提供两种缓存机制：
    /// 1. 泛型静态字段缓存（用于泛型方法，零开销）
    /// 2. 全局字典缓存（用于非泛型方法，运行时查找）
    /// </summary>
    public static class TypeHashCache
    {
        /// <summary>
        /// 全局类型哈希码缓存字典，用于非泛型方法的运行时查找。
        /// 使用 ConcurrentDictionary 保证线程安全。
        /// </summary>
        private static readonly ConcurrentDictionary<RuntimeTypeHandle, long> RuntimeCache = new();
        /// <summary>
        /// 过滤程序集版本号的正则表达式
        /// </summary>
        private static readonly Regex AssemblyInfoRegex = new Regex(
            @", [^\[\],]+, Version=[^,\]]+, Culture=[^,\]]+, PublicKeyToken=[^\]]+",
            RegexOptions.Compiled);
    
        /// <summary>
        /// 获取指定实体类型的哈希码（运行时查找）。
        /// 首次访问时计算并缓存，后续访问直接返回缓存值。
        /// </summary>
        /// <param name="type">实体类型</param>
        /// <returns>实体类型的哈希码</returns>
        /// <remarks>
        /// 使用场景：非泛型方法中使用，如 GetComponent(Type type)
        /// 性能：首次访问需要计算并插入字典，后续访问为 O(1) 字典查找
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetHashCode(Type type)
        {
            return RuntimeCache.GetOrAdd(type.TypeHandle,
                static (_, t) => HashCodeHelper.ComputeHash64(GetSimplifiedFullName(t)),
                type);
        }

        /// <summary>
        /// 获取简化的类型全名（移除程序集版本信息），用于 TypeHashCode 计算
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns>简化的类型全名，格式：命名空间.类型名`N[[参数类型, 程序集名],[...]]</returns>
        /// <remarks>
        /// 示例：
        /// - 非泛型：System.String
        /// - 泛型：Fantasy.GenericTest.TestEntity3`1[[System.Int32, System.Private.CoreLib]]
        /// 移除了 Version、Culture、PublicKeyToken 信息，确保热重载和跨版本兼容
        /// </remarks>
        private static string GetSimplifiedFullName(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            
            // 处理数组类型
            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var rank = type.GetArrayRank();
                var brackets = rank == 1 ? "[]" : $"[{new string(',', rank - 1)}]";
                return GetSimplifiedFullName(elementType) + brackets;
            }
            // 处理引用类型
            if (type.IsByRef)
            {
                return GetSimplifiedFullName(type.GetElementType()) + "&";
            }
            // 处理非泛型类型
            if (!type.IsGenericType)
            {
                return type.FullName ?? type.Name;
            }
            // 处理泛型类型
            // Type.FullName 格式示例：
            // "Fantasy.GenericTest.TestEntity3`1[[System.Int32, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]"
            // 目标格式：
            // "Fantasy.GenericTest.TestEntity3`1[[System.Int32]]"
            var fullName = type.FullName;
            if (string.IsNullOrEmpty(fullName))
            {
                // 如果 FullName 为 null（某些泛型类型可能出现），使用 ToString() 作为后备
                fullName = type.ToString();
            }
            // 使用编译的正则表达式移除版本信息
            return AssemblyInfoRegex.Replace(fullName, "");
        }
    
        /// <summary>
        /// 预热缓存，批量计算并缓存一组类型的哈希码。
        /// </summary>
        /// <param name="types">要预热的类型集合</param>
        /// <remarks>
        /// 建议在程序初始化时调用，避免运行时首次查找的计算开销。
        /// </remarks>
        public static void Warmup(IEnumerable<Type> types)
        {
            foreach (var type in types)
            {
                if (typeof(Entity).IsAssignableFrom(type))
                {
                    GetHashCode(type);
                }
            }
        }
    
        /// <summary>
        /// 清除所有缓存（仅用于热重载场景）。
        /// </summary>
        internal static void Clear()
        {
            RuntimeCache.Clear();
        }
    }

    /// <summary>
    /// 实体类型哈希码泛型缓存器。
    /// 通过泛型静态字段缓存每个实体类型的哈希码，实现零开销的类型哈希码访问。
    /// </summary>
    /// <remarks>
    /// 性能优势：
    /// <list type="bullet">
    /// <item><description>每个类型的哈希码只计算一次，后续访问直接返回缓存值</description></item>
    /// <item><description>JIT编译器会将静态字段访问内联为常量</description></item>
    /// <item><description>无需字典查找，性能远超运行时缓存</description></item>
    /// <item><description>适合在泛型方法中使用，如 GetComponent&lt;T&gt;()</description></item>
    /// </list>
    /// </remarks>
    internal static class TypeHashCache<T>
    {
        /// <summary>
        /// 获取实体类型 <typeparamref name="T"/> 的哈希码。
        /// 该值在首次访问时计算并缓存，后续访问直接返回缓存值。
        /// </summary>
        /// <value>
        /// 实体类型的哈希码，用于在 Entity 的 _tree 字典中快速查找组件。
        /// </value>
        public static long HashCode { get; }

        static TypeHashCache()
        {
            // 直接调用非泛型版本，复用计算逻辑并共享缓存
            HashCode = TypeHashCache.GetHashCode(typeof(T));
        }
    }
}
