// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted for Fantasy Framework

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
// ReSharper disable InconsistentNaming
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 为冻结集合提供核心哈希表。
    /// </summary>
    /// <remarks>
    /// 此哈希表不跟踪任何集合状态。它仅跟踪哈希码并将这些哈希码映射到集合中的条目范围。
    /// </remarks>
    internal readonly struct FrozenHashTable
    {
        private readonly Bucket[] _buckets;
        private readonly ulong _fastModMultiplier;

        /// <summary>
        /// 使用计算的哈希码和桶信息初始化哈希表。
        /// </summary>
        /// <param name="hashCodes">按桶分组为连续区域的哈希码数组。每个桶是数组的一个且仅一个区域。</param>
        /// <param name="buckets">
        /// 桶数组，通过 hashCodes % buckets.Length 索引，每个桶是
        /// 该桶中所有项的 <paramref name="hashCodes"/> 的起始/结束索引。
        /// </param>
        /// <param name="fastModMultiplier">用作 FastMod 方法调用一部分的乘数。</param>
        private FrozenHashTable(int[] hashCodes, Bucket[] buckets, ulong fastModMultiplier)
        {
            Debug.Assert(hashCodes.Length != 0);
            Debug.Assert(buckets.Length != 0);

            HashCodes = hashCodes;
            _buckets = buckets;
            _fastModMultiplier = fastModMultiplier;
        }

        /// <summary>
        /// 初始化冻结哈希表。
        /// </summary>
        /// <param name="hashCodes">预先计算的哈希码。方法完成时，将每个值分配给目标索引。</param>
        /// <param name="hashCodesAreUnique">当输入哈希码已经唯一时为 true（例如从整数字典提供）。</param>
        /// <remarks>
        /// 它将确定要分配的最佳哈希桶数量并填充桶表。
        /// 调用者负责使用写入 <paramref name="hashCodes"/> 的值并更新目标（如果需要）。
        /// <see cref="FindMatchingEntries(int, out int, out int)"/> 然后使用此索引通过索引到 <see cref="HashCodes"/> 来引用单个条目。
        /// </remarks>
        /// <returns>冻结哈希表。</returns>
        public static FrozenHashTable Create(Span<int> hashCodes, bool hashCodesAreUnique = false)
        {
            // 确定要使用的桶数。如果任何条目具有相同的哈希码（不仅仅是可能映射到同一桶的不同哈希码），
            // 这可能少于条目数。
            int numBuckets = CalcNumBuckets(hashCodes, hashCodesAreUnique);
            ulong fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)numBuckets);

            // 创建两个 span：
            // - bucketStarts：初始填充为所有 -1，第 i 个元素存储该桶链的头元素的 hashCodes 索引。
            // - nexts：第 i 个元素存储链中下一个项的索引。
            int[] arrayPoolBuckets = ArrayPool<int>.Shared.Rent(numBuckets + hashCodes.Length);
            Span<int> bucketStarts = arrayPoolBuckets.AsSpan(0, numBuckets);
            Span<int> nexts = arrayPoolBuckets.AsSpan(numBuckets, hashCodes.Length);
            bucketStarts.Fill(-1);

            // 填充桶条目和起始位置。对于每个哈希码，计算其桶，
            // 并在对应于哈希码项的桶条目处存储该项的条目，
            // 其中包括哈希码的副本和当前桶起始位置，
            // 然后将其替换为此条目，因为它被推入桶列表。
            for (int index = 0; index < hashCodes.Length; index++)
            {
                int hashCode = hashCodes[index];
                int bucketNum = (int)HashHelpers.FastMod((uint)hashCode, (uint)bucketStarts.Length, fastModMultiplier);

                ref int bucketStart = ref bucketStarts[bucketNum];
                nexts[index] = bucketStart;
                bucketStart = index;
            }

            // 写出 FrozenHashTable 实例要使用的哈希码和桶数组。
            // 我们迭代每个桶起始位置，并从每个起始位置迭代该链中的每个项，
            // 将每个链中的所有项相邻写入哈希码列表中
            //（并调用 setter 以允许使用者适当地重新排序其条目）。
            // 在此过程中，我们统计每个链中有多少项，并将其与起始索引一起使用
            // 来写出用于索引到哈希码的桶信息。
            var hashtableHashcodes = new int[hashCodes.Length];
            var hashtableBuckets = new Bucket[bucketStarts.Length];
            int count = 0;
            for (int bucketNum = 0; bucketNum < hashtableBuckets.Length; bucketNum++)
            {
                int bucketStart = bucketStarts[bucketNum];
                if (bucketStart < 0)
                {
                    continue;
                }

                int bucketCount = 0;
                int index = bucketStart;
                bucketStart = count;
                while (index >= 0)
                {
                    ref int hashCode = ref hashCodes[index];
                    hashtableHashcodes[count] = hashCode;
                    // 我们最后一次使用哈希码，现在我们重新使用缓冲区来存储目标索引
                    hashCode = count;
                    count++;
                    bucketCount++;

                    index = nexts[index];
                }

                hashtableBuckets[bucketNum] = new Bucket(bucketStart, bucketCount);
            }
            Debug.Assert(count == hashtableHashcodes.Length);

            ArrayPool<int>.Shared.Return(arrayPoolBuckets);

            return new FrozenHashTable(hashtableHashcodes, hashtableBuckets, fastModMultiplier);
        }

        /// <summary>
        /// 给定一个哈希码，返回关联匹配条目的第一个索引和最后一个索引。
        /// </summary>
        /// <param name="hashCode">要探测的哈希码。</param>
        /// <param name="startIndex">接收第一个匹配条目索引的变量。</param>
        /// <param name="endIndex">接收最后一个匹配条目索引加 1 的变量。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FindMatchingEntries(int hashCode, out int startIndex, out int endIndex)
        {
            Bucket[] buckets = _buckets;
            ref Bucket b = ref buckets[HashHelpers.FastMod((uint)hashCode, (uint)buckets.Length, _fastModMultiplier)];
            startIndex = b.StartIndex;
            endIndex = b.EndIndex;
        }

        /// <summary>
        /// 获取哈希表中的元素数量。
        /// </summary>
        public int Count => HashCodes.Length;

        /// <summary>
        /// 获取哈希码数组。
        /// </summary>
        internal int[] HashCodes { get; }

        /// <summary>
        /// 给定一个哈希码范围，计算最佳的哈希桶数量。
        /// </summary>
        /// <remarks>
        /// 这尝试选择质数桶数量。
        /// 这是确定良好桶数量的速度与最大密度之间的权衡。
        /// </remarks>
        private static int CalcNumBuckets(ReadOnlySpan<int> hashCodes, bool hashCodesAreUnique)
        {
            Debug.Assert(hashCodes.Length != 0);
            Debug.Assert(!hashCodesAreUnique || new HashSet<int>(hashCodes.ToArray()).Count == hashCodes.Length);

            const double AcceptableCollisionRate = 0.05;  // 可接受的哈希冲突率
            const int LargeInputSizeThreshold = 1000;     // 输入被视为"小"的限制
            const int MaxSmallBucketTableMultiplier = 16; // 小输入允许的桶表大小
            const int MaxLargeBucketTableMultiplier = 3;  // 大输入允许的桶表大小

            // 过滤掉重复的代码，因为增加桶数量不会避免重复输入哈希码的冲突。
            HashSet<int>? codes = null;
            int uniqueCodesCount = hashCodes.Length;
            if (!hashCodesAreUnique)
            {
                codes =
#if NET
                    new HashSet<int>(hashCodes.Length);
#else
                    new HashSet<int>();
#endif
                foreach (int hashCode in hashCodes)
                {
                    codes.Add(hashCode);
                }
                uniqueCodesCount = codes.Count;
            }
            Debug.Assert(uniqueCodesCount != 0);

            // 根据我们的观察，在超过 99.5% 的情况下，满足我们标准的桶数量
            // 至少是唯一哈希码数量的两倍。
            int minNumBuckets = uniqueCodesCount * 2;

            // 在我们预先计算的质数表中，找到至少与我们的哈希码数量一样大的最小质数的索引。
            ReadOnlySpan<int> primes = HashHelpers.Primes;
            int minPrimeIndexInclusive = 0;
            while ((uint)minPrimeIndexInclusive < (uint)primes.Length && minNumBuckets > primes[minPrimeIndexInclusive])
            {
                minPrimeIndexInclusive++;
            }

            if (minPrimeIndexInclusive >= primes.Length)
            {
                return HashHelpers.GetPrime(uniqueCodesCount);
            }

            // 根据输入数量的倍数，确定我们愿意使用的最大桶数量。
            // 对于较小的输入，我们允许更大的乘数。
            int maxNumBuckets =
                uniqueCodesCount *
                (uniqueCodesCount >= LargeInputSizeThreshold ? MaxLargeBucketTableMultiplier : MaxSmallBucketTableMultiplier);

            // 找到容纳我们最大桶数量的最小质数的索引。
            int maxPrimeIndexExclusive = minPrimeIndexInclusive;
            while ((uint)maxPrimeIndexExclusive < (uint)primes.Length && maxNumBuckets > primes[maxPrimeIndexExclusive])
            {
                maxPrimeIndexExclusive++;
            }

            if (maxPrimeIndexExclusive < primes.Length)
            {
                Debug.Assert(maxPrimeIndexExclusive != 0);
                maxNumBuckets = primes[maxPrimeIndexExclusive - 1];
            }

            const int BitsPerInt32 = 32;
            int[] seenBuckets = ArrayPool<int>.Shared.Rent((maxNumBuckets / BitsPerInt32) + 1);

            int bestNumBuckets = maxNumBuckets;
            int bestNumCollisions = uniqueCodesCount;
            int numBuckets = 0, numCollisions = 0;

            // 迭代最小和最大之间的每个可用质数。对于每个，计算冲突率。
            for (int primeIndex = minPrimeIndexInclusive; primeIndex < maxPrimeIndexExclusive; primeIndex++)
            {
                // 获取要尝试的桶数量，并清除我们看到的桶位图。
                numBuckets = primes[primeIndex];
                Array.Clear(seenBuckets, 0, Math.Min(numBuckets, seenBuckets.Length));

                // 确定每个哈希码的桶并将其标记为已看到。如果已经看到，则将其跟踪为冲突。
                numCollisions = 0;

                if (codes is not null && uniqueCodesCount != hashCodes.Length)
                {
                    foreach (int code in codes)
                    {
                        if (!IsBucketFirstVisit(code))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    // hashCodes 中的所有哈希码都是唯一的。在这种情况下，迭代 span 更快。
                    foreach (int code in hashCodes)
                    {
                        if (!IsBucketFirstVisit(code))
                        {
                            break;
                        }
                    }
                }

                // 如果此评估导致的冲突更少，则将其用作最佳值。
                // 如果低于我们的冲突阈值，我们就完成了。
                if (numCollisions < bestNumCollisions)
                {
                    bestNumBuckets = numBuckets;

                    if (numCollisions / (double)uniqueCodesCount <= AcceptableCollisionRate)
                    {
                        break;
                    }

                    bestNumCollisions = numCollisions;
                }
            }

            ArrayPool<int>.Shared.Return(seenBuckets);

            return bestNumBuckets;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool IsBucketFirstVisit(int code)
            {
                uint bucketNum = (uint)code % (uint)numBuckets;
                if ((seenBuckets[bucketNum / BitsPerInt32] & (1 << (int)bucketNum)) != 0)
                {
                    numCollisions++;
                    if (numCollisions >= bestNumCollisions)
                    {
                        // 如果我们已经达到了先前已知的最佳冲突数量，
                        // 继续下去没有意义，因为最坏的情况下我们只会使用那个。
                        return false;
                    }
                }
                else
                {
                    seenBuckets[bucketNum / BitsPerInt32] |= 1 << (int)bucketNum;
                }

                return true;
            }
        }

        /// <summary>
        /// 桶结构，存储起始索引和结束索引。
        /// </summary>
        private readonly struct Bucket
        {
            /// <summary>
            /// 起始索引。
            /// </summary>
            public readonly int StartIndex;

            /// <summary>
            /// 结束索引（包含）。
            /// </summary>
            public readonly int EndIndex;

            /// <summary>
            /// 初始化桶。
            /// </summary>
            /// <param name="startIndex">起始索引。</param>
            /// <param name="count">元素数量。</param>
            public Bucket(int startIndex, int count)
            {
                Debug.Assert(count > 0);

                StartIndex = startIndex;
                EndIndex = startIndex + count - 1;
            }
        }
    }
}
