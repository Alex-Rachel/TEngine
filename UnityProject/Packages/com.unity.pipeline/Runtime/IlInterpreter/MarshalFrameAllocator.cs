#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace IlInterpreter.Interpreter
{

// Default frame allocator used outside Unity (and anywhere a host doesn't inject its own).
// Marshal.AllocHGlobal is platform-neutral and AOT/IL2CPP-safe, and keeps the engine free of
// any Unity.Collections dependency. Returns 8-byte-aligned memory on all supported runtimes.
#if UNITY_6000_5_OR_NEWER
[NoAutoStaticsCleanup]
#endif
sealed unsafe class MarshalFrameAllocator : IFrameAllocator
{
    public static readonly MarshalFrameAllocator Instance = new();

    public void* Alloc(int byteCount) => (void*)Marshal.AllocHGlobal(byteCount);

    public void Free(void* ptr)
    {
        if (ptr != null) Marshal.FreeHGlobal((nint)ptr);
    }
}
}
