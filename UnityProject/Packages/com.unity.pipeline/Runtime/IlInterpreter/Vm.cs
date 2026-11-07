#nullable enable
using System;
using System.Collections.Generic;
namespace IlInterpreter
{

// VM intrinsics — always present, host-independent. Like syscalls / WASI imports.
// The interpreter intercepts calls to these methods; bodies exist only so the
// methods can be referenced from script source at compile time.
static class Vm
{
    public static void Log(string message) { }
}
}
