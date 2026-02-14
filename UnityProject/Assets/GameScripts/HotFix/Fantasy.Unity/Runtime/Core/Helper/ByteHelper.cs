using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Fantasy.Helper
{
    /// <summary>
    /// 提供字节操作辅助方法的静态类。
    /// </summary>
    public static class ByteHelper
    {
        private static readonly string[] HexTableUpper = new string[256];
        private static readonly string[] HexTableLower = new string[256];
        private static readonly string[] Suffix = { "Byte", "KB", "MB", "GB", "TB" };
        private static readonly long[] Divisors = { 1L, 1024L, 1024L * 1024, 1024L * 1024 * 1024, 1024L * 1024 * 1024 * 1024 };

        static ByteHelper()
        {
            // 预计算所有256个字节的十六进制表示
            for (var i = 0; i < 256; i++)
            {
                HexTableUpper[i] = i.ToString("X2"); // 大写：00, 01, ..., FF
                HexTableLower[i] = i.ToString("x2"); // 小写：00, 01, ..., ff
            }
        }
        
        #region Read

        /// <summary>
        /// 从指定的流中读取一个 64 位整数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadInt64(this Stream stream)
        {
            Span<byte> buffer = stackalloc byte[8];
#if FANTASY_NET
            stream.ReadExactly(buffer);
#else
            _ = stream.Read(buffer);
#endif
            return MemoryMarshal.Read<long>(buffer);
        }

        /// <summary>
        /// 从指定的流中读取一个 32 位整数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32(this Stream stream)
        {
            Span<byte> buffer = stackalloc byte[4];
#if FANTASY_NET
            stream.ReadExactly(buffer);
#else
            _ = stream.Read(buffer);
#endif
            return MemoryMarshal.Read<int>(buffer);
        }
        
        /// <summary>
        /// 从指定的流中读取一个 16 位整数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt16(this Stream stream)
        {
            Span<byte> buffer = stackalloc byte[2];
#if FANTASY_NET
            stream.ReadExactly(buffer);
#else
            _ = stream.Read(buffer);
#endif
            return MemoryMarshal.Read<short>(buffer);
        }

        /// <summary>
        /// 从指定的流中读取一个无符号 64 位整数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64(this Stream stream)
        {
            Span<byte> buffer = stackalloc byte[8];
#if FANTASY_NET
            stream.ReadExactly(buffer);
#else
            _ = stream.Read(buffer);
#endif
            return MemoryMarshal.Read<ulong>(buffer);
        }

        /// <summary>
        /// 从指定的流中读取一个无符号 32 位整数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt32(this Stream stream)
        {
            Span<byte> buffer = stackalloc byte[4];
#if FANTASY_NET
            stream.ReadExactly(buffer);
#else
            _ = stream.Read(buffer);
#endif
            return MemoryMarshal.Read<uint>(buffer);
        }

        /// <summary>
        /// 从指定的流中读取一个无符号 16 位整数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt16(this Stream stream)
        {
            Span<byte> buffer = stackalloc byte[2];
#if FANTASY_NET
            stream.ReadExactly(buffer);
#else
            _ = stream.Read(buffer);
#endif
            return MemoryMarshal.Read<ushort>(buffer);
        }

        #endregion

        #region WriteTo

        /// <summary>
        /// 将值写入字节数组的指定偏移位置。
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteTo<T>(this byte[] bytes, int offset, T value) where T : unmanaged
        {
            if (offset < 0 || offset + Marshal.SizeOf<T>() > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            MemoryMarshal.Write(bytes.AsSpan(offset),
#if FANTASY_NET || FANTASY_CONSOLE
                in value
#endif
#if FANTASY_UNITY
                ref value
#endif
            );
        }

        /// <summary>
        /// 将值写入Span的指定偏移位置。
        /// </summary>
        /// <param name="span"></param>
        /// <param name="offset"></param>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteTo<T>(this Span<byte> span, int offset, T value) where T : unmanaged
        {
            if (offset < 0 || offset + Marshal.SizeOf<T>() > span.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            MemoryMarshal.Write(span.Slice(offset),
#if FANTASY_NET || FANTASY_CONSOLE
                in value
#endif
#if FANTASY_UNITY
                ref value
#endif
            );
        }

        /// <summary>
        /// 将值写入内存流
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteTo<T>(this Stream stream, T value) where T : unmanaged
        {
            Span<byte> buffer = stackalloc byte[Marshal.SizeOf<T>()];
            MemoryMarshal.Write(buffer,
#if FANTASY_NET || FANTASY_CONSOLE
                in value
#endif
#if FANTASY_UNITY
                ref value
#endif
            );
            stream.Write(buffer);
        }
        
        /// <summary>
        /// 将值写入内存流的指定偏移位置。
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="offset"></param>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteTo<T>(this Stream stream, int offset, T value) where T : unmanaged
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
        
            if (!stream.CanSeek)
            {
                throw new NotSupportedException("Stream must support seeking");
            }
        
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }    
            
            var originalPosition = stream.Position;
            try
            {
                stream.Position = offset; 
                Span<byte> buffer = stackalloc byte[Marshal.SizeOf<T>()]; 
                MemoryMarshal.Write(buffer,
#if FANTASY_NET || FANTASY_CONSOLE
                in value
#endif
#if FANTASY_UNITY
                    ref value
#endif
                );
                stream.Write(buffer);
            }
            finally
            {
                stream.Position = originalPosition;  
            }
        }

        #endregion

        #region ReadFrom

        /// <summary>
        /// 从字节数组的指定偏移位置读取值。
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadFrom<T>(this byte[] bytes, int offset) where T : unmanaged
        {
            if (offset < 0 || offset + Marshal.SizeOf<T>() > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            
            return MemoryMarshal.Read<T>(bytes.AsSpan(offset));
        }

        /// <summary>
        /// 从Span的指定偏移位置读取值。
        /// </summary>
        /// <param name="span"></param>
        /// <param name="offset"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadFrom<T>(this ReadOnlySpan<byte> span, int offset) where T : unmanaged
        {
            if (offset < 0 || offset + Marshal.SizeOf<T>() > span.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            
            return MemoryMarshal.Read<T>(span.Slice(offset));
        }

        /// <summary>
        /// 从流的指定偏移位置读取值
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="offset"></param>
        /// <param name="restorePosition">是否在读取完成后恢复到原始位置</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="EndOfStreamException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadFrom<T>(this Stream stream, int offset, bool restorePosition = false) where T : unmanaged
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (!stream.CanSeek)
            {
                throw new NotSupportedException("Stream must support seeking");
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            
            var originalPosition = restorePosition ? stream.Position : 0;

            try
            {
                var sizeOf = Marshal.SizeOf<T>();
                stream.Position = offset;
                Span<byte> buffer = stackalloc byte[sizeOf];
#if FANTASY_NET
                stream.ReadExactly(buffer);
#else
                var bytesRead = stream.Read(buffer);
                if (bytesRead != sizeOf)
                {
                    throw new EndOfStreamException($"Could not read {sizeOf} bytes from stream");
                }
#endif
                return MemoryMarshal.Read<T>(buffer);
            }
            finally
            {
                if (restorePosition)
                {
                    stream.Position = originalPosition;
                }
            }
        }

        #endregion

        #region GetBytes

        /// <summary>
        /// 将值转换为字节数组。
        /// </summary>
        /// <param name="value"></param>
        /// <param name="buffer"></param>
        /// <typeparam name="T"></typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetBytes<T>(this T value, Span<byte> buffer) where T : unmanaged
        {
            if (buffer.Length < Marshal.SizeOf<T>())
            {
                throw new ArgumentException($"Buffer too small. Required: {Marshal.SizeOf<T>()}, Actual: {buffer.Length}");
            }
            
            MemoryMarshal.Write(buffer,
#if FANTASY_NET || FANTASY_CONSOLE
                in value
#endif
#if FANTASY_UNITY
                ref value
#endif
            );
        }

        /// <summary>
        /// 将值转换为字节数组。
        /// </summary>
        /// <param name="value"></param>
        /// <param name="buffer"></param>
        /// <typeparam name="T"></typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetBytes<T>(this T value, byte[] buffer) where T : unmanaged
        {
            value.GetBytes(buffer.AsSpan());
        }

        /// <summary>
        /// 将值转换为字节数组。
        /// </summary>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] GetBytes<T>(this T value) where T : unmanaged
        {
            var result = new byte[Marshal.SizeOf<T>()];
            MemoryMarshal.Write(result.AsSpan(),
#if FANTASY_NET || FANTASY_CONSOLE
                in value
#endif
#if FANTASY_UNITY
                ref value
#endif
            );
            return result;
        }

        #endregion

        #region ToReadableSpeed

        /// <summary>
        /// 将字节数转换为可读的大小表示
        /// </summary>
        /// <param name="byteCount"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToReadableSpeed(this long byteCount)
        {
            switch (byteCount)
            {
                case <= 0:
                {
                    return "0 Byte";
                }
                // < 1TB，使用快速版本
                case < 1L << 40:
                {
                    var minLevel = 0;
                    var temp = byteCount;
                    while (temp >= 1024 && minLevel < 4)
                    {
                        temp >>= 10;
                        minLevel++;
                    }
                    return $"{(double)byteCount / Divisors[minLevel]:0.##} {Suffix[minLevel]}";
                }
                default:
                {
#if NET6_0_OR_GREATER
                    var level = Math.Min((int)(Math.Log2(byteCount) / 10), Suffix.Length - 1);
#else
                    // .NET Framework / .NET Core < 6.0 替代方案
                    var level = Math.Min((int)(Math.Log(byteCount) / Math.Log(2) / 10), Suffix.Length - 1);
#endif
                    var divisor = 1L << (level * 10);
                    var value = (double)byteCount / divisor;
                    return $"{value:0.##} {Suffix[level]}";
                }
            }
        }
        
        /// <summary>
        /// 将字节数转换为可读的大小表示（无符号版本）
        /// </summary>
        /// <param name="byteCount"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToReadableSpeed(this ulong byteCount)
        {
            switch (byteCount)
            {
                case 0:
                {
                    return "0 Byte";
                }
                // < 1TB，使用快速版本
                case < 1L << 40:
                {
                    var minLevel = 0;
                    var temp = byteCount;
                    while (temp >= 1024 && minLevel < 4)
                    {
                        temp >>= 10;
                        minLevel++;
                    }
                    return $"{(double)byteCount / Divisors[minLevel]:0.##} {Suffix[minLevel]}";
                }
                default:
                {
#if NET6_0_OR_GREATER
                    var level = Math.Min((int)(Math.Log2(byteCount) / 10), Suffix.Length - 1);
#else
                    // .NET Framework / .NET Core < 6.0 替代方案
                    var level = Math.Min((int)(Math.Log(byteCount) / Math.Log(2) / 10), Suffix.Length - 1);
#endif
                    var divisor = 1L << (level * 10);
                    var value = (double)byteCount / divisor;
                    return $"{value:0.##} {Suffix[level]}";
                }
            }
        }

        #endregion

        #region MergeBytes

        /// <summary>
        /// 合并字节数组到目标缓冲区（零分配版本）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MergeBytesTo(Span<byte> destination, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        {
            if (destination.Length < first.Length + second.Length)
                throw new ArgumentException("Destination buffer too small");

            first.CopyTo(destination);
            second.CopyTo(destination.Slice(first.Length));
        }

        /// <summary>
        /// 合并两个字节数组。
        /// </summary>
        /// <param name="bytes">第一个字节数组</param>
        /// <param name="otherBytes">第二个字节数组</param>
        /// <returns>合并后的字节数组</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] MergeBytes(byte[] bytes, byte[] otherBytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (otherBytes == null)
            {
                throw new ArgumentNullException(nameof(otherBytes));
            }
            
            // 优化：如果其中一个为空，直接返回另一个的副本
            if (bytes.Length == 0)
            {
                return (byte[])otherBytes.Clone();
            }

            if (otherBytes.Length == 0)
            {
                return (byte[])bytes.Clone();
            }
            
            var result = new byte[bytes.Length + otherBytes.Length];
            bytes.CopyTo(result.AsSpan());
            otherBytes.CopyTo(result.AsSpan(bytes.Length));
            return result;
        }

        #endregion

        #region ToHex
        
        /// <summary>
        /// 将字节转换为大写十六进制字符串。
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(this byte b) 
        {
            return HexTableUpper[b];
        }
        
        /// <summary>
        /// 将字节转换为小写十六进制字符串。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHexLower(this byte b)
        {
            return HexTableLower[b];
        }
        
        /// <summary>
        /// 将字节转换为指定格式的十六进制字符串。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(this byte b, bool upperCase)
        {
            return upperCase ? HexTableUpper[b] : HexTableLower[b];
        }

        /// <summary>
        /// 将字节数组转换为十六进制字符串表示。
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="upperCase"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(this byte[] bytes, bool upperCase = true)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (bytes.Length == 0)
            {
                return string.Empty;
            }
            
            var hexTable = upperCase ? HexTableUpper : HexTableLower;

            return string.Create(bytes.Length * 2, (bytes, hexTable), (chars, state) =>
            {
                var (buffer, table) = state;
                var charIndex = 0;
                ref var bytesRef = ref MemoryMarshal.GetReference(buffer.AsSpan());

                for (var i = 0; i < buffer.Length; i++)
                {
                    var hexStr = table[Unsafe.Add(ref bytesRef, i)];
                    chars[charIndex++] = hexStr[0];
                    chars[charIndex++] = hexStr[1];
                }
            });
        }

        /// <summary>
        /// 将字节数组的指定范围按十六进制格式转换为字符串表示。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(this byte[] bytes, int offset, int count, bool upperCase = true)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0 || offset + count > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0)
            {
                return string.Empty;
            }

            var hexTable = upperCase ? HexTableUpper : HexTableLower;

            return string.Create(count * 2, (bytes, offset, count, hexTable), (chars, state) =>
            {
                var (buffer, start, length, table) = state;
                var charIndex = 0;
                ref var bytesRef = ref MemoryMarshal.GetReference(buffer.AsSpan());

                for (var i = 0; i < length; i++)
                {
                    var hexStr = table[Unsafe.Add(ref bytesRef, start + i)];
                    chars[charIndex++] = hexStr[0];
                    chars[charIndex++] = hexStr[1];
                }
            });
        }
        
        /// <summary>
        /// 将字节数组的指定范围按十六进制格式转换为字符串表示。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToHex(this ReadOnlySpan<byte> bytes, bool upperCase = true)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            var hexTable = upperCase ? HexTableUpper : HexTableLower;
            
            return string.Create(bytes.Length * 2, (MemoryMarshal.GetReference(bytes), bytes.Length, hexTable), (chars,
                state) =>
            {
                var (bytesRef, length, table) = state;
                var charIndex = 0;

                for (var i = 0; i < length; i++)
                {
                    var hexStr = table[Unsafe.Add(ref bytesRef, i)];
                    chars[charIndex++] = hexStr[0];
                    chars[charIndex++] = hexStr[1];
                }
            });
        }

        /// <summary>
        /// 将十六进制字符串转换为字节数组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] FromHex(this string hexString)
        {
            return hexString.AsSpan().FromHex();
        }
        
        /// <summary>
        /// 将十六进制字符串转换为字节数组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] FromHex(this ReadOnlySpan<char> hexSpan)
        {
            if (hexSpan.Length == 0)
            {
                return Array.Empty<byte>();
            }

            if (hexSpan.Length % 2 != 0)
            {
                throw new ArgumentException("Hex string must have even length");
            }

            var result = new byte[hexSpan.Length / 2];

            for (var i = 0; i < result.Length; i++)
            {
                var high = HexCharToValue(hexSpan[i * 2]);
                var low = HexCharToValue(hexSpan[i * 2 + 1]);
                result[i] = (byte)((high << 4) | low);
            }

            return result;
        }

        
        /// <summary>
        /// 将十六进制字符串转换到现有字节数组。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FromHexTo(this string hexString, Span<byte> destination)
        {
            if (string.IsNullOrEmpty(hexString))
            {
                return;
            }

            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException("Hex string must have even length", nameof(hexString));
            }

            if (destination.Length < hexString.Length / 2)
            {
                throw new ArgumentException("Destination buffer too small", nameof(destination));
            }

            for (var i = 0; i < hexString.Length / 2; i++)
            {
                var high = HexCharToValue(hexString[i * 2]);
                var low = HexCharToValue(hexString[i * 2 + 1]);
                destination[i] = (byte)((high << 4) | low);
            }
        }

        
        /// <summary>
        /// 将十六进制字符转换为数值。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HexCharToValue(char c)
        {
            return c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                >= 'a' and <= 'f' => c - 'a' + 10,
                _ => throw new ArgumentException($"Invalid hex character: {c}")
            };
        }


        /// <summary>
        /// 将字节数组转换为默认编码的字符串表示。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToStr(this byte[] bytes)
        {
            return Encoding.Default.GetString(bytes);
        }

        /// <summary>
        /// 将字节数组的指定范围按默认编码转换为字符串表示。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToStr(this byte[] bytes, int index, int count)
        {
            return Encoding.Default.GetString(bytes, index, count);
        }

        /// <summary>
        /// 将字节数组转换为 UTF-8 编码的字符串表示。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Utf8ToStr(this byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// 将字节数组的指定范围按 UTF-8 编码转换为字符串表示。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Utf8ToStr(this byte[] bytes, int index, int count)
        {
            return Encoding.UTF8.GetString(bytes, index, count);
        }

        
        #endregion
    }
}