// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted for Fantasy Framework

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 哈希辅助类，提供哈希表相关的工具方法。
    /// </summary>
    internal static class HashHelpers
    {
        /// <summary>
        /// 哈希冲突阈值。
        /// </summary>
        public const uint HashCollisionThreshold = 100;

        /// <summary>
        /// 小于数组最大长度的最大质数。
        /// </summary>
        public const int MaxPrimeArrayLength = 0x7FFFFFC3;

        /// <summary>
        /// 哈希质数。
        /// </summary>
        public const int HashPrime = 101;

#if FANTASY_UNITY
        /// <summary>
        /// 质数表，用于哈希表大小计算。
        /// </summary>
        private static readonly int[] s_primes = new int[]
        {
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        };

        internal static ReadOnlySpan<int> Primes => s_primes;
#endif
#if FANTASY_NET
        /// <summary>
        /// 质数表，用于哈希表大小计算。
        /// </summary>
        internal static ReadOnlySpan<int> Primes =>
        [
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        ];
#endif

        /// <summary>
        /// 判断一个数是否为质数。
        /// </summary>
        /// <param name="candidate">待判断的数。</param>
        /// <returns>如果是质数返回 true，否则返回 false。</returns>
        public static bool IsPrime(int candidate)
        {
            if ((candidate & 1) != 0)
            {
                int limit = (int)Math.Sqrt(candidate);
                for (int divisor = 3; divisor <= limit; divisor += 2)
                {
                    if ((candidate % divisor) == 0)
                        return false;
                }
                return true;
            }
            return candidate == 2;
        }

        /// <summary>
        /// 获取大于或等于指定值的最小质数。
        /// </summary>
        /// <param name="min">最小值。</param>
        /// <returns>大于或等于 min 的最小质数。</returns>
        /// <exception cref="ArgumentException">当 min 小于 0 时抛出。</exception>
        public static int GetPrime(int min)
        {
            if (min < 0)
                throw new ArgumentException("Capacity overflow");

            foreach (int prime in Primes)
            {
                if (prime >= min)
                    return prime;
            }

            // 超出预定义表范围，需要计算
            for (int i = (min | 1); i < int.MaxValue; i += 2)
            {
                if (IsPrime(i) && ((i - 1) % HashPrime != 0))
                    return i;
            }
            return min;
        }

        /// <summary>
        /// 扩展哈希表大小，返回新的质数大小。
        /// </summary>
        /// <param name="oldSize">旧的大小。</param>
        /// <returns>新的质数大小。</returns>
        public static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;

            // 允许哈希表增长到最大可能大小（约 2G 元素），然后才遇到容量溢出
            if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
            {
                Debug.Assert(MaxPrimeArrayLength == GetPrime(MaxPrimeArrayLength), "Invalid MaxPrimeArrayLength");
                return MaxPrimeArrayLength;
            }

            return GetPrime(newSize);
        }

        /// <summary>
        /// 返回除数的近似倒数：ceil(2**64 / divisor)。
        /// </summary>
        /// <param name="divisor">除数。</param>
        /// <returns>快速模运算的乘数。</returns>
        /// <remarks>此方法仅应在 64 位平台上使用。</remarks>
        public static ulong GetFastModMultiplier(uint divisor) =>
            ulong.MaxValue / divisor + 1;

        /// <summary>
        /// 使用预先计算的乘数执行快速模运算。
        /// </summary>
        /// <param name="value">被除数。</param>
        /// <param name="divisor">除数。</param>
        /// <param name="multiplier">使用 <see cref="GetFastModMultiplier"/> 预先计算的乘数。</param>
        /// <returns>模运算结果。</returns>
        /// <remarks>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint FastMod(uint value, uint divisor, ulong multiplier)
        {
            if (Environment.Is64BitProcess)
            {
                var highbits = (uint)(((((multiplier * value) >> 32) + 1) * divisor) >> 32);
                Debug.Assert(highbits == value % divisor);
                return highbits;
            }
            else
            {
                return value % divisor;
            }
        }
    }
}
