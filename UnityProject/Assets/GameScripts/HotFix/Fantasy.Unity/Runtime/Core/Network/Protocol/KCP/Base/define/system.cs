using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable CS1591

// ReSharper disable ALL

namespace kcp
{
    internal static unsafe partial class KCP
    {
        private static void* malloc(nuint size)
        {
#if NET6_0_OR_GREATER
            return NativeMemory.Alloc((nuint)size);
#else
            return (void*)Marshal.AllocHGlobal((nint)size);
#endif
        }

        private static void free(void* memory)
        {
#if NET6_0_OR_GREATER
            NativeMemory.Free(memory);
#else
            Marshal.FreeHGlobal((nint)memory);
#endif
        }

        private static void memcpy(void* dst, void* src, nuint size) => Unsafe.CopyBlockUnaligned(dst, src, (uint)size);

        private static void memset(void* dst, byte val, nuint size) => Unsafe.InitBlockUnaligned(dst, val, (uint)size);

        [Conditional("DEBUG")]
        private static void assert(bool condition) => Debug.Assert(condition);

        private static void abort() => Environment.Exit(-1);
    }
}