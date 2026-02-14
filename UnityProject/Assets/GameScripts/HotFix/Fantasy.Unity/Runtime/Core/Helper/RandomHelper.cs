#if FANTASY_NET || !FANTASY_WEBGL
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.Helper
{
    /// <summary>
    /// 随机数操作助手类，提供各种随机数生成和操作方法。
    /// </summary>
    public static partial class RandomHelper
    {
        [ThreadStatic]
        private static Random _random;

        /// <summary>
        /// 生成一个随机的无符号 64 位整数。
        /// </summary>
        /// <returns>无符号 64 位整数。</returns>
        public static ulong RandUInt64()
        {
            var byte8 = new FixedBytes8().AsSpan();
            var random = _random ??= new Random();
            random.NextBytes(byte8);
            return BitConverter.ToUInt64(byte8);
        }

        /// <summary>
        /// 生成一个随机的 64 位整数。
        /// </summary>
        /// <returns>64 位整数。</returns>
        public static long RandInt64()
        {
            var byte8 = new FixedBytes8().AsSpan();
            var random = _random ??= new Random();
            random.NextBytes(byte8);
            return BitConverter.ToInt64(byte8);
        }

        /// <summary>
        /// 生成一个随机的无符号 32 位整数。
        /// </summary>
        /// <returns>无符号 32 位整数。</returns>
        public static uint RandUInt32()
        { 
            var random = _random ??= new Random();
            return (uint) random.Next();
        }

        /// <summary>
        /// 生成一个随机的无符号 16 位整数。
        /// </summary>
        /// <returns>无符号 16 位整数。</returns>
        public static ushort RandUInt16()
        {
            var byte2 = new FixedBytes2().AsSpan();
            var random = _random ??= new Random();
            random.NextBytes(byte2);
            return BitConverter.ToUInt16(byte2);
        }

        /// <summary>
        /// 在指定范围内生成一个随机整数（包含下限，不包含上限）。
        /// </summary>
        /// <param name="lower">下限。</param>
        /// <param name="upper">上限。</param>
        /// <returns>生成的随机整数。</returns>
        public static int RandomNumber(int lower, int upper)
        {
            var random = _random ??= new Random();
            return random.Next(lower, upper);
        }

        /// <summary>
        /// 生成一个随机的布尔值。
        /// </summary>
        /// <returns>随机的布尔值。</returns>
        public static bool RandomBool()
        {
            var random = _random ??= new Random();
            return (random.Next() & 1) == 0;
        }

        /// <summary>
        /// 从数组中随机选择一个元素。
        /// </summary>
        /// <typeparam name="T">数组元素的类型。</typeparam>
        /// <param name="array">要选择的数组。</param>
        /// <returns>随机选择的数组元素。</returns>
        public static T RandomArray<T>(this T[] array)
        {
            return array[RandomNumber(0, array.Count())];
        }

        /// <summary>
        /// 从列表中随机选择一个元素。
        /// </summary>
        /// <typeparam name="T">列表元素的类型。</typeparam>
        /// <param name="array">要选择的列表。</param>
        /// <returns>随机选择的列表元素。</returns>
        public static T RandomArray<T>(this List<T> array)
        {
            return array[RandomNumber(0, array.Count())];
        }

        /// <summary>
        /// 打乱列表中元素的顺序。
        /// </summary>
        /// <typeparam name="T">列表元素的类型。</typeparam>
        /// <param name="arr">要打乱顺序的列表。</param>
        public static void BreakRank<T>(List<T> arr)
        {
            if (arr == null || arr.Count < 2)
            {
                return;
            }

            var random = _random ??= new Random();
            for (var i = 0; i < arr.Count / 2; i++)
            {
                var index = random.Next(0, arr.Count);
                (arr[index], arr[arr.Count - index - 1]) = (arr[arr.Count - index - 1], arr[index]);
            }
        }

        /// <summary>
        /// 生成一个介于 0 和 1 之间的随机单精度浮点数。
        /// </summary>
        /// <returns>随机单精度浮点数。</returns>
        public static float RandFloat01()
        {
            var random = _random ??= new Random();
            var value = random.NextDouble();
            return (float) value;
        }

        private static int Rand(int n)
        {
            var rd = new Random();
            // 注意，返回值是左闭右开，所以maxValue要加1
            return rd.Next(1, n + 1);
        }

        /// <summary>
        /// 根据权重随机选择一个索引。
        /// </summary>
        /// <param name="weights">权重数组，每个元素表示相应索引的权重。</param>
        /// <returns>随机选择的索引值。</returns>
        public static int RandomByWeight(int[] weights)
        {
            var sum = weights.Sum();
            var numberRand = Rand(sum);
            var sumTemp = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                sumTemp += weights[i];
                if (numberRand <= sumTemp)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 根据固定概率随机选择一个索引，即某个数值上限内随机多少次。
        /// </summary>
        /// <param name="args">概率数组，每个元素表示相应索引的概率。</param>
        /// <returns>随机选择的索引值。</returns>
        public static int RandomByFixedProbability(int[] args)
        {
            var random = _random ??= new Random();
            var argCount = args.Length;
            var sum = args.Sum();
            var value = random.NextDouble() * sum;
            while (sum > value)
            {
                sum -= args[argCount - 1];
                argCount--;
            }

            return argCount;
        }

        /// <summary>
        /// 返回随机数。
        /// </summary>
        /// <param name="containNegative">是否包含负数。</param>
        /// <returns>返回一个随机的单精度浮点数。</returns>
        public static float NextFloat(bool containNegative = false)
        {
            var random = _random ??= new Random();
            float f;
            var buffer = new FixedBytes4().AsSpan();
            if (containNegative)
            {
                do
                {
                    random.NextBytes(buffer);
                    f = BitConverter.ToSingle(buffer);
                } while ((f >= float.MinValue && f < float.MaxValue) == false);

                return f;
            }

            do
            {
                random.NextBytes(buffer);
                f = BitConverter.ToSingle(buffer);
            } while ((f >= 0 && f < float.MaxValue) == false);

            return f;
        }

        /// <summary>
        /// 返回一个小于所指定最大值的非负随机数。
        /// </summary>
        /// <param name="maxValue">要生成的随机数的上限（随机数不能取该上限值）。 maxValue 必须大于或等于零。</param>
        /// <returns>大于等于零且小于 maxValue 的单精度浮点数，即：返回值的范围通常包括零但不包括 maxValue。 不过，如果 maxValue 等于零，则返回 maxValue。</returns>
        public static float NextFloat(float maxValue)
        {
            if (maxValue.Equals(0))
            {
                return maxValue;
            }

            if (maxValue < 0)
            {
                throw new ArgumentOutOfRangeException("“maxValue”必须大于 0。", "maxValue");
            }

            var random = _random ??= new Random();
            float f;
            var buffer = new FixedBytes4().AsSpan();

            do
            {
                random.NextBytes(buffer);
                f = BitConverter.ToSingle(buffer);
            } while ((f >= 0 && f < maxValue) == false);

            return f;
        }

        /// <summary>
        /// 返回一个指定范围内的随机数。
        /// </summary>
        /// <param name="minValue">返回的随机数的下界（随机数可取该下界值）。</param>
        /// <param name="maxValue">返回的随机数的上界（随机数不能取该上界值）。 maxValue 必须大于或等于 minValue。</param>
        /// <returns>一个大于等于 minValue 且小于 maxValue 的单精度浮点数，即：返回的值范围包括 minValue 但不包括 maxValue。 如果 minValue 等于 maxValue，则返回 minValue。</returns>
        public static float NextFloat(float minValue, float maxValue)
        {
            if (minValue.Equals(maxValue))
            {
                return minValue;
            }

            if (minValue > maxValue)
            {
                throw new ArgumentOutOfRangeException("“minValue”不能大于 maxValue。", "minValue");
            }

            var random = _random ??= new Random();
            var buffer = new FixedBytes4().AsSpan();
            float f;

            do
            {
                random.NextBytes(buffer);
                f = BitConverter.ToSingle(buffer);
            } while ((f >= minValue && f < maxValue) == false);

            return f;
        }

        /// <summary>
        /// 在指定的矩形区域内随机生成一个二维向量位置。
        /// </summary>
        /// <param name="minX">X轴最小值。</param>
        /// <param name="maxX">X轴最大值。</param>
        /// <param name="minY">Y轴最小值。</param>
        /// <param name="maxY">Y轴最大值。</param>
        /// <returns>随机生成的二维向量位置。</returns>
        public static Vector2 NextVector2(float minX, float maxX, float minY, float maxY)
        {
            return new Vector2(NextFloat(minX, maxX), NextFloat(minY, maxY));
        }

        /// <summary>
        /// 生成指定长度的随机数字代码。
        /// </summary>
        /// <param name="len">数字代码的长度。</param>
        /// <returns>生成的随机数字代码。</returns>
        public static string RandomNumberCode(int len = 6)
        {
            int num = 0;
            for (int i = 0; i < len; i++)
            {
                int number = RandomNumber(0, 10);
                num = num * 10 + number;
            }

            return num.ToString();
        }
    }

    #region FixedBytes

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes1
    {
        private byte _e0;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes1, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes1>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes2
    {
        private FixedBytes1 _e0;
        private FixedBytes1 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes2, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes2>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes4
    {
        private FixedBytes2 _e0;
        private FixedBytes2 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes4, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes4>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes8
    {
        private FixedBytes4 _e0;
        private FixedBytes4 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes8, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes8>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes16
    {
        private FixedBytes8 _e0;
        private FixedBytes8 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes16, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes16>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes32
    {
        private FixedBytes16 _e0;
        private FixedBytes16 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes32, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes32>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes64
    {
        private FixedBytes32 _e0;
        private FixedBytes32 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes64, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes64>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes128
    {
        private FixedBytes64 _e0;
        private FixedBytes64 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes128, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes128>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes256
    {
        private FixedBytes128 _e0;
        private FixedBytes128 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes256, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes256>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes512
    {
        private FixedBytes256 _e0;
        private FixedBytes256 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes512, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes512>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedBytes1024
    {
        private FixedBytes512 _e0;
        private FixedBytes512 _e1;

        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.As<FixedBytes1024, byte>(ref Unsafe.AsRef(in this)), Unsafe.SizeOf<FixedBytes1024>());
    }

    #endregion
}
#endif