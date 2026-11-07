#nullable enable

namespace IlInterpreter.Interpreter
{
    // Supplies the Vm engine's unmanaged numeric frame. Injected so the engine stays
    // UnityEngine-free: the default is Marshal-based (works everywhere incl. IL2CPP), while a
    // Unity host can provide a NativeArray/UnsafeUtility-backed implementation.
    //
    // Contract: Alloc returns memory aligned to at least 8 bytes (both AllocHGlobal and
    // UnsafeUtility.Malloc(...,8,...) satisfy this), so a Value* frame and inline structs up to
    // 8-byte field alignment are safe to address. The returned block is owned by the caller until
    // passed back to Free; implementations must not move or GC it.
    unsafe interface IFrameAllocator
    {
        void* Alloc(int byteCount);
        void Free(void* ptr);
    }
}