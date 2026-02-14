using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace Fantasy.Helper
{
    /// <summary>
    /// HashCode算法帮助类
    /// </summary>
    public static partial class HashCodeHelper
    {
        /// <summary>
        /// 计算两个字符串的HashCode
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHashCode(string a, string b)
        {
            var hash = 17;
            hash = hash * 31 + a.GetHashCode();
            hash = hash * 31 + b.GetHashCode();
            return hash;
        }

        /// <summary>
        /// 使用bkdr算法生成一个long的值
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetBKDRHashCode(string str)
        {
            ulong hash = 0;
            const uint seed = 13131; //  如果要修改这个种子、建议选择一个质数来做种子 31 131 1313 13131 131313 etc..
            var span = str.AsSpan();
            ref var local = ref MemoryMarshal.GetReference(span);
            
            for (var i = 0; i < span.Length; i++)
            {
                var c = Unsafe.Add(ref local, i);
                var high = (byte)(c >> 8);
                var low = (byte)(c & byte.MaxValue);
                hash = hash * seed + high;
                hash = hash * seed + low;
            }

            return (long)hash;
        }

        /// <summary>
        /// 使用MurmurHash3算法生成一个uint的值
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint MurmurHash3(string str)
        {
            const uint seed = 0xc58f1a7b;
            uint hash = seed;
            uint c1 = 0xcc9e2d51;
            uint c2 = 0x1b873593;

            var span = str.AsSpan();
            ref var local = ref MemoryMarshal.GetReference(span);

            for (var i = 0; i < span.Length; i++)
            {
                var k1 = (uint)Unsafe.Add(ref local, i);
                k1 *= c1;
                k1 = (k1 << 15) | (k1 >> (32 - 15));
                k1 *= c2;

                hash ^= k1;
                hash = (hash << 13) | (hash >> (32 - 13));
                hash = hash * 5 + 0xe6546b64;
            }

            hash ^= (uint)str.Length;
            hash ^= hash >> 16;
            hash *= 0x85ebca6b;
            hash ^= hash >> 13;
            hash *= 0xc2b2ae35;
            hash ^= hash >> 16;
            return hash;
        }

        /// <summary>
        /// 使用MurmurHash3算法生成一个long的值
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ComputeHash64(string str) => Hash64(MemoryMarshal.AsBytes(str.AsSpan()));

        /// <summary>
        /// 根据字符串计算一个Hash值
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Hash32(string obj, uint seed = 0) => Hash32(obj.AsSpan(), seed);

        /// <summary>
        /// 根据字符串计算一个Hash值
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="seed"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Hash32<T>(in T obj, uint seed = 0) where T : unmanaged => Hash32(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in obj)), Unsafe.SizeOf<T>()),
            seed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash32<T>(ReadOnlySpan<T> buffer, uint seed = 0) where T : unmanaged =>
            Hash32(MemoryMarshal.Cast<T, byte>(buffer), seed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash32(ReadOnlySpan<byte> buffer, uint seed = 0)
        {
            int length = buffer.Length;
            ref byte local1 = ref MemoryMarshal.GetReference(buffer);
            uint num1;
            if (buffer.Length >= 16)
            {
                uint num2 = seed + 606290984U;
                uint num3 = seed + 2246822519U;
                uint num4 = seed;
                uint num5 = seed - 2654435761U;
                for (; length >= 16; length -= 16)
                {
                    const nint elementOffset1 = 4;
                    const nint elementOffset2 = 8;
                    const nint elementOffset3 = 12;
                    nint byteOffset = buffer.Length - length;
                    ref byte local2 = ref Unsafe.AddByteOffset(ref local1, byteOffset);
                    uint num6 = num2 + Unsafe.ReadUnaligned<uint>(ref local2) * 2246822519U;
                    num2 = (uint)((((int)num6 << 13) | (int)(num6 >> 19)) * -1640531535);
                    uint num7 = num3 +
                                Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref local2, elementOffset1)) *
                                2246822519U;
                    num3 = (uint)((((int)num7 << 13) | (int)(num7 >> 19)) * -1640531535);
                    uint num8 = num4 +
                                Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref local2, elementOffset2)) *
                                2246822519U;
                    num4 = (uint)((((int)num8 << 13) | (int)(num8 >> 19)) * -1640531535);
                    uint num9 = num5 +
                                Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref local2, elementOffset3)) *
                                2246822519U;
                    num5 = (uint)((((int)num9 << 13) | (int)(num9 >> 19)) * -1640531535);
                }

                num1 = (uint)((((int)num2 << 1) | (int)(num2 >> 31)) + (((int)num3 << 7) | (int)(num3 >> 25)) +
                              (((int)num4 << 12) | (int)(num4 >> 20)) + (((int)num5 << 18) | (int)(num5 >> 14)) +
                              buffer.Length);
            }
            else
                num1 = (uint)((int)seed + 374761393 + buffer.Length);

            for (; length >= 4; length -= 4)
            {
                nint byteOffset = buffer.Length - length;
                uint num10 = Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref local1, byteOffset));
                uint num11 = num1 + num10 * 3266489917U;
                num1 = (uint)((((int)num11 << 17) | (int)(num11 >> 15)) * 668265263);
            }

            nint byteOffset1 = buffer.Length - length;
            ref byte local3 = ref Unsafe.AddByteOffset(ref local1, byteOffset1);
            for (int index = 0; index < length; ++index)
            {
                nint byteOffset2 = index;
                uint num12 = Unsafe.AddByteOffset(ref local3, byteOffset2);
                uint num13 = num1 + num12 * 374761393U;
                num1 = (uint)((((int)num13 << 11) | (int)(num13 >> 21)) * -1640531535);
            }

#if NET7_0_OR_GREATER
            int num14 = ((int)num1 ^ (int)(num1 >> 15)) * -2048144777;
            int num15 = (num14 ^ (num14 >>> 13)) * -1028477379;
            return num15 ^ (num15 >>> 16);
#else
            int num14 = ((int)num1 ^ (int)(num1 >> 15)) * -2048144777;
            int num15 = (num14 ^ (int)((uint)num14 >> 13)) * -1028477379;
            return num15 ^ (int)((uint)num15 >> 16);
#endif
        }

        /// <summary>
        /// 根据字符串计算一个Hash值
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Hash64(string obj, ulong seed = 0) => Hash64(obj.AsSpan(), seed);

        /// <summary>
        /// 根据字符串计算一个Hash值
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="seed"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Hash64<T>(in T obj, ulong seed = 0) where T : unmanaged => Hash64(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in obj)), Unsafe.SizeOf<T>()),
            seed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long Hash64<T>(ReadOnlySpan<T> buffer, ulong seed = 0) where T : unmanaged =>
            Hash64(MemoryMarshal.Cast<T, byte>(buffer), seed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long Hash64(ReadOnlySpan<byte> buffer, ulong seed = 0)
        {
            ref var local1 = ref MemoryMarshal.GetReference(buffer);
            var length = buffer.Length;
            ulong num1;
            if (buffer.Length >= 32)
            {
                var num2 = seed + 6983438078262162902UL;
                var num3 = seed + 14029467366897019727UL;
                var num4 = seed;
                var num5 = seed - 11400714785074694791UL;
                for (; length >= 32; length -= 32)
                {
                    ref var local2 = ref Unsafe.AddByteOffset(ref local1, (IntPtr)(buffer.Length - length));
                    var num6 = num2 + Unsafe.ReadUnaligned<ulong>(ref local2) * 14029467366897019727UL;
                    num2 = (ulong)((((long)num6 << 31) | (long)(num6 >> 33)) * -7046029288634856825L);
                    var num7 = num3 +
                               Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref local2, new UIntPtr(8U))) *
                               14029467366897019727UL;
                    num3 = (ulong)((((long)num7 << 31) | (long)(num7 >> 33)) * -7046029288634856825L);
                    var num8 = num4 +
                               Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref local2, new UIntPtr(16U))) *
                               14029467366897019727UL;
                    num4 = (ulong)((((long)num8 << 31) | (long)(num8 >> 33)) * -7046029288634856825L);
                    var num9 = num5 +
                               Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref local2, new UIntPtr(24U))) *
                               14029467366897019727UL;
                    num5 = (ulong)((((long)num9 << 31) | (long)(num9 >> 33)) * -7046029288634856825L);
                }

                var num10 = (((long)num2 << 1) | (long)(num2 >> 63)) + (((long)num3 << 7) | (long)(num3 >> 57)) +
                            (((long)num4 << 12) | (long)(num4 >> 52)) + (((long)num5 << 18) | (long)(num5 >> 46));
                var num11 = num2 * 14029467366897019727UL;
                var num12 = (((long)num11 << 31) | (long)(num11 >> 33)) * -7046029288634856825L;
                var num13 = (num10 ^ num12) * -7046029288634856825L + -8796714831421723037L;
                var num14 = num3 * 14029467366897019727UL;
                var num15 = (((long)num14 << 31) | (long)(num14 >> 33)) * -7046029288634856825L;
                var num16 = (num13 ^ num15) * -7046029288634856825L + -8796714831421723037L;
                var num17 = num4 * 14029467366897019727UL;
                var num18 = (((long)num17 << 31) | (long)(num17 >> 33)) * -7046029288634856825L;
                var num19 = (num16 ^ num18) * -7046029288634856825L + -8796714831421723037L;
                var num20 = num5 * 14029467366897019727UL;
                var num21 = (((long)num20 << 31) | (long)(num20 >> 33)) * -7046029288634856825L;
                num1 = (ulong)((num19 ^ num21) * -7046029288634856825L + -8796714831421723037L);
            }
            else
                num1 = seed + 2870177450012600261UL;

            var num22 = num1 + (ulong)buffer.Length;
            for (; length >= 8; length -= 8)
            {
                var num23 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref local1,
                    (IntPtr)(buffer.Length - length))) * 14029467366897019727UL;
                var num24 = (ulong)((((long)num23 << 31) | (long)(num23 >> 33)) * -7046029288634856825L);
                var num25 = num22 ^ num24;
                num22 = (ulong)((((long)num25 << 27) | (long)(num25 >> 37)) * -7046029288634856825L +
                                -8796714831421723037L);
            }

            if (length >= 4)
            {
                ulong num26 =
                    Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref local1, (IntPtr)(buffer.Length - length)));
                var num27 = num22 ^ (num26 * 11400714785074694791UL);
                num22 = (ulong)((((long)num27 << 23) | (long)(num27 >> 41)) * -4417276706812531889L +
                                1609587929392839161L);
                length -= 4;
            }

            for (var byteOffset = 0; byteOffset < length; ++byteOffset)
            {
                ulong num28 =
                    Unsafe.AddByteOffset(ref Unsafe.AddByteOffset(ref local1, (IntPtr)(buffer.Length - length)),
                        (IntPtr)byteOffset);
                var num29 = num22 ^ (num28 * 2870177450012600261UL);
                num22 = (ulong)((((long)num29 << 11) | (long)(num29 >> 53)) * -7046029288634856825L);
            }

#if NET7_0_OR_GREATER
            var num30 = (long)num22;
            var num31 = (num30 ^ (num30 >>> 33)) * -4417276706812531889L;
            var num32 = (num31 ^ (num31 >>> 29)) * 1609587929392839161L;
            return num32 ^ (num32 >>> 32);
#else
            var num30 = (long)num22;
            var num31 = (num30 ^ (long)((ulong)num30 >> 33)) * -4417276706812531889L;
            var num32 = (num31 ^ (long)((ulong)num31 >> 29)) * 1609587929392839161L;
            return num32 ^ (long)((ulong)num32 >> 32);
#endif
        }
    }
}