#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IlInterpreter.Interpreter
{

// A single 4-byte slot of the Vm engine's unmanaged numeric frame. The slot's static SType
// (LoweredMethod.SlotTypes) tells the executor which field is live — there is no runtime tag.
// The dispatch loop reinterprets slots via byte* casts, so the backing memory stays native (no GC).
[StructLayout(LayoutKind.Explicit, Size = 4)]
internal struct Value
{
    [FieldOffset(0)] public int   I4; // int, bool (0/1), char, short — sign/zero-extended
    [FieldOffset(0)] public float R4;
    [FieldOffset(0)] public uint  U4; // bit-exact copies (mov, Vt span words)
}
}
