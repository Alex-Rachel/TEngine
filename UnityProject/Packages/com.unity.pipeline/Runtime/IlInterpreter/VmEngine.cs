#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
// Use the Unsafe bundled with the package (renamed with a "UnityPipeline." prefix), not the BCL or editor copy.
using Unsafe = UnityPipeline.System.Runtime.CompilerServices.Unsafe;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

#if !NET5_0_OR_GREATER
// SkipLocalsInit polyfill for the Unity (Mono/netstandard) build — the compiler keys on the
// attribute's full name, so a local declaration works like the BCL one.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct
        | AttributeTargets.Interface | AttributeTargets.Constructor | AttributeTargets.Method
        | AttributeTargets.Property | AttributeTargets.Event, Inherited = false)]
    internal sealed class SkipLocalsInitAttribute : Attribute { }
}
#endif

namespace IlInterpreter.Interpreter
{

#if ILINTERPRETER_OPSTATS
// Opcode histogram for the bench harness (IlInterpreter/tests/IlInterpreter.BenchStats).
static class VmOpStats
{
    public static readonly long[] Counts = new long[512];
    public static void Reset() => Array.Clear(Counts, 0, Counts.Length);
}
#endif

sealed partial class ScriptInterpreter
{
    // The execution engine. Runs lowered IR over a native (unmanaged) Value frame instead of a
    // managed byte[] — no GC for the frame, no per-dispatch `fixed` pin on the numeric stack.
    // SkipLocalsInit: Run() is reentered on every reentrant Invoke; without this the JIT
    // zero-fills its whole frame (including the 128-byte stackalloc continuation stack) per
    // call — a major chunk of Mono's 5x call overhead vs CoreCLR. Every local and stackalloc
    // slot below is definitely-assigned before use, so dropping .locals init is safe.
    [System.Runtime.CompilerServices.SkipLocalsInit]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    sealed unsafe class Vm : IDisposable
    {
        internal const int NumSlots = 256 * 1024; // Value slots (4 bytes each); also the lowering frame cap
        internal const int RefSlots = 128 * 1024;

        // Both arenas are indexed by bp+slot, but the reference arena (_ref, RefSlots) is smaller
        // than the numeric one (_num, NumSlots). A frame that fits the numeric arena can still
        // overrun _ref via its RefClearLen reference slots, so every frame entry must bound BOTH.
        // bp is cumulative across reentrant Invokes and nested script calls.
        static bool FrameFits(int bp, LoweredMethod lm) =>
            (long)bp + lm.FrameSize <= NumSlots && (long)bp + lm.RefClearLen <= RefSlots;

        // Cached array Types for the ldelem/stelem exact-type dispatch. typeof(byte[]) folds to a
        // constant on Mono, but IL2CPP re-resolves the array class through a metadata hashtable per
        // typeof site — hoisting to static readonly makes per-element dispatch a Type ref-compare.
        static readonly Type T_byteA = typeof(byte[]), T_sbyteA = typeof(sbyte[]),
                             T_shortA = typeof(short[]), T_ushortA = typeof(ushort[]),
                             T_charA = typeof(char[]), T_boolA = typeof(bool[]),
                             T_intA = typeof(int[]), T_uintA = typeof(uint[]), T_floatA = typeof(float[]),
                             T_longA = typeof(long[]), T_ulongA = typeof(ulong[]), T_doubleA = typeof(double[]);

        readonly ScriptInterpreter _owner;
        readonly IFrameAllocator   _alloc;
        readonly Value*            _num;   // unmanaged numeric/Vt frame
        readonly object?[]         _ref;   // managed reference frame (GC roots)
        readonly int[]             _steps = new int[1];
        bool _disposed;

        // Frame base for the NEXT Invoke: 0 at top level, bumped to the current frame's top while
        // a call executes, so a reentrant Invoke (a host call that woven-dispatches back into this
        // same VM) runs above the live frame instead of clobbering it at 0.
        int _base;

        // Saved caller state for the iterative script-call stack: call_script/newobj_script push
        // one of these and jump ip to the callee's blob region instead of recursing into Run()
        // (a recursive call of the giant dispatch method cost ~120ns on Mono). Shared across
        // reentrant Runs: each Run treats _framesTop at its entry as the floor, and
        // Invoke*/InvokeTyped restore _framesTop in their finally.
        struct SavedFrame
        {
            public ParsedMethod M;   // caller method
            public int Ip;           // blob-absolute resume ip (after the call op)
            public int Bp;           // caller frame base
            public int RetDst;       // caller slot for the callee's return; -1 = discard
            public int ContSp;       // caller's finally-continuation stack top
            // Caller slot to receive the callee's (mutated) flat-struct `this` bytes on return
            // (copy-in/copy-out byref semantics for struct instance methods/ctors); -1 = none.
            public int VtThisWb;
        }
        const int MaxCallDepth = 4096;
        readonly SavedFrame[] _frames = new SavedFrame[MaxCallDepth];
        int _framesTop;

        // Poison mode (ILINTERPRETER_POISON_FRAMES=1, tests/CI only): fill the numeric frame with
        // 0xCD before every top-level call so a never-written cell reads loud garbage instead of
        // a plausible zero. Frame-typing bugs are silent on Mono/CoreCLR's zeroed pages but
        // corrupt IL2CPP players running on recycled memory; poisoning makes every environment
        // behave like the worst case.
        static readonly bool s_PoisonFrames =
            Environment.GetEnvironmentVariable("ILINTERPRETER_POISON_FRAMES") == "1";

        // Execution value-trace (ILINTERPRETER_TRACE=1, dev diagnosis only): dump each IR op with its
        // IL offset and the current value of its first operand/dst slot, so a divergence can be
        // localized to the first step where a slot goes wrong — the value-flow view that otherwise
        // has to be hand-instrumented. One well-predicted branch per op when off; zero formatting.
        static readonly bool s_Trace =
            Environment.GetEnvironmentVariable("ILINTERPRETER_TRACE") == "1";

        void PoisonNumFrame() => Unsafe.InitBlockUnaligned((byte*)_num, 0xCD, (uint)(NumSlots * sizeof(Value)));

        // Formats one dispatch step for ILINTERPRETER_TRACE: IL offset + op + its operand slots'
        // CURRENT values (state entering the op — a source read here is the value the op consumes;
        // a dst read is its pre-op value, whose result shows up when the next op reads it). Words
        // that aren't frame slots (immediates/tokens/targets) fall back to their raw int.
        static void TraceStep(int ip, Op irop, uint* irP, int irLen, byte* numF,
            object?[] refStack, SType[] slotT, int bp, LoweredMethod lm, ParsedMethod method)
        {
            int rel = ip - lm.IrStart;
            int ilOff = (uint)rel < (uint)lm.IrToIlOffset.Length ? lm.IrToIlOffset[rel] : 0;
            string Slot(int w)
            {
                if ((uint)w >= (uint)slotT.Length) return w.ToString();
                return slotT[w] switch
                {
                    SType.I4 => $"s{w}={*(int*)(numF + w * 4)}",
                    SType.R4 => $"s{w}={*(float*)(numF + w * 4)}f",
                    _        => $"s{w}=<{refStack[bp + w] ?? "null"}>",
                };
            }
            string a = ip + 1 < irLen ? Slot((int)irP[ip + 1]) : "";
            string b = ip + 2 < irLen ? Slot((int)irP[ip + 2]) : "";
            string c = ip + 3 < irLen ? Slot((int)irP[ip + 3]) : "";
            string args = "";
            if (irop == Op.call_host || irop == Op.call_host_byref)
            {
                int argc = (int)irP[ip + 4];
                var sb = new System.Text.StringBuilder(" args=[");
                for (int k = 0; k < argc; k++) { if (k > 0) sb.Append(", "); sb.Append(Slot((int)irP[ip + 5 + k])); }
                sb.Append(']');
                args = sb.ToString();
            }
            Console.Error.WriteLine($"[exec] {method.Name} IL+0x{ilOff:X4} {irop} [{a} | {b} | {c}]{args}");
        }

        public Vm(ScriptInterpreter owner)
        {
            _owner = owner;
            _alloc = owner._allocator;
            _num   = (Value*)_alloc.Alloc(NumSlots * sizeof(Value));
            _ref   = new object?[RefSlots];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _alloc.Free(_num);
        }

        // Public entry: place args at the current frame base and run — the only boxing boundary.
        // _base is 0 at the top level and the live frame's top during a reentrant call, so nested
        // invocations never overwrite an in-progress frame.
        public object? Invoke(ParsedMethod method, object?[] args)
        {
            var asm = _owner._parsed
                ?? throw new ScriptRuntimeException("No script loaded — call Load() first");
            _steps[0] = _owner.StepLimit;
            if (s_PoisonFrames && _base == 0) PoisonNumFrame();
            int bp = _base, saved = _base, savedTop = _framesTop;
            _base = bp + method.Lowered.FrameSize;
            try { return Box(method, Run(method, args, asm, bp)); }
            finally { _base = saved; _framesTop = savedTop; }
        }

        // Zero-allocation typed entries — no object?[], no arg boxing. The result is boxed once
        // only if the method returns a primitive.
        public object? InvokeTyped(ParsedMethod method)
        {
            var asm = _owner._parsed ?? throw new ScriptRuntimeException("No script loaded — call Load() first");
            _steps[0] = _owner.StepLimit;
            if (s_PoisonFrames && _base == 0) PoisonNumFrame();
            var lm = method.Lowered;
            int bp = _base, saved = _base, savedTop = _framesTop;
            _base = bp + lm.FrameSize;
            try
            {
                if (!FrameFits(bp, lm)) throw new ScriptRuntimeException("Script frame overflow");
                if (lm.RefClearLen > 0) Array.Clear(_ref, bp, lm.RefClearLen);
                if (!method.IsStatic && _owner._instance != null) _ref[bp + (lm.ArgSlot != null ? lm.ArgSlot[0] : 0)] = _owner._instance;
                return Box(method, Run(method, null, asm, bp));
            }
            finally { _base = saved; _framesTop = savedTop; }
        }

        // Run an instance method with an EXPLICIT receiver instead of the script singleton —
        // the ScriptEnumerator bridge pumping an iterator state machine (receiver = the state
        // machine ScriptObject). Mirrors InvokeTyped(); reentrancy-safe via _base.
        public object? InvokeReceiver(ParsedMethod method, object receiver)
        {
            var asm = _owner._parsed ?? throw new ScriptRuntimeException("No script loaded — call Load() first");
            _steps[0] = _owner.StepLimit;
            if (s_PoisonFrames && _base == 0) PoisonNumFrame();
            var lm = method.Lowered;
            int bp = _base, saved = _base, savedTop = _framesTop;
            _base = bp + lm.FrameSize;
            try
            {
                if (!FrameFits(bp, lm)) throw new ScriptRuntimeException("Script frame overflow");
                if (lm.RefClearLen > 0) Array.Clear(_ref, bp, lm.RefClearLen);
                _ref[bp + (lm.ArgSlot != null ? lm.ArgSlot[0] : 0)] = receiver;
                return Box(method, Run(method, null, asm, bp));
            }
            finally { _base = saved; _framesTop = savedTop; }
        }

        public object? InvokeTyped<T1>(ParsedMethod method, T1 a1)
        {
            var asm = _owner._parsed ?? throw new ScriptRuntimeException("No script loaded — call Load() first");
            _steps[0] = _owner.StepLimit;
            if (s_PoisonFrames && _base == 0) PoisonNumFrame();
            var lm = method.Lowered;
            int bp = _base, saved = _base, savedTop = _framesTop;
            _base = bp + lm.FrameSize;
            try
            {
                if (!FrameFits(bp, lm)) throw new ScriptRuntimeException("Script frame overflow");
                if (lm.RefClearLen > 0) Array.Clear(_ref, bp, lm.RefClearLen);
                byte* num = (byte*)_num;
                int s = 0;
                if (!method.IsStatic && _owner._instance != null) { _ref[bp + (lm.ArgSlot != null ? lm.ArgSlot[0] : 0)] = _owner._instance; s = 1; }
                WriteTypedArg(num, _ref, lm.SlotTypes, bp, lm.ArgSlot != null && s < lm.ArgSlot.Length ? lm.ArgSlot[s] : s, a1);
                return Box(method, Run(method, null, asm, bp));
            }
            finally { _base = saved; _framesTop = savedTop; }
        }

        public object? InvokeTyped<T1, T2>(ParsedMethod method, T1 a1, T2 a2)
        {
            var asm = _owner._parsed ?? throw new ScriptRuntimeException("No script loaded — call Load() first");
            _steps[0] = _owner.StepLimit;
            if (s_PoisonFrames && _base == 0) PoisonNumFrame();
            var lm = method.Lowered;
            int bp = _base, saved = _base, savedTop = _framesTop;
            _base = bp + lm.FrameSize;
            try
            {
                if (!FrameFits(bp, lm)) throw new ScriptRuntimeException("Script frame overflow");
                if (lm.RefClearLen > 0) Array.Clear(_ref, bp, lm.RefClearLen);
                byte* num = (byte*)_num;
                int s = 0;
                if (!method.IsStatic && _owner._instance != null) { _ref[bp + (lm.ArgSlot != null ? lm.ArgSlot[0] : 0)] = _owner._instance; s = 1; }
                WriteTypedArg(num, _ref, lm.SlotTypes, bp, lm.ArgSlot != null && s < lm.ArgSlot.Length ? lm.ArgSlot[s] : s, a1); s++;
                WriteTypedArg(num, _ref, lm.SlotTypes, bp, lm.ArgSlot != null && s < lm.ArgSlot.Length ? lm.ArgSlot[s] : s, a2);
                return Box(method, Run(method, null, asm, bp));
            }
            finally { _base = saved; _framesTop = savedTop; }
        }

        public object? InvokeTyped<T1, T2, T3>(ParsedMethod method, T1 a1, T2 a2, T3 a3)
        {
            var asm = _owner._parsed ?? throw new ScriptRuntimeException("No script loaded — call Load() first");
            _steps[0] = _owner.StepLimit;
            if (s_PoisonFrames && _base == 0) PoisonNumFrame();
            var lm = method.Lowered;
            int bp = _base, saved = _base, savedTop = _framesTop;
            _base = bp + lm.FrameSize;
            try
            {
                if (!FrameFits(bp, lm)) throw new ScriptRuntimeException("Script frame overflow");
                if (lm.RefClearLen > 0) Array.Clear(_ref, bp, lm.RefClearLen);
                byte* num = (byte*)_num;
                int s = 0;
                if (!method.IsStatic && _owner._instance != null) { _ref[bp + (lm.ArgSlot != null ? lm.ArgSlot[0] : 0)] = _owner._instance; s = 1; }
                WriteTypedArg(num, _ref, lm.SlotTypes, bp, lm.ArgSlot != null && s < lm.ArgSlot.Length ? lm.ArgSlot[s] : s, a1); s++;
                WriteTypedArg(num, _ref, lm.SlotTypes, bp, lm.ArgSlot != null && s < lm.ArgSlot.Length ? lm.ArgSlot[s] : s, a2); s++;
                WriteTypedArg(num, _ref, lm.SlotTypes, bp, lm.ArgSlot != null && s < lm.ArgSlot.Length ? lm.ArgSlot[s] : s, a3);
                return Box(method, Run(method, null, asm, bp));
            }
            finally { _base = saved; _framesTop = savedTop; }
        }

        static object? Box(ParsedMethod method, CallReturn cr)
        {
            if (method.IsVoid) return null;
            // A bool-returning method stores its result as I4 but must box as bool — the lowerer
            // picks the ret op from the eval-stack slot type, so a bool value that flowed through an
            // I4 slot (e.g. a host op_Equality result now typed I4) would otherwise box as int and
            // fault a caller's (bool) cast.
            if (cr.Type == SType.I4 && method.ReturnIsBool) return cr.I4 != 0;
            return cr.Type switch
            {
                SType.I4 => (object?)cr.I4, SType.R4 => (object?)cr.R4,
                SType.I8 => cr.I8, SType.R8 => cr.R8,
                _ => cr.O,
            };
        }

        // Boxed value → I4 slot bits, covering the whole I4-mapped family: a delegate adapter (or
        // host callback) hands args boxed as their REAL type — char/byte/short/enum — and an
        // int/bool-only check silently wrote 0 (found by the delegate arg-type matrix).
        static int CoerceBoxedI4(object? a) => a switch
        {
            int i => i, bool b => b ? 1 : 0,
            char c => c, byte by => by, sbyte sb => sb, short sh => sh, ushort us => us,
            uint u => unchecked((int)u),
            // A boxed WIDE value reaches an I4 read when a generic host/delegate call returns
            // long/double into an O slot and `(int)` converts it (found by fuzzing: a
            // Func<long,long> result truncated to 0 because these arms were missing).
            long l => unchecked((int)l), ulong ul => unchecked((int)ul),
            float fl => (int)fl, double dl => (int)dl,
            Enum => unchecked((int)Convert.ToInt64(a)),
            _ => 0,
        };

        static float CoerceBoxedR4(object? a) => a switch
        {
            float f => f, int i => i, double d => (float)d,
            long l => l, ulong ul => ul, // boxed wide → float (see CoerceBoxedI4)
            _ => CoerceBoxedI4(a),
        };

        // Boxed value → I8, covering everything the interpreter maps to a 64-bit slot plus the
        // narrower families (a host return or delegate arg can arrive boxed as any of them).
        static long CoerceBoxedI8(object? a) => a switch
        {
            long l => l, ulong u => unchecked((long)u),
            int i => i, uint u4 => u4,
            bool b => b ? 1L : 0L, char c => c,
            byte by => by, sbyte sb => sb, short sh => sh, ushort us => us,
            float f => (long)f, double d => (long)d,
            Enum => Convert.ToInt64(a),
            _ => 0L,
        };

        static double CoerceBoxedR8(object? a) => a switch
        {
            double d => d, float f => f,
            long l => l, ulong u => u,
            int i => i, uint u4 => u4,
            _ => CoerceBoxedI8(a),
        };

        // Writes arg `val` at the absolute frame position bp+slot (slot type indexed
        // frame-relative), so a reentrant call writes into its own frame region.
        static void WriteTypedArg<T>(byte* num, object?[] r, SType[] t, int bp, int slot, T val)
        {
            if (slot < t.Length && t[slot] == SType.I4)
            {
                int iv = typeof(T) == typeof(int) ? Unsafe.As<T, int>(ref val)
                       : typeof(T) == typeof(bool) ? (Unsafe.As<T, bool>(ref val) ? 1 : 0)
                       : CoerceBoxedI4(val);
                *(int*)(num + (bp + slot) * 4) = iv;
            }
            else if (slot < t.Length && t[slot] == SType.R4)
            {
                float fv = typeof(T) == typeof(float) ? Unsafe.As<T, float>(ref val)
                         : typeof(T) == typeof(int) ? (float)Unsafe.As<T, int>(ref val)
                         : CoerceBoxedR4(val);
                *(float*)(num + (bp + slot) * 4) = fv;
            }
            else if (slot < t.Length && t[slot] == SType.I8)
            {
                Unsafe.WriteUnaligned(num + (bp + slot) * 4, CoerceBoxedI8(val));
            }
            else if (slot < t.Length && t[slot] == SType.R8)
            {
                Unsafe.WriteUnaligned(num + (bp + slot) * 4, CoerceBoxedR8(val));
            }
            else
            {
                r[bp + slot] = (object?)val;
            }
        }

        CallReturn Run(ParsedMethod method, object?[]? args, ParsedAssembly asm, int bp)
        {
            var lm       = method.Lowered;
            // Frame overflow guard for the top-level entries (Invoke*/InvokeTyped) and for reentrant
            // host-callback invokes that stack frames at a high bp. Without it a frame overrunning
            // either arena writes past the backing block. Bounds both the numeric and reference
            // arenas; FrameSize/RefClearLen are also capped at lowering.
            if (!FrameFits(bp, lm))
                throw new ScriptRuntimeException("Script frame overflow");
            var ir       = asm.IrBlob!;        // all methods' IR, blob-absolute branch targets
            var slotT    = lm.SlotTypes;
            var refStack = _ref;
            byte* numF   = (byte*)(_num + bp); // frame base; slot s lives at numF + s*4
            bool trace   = s_Trace; // hoist the debug-trace gate out of the per-op path

            // Copy args into their frame slots (only on the outermost call). Arg index maps
            // through lm.ArgSlot when flat-struct args shift the layout.
            if (args != null)
            {
                if (lm.RefClearLen > 0) Array.Clear(refStack, bp, lm.RefClearLen);
                var argSlots = lm.ArgSlot;
                for (int i = 0; i < args.Length && i < method.ArgCount; i++)
                {
                    object? a = args[i];
                    int si = argSlots != null && i < argSlots.Length ? argSlots[i] : i;
                    var st = si < slotT.Length ? slotT[si] : SType.O;
                    if (st == SType.I4)
                        *(int*)(numF + si * 4) = CoerceBoxedI4(a);
                    else if (st == SType.R4)
                        *(float*)(numF + si * 4) = CoerceBoxedR4(a);
                    else if (st == SType.I8)
                        Unsafe.WriteUnaligned(numF + si * 4, CoerceBoxedI8(a));
                    else if (st == SType.R8)
                        Unsafe.WriteUnaligned(numF + si * 4, CoerceBoxedR8(a));
                    else if (st == SType.Vt)
                        UnboxVt(numF, si, lm.StructLayouts![si]!, a); // boxed ScriptObject (or null → zero)
                    else
                        refStack[bp + si] = a;
                }
            }

            int ip = lm.IrStart;
            var steps = _steps;

            // Continuation stack for lowered try/finally: `leave` pushes the addresses to visit
            // after each finally handler (outer handlers, then the leave target); each handler's
            // br_cont (endfinally) pops the next. Shared by every frame of this Run (script calls
            // save/restore their contSp watermark in SavedFrame), so ContMax bounds the TOTAL
            // pending-finally depth across the whole script call stack.
            const int ContMax = 256;
            int* contStack = stackalloc int[ContMax];
            int contSp = 0;

            // Floor of the iterative call stack for THIS Run. Frames below it belong to outer
            // (reentrant) Runs. Invoke*/InvokeTyped restore _framesTop on exit, including when
            // an exception unwinds past us.
            int frameBase = _framesTop;
            var frames = _frames;

            fixed (uint* irP = ir)
            {
                while (true)
                {
                    if (--steps[0] < 0)
                    {
                        int rel = ip - lm.IrStart;
                        int ilOff = (uint)rel < (uint)lm.IrToIlOffset.Length ? lm.IrToIlOffset[rel] : 0;
                        throw new ScriptRuntimeException($"Script exceeded step limit{At(method, ilOff)}");
                    }

                    var irop = (Op)irP[ip];
                    if (trace) TraceStep(ip, irop, irP, ir.Length, numF, refStack, slotT, bp, lm, method);
#if ILINTERPRETER_OPSTATS
                    VmOpStats.Counts[(int)irop]++;
#endif
                    switch (irop)
                    {
                        case Op.nop: ip++; break;

                        case Op.mov:
                        {
                            int dst = (int)irP[ip + 1], src = (int)irP[ip + 2];
                            var st = slotT[src];
                            var dt = slotT[dst];
                            // Wide slots first: an 8-byte copy (or typed conversion) — the narrow
                            // matrix below would copy half the value.
                            if (dt == SType.I8)
                            { Unsafe.WriteUnaligned(numF + dst * 4, RdI8(numF, refStack, slotT, src, bp)); ip += 3; break; }
                            if (dt == SType.R8)
                            { Unsafe.WriteUnaligned(numF + dst * 4, RdR8(numF, refStack, slotT, src, bp)); ip += 3; break; }
                            if (st == SType.I8)
                            {
                                long wv = Unsafe.ReadUnaligned<long>(numF + src * 4);
                                if (dt == SType.I4) *(int*)(numF + dst * 4) = unchecked((int)wv);
                                else if (dt == SType.R4) *(float*)(numF + dst * 4) = wv;
                                else refStack[bp + dst] = wv;
                                ip += 3; break;
                            }
                            if (st == SType.R8)
                            {
                                double wd = Unsafe.ReadUnaligned<double>(numF + src * 4);
                                if (dt == SType.I4) *(int*)(numF + dst * 4) = (int)wd;
                                else if (dt == SType.R4) *(float*)(numF + dst * 4) = (float)wd;
                                else refStack[bp + dst] = wd;
                                ip += 3; break;
                            }
                            if (st != SType.O && dt != SType.O)
                                *(int*)(numF + dst * 4) = *(int*)(numF + src * 4);
                            else if (st == SType.O && dt == SType.O)
                                refStack[bp + dst] = refStack[bp + src];
                            else if (st == SType.I4)
                                refStack[bp + dst] = *(int*)(numF + src * 4);
                            else if (st == SType.R4)
                                refStack[bp + dst] = *(float*)(numF + src * 4);
                            else if (dt == SType.I4)
                            {
                                // Full boxed-I4 family, not just int/bool: a byref write-back can
                                // leave a boxed enum/char/uint in the O slot (Enum.TryParse out).
                                *(int*)(numF + dst * 4) = CoerceBoxedI4(refStack[bp + src]);
                            }
                            else
                            {
                                *(float*)(numF + dst * 4) = CoerceBoxedR4(refStack[bp + src]);
                            }
                            ip += 3; break;
                        }

                        case Op.clone_sc:
                        {
                            int dst = (int)irP[ip + 1], src = (int)irP[ip + 2];
                            var v = refStack[bp + src];
                            // Clone ONLY script-STRUCT values (value semantics). Class instances,
                            // strings, and arrays are reference types — pass them through so a dup
                            // of an O value can use clone_sc uniformly and still preserve reference
                            // identity for non-structs.
                            refStack[bp + dst] = v is ScriptObject so && so.Type.IsScriptStructValue ? so.Clone() : v;
                            ip += 3; break;
                        }

                        case Op.ldc_i4:
                        {
                            int dst = (int)irP[ip + 1];
                            *(int*)(numF + dst * 4) = (int)irP[ip + 2];
                            ip += 3; break;
                        }
                        case Op.ldc_r4:
                        {
                            int dst = (int)irP[ip + 1];
                            uint raw = irP[ip + 2]; *(float*)(numF + dst * 4) = Unsafe.As<uint, float>(ref raw);
                            ip += 3; break;
                        }
                        case Op.ldnull:
                            refStack[bp + (int)irP[ip + 1]] = null;
                            ip += 2; break;
                        case Op.ldstr:
                        {
                            int dst = (int)irP[ip + 1];
                            refStack[bp + dst] = lm.Strings[(int)irP[ip + 2]];
                            ip += 3; break;
                        }

                        case Op.add_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1+v2; ip+=4; break; }
                        case Op.add_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)+*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.sub_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1-v2; ip+=4; break; }
                        case Op.sub_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)-*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.mul_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1*v2; ip+=4; break; }
                        case Op.mul_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4) * *(int*)(numF+s2*4); ip+=4; break; }
                        case Op.div_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1/v2; ip+=4; break; }
                        case Op.div_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4) / *(int*)(numF+s2*4); ip+=4; break; }
                        case Op.rem_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1%v2; ip+=4; break; }
                        case Op.rem_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)%*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.neg_i4:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; int v=RdI4(numF,refStack,slotT,s,bp); *(int*)(numF+d*4)=-v; ip+=3; break; }
                        case Op.neg_i4_n:  { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=-*(int*)(numF+s*4); ip+=3; break; }

                        case Op.add_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(float*)(numF+d*4)=v1+v2; ip+=4; break; }
                        case Op.add_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s1*4)+*(float*)(numF+s2*4); ip+=4; break; }
                        case Op.sub_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(float*)(numF+d*4)=v1-v2; ip+=4; break; }
                        case Op.sub_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s1*4)-*(float*)(numF+s2*4); ip+=4; break; }
                        case Op.mul_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(float*)(numF+d*4)=v1*v2; ip+=4; break; }
                        case Op.mul_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s1*4) * *(float*)(numF+s2*4); ip+=4; break; }
                        case Op.div_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(float*)(numF+d*4)=v1/v2; ip+=4; break; }
                        case Op.div_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s1*4) / *(float*)(numF+s2*4); ip+=4; break; }
                        case Op.rem_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(float*)(numF+d*4)=v1%v2; ip+=4; break; }
                        case Op.rem_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s1*4)%*(float*)(numF+s2*4); ip+=4; break; }
                        case Op.neg_r4:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; float v=RdR4(numF,refStack,slotT,s,bp); *(float*)(numF+d*4)=-v; ip+=3; break; }
                        case Op.neg_r4_n:  { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(float*)(numF+d*4)=-*(float*)(numF+s*4); ip+=3; break; }

                        case Op.and_i4:       { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1&v2; ip+=4; break; }
                        case Op.and_i4_nn:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)&*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.or_i4:        { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1|v2; ip+=4; break; }
                        case Op.or_i4_nn:     { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)|*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.xor_i4:       { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1^v2; ip+=4; break; }
                        case Op.xor_i4_nn:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)^*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.not_i4:       { int d=(int)irP[ip+1],s=(int)irP[ip+2]; int v=RdI4(numF,refStack,slotT,s,bp); *(int*)(numF+d*4)=~v; ip+=3; break; }
                        case Op.not_i4_n:     { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=~*(int*)(numF+s*4); ip+=3; break; }
                        case Op.shl_i4:       { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1<<v2; ip+=4; break; }
                        case Op.shl_i4_nn:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)<<*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.shr_i4:       { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1>>v2; ip+=4; break; }
                        case Op.shr_i4_nn:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)>>*(int*)(numF+s2*4); ip+=4; break; }
                        case Op.shr_un_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=(int)((uint)v1>>v2); ip+=4; break; }
                        case Op.shr_un_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=(int)((uint)*(int*)(numF+s1*4)>>*(int*)(numF+s2*4)); ip+=4; break; }
                        case Op.div_un_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=(int)((uint)v1/(uint)v2); ip+=4; break; }
                        case Op.div_un_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=(int)((uint)*(int*)(numF+s1*4)/(uint)*(int*)(numF+s2*4)); ip+=4; break; }
                        case Op.rem_un_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=(int)((uint)v1%(uint)v2); ip+=4; break; }
                        case Op.rem_un_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=(int)((uint)*(int*)(numF+s1*4)%(uint)*(int*)(numF+s2*4)); ip+=4; break; }

                        // --- 64-bit family. Wide slots: Unsafe.Read/WriteUnaligned — a slot's
                        // byte offset is only 4-aligned. Operands read through Rd helpers so a
                        // stray I4/boxed operand coerces instead of reading garbage.
                        case Op.ldc_i8: { int d=(int)irP[ip+1]; ulong v=(ulong)irP[ip+2]|((ulong)irP[ip+3]<<32); Unsafe.WriteUnaligned(numF+d*4, unchecked((long)v)); ip+=4; break; }
                        case Op.ldc_r8: { int d=(int)irP[ip+1]; ulong v=(ulong)irP[ip+2]|((ulong)irP[ip+3]<<32); Unsafe.WriteUnaligned(numF+d*4, BitConverter.Int64BitsToDouble(unchecked((long)v))); ip+=4; break; }
                        case Op.add_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) + RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.sub_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) - RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.mul_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) * RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.div_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) / RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.rem_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) % RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.div_un_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, unchecked((long)((ulong)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) / (ulong)RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)))); ip+=4; break; }
                        case Op.rem_un_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, unchecked((long)((ulong)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) % (ulong)RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)))); ip+=4; break; }
                        case Op.and_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) & RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.or_i8:  { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) | RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.xor_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) ^ RdI8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.shl_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) << RdI4(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.shr_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) >> RdI4(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.shr_un_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, unchecked((long)((ulong)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) >> RdI4(numF,refStack,slotT,(int)irP[ip+3],bp)))); ip+=4; break; }
                        case Op.neg_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, -RdI8(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.not_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, ~RdI8(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.add_r8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) + RdR8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.sub_r8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) - RdR8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.mul_r8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) * RdR8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.div_r8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) / RdR8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.rem_r8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) % RdR8(numF,refStack,slotT,(int)irP[ip+3],bp)); ip+=4; break; }
                        case Op.neg_r8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, -RdR8(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.ceq_i8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) == RdI8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        case Op.cgt_i8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) > RdI8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        case Op.clt_i8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) < RdI8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        case Op.cgt_un_i8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = (ulong)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) > (ulong)RdI8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        case Op.clt_un_i8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = (ulong)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp) < (ulong)RdI8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        case Op.ceq_r8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) == RdR8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        case Op.cgt_r8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) > RdR8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        case Op.clt_r8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = RdR8(numF,refStack,slotT,(int)irP[ip+2],bp) < RdR8(numF,refStack,slotT,(int)irP[ip+3],bp) ? 1 : 0; ip+=4; break; }
                        // Unordered: true when NaN participates — IL semantics for negated float branches.
                        case Op.cgt_un_r8: { int d=(int)irP[ip+1]; double a8=RdR8(numF,refStack,slotT,(int)irP[ip+2],bp), b8=RdR8(numF,refStack,slotT,(int)irP[ip+3],bp); *(int*)(numF+d*4) = (a8 > b8 || double.IsNaN(a8) || double.IsNaN(b8)) ? 1 : 0; ip+=4; break; }
                        case Op.clt_un_r8: { int d=(int)irP[ip+1]; double a8=RdR8(numF,refStack,slotT,(int)irP[ip+2],bp), b8=RdR8(numF,refStack,slotT,(int)irP[ip+3],bp); *(int*)(numF+d*4) = (a8 < b8 || double.IsNaN(a8) || double.IsNaN(b8)) ? 1 : 0; ip+=4; break; }
                        case Op.conv_i8_i4: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (long)RdI4(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_i8_u4: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (long)(uint)RdI4(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_i4_i8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = unchecked((int)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_i8_r4: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (long)RdR4(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_r4_i8: { int d=(int)irP[ip+1]; *(float*)(numF+d*4) = RdI8(numF,refStack,slotT,(int)irP[ip+2],bp); ip+=3; break; }
                        case Op.conv_i8_r8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (long)RdR8(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_r8_i8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (double)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_r8_r4: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (double)RdR4(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_r4_r8: { int d=(int)irP[ip+1]; *(float*)(numF+d*4) = (float)RdR8(numF,refStack,slotT,(int)irP[ip+2],bp); ip+=3; break; }
                        case Op.conv_r8_i4: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (double)RdI4(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.conv_i4_r8: { int d=(int)irP[ip+1]; *(int*)(numF+d*4) = (int)RdR8(numF,refStack,slotT,(int)irP[ip+2],bp); ip+=3; break; }
                        case Op.conv_r8_u8: { int d=(int)irP[ip+1]; Unsafe.WriteUnaligned(numF+d*4, (double)(ulong)RdI8(numF,refStack,slotT,(int)irP[ip+2],bp)); ip+=3; break; }
                        case Op.brtrue_i8: { int cs=(int)irP[ip+1]; ip = RdI8(numF,refStack,slotT,cs,bp) != 0 ? (int)irP[ip+2] : ip+3; break; }
                        case Op.brfalse_i8: { int cs=(int)irP[ip+1]; ip = RdI8(numF,refStack,slotT,cs,bp) == 0 ? (int)irP[ip+2] : ip+3; break; }

                        case Op.ceq_i4:       { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1==v2?1:0; ip+=4; break; }
                        case Op.ceq_i4_nn:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)==*(int*)(numF+s2*4)?1:0; ip+=4; break; }
                        case Op.cgt_i4:       { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1>v2?1:0; ip+=4; break; }
                        case Op.cgt_i4_nn:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)>*(int*)(numF+s2*4)?1:0; ip+=4; break; }
                        case Op.clt_i4:       { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1<v2?1:0; ip+=4; break; }
                        case Op.clt_i4_nn:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(int*)(numF+s1*4)<*(int*)(numF+s2*4)?1:0; ip+=4; break; }
                        case Op.cgt_un_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=(uint)v1>(uint)v2?1:0; ip+=4; break; }
                        case Op.cgt_un_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=(uint)*(int*)(numF+s1*4)>(uint)*(int*)(numF+s2*4)?1:0; ip+=4; break; }
                        case Op.clt_un_i4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; int v1=RdI4(numF,refStack,slotT,s1,bp); int v2=RdI4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=(uint)v1<(uint)v2?1:0; ip+=4; break; }
                        case Op.clt_un_i4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=(uint)*(int*)(numF+s1*4)<(uint)*(int*)(numF+s2*4)?1:0; ip+=4; break; }

                        case Op.ceq_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1==v2?1:0; ip+=4; break; }
                        case Op.ceq_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(float*)(numF+s1*4)==*(float*)(numF+s2*4)?1:0; ip+=4; break; }
                        case Op.cgt_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1>v2?1:0; ip+=4; break; }
                        case Op.cgt_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(float*)(numF+s1*4)>*(float*)(numF+s2*4)?1:0; ip+=4; break; }
                        case Op.clt_r4:    { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=v1<v2?1:0; ip+=4; break; }
                        case Op.clt_r4_nn: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; *(int*)(numF+d*4)=*(float*)(numF+s1*4)<*(float*)(numF+s2*4)?1:0; ip+=4; break; }
                        case Op.cgt_un_r4: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=!(v1<=v2)?1:0; ip+=4; break; }
                        case Op.clt_un_r4: { int d=(int)irP[ip+1],s1=(int)irP[ip+2],s2=(int)irP[ip+3]; float v1=RdR4(numF,refStack,slotT,s1,bp); float v2=RdR4(numF,refStack,slotT,s2,bp); *(int*)(numF+d*4)=!(v1>=v2)?1:0; ip+=4; break; }

                        // --- Operand-immediate forms (_nk) ---
                        case Op.add_i4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)+(int)irP[ip+3]; ip+=4; break; }
                        case Op.add_r4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; uint k=irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s*4)+Unsafe.As<uint,float>(ref k); ip+=4; break; }
                        case Op.sub_i4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)-(int)irP[ip+3]; ip+=4; break; }
                        case Op.sub_r4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; uint k=irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s*4)-Unsafe.As<uint,float>(ref k); ip+=4; break; }
                        case Op.mul_r4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; uint k=irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s*4)*Unsafe.As<uint,float>(ref k); ip+=4; break; }
                        case Op.clt_i4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)<(int)irP[ip+3]?1:0; ip+=4; break; }
                        case Op.clt_r4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; uint k=irP[ip+3]; *(int*)(numF+d*4)=*(float*)(numF+s*4)<Unsafe.As<uint,float>(ref k)?1:0; ip+=4; break; }
                        case Op.cgt_i4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)>(int)irP[ip+3]?1:0; ip+=4; break; }
                        case Op.cgt_r4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; uint k=irP[ip+3]; *(int*)(numF+d*4)=*(float*)(numF+s*4)>Unsafe.As<uint,float>(ref k)?1:0; ip+=4; break; }
                        case Op.ceq_i4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)==(int)irP[ip+3]?1:0; ip+=4; break; }
                        case Op.mul_i4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)*(int)irP[ip+3]; ip+=4; break; }
                        case Op.div_i4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)/(int)irP[ip+3]; ip+=4; break; }
                        case Op.rem_i4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)%(int)irP[ip+3]; ip+=4; break; }
                        case Op.and_i4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)&(int)irP[ip+3]; ip+=4; break; }
                        case Op.or_i4_nk:     { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)|(int)irP[ip+3]; ip+=4; break; }
                        case Op.xor_i4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)^(int)irP[ip+3]; ip+=4; break; }
                        case Op.shl_i4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)<<(int)irP[ip+3]; ip+=4; break; }
                        case Op.shr_i4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=*(int*)(numF+s*4)>>(int)irP[ip+3]; ip+=4; break; }
                        case Op.shr_un_i4_nk: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; *(int*)(numF+d*4)=(int)((uint)*(int*)(numF+s*4)>>(int)irP[ip+3]); ip+=4; break; }
                        case Op.div_r4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; uint k=irP[ip+3]; *(float*)(numF+d*4)=*(float*)(numF+s*4)/Unsafe.As<uint,float>(ref k); ip+=4; break; }
                        case Op.ceq_r4_nk:    { int d=(int)irP[ip+1],s=(int)irP[ip+2]; uint k=irP[ip+3]; *(int*)(numF+d*4)=*(float*)(numF+s*4)==Unsafe.As<uint,float>(ref k)?1:0; ip+=4; break; }

                        // --- Fused compare-and-branch ---
                        case Op.blt_i4_nn: { int s1=(int)irP[ip+1],s2=(int)irP[ip+2]; int t=(int)irP[ip+3]; if (*(int*)(numF+s1*4) <  *(int*)(numF+s2*4)) ip = t; else ip += 4; break; }
                        case Op.blt_i4_nk: { int s=(int)irP[ip+1]; int k=(int)irP[ip+2]; int t=(int)irP[ip+3]; if (*(int*)(numF+s*4) <  k) ip = t; else ip += 4; break; }
                        case Op.bgt_r4_nn: { int s1=(int)irP[ip+1],s2=(int)irP[ip+2]; int t=(int)irP[ip+3]; if (*(float*)(numF+s1*4) >  *(float*)(numF+s2*4)) ip = t; else ip += 4; break; }
                        case Op.bgt_r4_nk: { int s=(int)irP[ip+1]; uint kk=irP[ip+2]; int t=(int)irP[ip+3]; if (*(float*)(numF+s*4) >  Unsafe.As<uint,float>(ref kk)) ip = t; else ip += 4; break; }
                        case Op.beq_i4_nn: { int s1=(int)irP[ip+1],s2=(int)irP[ip+2]; int t=(int)irP[ip+3]; if (*(int*)(numF+s1*4) == *(int*)(numF+s2*4)) ip = t; else ip += 4; break; }
                        case Op.beq_i4_nk: { int s=(int)irP[ip+1]; int k=(int)irP[ip+2]; int t=(int)irP[ip+3]; if (*(int*)(numF+s*4) == k) ip = t; else ip += 4; break; }
                        case Op.bne_i4_nn: { int s1=(int)irP[ip+1],s2=(int)irP[ip+2]; int t=(int)irP[ip+3]; if (*(int*)(numF+s1*4) != *(int*)(numF+s2*4)) ip = t; else ip += 4; break; }
                        case Op.bne_i4_nk: { int s=(int)irP[ip+1]; int k=(int)irP[ip+2]; int t=(int)irP[ip+3]; if (*(int*)(numF+s*4) != k) ip = t; else ip += 4; break; }

                        // --- For-loop super-instruction ---
                        case Op.for_i4_nk:
                        {
                            int s=(int)irP[ip+1]; int limit=(int)irP[ip+2]; int t=(int)irP[ip+3];
                            int v = *(int*)(numF+s*4) + 1;
                            *(int*)(numF+s*4) = v;
                            if (v < limit) ip = t; else ip += 4;
                            break;
                        }

                        case Op.ceq_o:
                        {
                            int d=(int)irP[ip+1];
                            var a=refStack[bp + (int)irP[ip+2]]; var b=refStack[bp + (int)irP[ip+3]];
                            // C# `==` on reference/object operands is reference equality (value types go
                            // through op_Equality, a host call). Object.Equals would value-compare two
                            // distinct boxes of the same primitive as equal — diverging from C#.
                            *(int*)(numF+d*4)=ReferenceEquals(a,b)?1:0; ip+=4; break;
                        }
                        case Op.cgt_un_o:
                        {
                            int d=(int)irP[ip+1];
                            *(int*)(numF+d*4)=refStack[bp + (int)irP[ip+2]] != null ? 1 : 0; ip+=4; break;
                        }

                        case Op.br: ip = (int)irP[ip + 1]; break;
                        case Op.push_cont:
                        {
                            if (contSp >= ContMax) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"finally nesting exceeds {ContMax}{At(method, il)}"); }
                            contStack[contSp++] = (int)irP[ip + 1];
                            ip += 2; break;
                        }
                        case Op.br_cont:
                        {
                            if (contSp == 0) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"endfinally with no pending continuation{At(method, il)}"); }
                            ip = contStack[--contSp];
                            break;
                        }
                        case Op.brtrue_i4:
                        {
                            int cond=(int)irP[ip+1]; int target=(int)irP[ip+2];
                            ip = *(int*)(numF+cond*4) != 0 ? target : ip + 3; break;
                        }
                        case Op.brfalse_i4:
                        {
                            int cond=(int)irP[ip+1]; int target=(int)irP[ip+2];
                            ip = *(int*)(numF+cond*4) == 0 ? target : ip + 3; break;
                        }
                        case Op.brtrue_o:
                        {
                            int cond=(int)irP[ip+1]; int target=(int)irP[ip+2];
                            // Pure REFERENCE test. Verified IL only puts references (or boxes) in
                            // an O-typed condition, and a box is non-null no matter its value —
                            // value-testing here made `(object)0 == null` true and flipped isinst
                            // results whose matched box held 0/false (found by fuzzing). Value-typed
                            // conditions run through brtrue_i4/brfalse_i4 instead.
                            ip = refStack[bp + cond] != null ? target : ip + 3; break;
                        }
                        case Op.brfalse_o:
                        {
                            int cond=(int)irP[ip+1]; int target=(int)irP[ip+2];
                            // Pure reference test — see brtrue_o.
                            ip = refStack[bp + cond] == null ? target : ip + 3; break;
                        }

                        case Op.conv_i4_r4: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; int sv=RdI4(numF,refStack,slotT,s,bp); *(float*)(numF+d*4)=(float)sv; ip+=3; break; }
                        case Op.conv_r4_i4: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; float sv=RdR4(numF,refStack,slotT,s,bp); *(int*)(numF+d*4)=(int)sv; ip+=3; break; }
                        case Op.conv_i4_i1: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; int sv=RdI4(numF,refStack,slotT,s,bp); *(int*)(numF+d*4)=(int)(sbyte)sv; ip+=3; break; }
                        case Op.conv_i4_u1: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; int sv=RdI4(numF,refStack,slotT,s,bp); *(int*)(numF+d*4)=(int)(byte)sv; ip+=3; break; }
                        case Op.conv_i4_i2: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; int sv=RdI4(numF,refStack,slotT,s,bp); *(int*)(numF+d*4)=(int)(short)sv; ip+=3; break; }
                        case Op.conv_i4_u2: { int d=(int)irP[ip+1],s=(int)irP[ip+2]; int sv=RdI4(numF,refStack,slotT,s,bp); *(int*)(numF+d*4)=(int)(ushort)sv; ip+=3; break; }

                        // A ret either leaves Run (call stack at this Run's floor) or pops the caller
                        // frame: read the return value from the CALLEE frame first, switch locals back
                        // to the caller, then store into the caller's RetDst slot. A primitive return
                        // into an O dst stays null; an O return into a numeric dst reads 0.
                        case Op.ret_void:
                        {
                            if (_framesTop == frameBase) return default;
                            var f = frames[--_framesTop];
                            byte* wbSrc = null; int wbSize = 0;
                            if (f.VtThisWb >= 0)
                            {
                                int thisSlot = lm.ArgSlot != null ? lm.ArgSlot[0] : 0;
                                wbSrc = numF + thisSlot * 4; wbSize = lm.StructLayouts![thisSlot]!.Size;
                            }
                            method = f.M; lm = method.Lowered; slotT = lm.SlotTypes; bp = f.Bp;
                            numF = (byte*)(_num + bp); ip = f.Ip; contSp = f.ContSp;
                            _base = bp + lm.FrameSize;
                            if (wbSize > 0) Buffer.MemoryCopy(wbSrc, numF + f.VtThisWb * 4, wbSize, wbSize);
                            if (f.RetDst != -1)
                            {
                                var st = slotT[f.RetDst];
                                if (st == SType.I4) *(int*)(numF + f.RetDst * 4) = 0;
                                else if (st == SType.R4) *(float*)(numF + f.RetDst * 4) = 0f;
                                else refStack[bp + f.RetDst] = null;
                            }
                            break;
                        }
                        case Op.ret_i4:
                        {
                            int s=(int)irP[ip+1]; var t=slotT[s];
                            int v=t==SType.I4?*(int*)(numF+s*4):t==SType.R4?(int)*(float*)(numF+s*4):CoerceBoxedI4(refStack[bp + s]);
                            if (_framesTop == frameBase) return CallReturn.FromI4(v);
                            var f = frames[--_framesTop];
                            byte* wbSrc = null; int wbSize = 0;
                            if (f.VtThisWb >= 0)
                            {
                                int thisSlot = lm.ArgSlot != null ? lm.ArgSlot[0] : 0;
                                wbSrc = numF + thisSlot * 4; wbSize = lm.StructLayouts![thisSlot]!.Size;
                            }
                            method = f.M; lm = method.Lowered; slotT = lm.SlotTypes; bp = f.Bp;
                            numF = (byte*)(_num + bp); ip = f.Ip; contSp = f.ContSp;
                            _base = bp + lm.FrameSize;
                            if (wbSize > 0) Buffer.MemoryCopy(wbSrc, numF + f.VtThisWb * 4, wbSize, wbSize);
                            if (f.RetDst != -1)
                            {
                                var st = slotT[f.RetDst];
                                if (st == SType.I4) *(int*)(numF + f.RetDst * 4) = v;
                                else if (st == SType.R4) *(float*)(numF + f.RetDst * 4) = (float)v;
                                else if (st == SType.I8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, (long)v);
                                else if (st == SType.R8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, (double)v);
                                else refStack[bp + f.RetDst] = v; // O-typed dst: box, don't drop
                            }
                            break;
                        }
                        case Op.ret_r4:
                        {
                            int s=(int)irP[ip+1]; var t=slotT[s];
                            float v=t==SType.R4?*(float*)(numF+s*4):t==SType.I4?(float)*(int*)(numF+s*4):CoerceBoxedR4(refStack[bp + s]);
                            if (_framesTop == frameBase) return CallReturn.FromR4(v);
                            var f = frames[--_framesTop];
                            byte* wbSrc = null; int wbSize = 0;
                            if (f.VtThisWb >= 0)
                            {
                                int thisSlot = lm.ArgSlot != null ? lm.ArgSlot[0] : 0;
                                wbSrc = numF + thisSlot * 4; wbSize = lm.StructLayouts![thisSlot]!.Size;
                            }
                            method = f.M; lm = method.Lowered; slotT = lm.SlotTypes; bp = f.Bp;
                            numF = (byte*)(_num + bp); ip = f.Ip; contSp = f.ContSp;
                            _base = bp + lm.FrameSize;
                            if (wbSize > 0) Buffer.MemoryCopy(wbSrc, numF + f.VtThisWb * 4, wbSize, wbSize);
                            if (f.RetDst != -1)
                            {
                                var st = slotT[f.RetDst];
                                if (st == SType.I4) *(int*)(numF + f.RetDst * 4) = (int)v;
                                else if (st == SType.R4) *(float*)(numF + f.RetDst * 4) = v;
                                else if (st == SType.I8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, (long)v);
                                else if (st == SType.R8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, (double)v);
                                else refStack[bp + f.RetDst] = v; // O-typed dst: box, don't drop
                            }
                            break;
                        }
                        case Op.ret_i8:
                        {
                            int s=(int)irP[ip+1];
                            long v=RdI8(numF,refStack,slotT,s,bp);
                            if (_framesTop == frameBase) return CallReturn.FromI8(v);
                            var f = frames[--_framesTop];
                            byte* wbSrc = null; int wbSize = 0;
                            if (f.VtThisWb >= 0)
                            {
                                int thisSlot = lm.ArgSlot != null ? lm.ArgSlot[0] : 0;
                                wbSrc = numF + thisSlot * 4; wbSize = lm.StructLayouts![thisSlot]!.Size;
                            }
                            method = f.M; lm = method.Lowered; slotT = lm.SlotTypes; bp = f.Bp;
                            numF = (byte*)(_num + bp); ip = f.Ip; contSp = f.ContSp;
                            _base = bp + lm.FrameSize;
                            if (wbSize > 0) Buffer.MemoryCopy(wbSrc, numF + f.VtThisWb * 4, wbSize, wbSize);
                            if (f.RetDst != -1)
                            {
                                var st = slotT[f.RetDst];
                                if (st == SType.I8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, v);
                                else if (st == SType.I4) *(int*)(numF + f.RetDst * 4) = unchecked((int)v);
                                else if (st == SType.R4) *(float*)(numF + f.RetDst * 4) = v;
                                else if (st == SType.R8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, (double)v);
                                else refStack[bp + f.RetDst] = v; // O-typed dst: box as long
                            }
                            break;
                        }
                        case Op.ret_r8:
                        {
                            int s=(int)irP[ip+1];
                            double v=RdR8(numF,refStack,slotT,s,bp);
                            if (_framesTop == frameBase) return CallReturn.FromR8(v);
                            var f = frames[--_framesTop];
                            byte* wbSrc = null; int wbSize = 0;
                            if (f.VtThisWb >= 0)
                            {
                                int thisSlot = lm.ArgSlot != null ? lm.ArgSlot[0] : 0;
                                wbSrc = numF + thisSlot * 4; wbSize = lm.StructLayouts![thisSlot]!.Size;
                            }
                            method = f.M; lm = method.Lowered; slotT = lm.SlotTypes; bp = f.Bp;
                            numF = (byte*)(_num + bp); ip = f.Ip; contSp = f.ContSp;
                            _base = bp + lm.FrameSize;
                            if (wbSize > 0) Buffer.MemoryCopy(wbSrc, numF + f.VtThisWb * 4, wbSize, wbSize);
                            if (f.RetDst != -1)
                            {
                                var st = slotT[f.RetDst];
                                if (st == SType.R8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, v);
                                else if (st == SType.R4) *(float*)(numF + f.RetDst * 4) = (float)v;
                                else if (st == SType.I4) *(int*)(numF + f.RetDst * 4) = (int)v;
                                else if (st == SType.I8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, (long)v);
                                else refStack[bp + f.RetDst] = v; // O-typed dst: box as double
                            }
                            break;
                        }
                        case Op.ret_vt:
                        {
                            int sV=(int)irP[ip+1];
                            var vlay = lm.StructLayouts![sV]!;
                            byte* retSrc = numF + sV * 4;
                            if (_framesTop == frameBase) return CallReturn.FromO(vlay.BoxFromPtr(retSrc));
                            var f = frames[--_framesTop];
                            byte* wbSrc = null; int wbSize = 0;
                            if (f.VtThisWb >= 0)
                            {
                                int thisSlot = lm.ArgSlot != null ? lm.ArgSlot[0] : 0;
                                wbSrc = numF + thisSlot * 4; wbSize = lm.StructLayouts![thisSlot]!.Size;
                            }
                            method = f.M; lm = method.Lowered; slotT = lm.SlotTypes; bp = f.Bp;
                            numF = (byte*)(_num + bp); ip = f.Ip; contSp = f.ContSp;
                            _base = bp + lm.FrameSize;
                            if (wbSize > 0) Buffer.MemoryCopy(wbSrc, numF + f.VtThisWb * 4, wbSize, wbSize);
                            if (f.RetDst != -1)
                            {
                                var st = slotT[f.RetDst];
                                // retSrc points into the (now-popped) callee frame region — still
                                // intact: nothing reuses it until the next call pushes past bp.
                                if (st == SType.Vt) Buffer.MemoryCopy(retSrc, numF + f.RetDst * 4, vlay.Size, vlay.Size);
                                else if (st == SType.O) refStack[bp + f.RetDst] = vlay.BoxFromPtr(retSrc);
                                else if (st == SType.I4) *(int*)(numF + f.RetDst * 4) = 0;
                                else *(float*)(numF + f.RetDst * 4) = 0f;
                            }
                            break;
                        }
                        case Op.throw_o:
                        {
                            // Script `throw`: unwinds exactly like a host-call throw — the shared
                            // exception path runs pending finally regions and maps the location.
                            object? ex = refStack[bp + (int)irP[ip + 1]];
                            throw ex as Exception
                                ?? new ScriptRuntimeException("throw of a null or non-exception object");
                        }
                        case Op.ret_o:
                        {
                            object? vo = refStack[bp + (int)irP[ip + 1]];
                            if (_framesTop == frameBase) return CallReturn.FromO(vo);
                            var f = frames[--_framesTop];
                            byte* wbSrc = null; int wbSize = 0;
                            if (f.VtThisWb >= 0)
                            {
                                int thisSlot = lm.ArgSlot != null ? lm.ArgSlot[0] : 0;
                                wbSrc = numF + thisSlot * 4; wbSize = lm.StructLayouts![thisSlot]!.Size;
                            }
                            method = f.M; lm = method.Lowered; slotT = lm.SlotTypes; bp = f.Bp;
                            numF = (byte*)(_num + bp); ip = f.Ip; contSp = f.ContSp;
                            _base = bp + lm.FrameSize;
                            if (wbSize > 0) Buffer.MemoryCopy(wbSrc, numF + f.VtThisWb * 4, wbSize, wbSize);
                            if (f.RetDst != -1)
                            {
                                // A numeric caller dst means the returned object is a boxed value
                                // (e.g. a host call's int/bool routed through an O-typed callee
                                // slot) — unbox it like WrObj; writing 0 would drop the value.
                                var st = slotT[f.RetDst];
                                if (st == SType.I4) *(int*)(numF + f.RetDst * 4) = CoerceBoxedI4(vo);
                                else if (st == SType.R4) *(float*)(numF + f.RetDst * 4) = CoerceBoxedR4(vo);
                                else if (st == SType.I8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, CoerceBoxedI8(vo));
                                else if (st == SType.R8) Unsafe.WriteUnaligned(numF + f.RetDst * 4, CoerceBoxedR8(vo));
                                else refStack[bp + f.RetDst] = vo;
                            }
                            break;
                        }

                        // --- Script-class fields (flat typed storage in ScriptObject) ---
                        case Op.ldfld_sc_i4: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; var so=(ScriptObject)refStack[bp+o]!; *(int*)(numF+dst*4)=Unsafe.ReadUnaligned<int>(ref so.PrimBytes[off]); ip+=4; break; }
                        case Op.ldfld_sc_r4: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; var so=(ScriptObject)refStack[bp+o]!; *(float*)(numF+dst*4)=Unsafe.ReadUnaligned<float>(ref so.PrimBytes[off]); ip+=4; break; }
                        case Op.ldfld_sc_o:  { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; var so=(ScriptObject)refStack[bp+o]!; refStack[bp+dst]=so.RefSlots[off]; ip+=4; break; }
                        case Op.stfld_sc_i4: { int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3]; var so=(ScriptObject)refStack[bp+o]!; Unsafe.WriteUnaligned(ref so.PrimBytes[off], RdI4(numF,refStack,slotT,s,bp)); ip+=4; break; }
                        case Op.stfld_sc_r4: { int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3]; var so=(ScriptObject)refStack[bp+o]!; Unsafe.WriteUnaligned(ref so.PrimBytes[off], RdR4(numF,refStack,slotT,s,bp)); ip+=4; break; }
                        case Op.stfld_sc_o:  { int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3]; var so=(ScriptObject)refStack[bp+o]!;
                            // A described throw instead of a bare IndexOutOfRangeException: an OOB
                            // ref-slot index here means a lowering emitted a PrimBytes byte offset
                            // where a RefSlots index belongs (unit-mixing bug), and the naive store
                            // could silently corrupt a neighboring field instead of landing OOB.
                            if ((uint)off >= (uint)so.RefSlots.Length) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"stfld_sc_o: ref-slot index {off} out of range for '{so.Type.Name}' ({so.RefSlots.Length} ref slots) — offset-unit bug in '{method.Name}'{At(method, il)}"); }
                            so.RefSlots[off]=RdObj(numF,refStack,slotT,s,bp); ip+=4; break; }
                        // Vt (flat host struct) field inline in ScriptObject.PrimBytes. Vt slots are
                        // addressed frame-relative (numF+slot*4; numF is already at this frame's base) —
                        // adding bp would double-apply the base and silently corrupt any nested call.
                        case Op.ldfld_sc_vt:
                        {
                            int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3];
                            var so=(ScriptObject)refStack[bp+o]!; int size=lm.StructLayouts![dst]!.Size;
                            byte* d=numF+dst*4; int c=0;
                            while (c+4<=size) { *(int*)(d+c)=Unsafe.ReadUnaligned<int>(ref so.PrimBytes[off+c]); c+=4; }
                            while (c<size) { d[c]=so.PrimBytes[off+c]; c++; }
                            ip+=4; break;
                        }
                        case Op.stfld_sc_vt:
                        {
                            int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3];
                            var so=(ScriptObject)refStack[bp+o]!; int size=lm.StructLayouts![s]!.Size;
                            byte* src=numF+s*4; int c=0;
                            while (c+4<=size) { Unsafe.WriteUnaligned(ref so.PrimBytes[off+c], *(int*)(src+c)); c+=4; }
                            while (c<size) { so.PrimBytes[off+c]=src[c]; c++; }
                            ip+=4; break;
                        }

                        // --- Host fields (reflection via token; script fields use ldfld_sc_*) ---
                        case Op.ldfld_o: case Op.ldfld_i4: case Op.ldfld_r4: case Op.ldfld_struct: case Op.ldflda:
                        {
                            int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int tokIdx=(int)irP[ip+3];
                            int tok=lm.Tokens[tokIdx]; var obj=refStack[bp+o];
                            object? val;
                            if (asm.HostFields.TryGetValue(tok, out var hf))
                            {
                                // Instance field on a null target: reflection would say "Non-static
                                // field requires a target" with no script location. Say what and where.
                                if (obj == null) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"NullReferenceException: reading field {hf.Name ?? "?"} on a null object{At(method, il)}"); }
                                val=hf.Get(obj);
                            }
                            else { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"Unknown field token 0x{tok:X8}{At(method, il)}"); }
                            if (irop==Op.ldfld_i4) *(int*)(numF+dst*4)=CoerceBoxedI4(val);
                            else if (irop==Op.ldfld_r4) *(float*)(numF+dst*4)=CoerceBoxedR4(val);
                            else refStack[bp+dst]=val;
                            ip+=4; break;
                        }
                        case Op.stfld_o: case Op.stfld_i4: case Op.stfld_r4: case Op.stfld_struct:
                        {
                            int o=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2]; int s=(int)irP[ip+3];
                            int tok=lm.Tokens[tokIdx]; var obj=refStack[bp+o];
                            object? val=RdObj(numF,refStack,slotT,s,bp);
                            if (asm.HostFields.TryGetValue(tok, out var hf))
                            {
                                if (obj == null) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"NullReferenceException: writing field {hf.Name ?? "?"} on a null object{At(method, il)}"); }
                                hf.Set(obj, val);
                            }
                            else { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"Unknown field token 0x{tok:X8}{At(method, il)}"); }
                            ip+=4; break;
                        }
                        case Op.ldsfld_struct:
                        {
                            // Host struct static field: unbox Get()'s boxed struct straight into
                            // the flat Vt dst so downstream flat consumers read real components.
                            int dst=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2];
                            int tok=lm.Tokens[tokIdx];
                            if (asm.HostFields.TryGetValue(tok, out var shf)) UnboxVt(numF, dst, lm.StructLayouts![dst]!, shf.Get(null));
                            else { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"Unknown static field token 0x{tok:X8}{At(method, il)}"); }
                            ip+=3; break;
                        }
                        case Op.ldsfld_o: case Op.ldsfld_i4: case Op.ldsfld_r4:
                        {
                            int dst=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2];
                            int tok=lm.Tokens[tokIdx];
                            if (asm.HostFields.TryGetValue(tok, out var shf)) refStack[bp+dst]=shf.Get(null);
                            // FieldDef (0x04) = a static field of a SCRIPT type — boxed lazy store.
                            // Reads before any write yield null/zero (see ParsedAssembly.ScriptStatics).
                            // Primitive script statics are lowered to typed dst slots: unbox into
                            // the numeric frame so slot-typed consumers (brtrue_i4, fast arith)
                            // see the value, not a reference.
                            else if ((tok >> 24) == 0x04)
                            {
                                // Primitive statics read from the UNBOXED store (miss -> 0). ldsfld_o
                                // (reference/long/double) reads the boxed store. See ScriptStaticsNum.
                                if (irop == Op.ldsfld_i4)
                                {
                                    asm.ScriptStaticsNum.TryGetValue(tok, out var nv);
                                    *(int*)(numF + dst * 4) = (int)nv;
                                }
                                else if (irop == Op.ldsfld_r4)
                                {
                                    asm.ScriptStaticsNum.TryGetValue(tok, out var nv);
                                    *(float*)(numF + dst * 4) = BitConverter.Int32BitsToSingle((int)nv);
                                }
                                else
                                {
                                    asm.ScriptStatics.TryGetValue(tok, out var sv);
                                    refStack[bp+dst]=sv;
                                }
                            }
                            else { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"Unknown static field token 0x{tok:X8}{At(method, il)}"); }
                            ip+=3; break;
                        }
                        case Op.stsfld_o: case Op.stsfld_i4: case Op.stsfld_r4: case Op.stsfld_struct:
                        {
                            int tokIdx=(int)irP[ip+1]; int s=(int)irP[ip+2];
                            int tok=lm.Tokens[tokIdx];
                            // Primitive SCRIPT statics store UNBOXED (raw bits) — no per-write heap box.
                            // The lowerer only emits stsfld_i4/r4 for script FieldDefs (never host fields).
                            if (irop == Op.stsfld_i4)
                            { asm.ScriptStaticsNum[tok] = RdI4(numF,refStack,slotT,s,bp); ip+=3; break; }
                            if (irop == Op.stsfld_r4)
                            { asm.ScriptStaticsNum[tok] = BitConverter.SingleToInt32Bits(RdR4(numF,refStack,slotT,s,bp)); ip+=3; break; }
                            object? val=RdObj(numF,refStack,slotT,s,bp);
                            if (asm.HostFields.TryGetValue(tok, out var shf)) shf.Set(null, val);
                            else if ((tok >> 24) == 0x04) asm.ScriptStatics[tok]=val;
                            else { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"Unknown static field token 0x{tok:X8}{At(method, il)}"); }
                            ip+=3; break;
                        }

                        // --- Arrays (object?[] by default; typed backings via newarr_i4/r4/vt) ---
                        case Op.newarr:
                        {
                            int dst=(int)irP[ip+1]; int lenSlot=(int)irP[ip+2];
                            int elemTokIdx=(int)irP[ip+3]; int fieldTokIdx=(int)irP[ip+4];
                            // The length slot can be classified O (boxed int in refStack), not I4-flat,
                            // so read via RdI4 (honours the slot type). A raw *(int*)(numF+…) read
                            // returns poison when the value is actually boxed.
                            int len=RdI4(numF,refStack,slotT,lenSlot,bp);
                            // Typed primitive backing (bool[]/char[]/byte[]/…): a real runtime array
                            // so element reads box with the correct type and defaults are C#-zeroed.
                            var primType = lm.PrimArrayElemTypeByTokIdx?[elemTokIdx];
                            if (primType != null)
                            {
                                var parr = System.Array.CreateInstance(primType, len);
                                if (fieldTokIdx != -1)
                                {
                                    int es = primType.IsEnum ? 4
                                        : primType == typeof(long) || primType == typeof(ulong) || primType == typeof(double) ? 8
                                        : primType == typeof(bool) || primType == typeof(byte) || primType == typeof(sbyte) ? 1 : 2;
                                    TryFillTypedArrayFromFieldBlob(parr, es, asm, lm.Tokens[fieldTokIdx]);
                                }
                                refStack[bp+dst]=parr; ip+=5; break;
                            }
                            var arr=new object?[len];
                            if (fieldTokIdx != -1) TryFillArrayFromFieldBlob(arr, asm, lm.Tokens[elemTokIdx], lm.Tokens[fieldTokIdx]);
                            // A script-struct element type is a value type: `new T[n]` holds n zeroed
                            // structs, not nulls, so per-element field mutation (arr[i].x = …) works.
                            else if (asm.TypeDefToType.TryGetValue(lm.Tokens[elemTokIdx], out var ed) && ed.IsScriptStructValue)
                                for (int i=0;i<len;i++) arr[i]=ScriptObject.Create(ed);
                            refStack[bp+dst]=arr; ip+=5; break;
                        }
                        // Typed backing for statically int/float element types: element reads/writes
                        // below take the non-boxing direct paths.
                        case Op.newarr_i4:
                        {
                            int dst=(int)irP[ip+1]; int lenSlot=(int)irP[ip+2]; int fieldTokIdx=(int)irP[ip+4];
                            int len=RdI4(numF,refStack,slotT,lenSlot,bp); // O-typed length slots: see Op.newarr
                            var a=new int[len];
                            if (fieldTokIdx != -1) TryFillI4ArrayFromFieldBlob(a, asm, lm.Tokens[fieldTokIdx]);
                            refStack[bp+dst]=a; ip+=5; break;
                        }
                        case Op.newarr_r4:
                        {
                            int dst=(int)irP[ip+1]; int lenSlot=(int)irP[ip+2]; int fieldTokIdx=(int)irP[ip+4];
                            int len=RdI4(numF,refStack,slotT,lenSlot,bp); var a=new float[len]; // O-typed len: see Op.newarr
                            if (fieldTokIdx != -1) TryFillR4ArrayFromFieldBlob(a, asm, lm.Tokens[fieldTokIdx]);
                            refStack[bp+dst]=a; ip+=5; break;
                        }
                        case Op.newarr_vt:
                        {
                            // Flat-struct element array: single byte[] backing, zero-init = C#
                            // array semantics, no per-element allocation.
                            int dst=(int)irP[ip+1]; int lenSlot=(int)irP[ip+2]; int elemTokIdx2=(int)irP[ip+3];
                            int len=RdI4(numF,refStack,slotT,lenSlot,bp); // O-typed len slots: see Op.newarr
                            var elay=lm.LayoutByTokIdx![elemTokIdx2]!;
                            int stride=(elay.Size + 3) & ~3;
                            // len*stride in 32-bit int overflows for large len (e.g. len=0x20000000,
                            // stride=8 wraps to 0): the backing byte[] would be undersized while
                            // Length reports the pre-overflow len — the (uint)idx<Length bounds check
                            // then passes and ldelem_vt/stelem_vt read/write off the end of the heap.
                            // Reject like the CLR does for `new T[n]` past the array-size limit.
                            long byteLen=(long)len*stride;
                            if (len<0 || byteLen>int.MaxValue)
                            { int il2=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array dimensions exceeded the supported range (length {len}){At(method, il2)}"); }
                            refStack[bp+dst]=new ScriptVtArray
                                { Layout=elay, Bytes=new byte[(int)byteLen], Length=len, Stride=stride };
                            ip+=5; break;
                        }
                        case Op.ldelem_vt:
                        {
                            int dst=(int)irP[ip+1]; int arrSlot=(int)irP[ip+2]; int idxSlot=(int)irP[ip+3];
                            int idx=*(int*)(numF+idxSlot*4);
                            var arrObj=refStack[bp+arrSlot];
                            var dlay=lm.StructLayouts![dst]!;
                            if (arrObj is ScriptVtArray sva)
                            {
                                if ((uint)idx >= (uint)sva.Length)
                                { int il2=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array index out of bounds{At(method, il2)}"); }
                                fixed (byte* srcB = sva.Bytes)
                                    Buffer.MemoryCopy(srcB + idx * sva.Stride, numF + dst * 4, dlay.Size, dlay.Size);
                            }
                            else
                            {
                                // Boxed fallback (object?[] of ScriptObjects, or a host T[]).
                                object? ev;
                                try { ev = arrObj is object?[] oa ? oa[idx] : ((System.Array)arrObj!).GetValue(idx); }
                                catch (IndexOutOfRangeException) { int il2=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array index out of bounds{At(method, il2)}"); }
                                UnboxVt(numF, dst, dlay, ev);
                            }
                            ip+=4; break;
                        }
                        case Op.stelem_vt:
                        {
                            int arrSlot=(int)irP[ip+1]; int idxSlot=(int)irP[ip+2]; int srcS=(int)irP[ip+3];
                            int idx=*(int*)(numF+idxSlot*4);
                            var arrObj=refStack[bp+arrSlot];
                            var slay=lm.StructLayouts![srcS]!;
                            if (arrObj is ScriptVtArray sva)
                            {
                                if ((uint)idx >= (uint)sva.Length)
                                { int il2=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array index out of bounds{At(method, il2)}"); }
                                fixed (byte* dstB = sva.Bytes)
                                    Buffer.MemoryCopy(numF + srcS * 4, dstB + idx * sva.Stride, slay.Size, slay.Size);
                            }
                            else
                            {
                                var v = BoxVt(numF, srcS, slay);
                                try { if (arrObj is object?[] oa) oa[idx]=v; else ((System.Array)arrObj!).SetValue(v, idx); }
                                catch (IndexOutOfRangeException) { int il2=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array index out of bounds{At(method, il2)}"); }
                            }
                            ip+=4; break;
                        }
                        // Script-created arrays are object?[] (int[]/float[] for the typed newarr variants);
                        // host members return real typed arrays. int[]/float[] hit the direct branches;
                        // reference-type arrays satisfy `is object?[]` by covariance; everything else falls
                        // back to System.Array (GetValue boxes, SetValue unboxes).
                        case Op.ldlen:
                        {
                            int dst=(int)irP[ip+1]; int s=(int)irP[ip+2];
                            var aObj=refStack[bp+s];
                            *(int*)(numF+dst*4) = aObj is ScriptVtArray svaL ? svaL.Length : ((System.Array)aObj!).Length;
                            ip+=3; break;
                        }
                        case Op.arr_bounds_check:
                        {
                            int s=(int)irP[ip+1]; int idxSlot=(int)irP[ip+2];
                            var aObj=refStack[bp+s];
                            int len = aObj is ScriptVtArray sva ? sva.Length : (aObj as System.Array)?.Length ?? 0;
                            int idx = RdI4(numF,refStack,slotT,idxSlot,bp);
                            if (aObj == null)
                            { int il2=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"NullReferenceException: element address of a null array{At(method, il2)}"); }
                            if ((uint)idx >= (uint)len)
                            { int il2=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array index out of bounds{At(method, il2)}"); }
                            ip+=3; break;
                        }
                        case Op.ldelem_o: case Op.ldelem_i4: case Op.ldelem_r4: case Op.ldelem_struct:
                        {
                            int dst=(int)irP[ip+1]; int arrSlot=(int)irP[ip+2]; int idxSlot=(int)irP[ip+3];
                            int idx=*(int*)(numF+idxSlot*4);
                            var arrObj=refStack[bp+arrSlot];
                            var dt=slotT[dst];
                            if (arrObj is int[] ia && (uint)idx < (uint)ia.Length)
                            {
                                int ei=ia[idx];
                                if (dt==SType.I4) *(int*)(numF+dst*4)=ei;
                                else if (dt==SType.R4) *(float*)(numF+dst*4)=(float)ei;
                                else if (dt==SType.I8) Unsafe.WriteUnaligned(numF+dst*4, (long)ei);
                                else if (dt==SType.R8) Unsafe.WriteUnaligned(numF+dst*4, (double)ei);
                                // CLR array covariance: Shape[] pattern-matches int[]. An O dst must
                                // box the element as its REAL type or Format holes render the number.
                                else refStack[bp+dst]=arrObj.GetType().GetElementType() is { IsEnum: true } eet2
                                    ? Enum.ToObject(eet2, ei) : ei;
                            }
                            else if (arrObj is long[] la8 && (uint)idx < (uint)la8.Length)
                            {
                                // Covers ulong[] via covariance — same 8-byte cells; the O-dst box
                                // must respect the REAL element type or ulongs print negative.
                                long el=la8[idx];
                                if (dt==SType.I8) Unsafe.WriteUnaligned(numF+dst*4, el);
                                else if (dt==SType.I4) *(int*)(numF+dst*4)=unchecked((int)el);
                                else if (dt==SType.R4) *(float*)(numF+dst*4)=el;
                                else if (dt==SType.R8) Unsafe.WriteUnaligned(numF+dst*4, (double)el);
                                else refStack[bp+dst]=arrObj is ulong[] ? unchecked((ulong)el) : (object)el;
                            }
                            else if (arrObj is double[] da8 && (uint)idx < (uint)da8.Length)
                            {
                                double ed=da8[idx];
                                if (dt==SType.R8) Unsafe.WriteUnaligned(numF+dst*4, ed);
                                else if (dt==SType.R4) *(float*)(numF+dst*4)=(float)ed;
                                else if (dt==SType.I4) *(int*)(numF+dst*4)=(int)ed;
                                else if (dt==SType.I8) Unsafe.WriteUnaligned(numF+dst*4, (long)ed);
                                else refStack[bp+dst]=ed;
                            }
                            else if (arrObj is float[] fa && (uint)idx < (uint)fa.Length)
                            {
                                float ef=fa[idx];
                                if (dt==SType.R4) *(float*)(numF+dst*4)=ef;
                                else if (dt==SType.I4) *(int*)(numF+dst*4)=(int)ef;
                                else refStack[bp+dst]=ef;
                            }
                            // Sub-4-byte primitive arrays read into a numeric slot WITHOUT boxing (the
                            // fallback's Array.GetValue heap-allocs per element). Dispatch on the EXACT type,
                            // not `is`: (object)sbyte[] is byte[] is true, so a byte[] arm reads it unsigned.
                            else if (dt != SType.O
                                     && arrObj is object arr && arr.GetType() is Type et
                                     && (et == T_byteA || et == T_sbyteA || et == T_shortA
                                         || et == T_ushortA || et == T_boolA || et == T_charA)
                                     && (uint)idx < (uint)Unsafe.As<System.Array>(arr).Length)
                            {
                                int ei =
                                    et == T_byteA   ? (int)Unsafe.As<byte[]>(arr)[idx]   :
                                    et == T_sbyteA  ? (int)Unsafe.As<sbyte[]>(arr)[idx]  :
                                    et == T_shortA  ? (int)Unsafe.As<short[]>(arr)[idx]  :
                                    et == T_ushortA ? (int)Unsafe.As<ushort[]>(arr)[idx] :
                                    et == T_charA   ? (int)Unsafe.As<char[]>(arr)[idx]   :
                                    (Unsafe.As<bool[]>(arr)[idx] ? 1 : 0);
                                if (dt==SType.I4) *(int*)(numF+dst*4)=ei;
                                else if (dt==SType.R4) *(float*)(numF+dst*4)=ei;
                                else if (dt==SType.I8) Unsafe.WriteUnaligned(numF+dst*4, (long)ei);
                                else Unsafe.WriteUnaligned(numF+dst*4, (double)ei); // R8
                            }
                            else
                            {
                                object? ev;
                                try { ev = arrObj is object?[] oa ? oa[idx] : ((System.Array)arrObj!).GetValue(idx); }
                                catch (IndexOutOfRangeException) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array index out of bounds{At(method, il)}"); }
                                // Typed primitive arrays (bool[]/char[]/byte[]/…) box their element
                                // with the correct runtime type here; unbox every I4-mapped primitive.
                                // O dst keeps the correctly-typed box (constrained-callvirt receiver).
                                // (int) per arm: without it the switch's best-common-type over mixed
                                // signed/unsigned arms zero-extends sbyte/short (an sbyte -1 → 255).
                                if (dt==SType.I4) *(int*)(numF+dst*4)= ev switch
                                {
                                    int ei => ei, bool eb => eb?1:0, char ec => (int)ec,
                                    byte by => (int)by, sbyte sb => (int)sb, short sh => (int)sh, ushort us => (int)us,
                                    uint eu => unchecked((int)eu),
                                    Enum => unchecked((int)Convert.ToInt64(ev)), // host enum[] elements
                                    _ => 0,
                                };
                                else if (dt==SType.R4) *(float*)(numF+dst*4)=CoerceBoxedR4(ev);
                                else if (dt==SType.I8) Unsafe.WriteUnaligned(numF+dst*4, CoerceBoxedI8(ev));
                                else if (dt==SType.R8) Unsafe.WriteUnaligned(numF+dst*4, CoerceBoxedR8(ev));
                                else refStack[bp+dst]=ev;
                            }
                            ip+=4; break;
                        }
                        case Op.stelem_o: case Op.stelem_i4: case Op.stelem_r4: case Op.stelem_struct:
                        {
                            int arrSlot=(int)irP[ip+1]; int idxSlot=(int)irP[ip+2]; int s=(int)irP[ip+3];
                            int idx=*(int*)(numF+idxSlot*4);
                            var arrObj=refStack[bp+arrSlot];
                            var st2=slotT[s];
                            // Dispatch on the exact array Type via cached-Type compares + Unsafe.As (no
                            // per-write isinst). Signed/unsigned pairs share layout, so one branch each.
                            // Fast writes guard (uint)idx<Length; out-of-range falls to the boxed fallback.
                            if (st2 != SType.Vt && arrObj is object arrW)
                            {
                                var wt = arrW.GetType();
                                if (wt == T_intA || wt == T_uintA)
                                {
                                    if ((uint)idx < (uint)Unsafe.As<System.Array>(arrW).Length)
                                        { Unsafe.As<int[]>(arrW)[idx]=RdI4(numF,refStack,slotT,s,bp); ip+=4; break; }
                                }
                                else if (wt == T_floatA)
                                {
                                    if ((uint)idx < (uint)Unsafe.As<System.Array>(arrW).Length)
                                        { Unsafe.As<float[]>(arrW)[idx]=RdR4(numF,refStack,slotT,s,bp); ip+=4; break; }
                                }
                                else if (wt == T_longA || wt == T_ulongA)
                                {
                                    if ((uint)idx < (uint)Unsafe.As<System.Array>(arrW).Length)
                                        { Unsafe.As<long[]>(arrW)[idx]=RdI8(numF,refStack,slotT,s,bp); ip+=4; break; }
                                }
                                else if (wt == T_doubleA)
                                {
                                    if ((uint)idx < (uint)Unsafe.As<System.Array>(arrW).Length)
                                        { Unsafe.As<double[]>(arrW)[idx]=RdR8(numF,refStack,slotT,s,bp); ip+=4; break; }
                                }
                                else if ((wt == T_boolA || wt == T_charA || wt == T_byteA || wt == T_sbyteA
                                          || wt == T_shortA || wt == T_ushortA)
                                         && (uint)idx < (uint)Unsafe.As<System.Array>(arrW).Length)
                                {
                                    int val = RdI4(numF,refStack,slotT,s,bp);
                                    if      (wt == T_boolA)  Unsafe.As<bool[]>(arrW)[idx]  = val != 0;
                                    else if (wt == T_charA)  Unsafe.As<char[]>(arrW)[idx]  = (char)val;
                                    else if (wt == T_byteA)  Unsafe.As<byte[]>(arrW)[idx]  = (byte)val;
                                    else if (wt == T_sbyteA) Unsafe.As<sbyte[]>(arrW)[idx] = (sbyte)val;
                                    else if (wt == T_shortA) Unsafe.As<short[]>(arrW)[idx] = (short)val;
                                    else                     Unsafe.As<ushort[]>(arrW)[idx]= (ushort)val;
                                    ip+=4; break;
                                }
                            }
                            // Fallback: object?[] element, a struct/enum-typed source, or an index the fast
                            // paths above declined (out of range) — box + SetValue, which keeps its own bounds
                            // check and throws a clean error.
                            {
                                // A Vt source slot lives in the numeric frame, not refStack; RdObj returns null
                                // for it, silently storing a default element. Box via the struct marshaller so
                                // both object?[] and real typed arrays receive the actual struct value.
                                var v = st2==SType.Vt ? BoxVt(numF, s, lm.StructLayouts![s]!) : RdObj(numF,refStack,slotT,s,bp);
                                // Enum arrays are real typed arrays; the source rides an I4 slot as a
                                // boxed int, which SetValue refuses — rebox as the element type.
                                if (arrObj?.GetType().GetElementType() is { IsEnum: true } eet && v is not Enum)
                                    v = Enum.ToObject(eet, CoerceBoxedI4(v));
                                try { if (arrObj is object?[] oa) oa[idx]=v; else ((System.Array)arrObj!).SetValue(v, idx); }
                                catch (IndexOutOfRangeException) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"array index out of bounds{At(method, il)}"); }
                            }
                            ip+=4; break;
                        }

                        // --- Script-to-script call (iterative: push caller frame, jump into callee) ---
                        case Op.call_script:
                        case Op.call_script_virt:
                        {
                            int dstSlot=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2]; int argc=(int)irP[ip+3];
                            var callee=lm.CalleeByTokIdx![tokIdx]!;
                            if (irop == Op.call_script_virt)
                            {
                                // Virtual dispatch: re-resolve from the RECEIVER's runtime type
                                // (arg 0 — an O-slot ScriptObject; script virtuals live on classes).
                                var recvObj = RdObj(numF, refStack, slotT, (int)irP[ip+4], bp);
                                // callvirt on a NULL receiver throws NRE before dispatch — the
                                // whole point of callvirt over call. Without this the null fell
                                // through to the static base method and ran it with a null `this`
                                // (found by fuzzing: `((C0)null).VTag()` returned the base value).
                                if (recvObj == null)
                                { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"NullReferenceException: virtual call '{callee.Name}' on a null receiver{At(method, il)}"); }
                                // Falls back to the static callee when no override is found — for
                                // an interface STUB there is nothing to fall back to (guard below).
                                if (recvObj is ScriptObject rvo && ResolveScriptVirtual(rvo.Type, callee) is { } ovr)
                                    callee = ovr;
                            }
                            if (callee.Lowered == null)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"no runnable implementation of '{callee.Name}' for this receiver{At(method, il)}"); }
                            var clm=callee.Lowered;
                            int calleeBp=bp+lm.FrameSize;
                            if (!FrameFits(calleeBp, clm) || _framesTop >= MaxCallDepth)
                                throw new ScriptRuntimeException("Script call stack overflow");
                            if (clm.RefClearLen > 0) Array.Clear(refStack, calleeBp, clm.RefClearLen);
                            var cst=clm.SlotTypes;
                            var cas=clm.ArgSlot; // arg index → callee frame slot (null = identity)
                            byte* cnum=(byte*)(_num+calleeBp);
                            int vtThisWb = -1;
                            for (int k=0;k<argc;k++)
                            {
                                int argSlot=(int)irP[ip+4+k];
                                int ck = cas != null && k < cas.Length ? cas[k] : k;
                                var ct = ck < cst.Length ? cst[ck] : SType.O;
                                if (ct==SType.I4) *(int*)(cnum+ck*4)=RdI4(numF,refStack,slotT,argSlot,bp);
                                else if (ct==SType.R4) *(float*)(cnum+ck*4)=RdR4(numF,refStack,slotT,argSlot,bp);
                                else if (ct==SType.I8) Unsafe.WriteUnaligned(cnum+ck*4, RdI8(numF,refStack,slotT,argSlot,bp));
                                else if (ct==SType.R8) Unsafe.WriteUnaligned(cnum+ck*4, RdR8(numF,refStack,slotT,argSlot,bp));
                                else if (ct==SType.Vt)
                                {
                                    var clay = clm.StructLayouts![ck]!;
                                    if (slotT[argSlot]==SType.Vt)
                                    {
                                        Buffer.MemoryCopy(numF + argSlot*4, cnum + ck*4, clay.Size, clay.Size);
                                        // Struct `this` (arg 0 of an instance callee) is byref in IL:
                                        // copy the callee's arg0 bytes back into the caller's slot on ret.
                                        if (k == 0 && !callee.IsStatic) vtThisWb = argSlot;
                                    }
                                    else
                                        UnboxVt(cnum, ck, clay, refStack[bp+argSlot]);
                                }
                                else refStack[calleeBp+ck]=RdObj(numF,refStack,slotT,argSlot,bp);
                            }
                            frames[_framesTop++] = new SavedFrame
                                { M = method, Ip = ip + 4 + argc, Bp = bp, RetDst = dstSlot, ContSp = contSp, VtThisWb = vtThisWb };
                            method=callee; lm=clm; slotT=cst; bp=calleeBp; numF=cnum; ip=clm.IrStart;
                            // Bump the reentrancy base past the callee frame so a reentrant Invoke from
                            // inside the callee lands above it, not on it. ret_* restores it; on
                            // exceptional unwind the Invoke*/InvokeTyped finally restores the outermost value.
                            _base=calleeBp+clm.FrameSize;
                            break;
                        }
                        // --- Delegate creation ---
                        // A delegate whose TYPE names a script type (e.g. Action<Node>) has no runtime
                        // System.Type (DelegateSite.DelegateType==null): build an internal ScriptDelegate
                        // {target, receiver} (fired by invoke_script_delegate) rather than a CLR delegate,
                        // since a CLR Invoke(Node) can't carry a ScriptObject. Everything else is a CLR
                        // delegate over the pre-resolved site (see ScriptDelegate / ScriptDelegateAdapter).
                        case Op.new_delegate:
                        {
                            int dstSlot=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2]; int recvSlot=(int)irP[ip+3];
                            var site=lm.DelegateSiteByTokIdx![tokIdx]!;
                            object? recv=refStack[bp+recvSlot]; // null for a static method group
                            refStack[bp+dstSlot] = site.DelegateType == null
                                ? _owner.CreateScriptDelegate(site, recv)
                                : _owner.GetOrCreateDelegate(site, recv);
                            ip+=4; break;
                        }

                        // --- Invoke a script-typed delegate: re-enter the VM on its target ---
                        // The receiver slot holds a ScriptDelegate; prepend its captured receiver (for a
                        // lambda's display class or an instance target) and re-enter via the same
                        // InvokeDelegateTarget plumbing the CLR adapter uses. Args are boxed by slot type
                        // (like call_host's slow path); the boxed result is unboxed into dst.
                        case Op.invoke_script_delegate:
                        {
                            int dstSlot=(int)irP[ip+1]; int recvSlot=(int)irP[ip+3]; int argc=(int)irP[ip+4];
                            var sd=refStack[bp+recvSlot] as ScriptDelegate;
                            if (sd==null)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"NullReferenceException: invoked a null delegate{At(method, il)}"); }
                            bool hasRecv = sd.Receiver != null;
                            int off = hasRecv ? 1 : 0;
                            var dargs = new object?[argc + off]; // fresh (adapter allocates too); consumed at Run entry
                            if (hasRecv) dargs[0] = sd.Receiver;
                            for (int k=0;k<argc;k++)
                                dargs[off+k] = RdArg(numF, refStack, slotT, (int)irP[ip+5+k], bp, lm.StructLayouts);
                            object? ret = _owner.InvokeDelegateTarget(sd.Target, dargs, sd.Generation);
                            if (dstSlot != -1)
                            {
                                if (slotT[dstSlot]==SType.Vt) UnboxVt(numF, dstSlot, lm.StructLayouts![dstSlot]!, ret);
                                else WrObj(numF, refStack, slotT, dstSlot, ret, bp);
                            }
                            ip += 5 + argc; break;
                        }

                        case Op.newobj_script:
                        {
                            int dstSlot=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2]; int argc=(int)irP[ip+3];
                            int tok=lm.Tokens[tokIdx];
                            asm.CtorToType.TryGetValue(tok, out var typeDesc);
                            var obj=ScriptObject.Create(typeDesc!);
                            int argBase=ip+4;
                            var ctorMethod=lm.CalleeByTokIdx?[tokIdx];
                            if (ctorMethod == null)
                            {
                                refStack[bp+dstSlot]=obj;
                                ip+=4+argc; break;
                            }
                            var clm=ctorMethod.Lowered;
                            int ctorBp=bp+lm.FrameSize;
                            if (!FrameFits(ctorBp, clm) || _framesTop >= MaxCallDepth)
                                throw new ScriptRuntimeException("Script call stack overflow");
                            if (clm.RefClearLen > 0) Array.Clear(refStack, ctorBp, clm.RefClearLen);
                            var cst=clm.SlotTypes;
                            var cas=clm.ArgSlot;
                            byte* cnum=(byte*)(_num+ctorBp);
                            refStack[ctorBp + (cas != null ? cas[0] : 0)]=obj; // 'this' (heap ctor: O slot)
                            for (int k=0;k<argc;k++)
                            {
                                int argSlot=(int)irP[argBase+k];
                                int ck = cas != null && k+1 < cas.Length ? cas[k+1] : k+1;
                                var ct = ck < cst.Length ? cst[ck] : SType.O;
                                if (ct==SType.I4) *(int*)(cnum+ck*4)=RdI4(numF,refStack,slotT,argSlot,bp);
                                else if (ct==SType.R4) *(float*)(cnum+ck*4)=RdR4(numF,refStack,slotT,argSlot,bp);
                                else if (ct==SType.I8) Unsafe.WriteUnaligned(cnum+ck*4, RdI8(numF,refStack,slotT,argSlot,bp));
                                else if (ct==SType.R8) Unsafe.WriteUnaligned(cnum+ck*4, RdR8(numF,refStack,slotT,argSlot,bp));
                                else if (ct==SType.Vt)
                                {
                                    var clay = clm.StructLayouts![ck]!;
                                    if (slotT[argSlot]==SType.Vt)
                                        Buffer.MemoryCopy(numF + argSlot*4, cnum + ck*4, clay.Size, clay.Size);
                                    else UnboxVt(cnum, ck, clay, refStack[bp+argSlot]);
                                }
                                else refStack[ctorBp+ck]=RdObj(numF,refStack,slotT,argSlot,bp);
                            }
                            // The dst slot is a fresh temp nothing reads until after the ctor
                            // completes, so store the (in-place mutated) object before jumping in.
                            refStack[bp+dstSlot]=obj;
                            frames[_framesTop++] = new SavedFrame
                                { M = method, Ip = ip + 4 + argc, Bp = bp, RetDst = -1, ContSp = contSp, VtThisWb = -1 };
                            method=ctorMethod; lm=clm; slotT=cst; bp=ctorBp; numF=cnum; ip=clm.IrStart;
                            _base=ctorBp+clm.FrameSize;
                            break;
                        }

                        case Op.box: { int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; refStack[bp+dst]=RdObj(numF,refStack,slotT,s,bp); ip+=3; break; }
                        case Op.box_prim:
                        {
                            int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; int tc=(int)irP[ip+3];
                            // Box the primitive as its TRUE type so ToString/is/GetType and reference
                            // identity are correct (bool -> boxed bool, not boxed int).
                            refStack[bp+dst] = tc switch
                            {
                                0 => (object)RdI4(numF,refStack,slotT,s,bp),
                                1 => RdI4(numF,refStack,slotT,s,bp) != 0,
                                2 => (char)RdI4(numF,refStack,slotT,s,bp),
                                3 => (byte)RdI4(numF,refStack,slotT,s,bp),
                                4 => (sbyte)RdI4(numF,refStack,slotT,s,bp),
                                5 => (short)RdI4(numF,refStack,slotT,s,bp),
                                6 => (ushort)RdI4(numF,refStack,slotT,s,bp),
                                8 => unchecked((uint)RdI4(numF,refStack,slotT,s,bp)),
                                9 => RdI8(numF,refStack,slotT,s,bp),
                                10 => unchecked((ulong)RdI8(numF,refStack,slotT,s,bp)),
                                11 => RdR8(numF,refStack,slotT,s,bp),
                                _ => RdR4(numF,refStack,slotT,s,bp),
                            };
                            ip+=4; break;
                        }
                        case Op.box_enum:
                        {
                            int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; int tokIdx=(int)irP[ip+3];
                            int ev=RdI4(numF,refStack,slotT,s,bp);
                            refStack[bp+dst] = asm.TokenTypes.TryGetValue(lm.Tokens[tokIdx], out var et)
                                ? Enum.ToObject(et, ev) : (object)ev;
                            ip+=4; break;
                        }
                        case Op.initobj:
                        {
                            int dst=(int)irP[ip+1]; var lay=lm.StructLayouts![dst]; int size=lay!=null?lay.Size:4;
                            byte* d=numF+dst*4; for (int i=0;i<size;i++) d[i]=0; ip+=2; break;
                        }
                        case Op.initobj_script:
                        {
                            int dst=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2];
                            asm.TypeDefToType.TryGetValue(lm.Tokens[tokIdx], out var d2);
                            refStack[bp+dst]=ScriptObject.Create(d2!);
                            ip+=3; break;
                        }
                        case Op.ensure_script:
                        {
                            int dst=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2];
                            if (refStack[bp+dst]==null)
                            {
                                int etok=lm.Tokens[tokIdx];
                                if (asm.TypeDefToType.TryGetValue(etok, out var d2))
                                    refStack[bp+dst]=ScriptObject.Create(d2);
                                else
                                    // Boxed host struct local with no initobj (definite assignment by
                                    // field stores): the type token's synthetic 0-arg ctor makes the box.
                                    refStack[bp+dst]=asm.HostCtors[etok].Binding.Invoke(null, Array.Empty<object?>());
                            }
                            ip+=3; break;
                        }
                        case Op.unbox_any: case Op.castclass:
                        {
                            int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; int tokIdx=(int)irP[ip+3];
                            int tok=lm.Tokens[tokIdx]; var val=refStack[bp+s];
                            // Script TypeDef target (`(C0)(object)o`): descriptor identity OR a
                            // script BASE of the value's type (derived→base and base-typed
                            // downcasts both walk the BaseDesc chain — script classes gained
                            // inheritance; the old exact-identity check rejected `(C1)c` on a C2).
                            if (val != null && irop==Op.castclass && asm.TypeDefToType.TryGetValue(tok, out var scd))
                            {
                                if (!(val is ScriptObject vso && IsScriptSubtypeOf(vso.Type, scd)))
                                { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"InvalidCastException{At(method, il)}: cannot cast to script type {scd.Name}"); }
                            }
                            else if (val != null && asm.TokenTypes.TryGetValue(tok, out var tt))
                            {
                                if (irop==Op.castclass && !tt.IsInstanceOfType(val))
                                { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"InvalidCastException{At(method, il)}: cannot cast {val.GetType().Name} to {tt.Name}"); }
                                // unbox.any is type-EXACT for value types: a boxed char can't unbox to
                                // int (C# throws InvalidCastException). Match that — the lenient RdI4
                                // read would otherwise silently convert. (Nullable<U> accepts a boxed U.)
                                if (irop==Op.unbox_any && tt.IsValueType)
                                {
                                    var underlying = Nullable.GetUnderlyingType(tt) ?? tt;
                                    if (val.GetType() != underlying)
                                    { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"InvalidCastException{At(method, il)}: cannot unbox {val.GetType().Name} to {tt.Name}"); }
                                }
                            }
                            // unbox.any of NULL into a non-nullable value type throws NRE (.NET).
                            // A non-O dst (I4/I8/R4/R8/Vt) is exactly a non-nullable value target —
                            // a Nullable<T> or reference target lowers to an O dst and accepts null.
                            // Without this, WrObj wrote 0 and the throw was silently swallowed
                            // (found by fuzzing: `(long)(object)((long?)null)`).
                            if (irop == Op.unbox_any && val == null && slotT[dst] != SType.O)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"NullReferenceException: unboxing a null reference to a value type{At(method, il)}"); }
                            // Route through WrObj: an I4/R4 dst (unbox.any of a primitive) gets the
                            // unboxed value in the numeric frame; an O dst keeps the reference.
                            WrObj(numF, refStack, slotT, dst, val, bp); ip+=4; break;
                        }
                        case Op.isinst:
                        {
                            int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; int tokIdx=(int)irP[ip+3];
                            int tok=lm.Tokens[tokIdx]; var val=refStack[bp+s];
                            if (val==null) refStack[bp+dst]=null;
                            // Script TypeDef target (`o is C0`): descriptor identity or a script
                            // BASE of the value's type — covers script classes (incl. inheritance)
                            // AND boxed script structs (found by fuzzing: threw "unallowed type"
                            // for `(object)o is C0`; exact identity later broke `c is C1` on a C2).
                            else if (asm.TypeDefToType.TryGetValue(tok, out var sisd)) refStack[bp+dst]=val is ScriptObject vso2 && IsScriptSubtypeOf(vso2.Type, sisd) ? val : null;
                            else if (asm.TokenTypes.TryGetValue(tok, out var tt)) refStack[bp+dst]=tt.IsInstanceOfType(val)?val:null;
                            else { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"isinst for unallowed type{At(method, il)}"); }
                            ip+=4; break;
                        }
                        case Op.ldtoken:
                        {
                            int dst=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2]; int tok=lm.Tokens[tokIdx];
                            if (!asm.TokenTypes.TryGetValue(tok, out var tt)) { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"ldtoken for unallowed type{At(method, il)}"); }
                            refStack[bp+dst]=tt; ip+=3; break;
                        }
                        case Op.switch_i4:
                        {
                            // Read the subject via RdI4 so an O-typed slot (a boxed int, e.g. a slow host
                            // call returning an enum) is honoured; a raw numF read yields 0 for a value
                            // that lives boxed in refFrame.
                            int valSlot=(int)irP[ip+1]; int n=(int)irP[ip+2]; int val=RdI4(numF,refStack,slotT,valSlot,bp);
                            ip = (uint)val < (uint)n ? (int)irP[ip+4+val] : (int)irP[ip+3];
                            break;
                        }

                        // --- Flat-struct (Vt) ops ---
                        case Op.ldfld_vt_vt:
                        {
                            int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3];
                            int size=lm.StructLayouts![dst]!.Size;
                            Buffer.MemoryCopy(numF+o*4+off, numF+dst*4, size, size);
                            ip+=4; break;
                        }
                        case Op.stfld_vt_vt:
                        {
                            int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int src2=(int)irP[ip+3];
                            int size=lm.StructLayouts![src2]!.Size;
                            Buffer.MemoryCopy(numF+src2*4, numF+o*4+off, size, size);
                            ip+=4; break;
                        }
                        case Op.ldfld_vt_i4: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; *(int*)(numF+dst*4)=*(int*)(numF+o*4+off); ip+=4; break; }
                        case Op.ldfld_vt_r4: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; *(float*)(numF+dst*4)=*(float*)(numF+o*4+off); ip+=4; break; }
                        case Op.stfld_vt_i4: { int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3]; *(int*)(numF+o*4+off)=RdI4(numF,refStack,slotT,s,bp); ip+=4; break; }
                        case Op.stfld_vt_r4: { int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3]; *(float*)(numF+o*4+off)=RdR4(numF,refStack,slotT,s,bp); ip+=4; break; }
                        // Sub-4-byte flat fields (Color32.r-class): loads widen into the I4 dst
                        // cell, stores truncate. Byte pointers — no alignment assumptions.
                        case Op.ldfld_vt_u1: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; *(int*)(numF+dst*4)=*(numF+o*4+off); ip+=4; break; }
                        case Op.ldfld_vt_i1: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; *(int*)(numF+dst*4)=*(sbyte*)(numF+o*4+off); ip+=4; break; }
                        case Op.ldfld_vt_u2: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; byte* p=numF+o*4+off; *(int*)(numF+dst*4)=(int)(uint)(p[0]|(p[1]<<8)); ip+=4; break; }
                        case Op.ldfld_vt_i2: { int dst=(int)irP[ip+1]; int o=(int)irP[ip+2]; int off=(int)irP[ip+3]; byte* p=numF+o*4+off; *(int*)(numF+dst*4)=(short)(p[0]|(p[1]<<8)); ip+=4; break; }
                        case Op.stfld_vt_b1: { int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3]; *(numF+o*4+off)=(byte)RdI4(numF,refStack,slotT,s,bp); ip+=4; break; }
                        case Op.stfld_vt_b2: { int o=(int)irP[ip+1]; int off=(int)irP[ip+2]; int s=(int)irP[ip+3]; int v=RdI4(numF,refStack,slotT,s,bp); byte* p=numF+o*4+off; p[0]=(byte)v; p[1]=(byte)(v>>8); ip+=4; break; }
                        case Op.mov_vt:
                        {
                            int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; int size=lm.StructLayouts![dst]!.Size;
                            byte* d=numF+dst*4; byte* src=numF+s*4; int c=0;
                            while (c+4<=size) { *(int*)(d+c)=*(int*)(src+c); c+=4; }
                            while (c<size) { d[c]=src[c]; c++; }
                            ip+=3; break;
                        }
                        case Op.box_vt:   { int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; refStack[bp+dst]=BoxVt(numF, s, lm.StructLayouts![s]!); ip+=3; break; }
                        case Op.unbox_vt: { int dst=(int)irP[ip+1]; int s=(int)irP[ip+2]; UnboxVt(numF, dst, lm.StructLayouts![dst]!, refStack[bp+s]); ip+=3; break; }

                        // --- Host calls (non-boxing fast delegate path with boxed reflection fallback) ---
                        case Op.call_host:
                        {
                            int dstSlot=(int)irP[ip+1]; int recvSlot=(int)irP[ip+2]; int tokIdx=(int)irP[ip+3]; int argc=(int)irP[ip+4];
                            // Fast path: the pre-resolved delegate reads args straight from the native
                            // frame and writes the result flat, via the byte* ABI ((byte*)_num is the
                            // absolute frame base).
                            var fastArr = lm.HostFastByTokIdx;
                            var fast = fastArr != null ? fastArr[tokIdx] : null;
                            // A flat (Vt) receiver rides the fast path only when the entry declares
                            // FastVtRecv (reads the receiver bytes in place from the numeric frame).
                            // Other fast closures take the receiver as an object (RdObj on a Vt slot
                            // yields null), so drop to the slow path, which boxes and writes the
                            // mutated box back.
                            bool vtRecv = recvSlot != -1 && slotT[recvSlot] == SType.Vt;
                            if (fast != null && vtRecv
                                && (lm.HostFastVtRecvByTokIdx == null || !lm.HostFastVtRecvByTokIdx[tokIdx])) fast = null;
                            if (fast != null
                                && (lm.HostFastWideOkByTokIdx == null || !lm.HostFastWideOkByTokIdx[tokIdx]))
                            {
                                // Fast closures read args / write the dst through 32-bit frame views;
                                // a wide (I8/R8) slot would be accessed half-width (Math.Floor's R8
                                // arg read as R4 garbage). Drop to the boxed path — unless the entry
                                // declares FastWideOk (the double-native Math closures).
                                if (dstSlot != -1 && slotT[dstSlot] is SType.I8 or SType.R8) fast = null;
                                else
                                    for (int k = 0; k < argc; k++)
                                        if (slotT[(int)irP[ip + 5 + k]] is SType.I8 or SType.R8) { fast = null; break; }
                            }
                            if (fast != null)
                            {
                                object? frecv = null;
                                if (recvSlot != -1 && !vtRecv)
                                {
                                    frecv = RdObj(numF, refStack, slotT, recvSlot, bp);
                                    if (frecv == null)
                                    { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"NullReferenceException: called a host member on a null object{At(method, il)}"); }
                                }
                                HostBinding.FastFrameBase = bp;
                                // Exceptions escaping a host call surface as ScriptRuntimeException with the
                                // ORIGINAL exception preserved as InnerException, so dispatchers can propagate
                                // the user exception (no inner = the VM itself detected the failure). SREs
                                // already annotated with a source location (reentrant script dispatch) pass
                                // through untouched.
                                try { fast(frecv, (byte*)_num, refStack, slotT, ir, ip + 5, dstSlot, bp); }
                                catch (ScriptRuntimeException sre) when (!sre.Message.Contains(" at line ") && !sre.Message.Contains(" at IL+"))
                                { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException(sre.Message + At(method, il), sre.InnerException); }
                                catch (ScriptRuntimeException) { throw; }
                                catch (System.Reflection.TargetInvocationException tie)
                                { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; var inner=tie.InnerException ?? tie; throw new ScriptRuntimeException($"{inner.GetType().Name}: {inner.Message}{At(method, il)}", inner); }
                                catch (Exception ex)
                                { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"{ex.GetType().Name}: {ex.Message}{At(method, il)}", ex); }
                                ip += 5 + argc; break;
                            }
                            int tok=lm.Tokens[tokIdx];
                            var hostEntry=asm.HostCalls[tok];
                            object? recv = recvSlot == -1 ? null
                                : hostEntry.ReceiverBox != ReceiverBoxKind.None && slotT[recvSlot] == SType.I4
                                    ? (hostEntry.ReceiverBox == ReceiverBoxKind.Char // box as char/bool, not int
                                        ? (char)RdI4(numF, refStack, slotT, recvSlot, bp)
                                        : RdI4(numF, refStack, slotT, recvSlot, bp) != 0)
                                    : slotT[recvSlot]==SType.Vt && hostEntry.ReceiverStruct != null
                                        ? BoxVt(numF, recvSlot, hostEntry.ReceiverStruct)
                                        : RdObj(numF, refStack, slotT, recvSlot, bp);
                            var hArgs = ArgBuf(argc);
                            for (int k=0;k<argc;k++)
                            {
                                var av = RdArg(numF, refStack, slotT, (int)irP[ip+5+k], bp, lm.StructLayouts);
                                // A script enumerator crossing into a host call must arrive as a
                                // host IEnumerator — the StartCoroutine(Run()) pattern inside a
                                // reloaded body. Raw ScriptObject is useless to the host anyway.
                                if (av is ScriptObject aso && asm.EnumeratorTypes.TryGetValue(aso.Type, out var aem))
                                    av = new ScriptEnumerator(_owner, aso, aem);
                                // uint/ulong parameters: the slot boxed as int/long; give the shim the
                                // declared unsigned type so `Math.Max(uint.MaxValue, 0u)` sees 4294967295, not -1.
                                if (hostEntry.UnsignedParams is { } up && k < up.Length && up[k] != 0)
                                    av = up[k] == 4 ? (object)unchecked((uint)CoerceBoxedI4(av)) : unchecked((ulong)CoerceBoxedI8(av));
                                hArgs[k] = av;
                            }
                            object? ret;
                            // Same contract as the fast path above: user exceptions from the host call are
                            // wrapped with the original as InnerException; VM-detected failures carry none.
                            try { ret = hostEntry.Binding.Invoke(recv, hArgs); }
                            catch (ScriptRuntimeException sre) when (!sre.Message.Contains(" at line ") && !sre.Message.Contains(" at IL+"))
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException(sre.Message + At(method, il), sre.InnerException); }
                            catch (ScriptRuntimeException) { throw; }
                            catch (System.Reflection.TargetException)
                            {
                                // TargetException = null receiver OR a receiver the resolved
                                // MethodInfo can't be invoked on (a short-name collision resolved
                                // the member on the wrong type). Report which — the "null object"
                                // wording masked a UnityEngine.Object/System.Object collision.
                                int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0;
                                string nm=asm.TokenNames.TryGetValue(tok, out var tn)?tn:"a host member";
                                throw new ScriptRuntimeException(recv == null
                                    ? $"NullReferenceException: called {nm} on a null object{At(method, il)}"
                                    : $"ArgumentException: {nm} resolved to a host method that cannot run on a receiver of type {recv.GetType().Name} — likely a short-type-name collision in the binding{At(method, il)}");
                            }
                            catch (System.Reflection.TargetInvocationException tie)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; var inner=tie.InnerException ?? tie; throw new ScriptRuntimeException($"{inner.GetType().Name}: {inner.Message}{At(method, il)}", inner); }
                            catch (InvalidCastException) when (recv is ScriptObject)
                            {
                                // A script-declared object reached a host shim that casts the
                                // receiver — e.g. foreach over a script IEnumerable<T> helper
                                // (GetEnumerator on the state machine). Name the limitation.
                                int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0;
                                string nm=asm.TokenNames.TryGetValue(tok, out var tn2)?tn2:"a host member";
                                throw new ScriptRuntimeException(
                                    $"called {nm} on a script-declared object — script types implementing " +
                                    "host interfaces (e.g. IEnumerable<T> iterator helpers) are not supported; " +
                                    $"coroutines returning IEnumerator are{At(method, il)}");
                            }
                            catch (Exception ex)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"{ex.GetType().Name}: {ex.Message}{At(method, il)}", ex); }
                            // A mutating instance member on a flat (Vt) receiver ran against a boxed
                            // copy — write the box back so property setters and mutating methods
                            // update the local.
                            if (recvSlot != -1 && slotT[recvSlot] == SType.Vt && hostEntry.ReceiverStruct != null && recv != null)
                                UnboxVt(numF, recvSlot, hostEntry.ReceiverStruct, recv);
                            if (dstSlot != -1)
                            {
                                // The lowerer may type the result slot I4/R4, so route the boxed return
                                // through WrObj — a direct refStack store would leave a numeric dst reading 0.
                                if (slotT[dstSlot]==SType.Vt) UnboxVt(numF, dstSlot, lm.StructLayouts![dstSlot]!, ret);
                                else WrObj(numF, refStack, slotT, dstSlot, ret, bp);
                            }
                            ip += 5 + argc; break;
                        }
                        case Op.call_host_byref:
                        {
                            int dstSlot=(int)irP[ip+1]; int recvSlot=(int)irP[ip+2]; int tokIdx=(int)irP[ip+3]; int argc=(int)irP[ip+4];
                            int tok=lm.Tokens[tokIdx];
                            var hostEntry=asm.HostCalls[tok];
                            object? recv = recvSlot == -1 ? null
                                : hostEntry.ReceiverBox != ReceiverBoxKind.None && slotT[recvSlot] == SType.I4
                                    ? (hostEntry.ReceiverBox == ReceiverBoxKind.Char // box as char/bool, not int
                                        ? (char)RdI4(numF, refStack, slotT, recvSlot, bp)
                                        : RdI4(numF, refStack, slotT, recvSlot, bp) != 0)
                                    : slotT[recvSlot]==SType.Vt && hostEntry.ReceiverStruct != null
                                        ? BoxVt(numF, recvSlot, hostEntry.ReceiverStruct)
                                        : RdObj(numF, refStack, slotT, recvSlot, bp);
                            // Exact-length buffer (ArgBuf, like the slow call_host path): the
                            // grow-only shared buffer kept its LARGEST length, and a later byref
                            // call with fewer params handed MethodInfo.Invoke an oversized array —
                            // TargetParameterCountException on the second invocation of any method
                            // whose small-arity byref call follows a larger one (found by fuzzing).
                            var hArgs = ArgBuf(argc);
                            for (int k=0;k<argc;k++) hArgs[k]=RdArg(numF, refStack, slotT, (int)irP[ip+5+k], bp, lm.StructLayouts);
                            object? ret;
                            // Same contract as call_host: user exceptions from the host call are wrapped
                            // with the original as InnerException; VM-detected failures carry none.
                            try { ret = hostEntry.Binding.Invoke(recv, hArgs); }
                            catch (ScriptRuntimeException sre) when (!sre.Message.Contains(" at line ") && !sre.Message.Contains(" at IL+"))
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException(sre.Message + At(method, il), sre.InnerException); }
                            catch (ScriptRuntimeException) { throw; }
                            catch (System.Reflection.TargetException)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; string nm=asm.TokenNames.TryGetValue(tok, out var tn)?tn:"a host member"; throw new ScriptRuntimeException($"NullReferenceException: called {nm} on a null object{At(method, il)}"); }
                            catch (System.Reflection.TargetInvocationException tie)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; var inner=tie.InnerException ?? tie; throw new ScriptRuntimeException($"{inner.GetType().Name}: {inner.Message}{At(method, il)}", inner); }
                            catch (Exception ex)
                            { int il=(uint)(ip-lm.IrStart)<(uint)lm.IrToIlOffset.Length?lm.IrToIlOffset[ip-lm.IrStart]:0; throw new ScriptRuntimeException($"{ex.GetType().Name}: {ex.Message}{At(method, il)}", ex); }
                            // Mirror call_host: a mutating instance member on a FLAT receiver ran
                            // against a boxed copy — write the box back into the Vt slot.
                            if (recvSlot != -1 && slotT[recvSlot] == SType.Vt && hostEntry.ReceiverStruct != null && recv != null)
                                UnboxVt(numF, recvSlot, hostEntry.ReceiverStruct, recv);
                            int wbBase = ip + 5 + argc;
                            int wbCount = (int)irP[wbBase];
                            for (int k=0;k<wbCount;k++)
                            {
                                int argIdx=(int)irP[wbBase+1+k*4+0]; int kind=(int)irP[wbBase+1+k*4+1];
                                int t1=(int)irP[wbBase+1+k*4+2]; int t2=(int)irP[wbBase+1+k*4+3];
                                object? v=hArgs[argIdx];
                                if (kind == 0)
                                {
                                    // A flat-struct (Vt) local reads from the numeric frame, so flatten
                                    // the written-back box into its bytes — WrObj's O path would strand
                                    // it in refStack (an out Vector3 silently staying (0,0,0)).
                                    if (slotT[t1]==SType.Vt && lm.StructLayouts?[t1] is { } wbLay)
                                        UnboxVt(numF, t1, wbLay, v);
                                    else WrObj(numF, refStack, slotT, t1, v, bp);
                                }
                                else if (kind == 1)
                                {
                                    int ftok=lm.Tokens[t2]; var obj=refStack[bp+t1];
                                    if (asm.FieldSlots.TryGetValue(ftok, out var fs))
                                    {
                                        var so=(ScriptObject)obj!; int fi=fs.Item2; SType fst=fs.Item1.FieldTypes[fi]; int foff=fs.Item1.FieldOffsets[fi];
                                        if (fst==SType.I4) Unsafe.WriteUnaligned(ref so.PrimBytes[foff], CoerceBoxedI4(v));
                                        else if (fst==SType.R4) Unsafe.WriteUnaligned(ref so.PrimBytes[foff], CoerceBoxedR4(v));
                                        else if (fst==SType.Vt && fs.Item1.VtFieldLayouts?[fi] is { } wbVtLay)
                                        {
                                            // Flat (Vt) field: flatten the returned box into PrimBytes at the
                                            // field's byte offset — the O path below would use that byte
                                            // offset as a RefSlots index (OOB throw or silent corruption).
                                            if (v == null) { for (int z=0; z<wbVtLay.Size; z++) so.PrimBytes[foff+z]=0; }
                                            else fixed (byte* pbWb = &so.PrimBytes[foff]) wbVtLay.CopyToPtr(pbWb, v);
                                        }
                                        else so.RefSlots[foff]=v;
                                    }
                                    else if (asm.HostFields.TryGetValue(ftok, out var hf)) hf.Set(obj, v);
                                }
                                else { int idx=*(int*)(numF+t2*4); var ao=refStack[bp+t1]; if (ao is object?[] oaw) oaw[idx]=v; else ((System.Array)ao!).SetValue(v, idx); }
                            }
                            if (dstSlot != -1) WrObj(numF, refStack, slotT, dstSlot, ret, bp);
                            for (int k=0;k<argc;k++) hArgs[k]=null; // drop refs; ArgBuf reuses per-size
                            ip = wbBase + 1 + wbCount * 4; break;
                        }
                        case Op.newobj_host:
                        {
                            int dstSlot=(int)irP[ip+1]; int tokIdx=(int)irP[ip+2]; int argc=(int)irP[ip+3];
                            int tok=lm.Tokens[tokIdx];
                            var hostCtor=asm.HostCtors[tok];
                            bool dstIsVt = dstSlot != -1 && slotT[dstSlot]==SType.Vt;
                            if (hostCtor.Binding.Fast != null)
                            {
                                HostBinding.FastFrameBase = bp;
                                hostCtor.Binding.Fast(null, (byte*)_num, refStack, slotT, ir, ip + 4, dstSlot, bp);
                                ip += 4 + argc; break;
                            }
                            var ctorArgs=ArgBuf(argc);
                            for (int k=0;k<argc;k++) ctorArgs[k]=RdArg(numF, refStack, slotT, (int)irP[ip+4+k], bp, lm.StructLayouts);
                            var ret=hostCtor.Binding.Invoke(null, ctorArgs);
                            if (dstIsVt) UnboxVt(numF, dstSlot, lm.StructLayouts![dstSlot]!, ret);
                            else if (dstSlot != -1) refStack[bp+dstSlot]=ret;
                            ip += 4 + argc; break;
                        }

                        default:
                            throw new NotSupportedException(
                                $"Vm engine: op {irop} not yet implemented (host calls land in M3): {irop}");
                    }
                }
            }

            // Unreachable: every method region ends in a ret (BuildIrBlob appends a
            // ret_void sentinel), and ret at this Run's frame floor returns above.
        }

        // Frame-relative slot readers (numF is already at the frame base).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int RdI4(byte* numF, object?[] r, SType[] t, int s, int bp)
        {
            var tt = t[s];
            if (tt == SType.I4) return *(int*)(numF + s * 4);
            if (tt == SType.R4) return (int)*(float*)(numF + s * 4);
            if (tt == SType.I8) return unchecked((int)Unsafe.ReadUnaligned<long>(numF + s * 4));
            if (tt == SType.R8) return (int)Unsafe.ReadUnaligned<double>(numF + s * 4);
            // Vt slots live in the numeric frame; their refFrame entry is never cleared
            // (RefClearLen covers only O slots) so it must not be read.
            if (tt == SType.Vt) return 0;
            // Unbox any boxed primitive the VM maps to I4 (box_prim can now produce boxed
            // char/byte/short/…, not just int/bool).
            var v = r[bp + s];
            return v switch
            {
                int ri => ri, bool rb => rb ? 1 : 0, char rc => rc,
                byte rby => rby, sbyte rsb => rsb, short rsh => rsh, ushort rus => rus,
                uint ru => unchecked((int)ru),
                // A host caller (delegate adapter, Invoke) can hand an enum arg boxed as its real
                // type into an O slot; reading it as I4 must see the underlying value, like the
                // enum host-field fix did for ldfld.
                Enum => unchecked((int)Convert.ToInt64(v)),
                _ => 0,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float RdR4(byte* numF, object?[] r, SType[] t, int s, int bp)
        {
            var tt = t[s];
            if (tt == SType.R4) return *(float*)(numF + s * 4);
            if (tt == SType.I4) return (float)*(int*)(numF + s * 4);
            if (tt == SType.I8) return Unsafe.ReadUnaligned<long>(numF + s * 4);
            if (tt == SType.R8) return (float)Unsafe.ReadUnaligned<double>(numF + s * 4);
            if (tt == SType.Vt) return 0f;
            return CoerceBoxedR4(r[bp + s]);
        }

        // Read a slot as object? (boxes I4/R4). Used at the remaining boxing boundaries:
        // host fields, array elements, O-typed stores.
        // Host-call ARGUMENT read: like RdObj, but a flat (Vt) argument boxes through its slot
        // layout. RdObj alone yields null for Vt slots, and MethodBase.Invoke silently turns a
        // null arg into default(T) — zeroed struct fields (the bounds_autobind divergence).
        static object? RdArg(byte* numF, object?[] r, SType[] t, int s, int bp,
            HostBinding.StructLayout?[]? layouts)
        {
            if (t[s] == SType.Vt && layouts?[s] is { } lay)
                return BoxVt(numF, s, lay);
            return RdObj(numF, r, t, s, bp);
        }

        // True when `t` IS `target` or derives from it through the script base-class chain.
        static bool IsScriptSubtypeOf(ScriptTypeDescriptor? t, ScriptTypeDescriptor target)
        {
            for (var d = t; d != null; d = d.BaseDesc)
                if (ReferenceEquals(d, target)) return true;
            return false;
        }

        // Walk the receiver type's descriptor chain for an override of `baseM` (matched by
        // name + signature, standard override rules: most-derived wins). Null = no override — the
        // static callee is already correct. Values in VirtualsBySig are ParsedMethods (stored as
        // object: ScriptObject.cs doesn't reference the interpreter's nested types).
        internal static ParsedMethod? ResolveScriptVirtual(ScriptTypeDescriptor? desc, ParsedMethod baseM)
        {
            string key = baseM.VirtKey;
            for (var d = desc; d != null; d = d.BaseDesc)
                if (d.VirtualsBySig != null && d.VirtualsBySig.TryGetValue(key, out var m))
                    return (ParsedMethod)m;
            return null;
        }

        static object? RdObj(byte* numF, object?[] r, SType[] t, int s, int bp)
        {
            var tt = t[s];
            if (tt == SType.I4) return *(int*)(numF + s * 4);
            if (tt == SType.R4) return *(float*)(numF + s * 4);
            // I8 boxes as long — like the I4 slot's int default, a ulong value loses its
            // unsigned identity here; the explicit box path (box_prim tc 10) keeps it.
            if (tt == SType.I8) return Unsafe.ReadUnaligned<long>(numF + s * 4);
            if (tt == SType.R8) return Unsafe.ReadUnaligned<double>(numF + s * 4);
            if (tt == SType.Vt) return null;
            return r[bp + s];
        }

        static long RdI8(byte* numF, object?[] r, SType[] t, int s, int bp)
        {
            var tt = t[s];
            if (tt == SType.I8) return Unsafe.ReadUnaligned<long>(numF + s * 4);
            if (tt == SType.I4) return *(int*)(numF + s * 4);
            if (tt == SType.R4) return (long)*(float*)(numF + s * 4);
            if (tt == SType.R8) return (long)Unsafe.ReadUnaligned<double>(numF + s * 4);
            if (tt == SType.Vt) return 0L;
            return CoerceBoxedI8(r[bp + s]);
        }

        static double RdR8(byte* numF, object?[] r, SType[] t, int s, int bp)
        {
            var tt = t[s];
            if (tt == SType.R8) return Unsafe.ReadUnaligned<double>(numF + s * 4);
            if (tt == SType.R4) return *(float*)(numF + s * 4);
            if (tt == SType.I4) return *(int*)(numF + s * 4);
            if (tt == SType.I8) return Unsafe.ReadUnaligned<long>(numF + s * 4);
            if (tt == SType.Vt) return 0d;
            return CoerceBoxedR8(r[bp + s]);
        }

        // Write an object? into a slot, routing to the correct frame (unboxes I4/R4).
        static void WrObj(byte* numF, object?[] r, SType[] t, int s, object? v, int bp)
        {
            var tt = t[s];
            // An I4 slot can receive any type the interpreter maps to I4 (Int32/Boolean/Char and the
            // sub-int integrals). A host method returning char (String.get_Chars) or byte/short lands
            // here boxed; recognizing only int/bool wrote 0 and dropped the value. (found by fuzzing.)
            if (tt == SType.I4) *(int*)(numF + s * 4) = v switch
            {
                int vi => vi,
                bool bv => bv ? 1 : 0,
                char vc => vc,
                byte vb => vb,
                sbyte vsb => vsb,
                short vsh => vsh,
                ushort vus => vus,
                uint vu => unchecked((int)vu),
                // A byref out-param written back boxed as its real enum (Enum.TryParse<T>) — the
                // int/char family above dropped it to 0. Truncate like `(int)someEnum`.
                Enum => unchecked((int)Convert.ToInt64(v)),
                _ => 0,
            };
            else if (tt == SType.R4) *(float*)(numF + s * 4) = CoerceBoxedR4(v);
            else if (tt == SType.I8) Unsafe.WriteUnaligned(numF + s * 4, CoerceBoxedI8(v));
            else if (tt == SType.R8) Unsafe.WriteUnaligned(numF + s * 4, CoerceBoxedR8(v));
            else r[bp + s] = v;
        }

        // Vt box/unbox via the registered pointer marshallers — straight off the unmanaged frame;
        // the box allocation in BoxVt is the boundary's only alloc.

        static object BoxVt(byte* numF, int slot, HostBinding.StructLayout lay)
            => lay.BoxFromPtr(numF + slot * 4);

        static void UnboxVt(byte* numF, int slot, HostBinding.StructLayout lay, object? boxed)
        {
            byte* dst = numF + slot * 4;
            if (boxed == null) { for (int i = 0; i < lay.Size; i++) dst[i] = 0; return; }
            lay.CopyToPtr(dst, boxed);
        }
    }
}
}
