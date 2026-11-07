#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
// The package bundles System.Reflection.Metadata renamed with a "UnityPipeline." prefix (Runtime/Plugins/CodeAnalysis);
// the dotnet test projects reference the same DLLs (IlInterpreter/BundledRoslyn.props).
using UnityPipeline.System.Reflection.Metadata;
using UnityPipeline.System.Reflection.Metadata.Ecma335;
using UnityPipeline.System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace IlInterpreter.Interpreter
{

#if UNITY_6000_5_OR_NEWER
[NoAutoStaticsCleanup]
#endif
sealed partial class ScriptInterpreter : IDisposable
{
    public const int DefaultStepLimit = 1_000_000;

    // Per-invocation IL-opcode budget. Set higher for benchmarks or long-running
    // workloads; the default catches accidental infinite loops in dev.
    public int StepLimit = DefaultStepLimit;

    readonly HostBinding?    _binding;
    readonly Action<string>  _logSink;
    readonly IFrameAllocator _allocator;
    Vm?                      _vm;        // lazily created on first Invoke
    ParsedAssembly?          _parsed;
    // The loaded assembly's parse state, for test/fuzz tooling that inspects script storage
    // directly (see IlInterpreter.Fuzz's StaticSnapshot). Null before Load.
    internal ParsedAssembly? Parsed => _parsed;
    ScriptObject?            _instance; // the live "Script" instance (null for static-only scripts)

    // Per-size thread-local buffers for host-call arguments (sizes 0-8).
    // Delegates registered via AllowType use MethodInfo.Invoke which requires an exact-length array.
    [ThreadStatic]
    static object?[][]? _argBufs;

    static object?[] ArgBuf(int count)
    {
        _argBufs ??= new object?[9][];
        if (count < _argBufs.Length) return _argBufs[count] ??= new object?[count];
        return new object?[count]; // rare: >8 args, just allocate
    }

    public ScriptInterpreter(HostBinding? binding = null, Action<string>? log = null,
                             IFrameAllocator? allocator = null)
    {
        _binding   = binding;
        _logSink   = log ?? (_ => { });
        _allocator = allocator ?? MarshalFrameAllocator.Instance;
    }

    // Parse the script IL; disposes any previously loaded assembly first (method reload).
    // Throws ScriptValidationException if any method can't be lowered — failing at Load,
    // where the user can act on it, rather than at Invoke time.
    // lenient=true keeps loading when individual methods can't be lowered (each becomes a skipped
    // stub queryable via TryGetLoweringSkip) instead of throwing for the whole assembly — see
    // IrLowerer.LowerAll. Method reload passes true so one bad method doesn't drop a whole file's reload.
    public void Load(IScript script, bool lenient = false)
    {
        _parsed?.PdbProvider?.Dispose();
        _parsed?.Pe.Dispose();
        // Parse reads attacker-reachable PE/metadata directly (System.Reflection.Metadata). Convert
        // a malformed-image fault into a clean load failure rather than letting a raw
        // BadImageFormatException/ArgumentException escape Load. ScriptExceptions (our own validation,
        // e.g. a cyclic base chain) pass through unchanged.
        try { _parsed = Parse(script.Il.ToArray(), _binding, _logSink); }
        catch (ScriptException) { throw; }
        catch (Exception ex) { throw new ScriptValidationException($"Failed to parse script assembly: {ex.Message}"); }
        var failures = IrLowerer.LowerAll(_parsed, lenient);
        if (failures.Count > 0)
        {
            var sb = new System.Text.StringBuilder("Script could not be lowered:");
            foreach (var f in failures) sb.Append('\n').Append("  ").Append(f.Name).Append(": ").Append(f.Reason);
            _parsed.PdbProvider?.Dispose();
            _parsed.Pe.Dispose();
            _parsed = null;
            throw new ScriptValidationException(sb.ToString());
        }
        IrLowerer.BuildIrBlob(_parsed);
        // Wire ScriptObject.ToString dispatch: a script ToString override must run wherever .NET
        // formats the box (string.Concat/Format included), and only the loaded interpreter can
        // execute it. Parse is static, so the instance hook lands here.
        // The invoker belongs to THIS parse: a host that kept a script object across a reload and
        // formats it later must not run the old ParsedMethod against the new IR blob (its offsets
        // would execute unrelated code). Stale objects get the default (type name) instead.
        var generation = _parsed;
        foreach (var d in _parsed.TypesByName.Values)
            d.ScriptToStringInvoker = (so, m) =>
                ReferenceEquals(_parsed, generation)
                    ? (_vm ??= new Vm(this)).Invoke((ParsedMethod)m, new object?[] { so })?.ToString() ?? ""
                    : so.Type.Name;
        _instance = MakeInstance(_parsed);
        // Run script static constructors eagerly (CLR runs them lazily; eager keeps the VM free of
        // per-access init checks). The load-bearing case is the compiler-generated `<>c..cctor`
        // that creates the singleton behind non-capturing lambdas — without it, `ldsfld <>9`
        // reads null and a delegate over an instance method loses its receiver. A user static
        // initializer that throws fails the Load, like everything else it validates.
        foreach (var m in _parsed.ByToken.Values)
            if (m.Name == ".cctor" && m.Lowered != null)
            {
                try { (_vm ??= new Vm(this)).Invoke(m, Array.Empty<object?>()); }
                catch (ScriptException ex)
                {
                    // Name the type whose static initializer faulted — a bare "array index out of
                    // bounds at IL+0x…" gives no clue which of an assembly's many .cctors blew up.
                    var typeName = _parsed.TypeDefToType.TryGetValue(m.DeclaringTypeDef, out var td)
                        ? td.Name : $"typedef 0x{m.DeclaringTypeDef:X8}";
                    throw new ScriptRuntimeException(
                        $"static initializer of type '{typeName}' failed at load: {ex.Message}", ex);
                }
            }
    }

    // Release the loaded assembly. Invoke() will throw until Load() is called again.
    public void Unload()
    {
        _parsed?.PdbProvider?.Dispose();
        _parsed?.Pe.Dispose();
        _parsed   = null;
        _instance = null;
    }

    public void Dispose()
    {
        Unload();
        _vm?.Dispose();
        _vm = null;
    }

    // True if the loaded script defines a method with this (simple) name. Lets callers
    // probe for an optional entry point (e.g. a method-reload override) without catching the
    // "method not found" exception that Invoke throws.
    public bool HasMethod(string name) => _parsed != null && _parsed.ByName.ContainsKey(name);

    // Host member refs of the loaded script that resolved to throwing stubs (or missing host
    // fields) — the script runs, but reaching one of these sites throws ScriptRuntimeException.
    // Callers surface these as load-time warnings instead of exploding mid-play on first call.
    public IReadOnlyList<string> UnboundHostMembers =>
        (IReadOnlyList<string>?)_parsed?.UnboundHostMembers ?? Array.Empty<string>();

    // A method that exists but was skipped at load — Lowered==null with a reason — because it
    // couldn't be lowered under lenient loading (see Load). Its call sites throw the reason if
    // reached, so the method-reload executor skips registering just that override instead of the file.
    public bool TryGetLoweringSkip(string methodName, out string reason)
    {
        if (_parsed != null && _parsed.ByName.TryGetValue(methodName, out var m)
            && m.Lowered == null && m.LoweringSkipReason != null)
        { reason = m.LoweringSkipReason; return true; }
        reason = null!;
        return false;
    }

    // Resolve a host-invocable method by simple name, rejecting stubs that were skipped at
    // load (cold enumerator members whose lowering failed).
    ParsedMethod ResolveInvocable(string methodName)
    {
        if (_parsed == null) throw new ScriptRuntimeException("No script loaded — call Load() first");
        if (!_parsed.ByName.TryGetValue(methodName, out var method))
            throw new ScriptRuntimeException($"Method '{methodName}' not found");
        if (method.Lowered == null)
            throw new ScriptRuntimeException(
                $"Method '{methodName}' was skipped at load: {method.LoweringSkipReason}");
        return method;
    }

    // Invoke a named method.
    // For instance methods: args[0] = script instance, args[1..] = extraArgs.
    // For static methods:   args[0..] = extraArgs.
    public object? Invoke(string methodName, params object?[] extraArgs)
    {
        var method = ResolveInvocable(methodName);

        // Hot no-arg path (OnUpdate/Run): the Vm places the receiver and runs with no object?[]
        // allocation. Only the primitive return is boxed (at the API boundary).
        if (extraArgs.Length == 0)
            return WrapForHost((_vm ??= new Vm(this)).InvokeTyped(method));

        var args = new object?[method.ArgCount];
        int slot = 0;
        if (!method.IsStatic && _instance != null) args[slot++] = _instance;
        for (int i = 0; i < extraArgs.Length && slot < method.ArgCount; i++)
            args[slot++] = extraArgs[i];

        return WrapForHost((_vm ??= new Vm(this)).Invoke(method, args));
    }

    // Typed 1-arg overload: bypasses object?[] allocation for primitive args.
    // RyuJIT specializes per value-type T1; typeof checks fold to constants in
    // each specialization, so no boxing occurs for int/float/bool callers.
    // Void-returning methods (the common Unity Update pattern) allocate zero bytes.
    // Numeric-returning methods re-box the result once (24 B); ref-returning methods
    // have no boxing on either side.
    public object? Invoke<T1>(string methodName, T1 a1)
    {
        var method = ResolveInvocable(methodName);
        return WrapForHost((_vm ??= new Vm(this)).InvokeTyped(method, a1));
    }

    public object? Invoke<T1, T2>(string methodName, T1 a1, T2 a2)
    {
        var method = ResolveInvocable(methodName);
        return WrapForHost((_vm ??= new Vm(this)).InvokeTyped(method, a1, a2));
    }

    public object? Invoke<T1, T2, T3>(string methodName, T1 a1, T2 a2, T3 a3)
    {
        var method = ResolveInvocable(methodName);
        return WrapForHost((_vm ??= new Vm(this)).InvokeTyped(method, a1, a2, a3));
    }

    // A script enumerator (iterator state machine) crossing to the host gets wrapped in a
    // bridge the host can drive (Unity's StartCoroutine, foreach, LINQ over IEnumerator).
    // Anything else passes through. ScriptObject is internal, so an unwrapped state machine
    // would be useless to the caller anyway.
    internal object? WrapForHost(object? result)
    {
        return result is ScriptObject so && _parsed != null &&
               _parsed.EnumeratorTypes.TryGetValue(so.Type, out var em)
            ? new ScriptEnumerator(this, so, em)
            : result;
    }

    // Re-enter the VM to run one member of a script enumerator with an explicit receiver
    // (the state machine ScriptObject). Used by the ScriptEnumerator bridge; safe to call
    // while another Invoke is live (the Vm's _base reentrancy handling covers it).
    internal object? InvokeEnumeratorMember(ParsedMethod member, ScriptObject receiver)
    {
        if (_parsed == null) throw new ScriptRuntimeException("No script loaded — call Load() first");
        if (member.Lowered == null)
            throw new ScriptRuntimeException(
                $"Method '{member.Name}' was skipped at load: {member.LoweringSkipReason}");
        return WrapForHost((_vm ??= new Vm(this)).InvokeReceiver(member, receiver));
    }

    // Re-enter the VM from a delegate created over an interpreted method (button click, event
    // callback). args includes the receiver at [0] for instance targets. Same reentrancy story
    // as InvokeEnumeratorMember. The generation check makes staleness a CLEAN error: the target's
    // IR offsets are blob-absolute into the assembly it was lowered with, so a delegate surviving
    // a re-Load would execute arbitrary words of the NEW blob — silently wrong, not crashing.
    internal object? InvokeDelegateTarget(ParsedMethod target, object?[] args, object generation)
    {
        if (_parsed == null) throw new ScriptRuntimeException("No script loaded — call Load() first");
        if (!ReferenceEquals(_parsed, generation))
            throw new ScriptRuntimeException(
                $"Delegate over '{target.Name}' was created by a previous script load and is no " +
                "longer valid — re-run the code that created it after a reload");
        return WrapForHost((_vm ??= new Vm(this)).Invoke(target, args));
    }

    // Per-receiver delegate cache: `b.clicked += Click; b.clicked -= Click` must observe delegate
    // equality, and script-adapter delegates only compare equal as the SAME instance.
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<DelegateSite, Delegate>>
        _delegatesByReceiver = new();

    internal Delegate GetOrCreateDelegate(DelegateSite site, object? receiver)
    {
        if (receiver == null)
            return site.CachedStatic ??= CreateDelegateCore(site, null);
        var perReceiver = _delegatesByReceiver.GetOrCreateValue(receiver);
        if (!perReceiver.TryGetValue(site, out var d))
            perReceiver[site] = d = CreateDelegateCore(site, receiver);
        return d;
    }

    // Build the internal value for a delegate whose type names a script type (see ScriptDelegate).
    // Unlike GetOrCreateDelegate there is no CLR Delegate and no per-receiver caching: the value is a
    // plain reference stored in an O slot, and invoke_script_delegate re-enters the VM on it.
    // The site's DelegateType is null here (that is what routed the executor to this path), so the
    // diagnostics name the delegate generically.
    internal ScriptDelegate CreateScriptDelegate(DelegateSite site, object? receiver)
        => new ScriptDelegate(ResolveScriptDelegateTarget(site, receiver, "delegate"), receiver, _parsed!);

    Delegate CreateDelegateCore(DelegateSite site, object? receiver)
    {
        if (site.HostMethod is { } hm)
        {
            if (hm.IsStatic)
                return Delegate.CreateDelegate(site.DelegateType, hm);
            if (receiver == null)
                throw new ScriptRuntimeException(
                    $"Cannot create a {site.DelegateType.Name} over instance method '{hm.Name}' with a null receiver");
            return Delegate.CreateDelegate(site.DelegateType, receiver, hm);
        }
        var target = ResolveScriptDelegateTarget(site, receiver, site.DelegateType.Name);
        return ScriptDelegateAdapter.Create(this, site.DelegateType, target, receiver);
    }

    // The script method a delegate over `site` runs for `receiver`. Shared by the CLR-adapter path
    // and the script-typed ScriptDelegate path so both agree on the creation-time semantics:
    // CLR throws at delegate CREATION over an instance method with a null receiver, and an
    // ldvirtftn group resolves the receiver's override once, at creation.
    static ParsedMethod ResolveScriptDelegateTarget(DelegateSite site, object? receiver, string delegateName)
    {
        var target = site.ScriptMethod!;
        if (!target.IsStatic && receiver == null)
            throw new ScriptRuntimeException(
                $"NullReferenceException: cannot create a {delegateName} over instance method '{target.Name}' with a null receiver");
        if (site.VirtualResolve && receiver is ScriptObject rso
            && Vm.ResolveScriptVirtual(rso.Type, target) is { } ovr)
            target = ovr;
        if (target.Lowered == null)
            throw new ScriptRuntimeException(
                $"Cannot create a {delegateName} over '{target.Name}': no runnable implementation");
        return target;
    }

    public object? Execute(IScript script, string methodName = "Run")
    {
        Load(script);
        return Invoke(methodName);
    }

    internal sealed class ParsedMethod
    {
        // Name/IlBytes/ArgSTypes are always set by Parse before the instance escapes;
        // `= null!` marks that invariant (same idiom as Lowered below).
        public string  Name = null!;
        public int     Token;
        public int     ArgCount;
        public int     LocalCount;
        public byte[]  IlBytes = null!;
        public bool    IsStatic;
        // Declared virtual (incl. overrides). A `callvirt` on such a method lowers to
        // call_script_virt, which re-resolves the target from the receiver's runtime type via
        // ScriptTypeDescriptor.VirtualsBySig; plain `call` (base.M()) stays statically bound.
        public bool    IsVirtual;
        // Dispatch identity for VirtualsBySig: name + the full method signature blob, so
        // overloads (`F(int)` / `F(string)`) get distinct slots. An override and the virtual it
        // overrides share the key because their signatures are byte-identical within one assembly.
        public string  VirtKey = "";
        // Bodyless signature stub for an interface member: only a callvirt dispatch anchor —
        // never lowered (no IL), never invocable directly; the executor requires the receiver
        // walk to produce a real implementation.
        public bool    IsAbstractStub;
        public bool    IsVoid;
        // True when the declared return type is bool (which stores as I4). The method-return boxing
        // uses this to hand back a boxed bool rather than a boxed int, so a host caller casting the
        // Invoke result to bool doesn't fault.
        public bool    ReturnIsBool;
        // Return and parameter types for IR slot classification (set during Parse)
        public SType ReturnSType;
        public SType[] ArgSTypes = null!; // length = explicit param count (no `this` slot)
        // Per-local struct layout. Non-null when the local is a registered host
        // value type and should be allocated as a Vt slot in numFrame.
        public HostBinding.StructLayout?[]? LocalStructLayouts;
        // Per-local DECLARED slot type, from the LocalVarSig — the authoritative, stable backing:
        // an IL local is single-typed, so its declared type (not last-wins store inference) says
        // whether its value lives unboxed in the numeric frame (I4/R4) or boxed in refStack (O).
        // Only the unambiguous, VM-consistent cases are captured (bool/char/sub-int/int -> I4,
        // float -> R4, string/object/class/array/generic -> O); null entries (valuetype/enum, long,
        // double, nint, byref, …) fall back to the inference pass. Non-null entries are FROZEN: the
        // lowering neither infers nor retypes them, so a fast op is never emitted over a slot whose
        // value is actually boxed. Null array when the sig gives nothing to freeze.
        public SType?[]? LocalSTypes;
        // Per-local flag: true when the local is a SCRIPT-defined struct (ELEMENT_TYPE_VALUETYPE
        // referencing a TypeDef in this assembly). Such locals are O ScriptObject references, so a
        // value load (ldloc) must clone to preserve struct copy semantics. Null when none are.
        // The flat-resolution post-pass CLEARS entries whose struct turned out blittable — those
        // locals get a Vt layout in LocalStructLayouts instead.
        public bool[]? LocalIsScriptStruct;
        // Per-local TypeDef token when the local is a script-defined struct (0 otherwise). Lets the
        // post-pass resolve the local to a descriptor once all types are built. Null when none.
        public int[]? LocalScriptStructTypeDefs;
        // Per-explicit-param / return TypeDef token for script-struct types (0 = not one).
        public int[]? ArgScriptStructTypeDefs;
        public int    ReturnScriptStructTypeDef;
        // Per-explicit-param flat layout (parallel to ArgSTypes; entry non-null ⇔ ArgSTypes[i]==Vt).
        public HostBinding.StructLayout?[]? ArgStructLayouts;
        // Flat layout of a Vt return (ReturnSType == Vt). Null otherwise.
        public HostBinding.StructLayout? ReturnStructLayout;
        // Declaring TypeDef token (0 when unknown). Set at parse; lets the post-pass flatten
        // `this` for instance methods declared on a blittable script struct.
        public int DeclaringTypeDef;
        // Flat layout for `this` (arg slot 0) when the declaring type is a flat script struct.
        public HostBinding.StructLayout? ThisStructLayout;
        // Doc is the source file name (no directory), "" when the PDB document had no name.
        public (int IlOffset, int Line, string Doc)[]? SeqPoints;
        // Finally clauses from the method body's exception-region table (offsets are IL byte
        // offsets, End = exclusive). Null when the method has none. Consumed by the lowerer's
        // leave/endfinally handling. Only the NON-exceptional path runs handlers.
        public (int TryStart, int TryEnd, int HandlerStart, int HandlerEnd)[]? FinallyRegions;
        // Fault-handler entry IL offsets. Fault handlers never run (the interpreter has no
        // exception dispatch — see the region parse in Parse), but their bodies still lower as
        // unreachable code; the lowerer treats each entry as a basic-block boundary.
        public int[]? FaultHandlerStarts;
        // True when the body has catch/filter clauses — those need real exception
        // dispatch, which the interpreter doesn't do; lowering fails with a clear reason.
        public bool HasUnsupportedEhClauses;
        // True for enumerator members the host never drives (Reset — its body is
        // `throw new NotSupportedException()`, which the interpreter can't lower). A lowering
        // failure on a cold member becomes a skipped stub (LoweringSkipReason) instead of
        // failing the whole Load.
        public bool IsColdEnumeratorMember;
        // Set when lowering this method failed but was tolerated (cold enumerator member).
        // Lowered stays null; invoking the method throws this reason.
        public string? LoweringSkipReason;
        // Non-nullable post-Load for every invocable method: Load throws
        // ScriptValidationException listing every method that didn't lower. The one exception
        // is a cold enumerator member (IsColdEnumeratorMember) whose lowering failed — it keeps
        // Lowered null and carries LoweringSkipReason; the Vm entry points guard against it.
        public LoweredMethod  Lowered = null!;
    }

    // Output of the lowering pass for one method.
    internal sealed class LoweredMethod
    {
        // All non-nullable fields are set by the lowering pass before the instance escapes.
        public uint[]   Ir = null!;           // flat instruction stream (variable-width words)
        public int      FrameSize;    // number of logical slots (numFrame = FrameSize*4 bytes, refFrame = FrameSize refs)
        public int[]    IrToIlOffset = null!; // ir word index → IL offset (for source mapping)
        public string[] Strings = null!;      // string table, indexed by ldstr immediate
        public int[]    Tokens = null!;       // token table (field/type/method tokens), indexed by tok_idx immediate
        // Per-slot type: I4/R4 → numeric byte frame; O → reference frame; Vt → flat struct
        // bytes occupying ceil(StructLayouts[s].Size/4) consecutive slots. Indexed by slot number.
        public SType[] SlotTypes = null!;
        // Parallel to SlotTypes; non-null entries describe the struct layout for Vt slots.
        public HostBinding.StructLayout?[]? StructLayouts;
        // Pre-resolved fast-delegate cache, parallel to Tokens. For each tokIdx whose
        // entry has a Fast closure, HostFastByTokIdx[tokIdx] is that closure. Eliminates
        // the per-call_host `asm.HostCalls[tok]` dictionary lookup on the hot path.
        // Null entries fall through to the slow path (which still does the dict lookup).
        public FastCallDelegate?[]? HostFastByTokIdx;
        // Parallel to HostFastByTokIdx: true when the entry's Fast closure handles a FLAT
        // (Vt) receiver itself (Entry.FastVtRecv) — the executor may keep the fast path
        // instead of dropping to the boxing slow path.
        public bool[]? HostFastVtRecvByTokIdx;
        public bool[]? HostFastWideOkByTokIdx; // Fast closure handles I8/R8 slots itself

        // Pre-resolved script callee per tokIdx. Eliminates the asm.ByToken[tok] dict
        // lookup on the call_script / newobj_script hot path. Null for tokens that don't
        // resolve to a script method (host calls, host ctors, fields, etc.).
        public ParsedMethod?[]? CalleeByTokIdx;

        // Highest O-typed slot index + 1 (0 when the frame is all-numeric). Frame setup only
        // needs to null refFrame slots the method can actually read as references — clearing
        // the numeric remainder is wasted work on every call_script into a math-y helper.
        public int RefClearLen;

        // This method's base offset inside ParsedAssembly.IrBlob. Execution ips are
        // blob-absolute; diagnostics index IrToIlOffset with (ip - IrStart).
        public int IrStart;

        // Arg index → frame slot. Null when identity (no Vt args): args live at slots
        // 0..ArgCount-1. Non-null when a flat-struct arg occupies multiple slots, shifting
        // the args after it.
        public int[]? ArgSlot;

        // Sparse per-tokIdx flat layout for newarr_vt element types (parallel to Tokens).
        public HostBinding.StructLayout?[]? LayoutByTokIdx;

        // Sparse per-tokIdx element Type for primitive arrays that need a typed runtime backing
        // (bool[]/char[]/byte[]/sbyte[]/short[]/ushort[]). Int32/Single use the dedicated
        // newarr_i4/newarr_r4 ops; every other primitive was previously object?[]-backed, which
        // lost the element type (element ToString/boxing-identity wrong). Null when no such array.
        public Type?[]? PrimArrayElemTypeByTokIdx;

        // Sparse per-tokIdx delegate-creation site (parallel to Tokens, keyed by the delegate
        // ctor's tokIdx). Null when the method creates no delegates.
        public DelegateSite?[]? DelegateSiteByTokIdx;
    }

    // One ldftn/ldvirtftn + newobj delegate-creation site, pre-resolved at lowering: the runtime
    // delegate type plus the target — a host MethodInfo (bound via Delegate.CreateDelegate) or an
    // interpreted method (bound via a ScriptDelegateAdapter that re-enters the VM).
    internal sealed class DelegateSite
    {
        public Type DelegateType = null!;
        public MethodInfo? HostMethod;      // exactly one of HostMethod / ScriptMethod is set
        public ParsedMethod? ScriptMethod;
        // ldvirtftn site over a virtual script method: CreateDelegateCore re-resolves the target
        // from the receiver's runtime type (once, at creation — CLR ldvirtftn semantics).
        public bool VirtualResolve;
        // Receiverless creations (static target) share one delegate per site, so repeated
        // event `+=`/`-=` over the same method group observe delegate equality.
        public Delegate? CachedStatic;
    }

    internal sealed class HostEntry
    {
        public HostBinding.Entry Binding = null!; // the registered entry (handles int→bool coercion); always set at construction
        public bool IsVoid;      // true when return type is void — don't push result
        // Per-arg flat-struct layouts (length = explicit param count, no receiver).
        // Non-null entries indicate the parameter type was registered via AllowTypeStruct
        // and its arg should be marshalled from a Vt slot at call time. null = boxed object.
        public HostBinding.StructLayout?[]? ArgStructs;
        // Receiver struct layout: non-null when the receiver is a flat struct (rare).
        public HostBinding.StructLayout? ReceiverStruct;
        // Return struct layout: non-null when the return type was registered via AllowTypeStruct.
        public HostBinding.StructLayout? ReturnStruct;
        // The EXACT overload this token resolved to. Aliased operator pairs (Quaternion's
        // op_Multiply(Q,Q)->Q and op_Multiply(Q,V3)->V3) share one Entry whose Method is the
        // FIRST overload — typing a call site from Entry.Method then mislabels the return
        // (a Quaternion Vt dst for a Vector3 result => InvalidCastException at UnboxVt).
        // Null when resolution didn't go through an exact MethodInfo (string fallback).
        public MethodInfo? ResolvedMethod;
        // Return SType from the call site's METADATA signature. The lowering types the result slot
        // from ResolvedMethod when available; for string-keyed entries (hand-wired Allow/AllowBcl
        // shims) there is no MethodInfo, and without this the slot defaulted to O — boxing every
        // numeric host return, which broke `a.Length != a.Length` (reference-compared boxes) and
        // any op that writes its result flat.
        public SType SigRetSType = SType.O;
        // How to box an I4-slot receiver for an instance host call. char and bool are stored in I4
        // slots but their boxed identity differs from int: 'r' (114) boxed as int renders "114" not
        // "r", and a bool boxed as int renders "1"/"0" not "True"/"False". char and bool are the only
        // I4-mapped receivers whose boxed identity changes the result (byte/short/... render the same
        // digits as int). None for every other receiver.
        public ReceiverBoxKind ReceiverBox;
        // Per explicit parameter, from the call site's METADATA signature: 4 = uint, 8 = ulong, 0 =
        // anything else; null when no parameter is unsigned. uint/ulong ride the signed I4/I8 slots,
        // so RdArg boxes them as int/long — a string-keyed shim switching on the runtime type then
        // sees `uint.MaxValue` as `-1` (Math.Max(uint.MaxValue, 0u) returned 0). The call site
        // re-boxes by the declared type before invoking (see call_host in VmEngine).
        public byte[]? UnsignedParams;
    }

    // OR'ed into a MemberRef token to address the NON-virtual binding of a member that also has a
    // virtual one. `base.ToString()` compiles to a plain `call Object::ToString`; the shared shim does
    // `recv.ToString()`, which is virtual and re-enters the script override forever. The plain-call
    // lowering uses this token and Parse registers the non-virtual entry beside the virtual one.
    // Bit 30 is outside every metadata table id.
    internal const int NonVirtualCallBit = 0x40000000;

    internal enum ReceiverBoxKind : byte { None, Char, Bool }


    // Driver members of a script enumerator type (iterator state machine): what the host
    // bridge invokes to pump it. Dispose is null when the type has no (parsed) Dispose.
    internal readonly struct EnumeratorMembers
    {
        public readonly ParsedMethod MoveNext;
        public readonly ParsedMethod GetCurrent;
        public readonly ParsedMethod? Dispose;
        public EnumeratorMembers(ParsedMethod moveNext, ParsedMethod getCurrent, ParsedMethod? dispose)
        {
            MoveNext = moveNext; GetCurrent = getCurrent; Dispose = dispose;
        }
    }

    internal sealed class ParsedAssembly
    {
        // Every non-nullable field is set by Parse's object initializer before the instance escapes.
        public Dictionary<int, ParsedMethod>              ByToken = null!;
        public Dictionary<string, ParsedMethod>           ByName = null!;
        public Dictionary<int, HostEntry>                 HostCalls = null!;   // MemberRef token → host method
        public Dictionary<int, HostEntry>                 HostCtors = null!;   // MemberRef token → host constructor
        public Dictionary<int, HostBinding.FieldEntry>    HostFields = null!;  // MemberRef token → host field
        public Dictionary<int, int>                       HostFieldDeclTypeTok = null!; // host field MemberRef token → declaring TypeRef token (ensure-init of boxed struct locals)
        public Dictionary<int, string>                    TokenNames = null!;  // MemberRef token → "Type.Method/N" for diagnostics
        public Dictionary<int, (ScriptTypeDescriptor, int)> FieldSlots = null!;     // FieldDef token → (type, slot)
        public Dictionary<int, SType>                        FieldSTypes = null!;  // FieldDef token → numeric type (for IR lowering)
        // FieldDef token → I8/R8 for long/ulong/double script fields. Their STORAGE stays boxed in
        // a ref slot (FieldSTypes says O — an 8-byte PrimBytes cell would ripple through every
        // offset consumer), but field LOADS must unbox into a correctly-typed wide slot: two
        // O-typed operands fall into the i4 arithmetic path and truncate (found by fuzzing).
        public Dictionary<int, SType>                        FieldWide = null!;
        public HashSet<int>                               FieldIsScriptStruct = null!; // FieldDef tokens whose type is a script-defined struct (O storage, needs clone on value-load)
        public Dictionary<int, int>                       FieldToTypeDef = null!;   // FieldDef token → declaring TypeDef token
        public Dictionary<int, ScriptTypeDescriptor>      CtorToType = null!;      // .ctor MethodDef token → type
        public Dictionary<int, int>                       TypeDefToCtorTok = null!; // TypeDef token → default .ctor MethodDef token
        public Dictionary<int, ScriptTypeDescriptor>      TypeDefToType = null!;    // TypeDef token → script type (for initobj)
        public Dictionary<string, ScriptTypeDescriptor>   TypesByName = null!;
        // Script enumerator types (iterator state machines) → driver members for the host bridge.
        public Dictionary<ScriptTypeDescriptor, EnumeratorMembers> EnumeratorTypes = null!;
        public Dictionary<int, Type>                      TokenTypes = null!;  // TypeRef/TypeSpec token → host Type (for castclass/isinst/ldtoken)
        public MetadataReader Reader = null!;
        public PEReader Pe = null!;
        public MetadataReaderProvider? PdbProvider;
        // Host member refs that resolved to throwing stubs (or missing host fields) — the script
        // loaded, but reaching one of these call sites throws ScriptRuntimeException. Surfaced by
        // ScriptInterpreter.UnboundHostMembers so callers can warn at load time.
        public List<string> UnboundHostMembers = new();

        // SCRIPT static fields (FieldDef tokens accessed by ldsfld/stsfld), stored boxed, lazily.
        // Unwritten fields read as null — matching CLR zero-init for the one pattern that needs
        // this: the C# 11+ method-group delegate cache (`ldsfld <>O.<n>__M; dup; brtrue …`),
        // which null-checks before every use. Static ctors never run (pre-existing semantics),
        // so a numeric static read before any write still fails, as it did before.
        public Dictionary<int, object?> ScriptStatics = new();
        // Primitive (I4/R4) script statics, UNBOXED as raw bits (written by stsfld_i4/r4, read by
        // ldsfld_i4/r4) — avoids a heap box on every static write. Miss = 0 (CLR zero-init);
        // reference/long/double statics stay in the boxed ScriptStatics above.
        public Dictionary<int, long>    ScriptStaticsNum = new();

        // The host binding this assembly was parsed against — lets the LOWERER resolve
        // flat layouts for host struct types (e.g. Vector3[] element layouts) by TypeRef name.
        public HostBinding? HostSurface;

        // All methods' lowered IR concatenated (branch targets blob-absolute, one ret_void
        // sentinel after each method's region). Built by IrLowerer.BuildIrBlob after LowerAll.
        // Lets the Vm run script-to-script calls iteratively under a single `fixed` — no
        // recursive Run() invocation (measured ~120ns per nested call on Mono) and no per-call
        // array pin.
        public uint[]? IrBlob;
    }

    // CLR full name for a TypeRef — namespace-qualified, nested types joined with '+' — matching
    // what Assembly.GetType expects. A nested TypeRef's ResolutionScope is its declaring TypeRef.
    // Virtual SLOT identity. Name + signature is not enough: `new virtual F()` (MethodAttributes.NewSlot)
    // hides Base.F in a fresh slot, so `Base b = new Derived(); b.F()` must still run Base.F. Rule:
    // a NewSlot method that hides a same-named, same-signature virtual somewhere up the SCRIPT base
    // chain gets its own key (`name/sig@typedef`); an override (no NewSlot) inherits the key of the
    // nearest such base method; everything else keeps the plain `name/sig` key — interface stubs,
    // their implicit implementations, and slots owned outside the assembly (ToString) all meet on it.
    static void AssignVirtualSlotKeys(MetadataReader reader, Dictionary<int, ParsedMethod> byToken)
    {
        var owner = new Dictionary<int, (TypeDefinitionHandle Type, MethodDefinition Def)>();
        foreach (var th in reader.TypeDefinitions)
            foreach (var mh in reader.GetTypeDefinition(th).GetMethods())
                owner[MetadataTokens.GetToken(mh)] = (th, reader.GetMethodDefinition(mh));
        var memo = new Dictionary<int, string>();

        string KeyFor(int tok)
        {
            if (memo.TryGetValue(tok, out var k)) return k;
            var (th, md) = owner[tok];
            var name = reader.GetString(md.Name);
            var sig = reader.GetBlobBytes(md.Signature);
            string plain = ScriptTypeDescriptor.VirtualKey(name, sig);
            k = plain;
            int hidden = FindBaseVirtual(th, name, sig);
            if (hidden != 0)
                k = (md.Attributes & MethodAttributes.NewSlot) != 0
                    ? $"{plain}@{MetadataTokens.GetToken(th):X8}"   // hides it: a new slot
                    : KeyFor(hidden);                               // overrides it: the same slot
            memo[tok] = k;
            return k;
        }

        // The nearest virtual with this name and signature on a base TypeDef of this assembly; 0 if
        // the chain leaves the assembly first (host base such as Object/MonoBehaviour).
        int FindBaseVirtual(TypeDefinitionHandle th, string name, byte[] sig)
        {
            var bt = reader.GetTypeDefinition(th).BaseType;
            while (!bt.IsNil && bt.Kind == HandleKind.TypeDefinition)
            {
                var bth = (TypeDefinitionHandle)bt;
                var btd = reader.GetTypeDefinition(bth);
                foreach (var mh in btd.GetMethods())
                {
                    var bmd = reader.GetMethodDefinition(mh);
                    if ((bmd.Attributes & MethodAttributes.Virtual) == 0 || (bmd.Attributes & MethodAttributes.Static) != 0) continue;
                    if (reader.GetString(bmd.Name) != name) continue;
                    if (reader.GetBlobBytes(bmd.Signature).AsSpan().SequenceEqual(sig)) return MetadataTokens.GetToken(mh);
                }
                bt = btd.BaseType;
            }
            return 0;
        }

        foreach (var (tok, pm) in byToken)
            if (pm.IsVirtual && !pm.IsStatic && owner.ContainsKey(tok))
                pm.VirtKey = KeyFor(tok);
    }

    // Register a type's interface implementations in its dispatch map under the INTERFACE method's
    // key: explicit ones from the MethodImpl table (`IThing.M` bodies), and implicit ones whose own
    // key was suffixed because the method also hides a base virtual. Interfaces from other
    // assemblies (MemberRef declarations) are host-dispatched and skipped.
    static void RegisterInterfaceImplementations(MetadataReader reader, TypeDefinition typeDef,
        ScriptTypeDescriptor desc, Dictionary<int, ParsedMethod> byToken)
    {
        foreach (var mih in typeDef.GetMethodImplementations())
        {
            var mi = reader.GetMethodImplementation(mih);
            if (mi.MethodDeclaration.Kind != HandleKind.MethodDefinition || mi.MethodBody.Kind != HandleKind.MethodDefinition) continue;
            if (byToken.TryGetValue(MetadataTokens.GetToken(mi.MethodDeclaration), out var decl)
                && byToken.TryGetValue(MetadataTokens.GetToken(mi.MethodBody), out var body))
                (desc.VirtualsBySig ??= new Dictionary<string, object>())[decl.VirtKey] = body;
        }
        foreach (var ih in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(ih).Interface;
            if (iface.Kind != HandleKind.TypeDefinition) continue;
            foreach (var imh in reader.GetTypeDefinition((TypeDefinitionHandle)iface).GetMethods())
            {
                if (!byToken.TryGetValue(MetadataTokens.GetToken(imh), out var stub)) continue;
                var imd = reader.GetMethodDefinition(imh);
                var name = reader.GetString(imd.Name); var sig = reader.GetBlobBytes(imd.Signature);
                foreach (var mh in typeDef.GetMethods())
                {
                    var md = reader.GetMethodDefinition(mh);
                    if ((md.Attributes & MethodAttributes.Virtual) == 0 || reader.GetString(md.Name) != name) continue;
                    if (!reader.GetBlobBytes(md.Signature).AsSpan().SequenceEqual(sig)) continue;
                    if (byToken.TryGetValue(MetadataTokens.GetToken(mh), out var impl) && impl.VirtKey != stub.VirtKey)
                    {
                        var map = desc.VirtualsBySig ??= new Dictionary<string, object>();
                        if (!map.ContainsKey(stub.VirtKey)) map[stub.VirtKey] = impl;   // an explicit impl wins
                    }
                }
            }
        }
    }

    static string TypeRefFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var tr   = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeRefFullName(reader, (TypeReferenceHandle)tr.ResolutionScope) + "+" + name;
        var ns = reader.GetString(tr.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    static ParsedAssembly Parse(byte[] bytes, HostBinding? binding, Action<string> logSink)
    {
        var pe     = new PEReader(new MemoryStream(bytes), PEStreamOptions.PrefetchEntireImage);
        MetadataReader reader = pe.GetMetadataReader();

        // --- Demand-time auto-bind: register this script's referenced host types on first use ---
        // When the binding carries an AutoBindResolver policy, resolve every TypeRef the script
        // references and AllowType the ones the policy accepts, BEFORE the MemberRef resolution
        // below — every resolution site then sees the type as if it had been curated. What binds
        // is entirely the resolver's decision (which types exist, which assemblies are policy-
        // skipped); this pass just walks the refs. Type args of closed generics appear as their
        // own TypeRef rows, so MethodSpec instantiation (AddComponent<T>) benefits too.
        if (binding?.AutoBindResolver != null)
            foreach (var trHandle in reader.TypeReferences)
                binding.TryAutoBindType(TypeRefFullName(reader, trHandle));

        // --- Script methods ---
        var byToken = new Dictionary<int, ParsedMethod>();
        var byName  = new Dictionary<string, ParsedMethod>();

        foreach (var handle in reader.MethodDefinitions)
        {
            var def   = reader.GetMethodDefinition(handle);
            var name  = reader.GetString(def.Name);
            var token = MetadataTokens.GetToken(handle);

            if (def.RelativeVirtualAddress == 0)
            {
                // Bodyless ABSTRACT VIRTUAL method — in practice an INTERFACE member. Callvirt
                // sites need a signature STUB in ByToken so they lower to call_script_virt and
                // resolve the implementation from the receiver's runtime type (C#'s implicit
                // interface impls are `virtual final` in metadata, so they sit in VirtualsBySig).
                // Without the stub the token fell into the host-member decode and died with a raw
                // BadImageFormatException (found by fuzzing probes). Not registered in ByName —
                // stubs aren't invocable — and skipped by LowerAll (no IL to lower).
                if ((def.Attributes & MethodAttributes.Abstract) != 0
                    && (def.Attributes & MethodAttributes.Virtual) != 0
                    && (def.Attributes & MethodAttributes.Static) == 0)
                {
                    var stubSig = reader.GetBlobBytes(def.Signature);
                    var (stubVoid, stubRet, stubRetBool) = SigReturn(stubSig);
                    var stubArgs = SigParamSTypes(stubSig);
                    byToken[token] = new ParsedMethod
                    {
                        Name = name, Token = token, ArgCount = 1 + stubArgs.Length,
                        IlBytes = Array.Empty<byte>(), IsStatic = false, IsVirtual = true,
                        IsAbstractStub = true, IsVoid = stubVoid, ReturnIsBool = stubRetBool,
                        VirtKey = ScriptTypeDescriptor.VirtualKey(name, stubSig),
                        ReturnSType = stubRet, ArgSTypes = stubArgs,
                    };
                }
                continue;
            }

            var body       = pe.GetMethodBody(def.RelativeVirtualAddress);
            var ilBytes    = body.GetILBytes() ?? Array.Empty<byte>();
            var localCount = ReadLocalCount(reader, body.LocalSignature);
            var localStrs  = ReadLocalStructLayouts(reader, body.LocalSignature, localCount, binding);
            var localScSt  = ReadLocalIsScriptStruct(reader, body.LocalSignature, localCount, out var localScTds);
            var localSTps  = ReadLocalSTypes(reader, body.LocalSignature, localCount);
            var isStatic   = (def.Attributes & MethodAttributes.Static) != 0;
            var isVirtual  = (def.Attributes & MethodAttributes.Virtual) != 0;
            var sigBytes   = reader.GetBlobBytes(def.Signature);
            var (isVoid, retSType, retIsBool) = SigReturn(sigBytes);
            var argSTypes  = SigParamSTypes(sigBytes);
            // Use signature param count, not Param-table count: Roslyn omits Param table entries
            // for synthetic parameters in compiler-generated methods (closures, state machines).
            var argCount   = (isStatic ? 0 : 1) + argSTypes.Length;

            // Exception-region table: finally clauses are lowered (leave/endfinally continuation
            // chain); catch/filter need real exception dispatch and fail lowering instead.
            // FAULT clauses are IGNORED: a fault handler runs only under exception dispatch,
            // which the interpreter never does — a script exception propagates straight to the
            // host and the frame is abandoned — so skipping the region keeps normal-path
            // semantics exact (fault must NOT run on normal exit). C# can't write a fault
            // clause; Roslyn emits them in iterator/async MoveNext bodies, so this is what lets
            // `yield return` state machines with try/finally lower. The handler body still
            // lowers (as unreachable code); its start is recorded so the lowerer treats it as a
            // basic-block boundary.
            (int, int, int, int)[]? finRegions = null;
            int[]? faultHandlerStarts = null;
            bool unsupportedEh = false;
            if (!body.ExceptionRegions.IsEmpty)
            {
                var fins = new List<(int, int, int, int)>();
                List<int>? faults = null;
                foreach (var er in body.ExceptionRegions)
                {
                    if (er.Kind == ExceptionRegionKind.Finally)
                        fins.Add((er.TryOffset, er.TryOffset + er.TryLength,
                                  er.HandlerOffset, er.HandlerOffset + er.HandlerLength));
                    else if (er.Kind == ExceptionRegionKind.Fault)
                        (faults ??= new List<int>()).Add(er.HandlerOffset);
                    else
                        unsupportedEh = true;
                }
                if (fins.Count > 0) finRegions = fins.ToArray();
                if (faults != null) faultHandlerStarts = faults.ToArray();
            }

            var (retScTd, argScTds) = ReadSigScriptStructTokens(sigBytes);
            int declTypeDef = 0;
            try { declTypeDef = MetadataTokens.GetToken(def.GetDeclaringType()); } catch (Exception) { }
            var m = new ParsedMethod { Name = name, Token = token, DeclaringTypeDef = declTypeDef, ArgCount = argCount, LocalCount = localCount, IlBytes = ilBytes, IsStatic = isStatic, IsVirtual = isVirtual, VirtKey = ScriptTypeDescriptor.VirtualKey(name, sigBytes), IsVoid = isVoid, ReturnIsBool = retIsBool, ReturnSType = retSType, ArgSTypes = argSTypes, LocalStructLayouts = localStrs, LocalSTypes = localSTps, LocalIsScriptStruct = localScSt, LocalScriptStructTypeDefs = localScTds, ArgScriptStructTypeDefs = argScTds, ReturnScriptStructTypeDef = retScTd, FinallyRegions = finRegions, FaultHandlerStarts = faultHandlerStarts, HasUnsupportedEhClauses = unsupportedEh };
            byToken[token] = m;
            byName[name]   = m;
        }

        // Virtual slot identity for the dispatch maps (see AssignVirtualSlotKeys): needs every
        // ParsedMethod to exist, so it runs between the method pass and the type pass.
        AssignVirtualSlotKeys(reader, byToken);

        // --- Script-defined types: field slots, .ctor → type mapping, and name → type ---
        var fieldSlots       = new Dictionary<int, (ScriptTypeDescriptor, int)>();
        var fieldSTypes      = new Dictionary<int, SType>();
        var fieldWide        = new Dictionary<int, SType>(); // long/ulong/double fields (see ParsedAssembly.FieldWide)
        var fieldIsScStruct  = new HashSet<int>();
        var fieldToTypeDef   = new Dictionary<int, int>(); // FieldDef token → declaring TypeDef token
        var ctorToType       = new Dictionary<int, ScriptTypeDescriptor>();
        var typeDefToCtorTok = new Dictionary<int, int>(); // TypeDef token → default .ctor MethodDef token
        var typeDefToType    = new Dictionary<int, ScriptTypeDescriptor>(); // TypeDef token → type
        var typesByName      = new Dictionary<string, ScriptTypeDescriptor>();
        // Script types implementing System.Collections.IEnumerator → their driver members,
        // so the host bridge (ScriptEnumerator) can pump an interpreted state machine.
        var enumeratorTypes  = new Dictionary<ScriptTypeDescriptor, EnumeratorMembers>();
        // (descriptor, per-field nested-struct TypeDef token) pending resolution once all types exist.
        var pendingStructFields = new List<(ScriptTypeDescriptor desc, int[] typeDefs)>();
        // Script base-class links, resolved after all descriptors exist (declaration order is
        // arbitrary). Feeds virtual dispatch (BaseDesc chain walk in call_script_virt).
        var pendingBaseLinks = new List<(ScriptTypeDescriptor desc, int baseTypeDefTok)>();

        // This type's own INSTANCE fields, declaration order. Statics are excluded: an enum's
        // members are static literals typed as the enum itself, which made the descriptor
        // self-referential and recursed ScriptObject.Create forever.
        List<FieldDefinitionHandle> GetOwnInstanceFields(TypeDefinition td)
        {
            var handles = td.GetFields();
            var result = new List<FieldDefinitionHandle>(handles.Count);
            foreach (var fh in handles)
                if ((reader.GetFieldDefinition(fh).Attributes & FieldAttributes.Static) == 0)
                    result.Add(fh);
            return result;
        }

        // Base-first field layout ([root base]..[own]), walking in-assembly BaseTypes. ownStart =
        // first own field. Invariant this fix relies on: a base field lands at the identical
        // slot/offset in every descriptor that inherits it.
        List<FieldDefinitionHandle> BuildBaseFirstFields(TypeDefinition td, out int ownStart, int depth = 0)
        {
            // Guard against a cyclic base chain (A : B, B : A) in hostile metadata — an unbounded
            // recursion here is a StackOverflowException, which .NET cannot catch (whole-process
            // kill). A real inheritance depth never approaches this.
            if (depth > 256)
                throw new ScriptValidationException(
                    "base-type chain is too deep or cyclic — malformed metadata");
            List<FieldDefinitionHandle> fields;
            var bt = td.BaseType;
            // NIL base (interfaces) still reads Kind == TypeDefinition — dereferencing it threw
            // BadImageFormatException (same hole as the isStructType check above).
            if (!bt.IsNil && bt.Kind == HandleKind.TypeDefinition)
            {
                var baseTd   = reader.GetTypeDefinition((TypeDefinitionHandle)bt);
                var baseName = reader.GetString(baseTd.Name);
                fields = (baseName == "ValueType" || baseName == "Enum")
                    ? new List<FieldDefinitionHandle>()
                    : BuildBaseFirstFields(baseTd, out _, depth + 1);
            }
            else
            {
                fields = new List<FieldDefinitionHandle>();
            }
            ownStart = fields.Count;
            fields.AddRange(GetOwnInstanceFields(td));
            return fields;
        }

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef  = reader.GetTypeDefinition(typeHandle);
            var typeName = reader.GetString(typeDef.Name);
            if (typeName == "<Module>") continue;

            // Detect a script-defined struct (base type System.ValueType, excluding enums whose base is
            // System.Enum). Used to zero-init `new T[n]` elements to usable structs rather than nulls.
            bool isStructType = false;
            {
                var bt = typeDef.BaseType;
                string baseName = "";
                // INTERFACES have a NIL base handle (Kind still reads TypeDefinition) — reading
                // through it threw BadImageFormatException "Read out of bounds" (found by fuzzing
                // probes the moment a script interface appeared).
                if (bt.IsNil) { }
                else if (bt.Kind == HandleKind.TypeReference)
                    baseName = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)bt).Name);
                else if (bt.Kind == HandleKind.TypeDefinition)
                    baseName = reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)bt).Name);
                isStructType = baseName == "ValueType"; // "Enum" (also a value type) intentionally excluded
            }
            // A TypeDefinition base = a SCRIPT base class: link descriptors after the loop so
            // virtual dispatch can walk the chain (see pendingBaseLinks).
            int scriptBaseTok = !typeDef.BaseType.IsNil && typeDef.BaseType.Kind == HandleKind.TypeDefinition
                ? MetadataTokens.GetToken((TypeDefinitionHandle)typeDef.BaseType) : 0;

            // Assign field slots in declaration order, BASE-FIRST: inherited fields occupy the front
            // (same indices/offsets as in their own descriptor), then this type's own fields. See
            // BuildBaseFirstFields for the invariant this depends on.
            var typeFields = BuildBaseFirstFields(typeDef, out int ownFieldStart).ToArray();
            var fieldSlotMap  = new Dictionary<int, int>(typeFields.Length);
            var fldTypes      = new SType[typeFields.Length];
            byte[]? fldWideKinds = null; // 1 = I8, 2 = R8 (see ScriptTypeDescriptor.FieldWideKinds)
            var fldOffsets    = new int[typeFields.Length];
            HostBinding.StructLayout?[]? fldVtLayouts = null; // allocated on demand
            bool[]? fldIsScStruct = null;                     // allocated on demand (nested value-type fields)
            int[]?  fldScStructTypeDef = null;                // nested script-struct field's TypeDef token (resolved to a descriptor after the loop)
            int primByteOff   = 0;
            int refSlotOff    = 0;
            for (int i = 0; i < typeFields.Length; i++)
            {
                int fieldToken = MetadataTokens.GetToken(typeFields[i]);
                bool isOwnField = i >= ownFieldStart;
                fieldSlotMap[fieldToken] = i;
                // Global field maps are keyed by declaring type only, so register OWN fields here;
                // an inherited field is registered by its own type's pass (base-first makes the
                // value identical anyway, and re-keying it here corrupts declaring-type bookkeeping).
                if (isOwnField)
                    fieldSlots[fieldToken] = (null!, i); // type filled in below
                var fDef = reader.GetFieldDefinition(typeFields[i]);
                var fSig = reader.GetBlobBytes(fDef.Signature);
                // Field sig: [calling_conv(0x06), type_byte, ...]
                // VALUETYPE (0x11) followed by compressed TypeRef/TypeDef token — check
                // if it's a registered host struct; if so, store inline in PrimBytes (SType.Vt).
                SType fst = SType.O;
                HostBinding.StructLayout? vtLayout = null;
                if (fSig.Length >= 2)
                {
                    byte tb = fSig[1];
                    // 0x03 = ELEMENT_TYPE_CHAR: char is I4-typed in the VM; omitting it typed a
                    // hoisted char field O (a ref slot), the mirror of the SigReturn char fix
                    // (found by fuzzing: a foreach-over-string char hoisted across a yield).
                    // nint/nuint (0x18/0x19) are NOT here: an I4 slot would truncate 64-bit
                    // handles (AndroidJNI-style IntPtrs); they ride O slots as boxed IntPtr.
                    if (tb is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09)
                        fst = SType.I4;
                    else if (tb == 0x0C)
                        fst = SType.R4;
                    else if (tb is 0x0A or 0x0B or 0x0D)
                    {
                        // long/ulong/double: fst stays O (boxed ref-slot storage), but record the
                        // true width so loads unbox into a wide slot (see ParsedAssembly.FieldWide)
                        // and Create pre-boxes a typed zero (see FieldWideKinds).
                        if (isOwnField) fieldWide[fieldToken] = tb == 0x0D ? SType.R8 : SType.I8;
                        (fldWideKinds ??= new byte[typeFields.Length])[i] = tb == 0x0D ? (byte)2 : (byte)1;
                    }
                    else if (tb == 0x11 && fSig.Length >= 3)
                    {
                        // VALUETYPE: try to decode the TypeRef name and look up a struct layout
                        int codedIdx = 0, n = 2;
                        while (n < fSig.Length)
                        {
                            byte b = fSig[n++];
                            if ((b & 0x80) == 0) { codedIdx |= b; break; }
                            if ((b & 0x40) == 0) { codedIdx = ((b & 0x3F) << 8) | fSig[n++]; break; }
                            codedIdx = ((b & 0x1F) << 24) | (fSig[n] << 16) | (fSig[n+1] << 8) | fSig[n+2]; n += 3; break;
                        }
                        int table = codedIdx & 0x03; int row = codedIdx >> 2;
                        string vtName = "";
                        if (table == 1 && row > 0) // TypeRef
                        {
                            try { vtName = reader.GetString(reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(row)).Name); }
                            catch { }
                        }
                        else if (table == 0 && row > 0) // TypeDef
                        {
                            try { vtName = reader.GetString(reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(row)).Name); }
                            catch { }
                        }
                        if (vtName != "" && binding != null && binding.TryGetStructLayout(vtName, out vtLayout) && vtLayout != null)
                            fst = SType.Vt;
                        else if (table == 0 && IsEnumTypeDef(reader, row))
                        {
                            // VALUETYPE that is a script-defined ENUM: the value is a plain int,
                            // not a nested struct — classify I4 so loads/stores/compares treat it
                            // as a number and Create never allocates a ScriptObject for it.
                            fst = SType.I4;
                        }
                        else if (table == 0)
                        {
                            // VALUETYPE referencing a TypeDef in this assembly that isn't a registered
                            // host struct = a script-defined struct. Stored as O (boxed ScriptObject),
                            // so a value-load of this field must clone to preserve copy semantics.
                            if (isOwnField) fieldIsScStruct.Add(fieldToken);
                            (fldIsScStruct ??= new bool[typeFields.Length])[i] = true;
                            // Stash the field's own TypeDef token (0x02 table) to resolve to a descriptor
                            // after all types are built, so Create can allocate the nested struct.
                            (fldScStructTypeDef ??= new int[typeFields.Length])[i] =
                                MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(row));
                        }
                    }
                }
                if (isOwnField) fieldSTypes[fieldToken] = fst;
                fldTypes[i] = fst;
                if (fst == SType.I4 || fst == SType.R4)
                {
                    fldOffsets[i] = primByteOff;
                    primByteOff += 4;
                }
                else if (fst == SType.Vt && vtLayout != null)
                {
                    // Align to 4 bytes (structs are naturally aligned in Marshal layout)
                    int align = (4 - primByteOff % 4) % 4;
                    primByteOff += align;
                    fldOffsets[i] = primByteOff;
                    primByteOff += vtLayout.Size;
                    if (primByteOff % 4 != 0) primByteOff += 4 - primByteOff % 4;
                    if (fldVtLayouts == null) fldVtLayouts = new HostBinding.StructLayout?[typeFields.Length];
                    fldVtLayouts[i] = vtLayout;
                }
                else
                {
                    fldOffsets[i] = refSlotOff;
                    refSlotOff++;
                }
            }

            var desc = new ScriptTypeDescriptor
            {
                Name           = typeName,
                FieldCount     = typeFields.Length,
                FieldSlots     = fieldSlotMap,
                FieldTypes     = fldTypes,
                FieldOffsets   = fldOffsets,
                PrimByteSize   = primByteOff,
                RefSlotCount   = refSlotOff,
                VtFieldLayouts = fldVtLayouts,
                FieldIsScriptStruct = fldIsScStruct,
                IsScriptStructValue = isStructType,
                FieldWideKinds = fldWideKinds,
            };

            // Patch back the type reference into fieldSlots — OWN fields only (see isOwnField above).
            foreach (var ft in fieldSlotMap)
                if (ft.Value >= ownFieldStart)
                    fieldSlots[ft.Key] = (desc, ft.Value);

            typesByName[typeName] = desc;
            if (fldScStructTypeDef != null) pendingStructFields.Add((desc, fldScStructTypeDef));
            if (scriptBaseTok != 0) pendingBaseLinks.Add((desc, scriptBaseTok));

            // Map TypeDef token → type descriptor (used by initobj lowering).
            int typeDefTok = MetadataTokens.GetToken(typeHandle);
            typeDefToType[typeDefTok] = desc;
            // Map each OWN field → declaring TypeDef token (used by auto-initobj in stfld lowering).
            // An inherited field's declaring TypeDef is registered when its own type is processed.
            foreach (var ft in fieldSlotMap)
                if (ft.Value >= ownFieldStart)
                    fieldToTypeDef[ft.Key] = typeDefTok;

            // Map each .ctor to this type; also record TypeDef → default-ctor for initobj handling.
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var mDef   = reader.GetMethodDefinition(methodHandle);
                var mName  = reader.GetString(mDef.Name);
                // Virtual instance methods (incl. overrides) join the type's dispatch map, keyed
                // by name + signature (VirtKey) — call_script_virt walks the BaseDesc chain from
                // the RECEIVER's descriptor so `((Base)derived).M()` runs the override.
                if (mName != ".ctor")
                {
                    int vTok = MetadataTokens.GetToken(methodHandle);
                    if ((mDef.Attributes & MethodAttributes.Virtual) != 0
                        && (mDef.Attributes & MethodAttributes.Static) == 0
                        && byToken.TryGetValue(vTok, out var vM))
                        (desc.VirtualsBySig ??= new Dictionary<string, object>())[vM.VirtKey] = vM;
                    continue;
                }
                int ctorTok = MetadataTokens.GetToken(methodHandle);
                ctorToType[ctorTok] = desc;
                // Record ONLY a genuine 0-arg ctor (ArgCount==1 = just `this`) as the default.
                // The old first-ctor fallback made initobj on a type with only PARAMETERIZED
                // ctors call one with argc=0 — the ctor body ran over unwritten (pooled-garbage)
                // arg slots and corrupted the zero-init default (found by fuzzing). A type with
                // no 0-arg ctor zero-inits via initobj_script instead, which is what C#'s
                // `new S()` / default(S) means anyway.
                if (byToken.TryGetValue(ctorTok, out var ctorM) && ctorM.ArgCount == 1)
                    typeDefToCtorTok[typeDefTok] = ctorTok;
            }

            // Interface dispatch: explicit implementations (MethodImpl rows: the body is named
            // `IThing.M`, the stub `M`) and implicit ones whose own slot key differs from the
            // interface method's (a class method that also hides a base virtual).
            RegisterInterfaceImplementations(reader, typeDef, desc, byToken);

            // --- Enumerator (iterator state-machine) detection ---
            // A script type implementing System.Collections.IEnumerator — in practice a Roslyn
            // `yield return` state machine — gets its driver members resolved so the host
            // bridge (ScriptEnumerator) can pump it from Unity's coroutine scheduler. Reset is
            // marked cold: its body is `throw new NotSupportedException()`, which the
            // interpreter can't lower, and no driver ever calls it.
            bool isEnumerator = false;
            foreach (var ih in typeDef.GetInterfaceImplementations())
            {
                var ii = reader.GetInterfaceImplementation(ih);
                if (ii.Interface.Kind == HandleKind.TypeReference &&
                    TypeRefFullName(reader, (TypeReferenceHandle)ii.Interface) == "System.Collections.IEnumerator")
                {
                    isEnumerator = true;
                    break;
                }
            }
            if (isEnumerator)
            {
                ParsedMethod? moveNext = null, getCurrent = null, dispose = null;
                foreach (var mh in typeDef.GetMethods())
                {
                    if (!byToken.TryGetValue(MetadataTokens.GetToken(mh), out var pm)) continue;
                    switch (pm.Name)
                    {
                        case "MoveNext":
                        case "System.Collections.IEnumerator.MoveNext":
                            moveNext = pm; break;
                        // The non-generic getter — present in every flavor (a generic
                        // IEnumerator<T> state machine also implements it). Overwrites a plain
                        // get_Current seen earlier; the plain name only fills when nothing
                        // better exists (hand-written enumerator without explicit impls).
                        case "System.Collections.IEnumerator.get_Current":
                            getCurrent = pm; break;
                        case "get_Current":
                            getCurrent ??= pm; break;
                        case "Dispose":
                        case "System.IDisposable.Dispose":
                            dispose = pm; break;
                        case "Reset":
                        case "System.Collections.IEnumerator.Reset":
                            pm.IsColdEnumeratorMember = true; break;
                    }
                }
                if (moveNext != null && getCurrent != null)
                    enumeratorTypes[desc] = new EnumeratorMembers(moveNext, getCurrent, dispose);
            }
        }

        // Link script base classes now that every descriptor exists (see pendingBaseLinks).
        foreach (var (bDesc, bTok) in pendingBaseLinks)
            if (typeDefToType.TryGetValue(bTok, out var bBase))
                bDesc.BaseDesc = bBase;
        // Guarantee the BaseDesc graph is ACYCLIC at load, so every runtime chain walk
        // (virtual dispatch, subtype casts, ScriptObject.ToString) can loop without guards.
        // Hostile metadata can encode A : B : A — that must fail the Load, not hang the VM.
        foreach (var (bDesc, _) in pendingBaseLinks)
        {
            int steps = 0;
            for (var d = bDesc.BaseDesc; d != null; d = d.BaseDesc)
                if (++steps > 512)
                    throw new ScriptValidationException($"cyclic base-class chain through '{bDesc.Name}'");
        }

        // Resolve each script-struct field's nested TypeDef token to a descriptor now that every type
        // is built (declaration order isn't guaranteed to be dependency order). Enables Create() to
        // recursively allocate nested value-type fields.
        foreach (var (desc, typeDefs) in pendingStructFields)
        {
            ScriptTypeDescriptor?[]? arr = null;
            for (int i = 0; i < typeDefs.Length; i++)
                if (typeDefs[i] != 0 && typeDefToType.TryGetValue(typeDefs[i], out var sub))
                    (arr ??= new ScriptTypeDescriptor?[typeDefs.Length])[i] = sub;
            desc.FieldStructDescriptors = arr;
        }

        // --- Flat script-struct resolution (zero-GC structs) ---
        // Recursively classify blittable script structs (no reference-frame fields, transitively),
        // inline nested blittable struct FIELDS into the parent's PrimBytes (they were RefSlot
        // ScriptObjects), and synthesize a FlatLayout so blittable structs live in Vt frame slots.
        // Runs after FieldStructDescriptors resolution (needs nested descs) and before methods'
        // flat metadata below.
        {
            var flatVisited = new HashSet<ScriptTypeDescriptor>();
            foreach (var d in typesByName.Values)
                ResolveFlatScriptStruct(d, flatVisited, fieldSTypes, fieldIsScStruct);

            // Method metadata post-pass: locals/args/returns whose script-struct type resolved
            // flat switch from the heap (O + clone_sc) representation to Vt.
            foreach (var m in byToken.Values)
            {
                var ltds = m.LocalScriptStructTypeDefs;
                if (ltds != null)
                    for (int j = 0; j < m.LocalCount && j < ltds.Length; j++)
                        if (ltds[j] != 0 && typeDefToType.TryGetValue(ltds[j], out var ld) && ld.FlatLayout != null)
                        {
                            (m.LocalStructLayouts ??= new HostBinding.StructLayout?[m.LocalCount])[j] = ld.FlatLayout;
                            if (m.LocalIsScriptStruct != null) m.LocalIsScriptStruct[j] = false;
                        }
                var atds = m.ArgScriptStructTypeDefs;
                if (atds != null)
                    for (int j = 0; j < m.ArgSTypes.Length && j < atds.Length; j++)
                        if (atds[j] != 0 && typeDefToType.TryGetValue(atds[j], out var ad) && ad.FlatLayout != null)
                        {
                            m.ArgSTypes[j] = SType.Vt;
                            (m.ArgStructLayouts ??= new HostBinding.StructLayout?[m.ArgSTypes.Length])[j] = ad.FlatLayout;
                        }
                if (m.ReturnScriptStructTypeDef != 0
                    && typeDefToType.TryGetValue(m.ReturnScriptStructTypeDef, out var rd) && rd.FlatLayout != null)
                {
                    m.ReturnSType = SType.Vt;
                    m.ReturnStructLayout = rd.FlatLayout;
                }
                if (!m.IsStatic && m.DeclaringTypeDef != 0
                    && typeDefToType.TryGetValue(m.DeclaringTypeDef, out var dd) && dd.FlatLayout != null)
                    m.ThisStructLayout = dd.FlatLayout;
            }
        }

        // --- Host calls: resolve MemberRef tokens ---
        var hostCalls  = new Dictionary<int, HostEntry>();
        var hostCtors  = new Dictionary<int, HostEntry>();
        var hostFields = new Dictionary<int, HostBinding.FieldEntry>();
        var hostFieldDeclTypeTok = new Dictionary<int, int>();
        var tokenNames = new Dictionary<int, string>();
        var unboundHostMembers = new List<string>();

        foreach (var memberRefHandle in reader.MemberReferences)
        {
            var memberRef  = reader.GetMemberReference(memberRefHandle);
            var memberName = reader.GetString(memberRef.Name);
            int token      = MetadataTokens.GetToken(memberRefHandle);

            string typeName2 = "";
            string typeNs2   = "";
            if (memberRef.Parent.Kind == HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                typeName2 = reader.GetString(typeRef.Name);
                typeNs2   = reader.GetString(typeRef.Namespace);
            }
            else if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
            {
                typeName2 = ResolveTypeSpecName(reader, (TypeSpecificationHandle)memberRef.Parent);
            }

            // Field refs have signature byte 0x06 (FIELD calling convention)
            var sigBytes = reader.GetBlobBytes(memberRef.Signature);
            if (sigBytes.Length > 0 && sigBytes[0] == 0x06)
            {
                if (binding != null && binding.TryGetField($"{typeName2}.{memberName}", out var fe))
                {
                    hostFields[token] = fe!;
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                        hostFieldDeclTypeTok[token] = MetadataTokens.GetToken(memberRef.Parent);
                    // Classify the field type so the IR lowerer can emit ldfld_i4/ldfld_r4. Prefer the
                    // binding's FieldSt (derived from the CLR field type, so it knows an enum is I4); the
                    // signature-byte guess below can't distinguish an enum (0x11 VALUETYPE) from a struct
                    // and would mis-type it O. Fall back to the sig guess only when FieldSt is unset (O).
                    if (fe!.FieldSt != SType.O)
                        fieldSTypes[token] = fe.FieldSt;
                    else if (sigBytes.Length >= 2)
                        fieldSTypes[token] = sigBytes[1] switch
                        {
                            // nint/nuint (0x18/0x19) stay O: boxed IntPtr, no 64-bit truncation
                            0x02 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => SType.I4,
                            0x0C => SType.R4,
                            _ => SType.O,
                        };
                }
                else if (binding != null && memberRef.Parent.Kind == HandleKind.TypeSpecification
                         && ResolveClosedSpecType(reader, (TypeSpecificationHandle)memberRef.Parent, binding) is { } fldDecl
                         && fldDecl.GetField(memberName) is { } specFi)
                {
                    // Field reached through a closed generic instantiation (ValueTuple`2.Item1):
                    // synthesize per token — each instantiation's MemberRef row carries its own
                    // FieldInfo. Reads only on value types: writing through the boxed receiver
                    // would alias every copy of the struct (silent value-semantics break).
                    var specFe = HostBinding.BuildFieldEntry(specFi);
                    if (fldDecl.IsValueType)
                    {
                        var fname = $"{typeName2}.{memberName}";
                        specFe.Set = (_, _2) => throw new ScriptRuntimeException(
                            $"assigning {fname} through a boxed struct is not supported — construct a new value instead");
                    }
                    hostFields[token] = specFe;
                    if (specFe.FieldSt != SType.O)
                        fieldSTypes[token] = specFe.FieldSt;
                }
                else if (binding != null)
                    unboundHostMembers.Add($"{typeName2}.{memberName} (field)");
                continue;
            }

            // Constructor — registered ones become host ctors; others are no-op base inits.
            // System.Object::.ctor() is always the canonical base ctor (script chains to it
            // via `ldarg.0; call .ctor`); force it to noop even if a name-only collision
            // (e.g. UnityEngine.Object also being registered) would otherwise route it to
            // hostCtors. Lookups in `_entries` are unqualified `TypeName..ctor/N`.
            if (memberName == ".ctor")
            {
                int memberParamCount0 = DecompressInt(sigBytes, 1);
                // new Nullable<T>(v) — including the implicit T→T? conversion and `T? x = v;` local
                // inits (ldloca + call .ctor, routed through the Call lowering's hostCtor pattern).
                // The interpreter stores nullables the way CLR boxing does — as the boxed T, or
                // null — so construction is the identity on the single argument.
                if (memberParamCount0 == 1 && typeName2 == "Nullable`1"
                    && memberRef.Parent.Kind == HandleKind.TypeSpecification)
                {
                    var nullableCtor = new HostBinding.Entry
                    {
                        Delegate   = (_, args) => args[0],
                        ParamCount = 1,
                        HasThis    = false,
                    };
                    hostCtors[token] = new HostEntry { Binding = nullableCtor, IsVoid = true };
                    continue;
                }
                // In-script `new List<T>()` / Dictionary / HashSet / Queue / Stack: the shimmed
                // collections dispatch every member on the receiver's runtime type, so construction
                // was the only missing piece. Resolve the TypeSpec to its closed type and bind the
                // ctor whose params decode concretely — the 0-arg and int-capacity overloads; a
                // VAR-typed param (e.g. IEnumerable<T>) fails the decode and stays loud-unsupported.
                if (memberRef.Parent.Kind == HandleKind.TypeSpecification && binding != null
                    && (typeName2 is "List`1" or "Dictionary`2" or "HashSet`1" or "Queue`1" or "Stack`1"
                        || typeName2.StartsWith("ValueTuple`", StringComparison.Ordinal)))
                {
                    var closedType = ResolveClosedSpecType(reader, (TypeSpecificationHandle)memberRef.Parent, binding);
                    ConstructorInfo? ci = null;
                    if (closedType != null)
                    {
                        if (memberParamCount0 == 0)
                            ci = closedType.GetConstructor(Type.EmptyTypes);
                        else if (DecodeMethodSigParamTypes(reader, sigBytes, binding) is { } ctorParams)
                            ci = closedType.GetConstructor(ctorParams);
                        // Tuple ctor params are VARs (!0, !1) — the sig decode can't name them, but
                        // for a CLOSED spec they are exactly the instantiation's type args.
                        if (ci == null && closedType.IsGenericType
                            && closedType.GetGenericArguments() is { } ga && ga.Length == memberParamCount0)
                            ci = closedType.GetConstructor(ga);
                    }
                    if (ci != null)
                    {
                        var ciLocal = ci;
                        var ctorParamTypes = Array.ConvertAll(ci.GetParameters(), p => p.ParameterType);
                        hostCtors[token] = new HostEntry
                        {
                            Binding = new HostBinding.Entry
                            {
                                // I4-slot identity erasure: a char/bool/enum arg arrives boxed as
                                // int; Invoke is strict, so coerce to the declared param type.
                                Delegate = (_, args) =>
                                {
                                    for (int ai = 0; ai < ctorParamTypes.Length; ai++)
                                    {
                                        var pt = ctorParamTypes[ai];
                                        if (args[ai] is int iv && pt != typeof(int) && pt != typeof(object))
                                        {
                                            if (pt == typeof(char)) args[ai] = (char)iv;
                                            else if (pt == typeof(bool)) args[ai] = iv != 0;
                                            else if (pt.IsEnum) args[ai] = Enum.ToObject(pt, iv);
                                            else if (pt == typeof(float)) args[ai] = (float)iv;
                                            else args[ai] = HostBinding.ToFieldIntegral(iv, pt);
                                        }
                                    }
                                    return ciLocal.Invoke(args);
                                },
                                ParamCount = memberParamCount0,
                                HasThis    = false,
                            },
                            IsVoid = true,
                        };
                        continue;
                    }
                }
                bool isSystemObjectCtor = typeNs2 == "System" && typeName2 == "Object" && memberParamCount0 == 0;
                if (!isSystemObjectCtor && binding != null && binding.TryGet($"{typeName2}..ctor", memberParamCount0, out var ctorEntry))
                    hostCtors[token] = new HostEntry { Binding = ctorEntry!, IsVoid = true };
                else
                {
                    var noop = new HostBinding.Entry { Delegate = (_, _) => null, ParamCount = 0, HasThis = true };
                    hostCalls[token] = new HostEntry { Binding = noop, IsVoid = true };
                }
                continue;
            }

            int memberParamCount = DecompressInt(sigBytes, 1);
            var (sigIsVoid, sigRetSType, _) = SigReturn(sigBytes);

            tokenNames[token] = $"{typeName2}.{memberName}/{memberParamCount}";

            // System.Object's members are intrinsics dispatched virtually on the receiver.
            // Resolving them through the binding by SHORT name is wrong: "Object" collides with
            // UnityEngine.Object in the standard surface, so a `constrained.` callvirt (e.g.
            // `"impact" + quaternion` → Object.ToString on the boxed struct) invoked
            // UnityEngine.Object.ToString with a foreign receiver — a TargetException the call
            // site remapped to a misleading "called Object.ToString/0 on a null object".
            // The namespace check mirrors the System.Object ctor special case above.
            if (typeNs2 == "System" && typeName2 == "Object"
                && memberRef.Parent.Kind == HandleKind.TypeReference)
            {
                bool objHasThis = sigBytes.Length > 0 && (sigBytes[0] & 0x20) != 0;
                HostBinding.Entry objIntrinsic = memberName switch
                {
                    "ToString" when objHasThis && memberParamCount == 0 => new HostBinding.Entry
                    {
                        Delegate = (recv, _) => recv != null ? recv.ToString()
                            : throw new ScriptRuntimeException("NullReferenceException: called Object.ToString on a null object"),
                        ParamCount = 0, HasThis = true,
                    },
                    "GetHashCode" when objHasThis && memberParamCount == 0 => new HostBinding.Entry
                    {
                        Delegate = (recv, _) => recv != null ? recv.GetHashCode()
                            : throw new ScriptRuntimeException("NullReferenceException: called Object.GetHashCode on a null object"),
                        ParamCount = 0, HasThis = true,
                    },
                    "GetType" when objHasThis && memberParamCount == 0 => new HostBinding.Entry
                    {
                        Delegate = (recv, _) => recv != null ? recv.GetType()
                            : throw new ScriptRuntimeException("NullReferenceException: called Object.GetType on a null object"),
                        ParamCount = 0, HasThis = true,
                    },
                    "Equals" when objHasThis && memberParamCount == 1 => new HostBinding.Entry
                    {
                        Delegate = (recv, args) => recv != null ? recv.Equals(args![0])
                            : throw new ScriptRuntimeException("NullReferenceException: called Object.Equals on a null object"),
                        ParamCount = 1, HasThis = true,
                    },
                    "Equals" when !objHasThis && memberParamCount == 2 => new HostBinding.Entry
                    { Delegate = (_, args) => Equals(args![0], args[1]), ParamCount = 2, HasThis = false },
                    "ReferenceEquals" when !objHasThis && memberParamCount == 2 => new HostBinding.Entry
                    { Delegate = (_, args) => ReferenceEquals(args![0], args[1]), ParamCount = 2, HasThis = false },
                    _ => null!,
                };
                if (objIntrinsic != null)
                {
                    hostCalls[token] = new HostEntry
                    { Binding = objIntrinsic, IsVoid = false, SigRetSType = sigRetSType };
                    // `base.ToString()` inside a script override is a plain `call` on this token. The
                    // virtual intrinsic above would land in ScriptObject.ToString and re-enter the
                    // override forever; the statically bound answer is Object's default, the type name.
                    // The plain-call lowering addresses this entry via NonVirtualCallBit.
                    if (memberName == "ToString" && objHasThis && memberParamCount == 0)
                    {
                        hostCalls[token | NonVirtualCallBit] = new HostEntry
                        {
                            Binding = new HostBinding.Entry
                            {
                                Delegate = (recv, _) => recv is ScriptObject so ? so.Type.Name : recv != null ? recv.ToString()
                                    : throw new ScriptRuntimeException("NullReferenceException: called Object.ToString on a null object"),
                                ParamCount = 0, HasThis = true,
                            },
                            IsVoid = false, SigRetSType = sigRetSType,
                        };
                        tokenNames[token | NonVirtualCallBit] = "Object.ToString/0 (base call)";
                    }
                    continue;
                }
            }

            // VM intrinsic: IlInterpreter.Vm.Log → route to per-VM logSink.
            if (typeNs2 == "IlInterpreter" && typeName2 == "Vm" && memberName == "Log" && memberParamCount == 1)
            {
                var entry = new HostBinding.Entry
                {
                    Delegate   = (_, args) => { logSink((string)args[0]!); return null; },
                    ParamCount = 1,
                    HasThis    = false,
                };
                hostCalls[token] = new HostEntry { Binding = entry, IsVoid = true };
                continue;
            }

            // System.Nullable<T> members are intrinsics. The interpreter stores nullables the way
            // CLR boxing does — as the boxed T, or null — so HasValue/Value/GetValueOrDefault
            // reduce to null checks on the receiver slot's boxed value (the ldloca/ldflda receiver
            // phantom resolves to that boxed value before the call).
            if (typeName2 == "Nullable`1" && memberRef.Parent.Kind == HandleKind.TypeSpecification)
            {
                var (_, nullableArg) = DecodeGenericInstHeader(reader, (TypeSpecificationHandle)memberRef.Parent);
                var nullableEntry = MakeNullableIntrinsic(memberName, memberParamCount, nullableArg, binding);
                if (nullableEntry != null)
                {
                    // Type the result slot from the RESOLVED type arg (not the open sig, which says
                    // bool for HasValue but generic T for Value/GetValueOrDefault -> O). get_HasValue
                    // -> I4 (bool); Value/GetValueOrDefault -> the type arg's SType. Without this the
                    // slot defaulted to O (boxed) and numeric ops on the result used object compares
                    // (found by fuzzing: `q != null` boxes, and `0 != q.GetValueOrDefault()` too).
                    hostCalls[token] = new HostEntry
                    {
                        Binding = nullableEntry,
                        IsVoid = false,
                        SigRetSType = NullableIntrinsicResultSType(memberName, nullableArg),
                    };
                    continue;
                }
                // Unmodeled members (ToString, GetHashCode, Equals) fall through to the normal
                // resolution/stub path.
            }

            // TypeSpec: attempt MethodHandle-based resolution for generic types
            if (memberRef.Parent.Kind == HandleKind.TypeSpecification && binding != null)
            {
                var (outerName, arg0Name) = DecodeGenericInstHeader(
                    reader, (TypeSpecificationHandle)memberRef.Parent);
                if (outerName != "" && arg0Name != "" &&
                    (binding.TryGetGenericType(outerName, arg0Name, out var genType)
                     || binding.TryMakeClosedGenericType(outerName, arg0Name, out genType)))
                {
                    var mi = FindMethod(genType!, memberName, memberParamCount);
                    if (mi != null && binding.TryGetByHandle(mi.MethodHandle.Value, out var he))
                    {
                        hostCalls[token] = new HostEntry { Binding = he!, IsVoid = sigIsVoid, ResolvedMethod = mi };
                        continue;
                    }
                    // Mono: MethodHandle.Value for closed generic instantiations may differ
                    // between registration time and load time, causing TryGetByHandle to miss.
                    // Synthesize an Entry directly from the MethodInfo so the call still works.
                    if (mi != null)
                    {
                        hostCalls[token] = FallbackEntry(mi, sigIsVoid,
                            $"[IlInterpreter] TypeSpec handle fallback: {typeName2}.{memberName}/{memberParamCount}", logSink);
                        continue;
                    }
                }
            }

            // Signature-based resolution: TypeRef parent + registered type → decode full param types → GetMethod
            if (memberRef.Parent.Kind == HandleKind.TypeReference && binding != null &&
                TryResolveReceiverTypeRef(reader, (TypeReferenceHandle)memberRef.Parent, typeName2,
                    binding, out var hostType2) && hostType2 != null)
            {
                var paramTypes = DecodeMethodSigParamTypes(reader, sigBytes, binding);
                MethodInfo? mi2 = null;
                if (paramTypes != null)
                {
                    // Manual exact-signature scan rather than GetMethod: DefaultBinder ignores
                    // generic-ness, so a generic twin with the same parameter list (Go(string,
                    // object[]) vs Go<T>(string, object[])) throws AmbiguousMatchException, and
                    // `new`-hiding across the hierarchy under Public|NonPublic does too. A plain
                    // MemberRef never targets a generic method definition (those calls arrive via
                    // MethodSpec), so skip them outright; GetMethods lists most-derived first, so
                    // a `new`-hidden pair resolves to the hiding member like a normal call would.
                    foreach (var m in hostType2.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    {
                        if (m.Name != memberName || m.IsGenericMethodDefinition) continue;
                        var ps = m.GetParameters();
                        if (ps.Length != paramTypes.Length) continue;
                        bool exact = true;
                        for (int i = 0; i < ps.Length; i++)
                            if (ps[i].ParameterType != paramTypes[i]) { exact = false; break; }
                        if (exact) { mi2 = m; break; }
                    }
                }
                // Fallback: if sig decode failed (e.g. unregistered VALUETYPE param) and EXACTLY ONE
                // non-generic method with this name+arity exists on the registered type, use it
                // unambiguously. Multiple candidates → bail; the string-based fallback or a runtime
                // error is safer than a silent mispick. Generic method definitions never count:
                // a plain MemberRef row can't be their call site (those go through MethodSpec).
                if (mi2 == null)
                {
                    MethodInfo? unique = null;
                    foreach (var m in hostType2.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    {
                        if (m.Name != memberName || m.IsGenericMethodDefinition ||
                            m.GetParameters().Length != memberParamCount) continue;
                        if (unique != null) { unique = null; break; } // ambiguous
                        unique = m;
                    }
                    mi2 = unique;
                }
                if (mi2 != null && binding.TryGetByHandle(mi2.MethodHandle.Value, out var he2))
                {
                    hostCalls[token] = new HostEntry { Binding = he2!, IsVoid = sigIsVoid, ResolvedMethod = mi2, ReceiverBox = ReceiverBoxFor(typeName2) };
                    continue;
                }
                // Mono/IL2CPP: MethodHandle.Value may differ between registration and load time.
                // When TryGetByHandle misses but the MethodInfo is unambiguous, synthesize an
                // Entry directly — the string-keyed fallback only covers properties/operators.
                if (mi2 != null)
                {
                    hostCalls[token] = FallbackEntry(mi2, sigIsVoid,
                        $"[IlInterpreter] TypeRef handle fallback: {typeName2}.{memberName}/{memberParamCount}", logSink);
                    continue;
                }
            }

            // String-based fallback (properties, operators, AllowBcl, hand-wired Allow/AllowStatic)
            if (binding != null && binding.TryGet($"{typeName2}.{memberName}", memberParamCount, out var entry2))
            {
                // A TypeSpec member returning the generic VAR `!n` (Func`N.Invoke's TResult,
                // List<T>.get_Item, Enumerator.get_Current, KeyValuePair Key/Value, …) types O
                // from the open signature — but `weights[0] + weights[2]` on a List<float> then
                // lowered add_i4 over boxed floats (sum 0), and `switch (s)` over a List<Shape>
                // element lowered brfalse_o on a boxed 0 (case silently missed). Recover the
                // CLOSED return SType from the instantiation's n-th type argument — enums
                // included (their values ride I4 like everywhere else).
                var invRetSt = sigRetSType;
                if (memberRef.Parent.Kind == HandleKind.TypeSpecification && invRetSt == SType.O)
                {
                    var d = GenericVarReturnSType(reader,
                        (TypeSpecificationHandle)memberRef.Parent, sigBytes, binding);
                    if (d != SType.O) invRetSt = d;
                }
                hostCalls[token] = new HostEntry { Binding = entry2!, IsVoid = sigIsVoid, SigRetSType = invRetSt, ReceiverBox = ReceiverBoxFor(typeName2),
                                                   UnsignedParams = UnsignedParamKinds(sigBytes) };
            }
            else
            {
                // Unregistered external call: stub that throws at runtime, so the method still
                // lowers and the gap is reported in UnboundHostMembers at load time.
                //
                // EXCEPT open generic rows (GENERIC bit set on the calling convention): IL calls
                // generic methods through MethodSpec tokens, which resolve — and report real gaps
                // as 'Type.Name<T>/N' — in the pass below. The open MemberRef row is only their
                // metadata parent; reporting it too flagged every generic call as unbound
                // ("Component.GetComponent/1") even when all instantiations resolved fine.
                string msgKey = $"{typeName2}.{memberName}";
                bool isOpenGenericRow = sigBytes.Length > 0 && (sigBytes[0] & 0x10) != 0;
                if (!isOpenGenericRow)
                    unboundHostMembers.Add($"{msgKey}/{memberParamCount}");
                var stub = new HostBinding.Entry
                {
                    Delegate   = (_, _) => throw new ScriptRuntimeException($"Host method '{msgKey}' is not registered in the binding"),
                    ParamCount = memberParamCount,
                    HasThis    = sigBytes.Length > 0 && (sigBytes[0] & 0x20) != 0,
                };
                hostCalls[token] = new HostEntry { Binding = stub, IsVoid = sigIsVoid };
            }
        }

        // --- MethodSpec resolution: closed generic method calls (e.g. AddComponent<Rigidbody>) ---
        // For each MethodSpec token, decode (open def, type args), instantiate via MakeGenericMethod,
        // and register the resulting closed method under the methodspec token.
        int msCount = reader.GetTableRowCount(TableIndex.MethodSpec);
        for (int msRow = 1; msRow <= msCount; msRow++)
        {
            var msHandle = MetadataTokens.MethodSpecificationHandle(msRow);
            var ms       = reader.GetMethodSpecification(msHandle);
            int token = MetadataTokens.GetToken(msHandle);

            // Parent for host generic methods is always a MemberRef (script-defined open generics
            // are banned by the validator).
            if (ms.Method.Kind != HandleKind.MemberReference) continue;
            var parentRef = reader.GetMemberReference((MemberReferenceHandle)ms.Method);
            if (parentRef.Parent.Kind != HandleKind.TypeReference) continue;

            var parentTypeRef = reader.GetTypeReference((TypeReferenceHandle)parentRef.Parent);
            var parentType    = reader.GetString(parentTypeRef.Name);
            var parentName    = reader.GetString(parentRef.Name);

            // Generic method signature: [conv-byte][genParamCount][paramCount]...
            // The GENERIC bit (0x10) must be set on the calling-convention byte.
            var parentSig = reader.GetBlobBytes(parentRef.Signature);
            if (parentSig.Length < 3) continue;
            int sigIdx = 0;
            byte conv = parentSig[sigIdx++];
            if ((conv & 0x10) == 0) continue;
            DecompressIntAdv(parentSig, ref sigIdx); // skip genParamCount
            int paramCount = DecompressIntAdv(parentSig, ref sigIdx);

            if (binding == null) continue;
            if (!binding.TryGetOpenGenerics(parentType, parentName, paramCount, out var openDefs))
            {
                // Unregistered open generic: insert a runtime stub so the lowerer can still lower
                // the method. The stub throws at runtime if the call is actually reached.
                string stubKey = $"{parentType}.{parentName}<T>/{paramCount}";
                unboundHostMembers.Add(stubKey);
                bool hasThisStub = (conv & 0x20) != 0;
                var stub2 = new HostBinding.Entry
                {
                    Delegate   = (_, _) => throw new ScriptRuntimeException($"Host generic method '{stubKey}' is not in the binding allowlist"),
                    ParamCount = paramCount,
                    HasThis    = hasThisStub,
                };
                hostCalls[token] = new HostEntry { Binding = stub2, IsVoid = false };
                continue;
            }

            var typeArgs = DecodeMethodSpecArgs(reader, ms.Signature, binding, token);
            // Several open overloads can share (type, name, arity) — Object.Instantiate<T> has both
            // (T, Transform, bool) and (T, Vector3, Quaternion). Pick by matching the MemberRef's
            // parameter signature (sigIdx points at the return type here); a mispick invokes the
            // wrong host method with type-mismatched args.
            var openDef = openDefs!.Count == 1
                ? openDefs[0]
                : SelectOpenGenericOverload(reader, openDefs!, parentSig, sigIdx, typeArgs);
            if (openDef == null)
            {
                string stubKey = $"{parentType}.{parentName}<T>/{paramCount}";
                unboundHostMembers.Add(stubKey + " (ambiguous overloads)");
                bool hasThisStub2 = (conv & 0x20) != 0;
                var stub3 = new HostBinding.Entry
                {
                    Delegate   = (_, _) => throw new ScriptRuntimeException($"Host generic method '{stubKey}' has multiple overloads and none matched the call signature"),
                    ParamCount = paramCount,
                    HasThis    = hasThisStub2,
                };
                hostCalls[token] = new HostEntry { Binding = stub3, IsVoid = false };
                continue;
            }
            var closed   = binding.GetOrMakeClosedMethod(openDef!, typeArgs);
            bool hasThis = !openDef!.IsStatic;
            var ps       = closed.GetParameters();
            var msEntry  = new HostBinding.Entry
            {
                Delegate   = (recv, args) => closed.Invoke(recv, args),
                ParamCount = ps.Length,
                HasThis    = hasThis,
                Params     = ps,
            };
            // ResolvedMethod = the CLOSED generic method (e.g. Echo<int>): its ReturnType is the
            // concrete instantiation (int), so the call-site lowering types the result slot I4/R4
            // instead of defaulting to O. Without it a generic call returning int/float/bool boxed
            // its result, and a numeric use (e.g. `Echo<int>(x) != Echo<int>(x)`) reference-compared
            // two boxes (found by fuzzing the MethodSpec path).
            hostCalls[token] = new HostEntry
            {
                Binding = msEntry,
                IsVoid = closed.ReturnType == typeof(void),
                ResolvedMethod = closed as MethodInfo,
            };
        }

        // --- Token → Type map: resolve TypeRef/TypeSpec tokens for castclass/isinst/ldtoken ---
        var tokenTypes = new Dictionary<int, Type>();
        if (binding != null)
        {
            foreach (var trHandle in reader.TypeReferences)
            {
                var tr    = reader.GetTypeReference(trHandle);
                var name  = reader.GetString(tr.Name);
                int tok   = MetadataTokens.GetToken(trHandle);
                // System primitives, so unbox.any is type-exact (a boxed char can't unbox to int),
                // and isinst/castclass/typeof work on boxed primitives (box_prim gives them the real
                // runtime type). The binding doesn't register these as host types.
                Type? primType = reader.GetString(tr.Namespace) == "System" ? name switch
                {
                    "Int32" => typeof(int), "Boolean" => typeof(bool), "Char" => typeof(char),
                    "Byte" => typeof(byte), "SByte" => typeof(sbyte), "Int16" => typeof(short),
                    "UInt16" => typeof(ushort), "UInt32" => typeof(uint), "Int64" => typeof(long),
                    "UInt64" => typeof(ulong), "Single" => typeof(float), "Double" => typeof(double),
                    _ => null,
                } : null;
                if (primType != null)
                    tokenTypes[tok] = primType;
                else if (binding.TryGetTypeByName(name, out var t) && t != null)
                    tokenTypes[tok] = t;
            }
            int typeSpecCount = reader.GetTableRowCount(TableIndex.TypeSpec);
            for (int tsRow = 1; tsRow <= typeSpecCount; tsRow++)
            {
                var tsHandle = MetadataTokens.TypeSpecificationHandle(tsRow);
                int tok      = MetadataTokens.GetToken(tsHandle);
                var name     = ResolveTypeSpecName(reader, tsHandle);
                if (name != "" && binding.TryGetTypeByName(name, out var t) && t != null)
                    tokenTypes[tok] = t;
            }

            // `new S { x = 1 }` / `default(S)` on a BOXED host struct local (AllowType, the
            // auto-bind default) lowers to `ldloca; initobj S; ldloca; stfld x`. The O slot has
            // no box yet, and stfld on null threw "Non-static field requires a target" (found
            // live in CubeFlipr's Update). Give initobj a 0-arg "ctor" keyed by the TYPE token
            // — disjoint from the MemberRef tokens real ctors use — so the lowerer can emit
            // newobj_host to produce the default box for the following stores to mutate in
            // place. Nullable<T> keeps the ldnull path (null IS its representation here);
            // primitives/enums live in numeric slots; flat structs never reach the O-slot path.
            foreach (var kv in tokenTypes)
            {
                var vt = kv.Value;
                if (!vt.IsValueType || vt.IsPrimitive || vt.IsEnum || Nullable.GetUnderlyingType(vt) != null)
                    continue;
                if (hostCtors.ContainsKey(kv.Key)) continue;
                var vtLocal = vt;
                hostCtors[kv.Key] = new HostEntry
                {
                    Binding = new HostBinding.Entry
                    {
                        Delegate = (_, _) => Activator.CreateInstance(vtLocal),
                        ParamCount = 0, HasThis = false,
                    },
                    IsVoid = true,
                };
            }
        }

        // --- Embedded PDB: read sequence points for source-mapped errors ---
        MetadataReaderProvider? pdbProvider = null;
        foreach (var entry in pe.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                pdbProvider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                break;
            }
        }
        if (pdbProvider != null)
        {
            var pdbReader = pdbProvider.GetMetadataReader();
            // Document handle → short file name, resolved once per document. With #line directives
            // in the compiled source (the method-reload transform emits them), documents are the USER's
            // files, so the name makes error locations self-locating: " at line 394 (TankAI.cs)".
            var docNames = new Dictionary<DocumentHandle, string>();
            foreach (var handle in reader.MethodDefinitions)
            {
                int token = MetadataTokens.GetToken(handle);
                if (!byToken.TryGetValue(token, out var pm)) continue;
                int row = MetadataTokens.GetRowNumber(handle);
                var debugInfo = pdbReader.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(row));
                if (debugInfo.SequencePointsBlob.IsNil) continue;
                var points = new List<(int, int, string)>();
                foreach (var sp in debugInfo.GetSequencePoints())
                {
                    if (sp.IsHidden) continue;
                    if (!docNames.TryGetValue(sp.Document, out var doc))
                    {
                        var fullName = sp.Document.IsNil ? "" : pdbReader.GetString(pdbReader.GetDocument(sp.Document).Name);
                        doc = Path.GetFileName(fullName);
                        docNames[sp.Document] = doc;
                    }
                    points.Add((sp.Offset, sp.StartLine, doc));
                }
                if (points.Count > 0)
                    pm.SeqPoints = points.ToArray();
            }
        }

        // Resolve flat-struct (Vt) layouts for every host call / host ctor whose params,
        // return, or receiver are registered struct types. Done in one sweep at the end
        // so the per-site HostEntry construction code stays unchanged.
        foreach (var he in hostCalls.Values) FillStructInfo(he, binding, isCtor: false);
        foreach (var he in hostCtors.Values) FillStructInfo(he, binding, isCtor: true);

        return new ParsedAssembly
        {
            ByToken     = byToken,
            ByName      = byName,
            HostCalls   = hostCalls,
            HostCtors   = hostCtors,
            HostFieldDeclTypeTok = hostFieldDeclTypeTok,
            HostFields  = hostFields,
            TokenNames  = tokenNames,
            FieldSlots     = fieldSlots,
            FieldSTypes    = fieldSTypes,
            FieldWide      = fieldWide,
            FieldIsScriptStruct = fieldIsScStruct,
            FieldToTypeDef = fieldToTypeDef,
            CtorToType       = ctorToType,
            TypeDefToCtorTok = typeDefToCtorTok,
            TypeDefToType    = typeDefToType,
            TypesByName = typesByName,
            EnumeratorTypes = enumeratorTypes,
            TokenTypes  = tokenTypes,
            Reader      = reader,
            Pe          = pe,
            PdbProvider = pdbProvider,
            UnboundHostMembers = unboundHostMembers,
            HostSurface = binding,
        };
    }

    // Build an intrinsic Entry for a System.Nullable<T> member. The receiver arrives as the slot's
    // boxed value — boxed T or null, the CLR boxed representation of a nullable — so the semantics
    // are plain null checks. Returns null for members that aren't modeled (ToString, GetHashCode,
    // Equals), which then resolve or stub through the normal path.
    static HostBinding.Entry? MakeNullableIntrinsic(string memberName, int paramCount, string typeArgName, HostBinding? binding)
    {
        switch (memberName)
        {
            case "get_HasValue" when paramCount == 0:
                return new HostBinding.Entry
                { Delegate = (recv, _) => recv != null, ParamCount = 0, HasThis = true };

            case "get_Value" when paramCount == 0:
                return new HostBinding.Entry
                {
                    Delegate = (recv, _) => recv ?? throw new ScriptRuntimeException(
                        "InvalidOperationException: Nullable object must have a value"),
                    ParamCount = 0,
                    HasThis   = true,
                };

            case "GetValueOrDefault" when paramCount == 0:
            {
                // default(T), boxed once at load time. Stays null when T can't be resolved — safe
                // for the compiler-generated uses (`x ?? y`, `x?.M()` evaluate GetValueOrDefault
                // only alongside a HasValue guard); a hand-written call on an unresolved T yields
                // null, which downstream O-slot ops treat as default anyway.
                object? boxedDefault = null;
                var t = ResolveNullableTypeArg(typeArgName, binding);
                if (t != null && t.IsValueType)
                    try { boxedDefault = HostBinding.NormalizeIntegralReturn(Activator.CreateInstance(t)); } catch { /* keep null */ }
                return new HostBinding.Entry
                { Delegate = (recv, _) => recv ?? boxedDefault, ParamCount = 0, HasThis = true };
            }

            case "GetValueOrDefault" when paramCount == 1:
                return new HostBinding.Entry
                { Delegate = (recv, args) => recv ?? args[0], ParamCount = 1, HasThis = true };

            default:
                return null;
        }
    }

    // Result SType for a Nullable<T> intrinsic. get_HasValue returns bool (I4); get_Value and
    // GetValueOrDefault return T, whose SType comes from the RESOLVED type arg — the open signature
    // says T (which SigReturn can only classify as O), so without this the result lands in a boxed
    // O slot and a numeric comparison like `0 != n.GetValueOrDefault()` lowers to an OBJECT compare
    // (reference-unequal) instead of an int compare (found by fuzzing).
    static SType NullableIntrinsicResultSType(string memberName, string typeArgName)
    {
        if (memberName == "get_HasValue") return SType.I4; // bool
        switch (typeArgName)
        {
            case "Boolean": case "Char": case "SByte": case "Byte":
            case "Int16": case "UInt16": case "Int32": case "UInt32":
                return SType.I4;
            case "Single":
                return SType.R4;
            // Wide type args ride wide (two-cell) slots.
            case "Double":
                return SType.R8;
            case "Int64": case "UInt64":
                return SType.I8;
            default:
                return SType.O; // enums/ref types stay boxed
        }
    }

    // Resolve a Nullable<T> type-arg name (as produced by DecodeGenericInstHeader) to a Type:
    // CLR primitive names first, then any binding-registered type.
    static Type? ResolveNullableTypeArg(string name, HostBinding? binding)
    {
        switch (name)
        {
            case "Boolean": return typeof(bool);
            case "Char":    return typeof(char);
            case "SByte":   return typeof(sbyte);
            case "Byte":    return typeof(byte);
            case "Int16":   return typeof(short);
            case "UInt16":  return typeof(ushort);
            case "Int32":   return typeof(int);
            case "UInt32":  return typeof(uint);
            case "Int64":   return typeof(long);
            case "UInt64":  return typeof(ulong);
            case "Single":  return typeof(float);
            case "Double":  return typeof(double);
        }
        return binding != null && binding.TryGetTypeByName(name, out var t) ? t : null;
    }

    // Build a HostEntry from a MethodInfo that slipped through TryGetByHandle (Mono/IL2CPP handle instability).
    static HostEntry FallbackEntry(MethodInfo mi, bool isVoid, string label, Action<string> logSink)
    {
        logSink(label);
        var captured = mi;
        var mp = mi.GetParameters();
        var entry = new HostBinding.Entry
        {
            Delegate   = mi.IsStatic
                ? (_, args) => captured.Invoke(null, args)
                : (recv, args) => captured.Invoke(recv, args),
            ParamCount = mp.Length,
            HasThis    = !mi.IsStatic,
            Params     = mp,
        };
        return new HostEntry { Binding = entry, IsVoid = isVoid };
    }

    // Create one instance of the script's "Script" class and run its default .ctor.
    // The ctor runs on the Vm (the sole engine) via _parsed, which Load() has already set.
    ScriptObject? MakeInstance(ParsedAssembly asm)
    {
        if (!asm.TypesByName.TryGetValue("Script", out var desc)) return null;
        var obj = ScriptObject.Create(desc);
        // Find the default .ctor (ArgCount == 1 means just 'this')
        foreach (var (tok, type) in asm.CtorToType)
        {
            if (type == desc && asm.ByToken.TryGetValue(tok, out var ctor) && ctor.ArgCount == 1)
            {
                (_vm ??= new Vm(this)).Invoke(ctor, new object?[] { obj }); // result discarded (ctor)
                break;
            }
        }
        return obj;
    }

    // Recursive flat classification for script-defined structs (see the Parse call site).
    // Relayouts a type in place when nested blittable struct fields move inline, updates the
    // assembly-level per-field-token maps, and synthesizes FlatLayout for blittable structs
    // (Size = PrimByteSize; box form = a ScriptObject whose PrimBytes IS the flat image).
    static unsafe void ResolveFlatScriptStruct(ScriptTypeDescriptor desc,
        HashSet<ScriptTypeDescriptor> visited,
        Dictionary<int, SType> fieldSTypes, HashSet<int> fieldIsScStruct)
    {
        if (!visited.Add(desc)) return;
        var nested = desc.FieldStructDescriptors;
        if (nested != null)
            foreach (var nd in nested)
                if (nd != null) ResolveFlatScriptStruct(nd, visited, fieldSTypes, fieldIsScStruct);

        bool anyInline = false;
        if (desc.FieldIsScriptStruct != null && nested != null)
            for (int i = 0; i < desc.FieldCount; i++)
                if (desc.FieldIsScriptStruct[i] && i < nested.Length && nested[i]?.FlatLayout != null)
                { anyInline = true; break; }

        if (anyInline)
        {
            int prim = 0, refs = 0;
            for (int i = 0; i < desc.FieldCount; i++)
            {
                bool inline = desc.FieldIsScriptStruct != null && desc.FieldIsScriptStruct[i]
                              && nested != null && i < nested.Length && nested[i]?.FlatLayout != null;
                var ft = desc.FieldTypes[i];
                if (inline)
                {
                    var flay = nested![i]!.FlatLayout!;
                    prim = (prim + 3) & ~3;
                    desc.FieldOffsets[i] = prim;
                    prim += (flay.Size + 3) & ~3;
                    desc.FieldTypes[i] = SType.Vt;
                    (desc.VtFieldLayouts ??= new HostBinding.StructLayout?[desc.FieldCount])[i] = flay;
                    desc.FieldIsScriptStruct![i] = false;
                    nested![i] = null;
                }
                else if (ft == SType.I4 || ft == SType.R4)
                {
                    desc.FieldOffsets[i] = prim; prim += 4;
                }
                else if (ft == SType.Vt)
                {
                    var lay = desc.VtFieldLayouts![i]!;
                    prim = (prim + 3) & ~3;
                    desc.FieldOffsets[i] = prim;
                    prim += (lay.Size + 3) & ~3;
                }
                else
                {
                    desc.FieldOffsets[i] = refs++;
                }
            }
            desc.PrimByteSize = prim;
            desc.RefSlotCount = refs;
            foreach (var kv in desc.FieldSlots)
            {
                fieldSTypes[kv.Key] = desc.FieldTypes[kv.Value];
                if (desc.FieldTypes[kv.Value] == SType.Vt) fieldIsScStruct.Remove(kv.Key);
            }
        }

        if (desc.IsScriptStructValue && desc.RefSlotCount == 0 && desc.FlatLayout == null
            // A local function's captured environment is a display STRUCT the caller mutates
            // in place and passes BYREF (`call Scale(x, ref V_0)`). Flattening it splits the
            // representation — caller writes Vt frame bytes, callee reads a ScriptObject —
            // so the environment must stay ScriptObject-backed and shared by reference.
            && !desc.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
        {
            int copyLen = desc.PrimByteSize;
            int size = copyLen == 0 ? 4 : copyLen; // empty struct still occupies one slot
            var d = desc;
            HostBinding.BoxFromPtrDelegate box = src =>
            {
                var so = ScriptObject.Create(d);
                if (copyLen > 0) Marshal.Copy((IntPtr)src, so.PrimBytes, 0, copyLen);
                return so;
            };
            HostBinding.CopyToPtrDelegate copy = (dst, boxed) =>
            {
                var so = (ScriptObject)boxed;
                int n = so.PrimBytes.Length < copyLen ? so.PrimBytes.Length : copyLen;
                if (n > 0) Marshal.Copy(so.PrimBytes, 0, (IntPtr)dst, n);
            };
            // Fields is scanned BY OFFSET (host-call receiver materialization for primitives
            // inside flat struct slots — `v.f0.ToString()`); metadata field names aren't retained
            // on the descriptor, so keys are synthetic. Nested flat structs contribute their
            // primitives at composed offsets (their own layouts were built first, bottom-up).
            var flatFields = new Dictionary<string, (int, SType)>(desc.FieldCount);
            for (int i = 0; i < desc.FieldCount; i++)
            {
                if (desc.FieldTypes[i] is SType.I4 or SType.R4)
                    flatFields["#" + i] = (desc.FieldOffsets[i], desc.FieldTypes[i]);
                else if (desc.FieldTypes[i] == SType.Vt && desc.VtFieldLayouts?[i] is { } nlay)
                    foreach (var kv in nlay.Fields)
                        flatFields[$"#{i}.{kv.Key}"] = (desc.FieldOffsets[i] + kv.Value.Offset, kv.Value.St);
            }
            desc.FlatLayout = new HostBinding.StructLayout
            {
                Type     = typeof(ScriptObject),
                Size     = size,
                TypeName = desc.Name,
                Fields   = flatFields,
                BoxFromPtr = box,
                CopyToPtr  = copy,
            };
        }
    }

    static int ReadLocalCount(MetadataReader reader, StandaloneSignatureHandle sig)
    {
        if (sig.IsNil) return 0;
        var blob = reader.GetBlobBytes(reader.GetStandaloneSignature(sig).Signature);
        if (blob.Length < 2 || blob[0] != 0x07) return 0; // 0x07 = LOCAL_SIG marker
        int count = DecompressInt(blob, 1);
        // Every local needs at least one byte of type signature, so a well-formed LOCAL_SIG can't
        // declare more locals than the blob has bytes. A larger count is a lying/hostile signature
        // that would otherwise size a per-local array from it and OOM in Parse.
        if ((uint)count > (uint)blob.Length)
            throw new ScriptValidationException(
                $"LOCAL_SIG declares {count} locals but the signature blob is only {blob.Length} bytes — malformed metadata");
        return count;
    }

    // Decode each local's struct layout from the LOCAL_SIG blob. Returns null when
    // none are structs registered with the binding (saves an array allocation per method).
    // LOCAL_SIG = 0x07 [count] (type)*. Per-local: optional CMOD/PINNED/BYREF prefixes,
    // then ELEMENT_TYPE_VALUETYPE (0x11) + coded TypeDefOrRef → look up via TypeRef name.
    static HostBinding.StructLayout?[]? ReadLocalStructLayouts(
        MetadataReader reader, StandaloneSignatureHandle sig, int localCount, HostBinding? binding)
    {
        if (sig.IsNil || binding == null || localCount == 0) return null;
        var blob = reader.GetBlobBytes(reader.GetStandaloneSignature(sig).Signature);
        if (blob.Length < 2 || blob[0] != 0x07) return null;
        int idx = 1;
        DecompressIntAdv(blob, ref idx); // count (already known)
        var result = new HostBinding.StructLayout?[localCount];
        bool any = false;
        for (int i = 0; i < localCount && idx < blob.Length; i++)
        {
            // Skip CMOD_REQD (0x1F), CMOD_OPT (0x20), PINNED (0x45), BYREF (0x10)
            while (idx < blob.Length && (blob[idx] == 0x1F || blob[idx] == 0x20 || blob[idx] == 0x45 || blob[idx] == 0x10))
            {
                byte tag = blob[idx++];
                if (tag == 0x1F || tag == 0x20) DecompressIntAdv(blob, ref idx);
            }
            if (idx >= blob.Length) break;
            byte t = blob[idx++];
            if (t == 0x11) // VALUETYPE
            {
                int coded = DecompressIntAdv(blob, ref idx);
                int table = coded & 0x03; int row = coded >> 2;
                if (table == 1) // TypeRef
                {
                    var name = reader.GetString(reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(row)).Name);
                    if (binding.TryGetStructLayout(name, out var lay)) { result[i] = lay; any = true; }
                }
            }
            else
            {
                // Skip nested types (SZARRAY 0x1D, GENERICINST 0x15) — none are structs we track.
                // Primitive types (I4/R4/...) — just consume the single tag byte (already done).
                if (t == 0x12) DecompressIntAdv(blob, ref idx); // CLASS — also consumes coded index
                else if (t == 0x1D) SkipSigType(blob, ref idx);  // SZARRAY → recurse
                else if (t == 0x15)
                {
                    // GENERICINST: 0x11/0x12, coded index, arity, args. Skip conservatively.
                    if (idx < blob.Length) idx++; // value/class flag
                    if (idx < blob.Length) DecompressIntAdv(blob, ref idx);
                    if (idx < blob.Length)
                    {
                        int n = DecompressIntAdv(blob, ref idx);
                        for (int j = 0; j < n; j++) SkipSigType(blob, ref idx);
                    }
                }
            }
        }
        return any ? result : null;
    }

    // Per-local DECLARED SType from the LocalVarSig (see ParsedMethod.LocalSTypes). Walks the blob
    // exactly like ReadLocalStructLayouts to stay byte-aligned, but classifies each local into the
    // FROZEN set — only the unambiguous, VM-consistent declared types. Anything else (valuetype/enum,
    // long/ulong, double, nint/nuint, byref/pinned locals) is left null to fall back to inference,
    // so this can only make typing MORE stable, never change a type the fuzzer hasn't validated.
    static SType?[]? ReadLocalSTypes(MetadataReader reader, StandaloneSignatureHandle sig, int localCount)
    {
        if (sig.IsNil || localCount == 0) return null;
        var blob = reader.GetBlobBytes(reader.GetStandaloneSignature(sig).Signature);
        if (blob.Length < 2 || blob[0] != 0x07) return null;
        int idx = 1;
        DecompressIntAdv(blob, ref idx); // count (already known)
        var result = new SType?[localCount];
        bool any = false;
        for (int i = 0; i < localCount && idx < blob.Length; i++)
        {
            bool byRef = false;
            while (idx < blob.Length && (blob[idx] == 0x1F || blob[idx] == 0x20 || blob[idx] == 0x45 || blob[idx] == 0x10))
            {
                byte tag = blob[idx++];
                if (tag == 0x10) byRef = true;                       // BYREF local — not a plain value; fall back
                else if (tag == 0x1F || tag == 0x20) DecompressIntAdv(blob, ref idx); // CMOD token
            }
            if (idx >= blob.Length) break;
            byte t = blob[idx++];
            SType? st;
            if (t == 0x11 || t == 0x12)
            {
                DecompressIntAdv(blob, ref idx); // TypeDefOrRef token (also the alignment skip)
                // CLASS (0x12) is a reference -> O. VALUETYPE (0x11) is left to inference: a host
                // struct is Vt / a script struct is O (classified elsewhere), and an ENUM is NOT
                // safely I4 here — an enum local zero-inited via `initobj En` takes the script-object
                // path, so freezing it I4 regressed real fuzzed programs (oracle [False] vs interp []).
                st = t == 0x12 ? SType.O : (SType?)null;
            }
            else
            {
                st = t switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => SType.I4, // bool,char,i1,u1,i2,u2,i4,u4
                    0x0C => SType.R4,                                                          // float
                    0x0A or 0x0B => SType.I8,                                                  // long/ulong — wide slot
                    0x0D => SType.R8,                                                          // double — wide slot
                    0x0E or 0x1C or 0x1D or 0x15 => SType.O,                                   // string,object,array,generic
                    _ => (SType?)null,   // nint/… left to inference (rare or validator-rejected)
                };
                // Keep idx aligned for the next local (mirror ReadLocalStructLayouts's skips).
                if (t == 0x1D) SkipSigType(blob, ref idx);
                else if (t == 0x15)
                {
                    if (idx < blob.Length) idx++;                        // value/class flag
                    if (idx < blob.Length) DecompressIntAdv(blob, ref idx);
                    if (idx < blob.Length) { int n = DecompressIntAdv(blob, ref idx); for (int j = 0; j < n; j++) SkipSigType(blob, ref idx); }
                }
            }
            if (byRef) st = null;
            if (st is { }) { result[i] = st; any = true; }
        }
        return any ? result : null;
    }

    // True when TypeDef row `row` in this assembly is an enum (base type System.Enum). Enum values
    // are I4 ints, not nested script structs — the distinction matters wherever a VALUETYPE TypeDef
    // is classified (fields, locals): misclassifying an enum as a struct gives it object identity
    // and, through its own static literal members, a self-referential descriptor.
    static bool IsEnumTypeDef(MetadataReader reader, int row)
    {
        try
        {
            var bt = reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(row)).BaseType;
            return bt.Kind switch
            {
                HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)bt).Name) == "Enum",
                HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)bt).Name) == "Enum",
                _ => false,
            };
        }
        catch { return false; }
    }

    // Decode which locals are SCRIPT-defined structs: ELEMENT_TYPE_VALUETYPE (0x11) whose
    // TypeDefOrRef coded token resolves to a TypeDef (table 0 — defined in THIS assembly), as opposed
    // to a TypeRef (table 1 — a host struct, handled by ReadLocalStructLayouts). Such locals are stored
    // as O ScriptObject references, so a value load must clone them. Returns null when none qualify.
    static bool[]? ReadLocalIsScriptStruct(
        MetadataReader reader, StandaloneSignatureHandle sig, int localCount, out int[]? typeDefs)
    {
        typeDefs = null;
        if (sig.IsNil || localCount == 0) return null;
        var blob = reader.GetBlobBytes(reader.GetStandaloneSignature(sig).Signature);
        if (blob.Length < 2 || blob[0] != 0x07) return null;
        int idx = 1;
        DecompressIntAdv(blob, ref idx); // count (already known)
        var result = new bool[localCount];
        bool any = false;
        for (int i = 0; i < localCount && idx < blob.Length; i++)
        {
            // Skip CMOD_REQD (0x1F), CMOD_OPT (0x20), PINNED (0x45), BYREF (0x10)
            while (idx < blob.Length && (blob[idx] == 0x1F || blob[idx] == 0x20 || blob[idx] == 0x45 || blob[idx] == 0x10))
            {
                byte tag = blob[idx++];
                if (tag == 0x1F || tag == 0x20) DecompressIntAdv(blob, ref idx);
            }
            if (idx >= blob.Length) break;
            byte t = blob[idx++];
            if (t == 0x11) // VALUETYPE
            {
                int coded = DecompressIntAdv(blob, ref idx);
                if ((coded & 0x03) == 0) // table 0 == TypeDef
                {
                    result[i] = true; any = true;
                    (typeDefs ??= new int[localCount])[i] =
                        MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(coded >> 2));
                }
            }
            else if (t == 0x12) DecompressIntAdv(blob, ref idx);           // CLASS: consume coded index
            else if (t == 0x1D) SkipSigType(blob, ref idx);                // SZARRAY: recurse
            else if (t == 0x15)                                           // GENERICINST: skip
            {
                if (idx < blob.Length) idx++;
                if (idx < blob.Length) DecompressIntAdv(blob, ref idx);
                if (idx < blob.Length)
                {
                    int n = DecompressIntAdv(blob, ref idx);
                    for (int j = 0; j < n; j++) SkipSigType(blob, ref idx);
                }
            }
        }
        return any ? result : null;
    }

    // Decode a TypeSpec signature to extract the simple type name.
    // TypeSpec for a generic instantiation: 0x15 (CLASS|VALUETYPE) coded-index arg-count args...
    static string ResolveTypeSpecName(MetadataReader reader, TypeSpecificationHandle handle)
    {
        var blob = reader.GetBlobBytes(reader.GetTypeSpecification(handle).Signature);
        if (blob.Length < 3 || blob[0] != 0x15) return "";
        int codedIndex = DecompressInt(blob, 2);
        int table = codedIndex & 0x03;
        int row   = codedIndex >> 2;
        if (table == 1) // TypeRef
            return reader.GetString(reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(row)).Name);
        if (table == 0) // TypeDef
            return reader.GetString(reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(row)).Name);
        return "";
    }

    // Parses the return info from a method signature blob.
    // Sig layout: [callconv (1 byte)] [paramCount (compressed)] [CMOD_REQD/OPT*] [returnType] ...
    // How an I4-slot receiver of an instance host call on this type must be boxed (see ReceiverBox).
    static ReceiverBoxKind ReceiverBoxFor(string typeName) => typeName switch
    {
        "Char" => ReceiverBoxKind.Char,
        "Boolean" => ReceiverBoxKind.Bool,
        _ => ReceiverBoxKind.None,
    };

    // Returns (isVoid, retSType, isBool): retSType is O when isVoid is true. isBool is true only for
    // ELEMENT_TYPE_BOOLEAN — bool collapses to I4 for storage, but the method-return boxing must
    // still hand back a boxed bool (not int), so callers casting the result to bool don't fault.
    static (bool isVoid, SType retSType, bool isBool) SigReturn(byte[] sig)
    {
        if (sig.Length < 3) return (false, SType.O, false);
        int idx = 1; // skip calling convention byte
        DecompressIntAdv(sig, ref idx); // skip paramCount
        // skip custom modifiers (CMOD_REQD=0x1F, CMOD_OPT=0x20) each followed by a compressed type ref
        while (idx < sig.Length && (sig[idx] == 0x1F || sig[idx] == 0x20))
        {
            idx++; // skip modifier tag
            DecompressIntAdv(sig, ref idx); // skip encoded type reference
        }
        if (idx >= sig.Length) return (false, SType.O, false);
        byte tb = sig[idx];
        if (tb == 0x01) return (true, SType.O, false); // ELEMENT_TYPE_VOID
        SType st = tb switch
        {
            // 0x03 = ELEMENT_TYPE_CHAR: char is I4-typed in the VM; omitting it left a char return
            // (e.g. String.get_Chars) boxed in an O slot, read back as 0. (found by fuzzing.)
            // nint/nuint (0x18/0x19) stay O: boxed IntPtr, no 64-bit truncation.
            0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => SType.I4,
            0x0C => SType.R4,
            0x0A or 0x0B => SType.I8, // long/ulong — wide slot
            0x0D => SType.R8,         // double — wide slot
            _ => SType.O,
        };
        return (false, st, tb == 0x02); // 0x02 = ELEMENT_TYPE_BOOLEAN
    }

    // Parse explicit parameter types from a method signature blob.
    // Returns SType[] of length = explicit param count (not counting implicit `this`).
    // Per explicit parameter: 4 for uint (0x09), 8 for ulong (0x0B), 0 otherwise; null when none is
    // unsigned. See HostEntry.UnsignedParams.
    static byte[]? UnsignedParamKinds(byte[] sig)
    {
        if (sig.Length < 2) return null;
        int idx = 1;
        int paramCount = DecompressIntAdv(sig, ref idx);
        if (paramCount == 0) return null;
        while (idx < sig.Length && (sig[idx] == 0x1F || sig[idx] == 0x20)) { idx++; DecompressIntAdv(sig, ref idx); }
        if (idx < sig.Length) SkipSigType(sig, ref idx);
        byte[]? kinds = null;
        for (int p = 0; p < paramCount && idx < sig.Length; p++)
        {
            while (idx < sig.Length && (sig[idx] == 0x1F || sig[idx] == 0x20)) { idx++; DecompressIntAdv(sig, ref idx); }
            if (idx >= sig.Length) break;
            byte tb = sig[idx];
            if (tb == 0x09 || tb == 0x0B) (kinds ??= new byte[paramCount])[p] = tb == 0x09 ? (byte)4 : (byte)8;
            if (!SkipSigType(sig, ref idx)) break;
        }
        return kinds;
    }

    static SType[] SigParamSTypes(byte[] sig)
    {
        if (sig.Length < 2) return Array.Empty<SType>();
        int idx = 1; // skip calling convention
        int paramCount = DecompressIntAdv(sig, ref idx);
        if (paramCount == 0) return Array.Empty<SType>();
        // skip return type (with optional modopt/modreq then the whole type)
        while (idx < sig.Length && (sig[idx] == 0x1F || sig[idx] == 0x20))
        { idx++; DecompressIntAdv(sig, ref idx); }
        // Skip the ENTIRE return type via SkipSigType. Consuming only the leading tag left a
        // compound return's inner tag (SZARRAY/GENERICINST) to be read as the first parameter's
        // type, desyncing every following param by one signature byte.
        if (idx < sig.Length) SkipSigType(sig, ref idx);
        var result = new SType[paramCount];
        for (int p = 0; p < paramCount && idx < sig.Length; p++)
        {
            // skip any modopt/modreq
            while (idx < sig.Length && (sig[idx] == 0x1F || sig[idx] == 0x20))
            { idx++; DecompressIntAdv(sig, ref idx); }
            if (idx >= sig.Length) break;
            byte tb = sig[idx];
            result[p] = tb switch
            {
                // 0x03 = ELEMENT_TYPE_CHAR — I4-typed in the VM (see SigReturn).
                // nint/nuint (0x18/0x19) stay O: boxed IntPtr, no 64-bit truncation.
                0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => SType.I4,
                0x0C => SType.R4,
                0x0A or 0x0B => SType.I8, // long/ulong — wide slot
                0x0D => SType.R8,         // double — wide slot
                _ => SType.O,
            };
            // Advance over the WHOLE type (coded token, byref/array inner type, generic
            // instantiation with all its arguments, VAR index) so the next parameter's tag is read
            // from the right byte. Skipping only part of a compound type mistyped every later
            // parameter: `(Action<Node,S> f, Node n, S s)` read `n` as I4 and lost `s`'s struct-ness.
            if (!SkipSigType(sig, ref idx)) break;
        }
        return result;
    }

    // Walks a method signature like SigReturn/SigParamSTypes but captures the TypeDef token of
    // every VALUETYPE (0x11, coded table 0) return/param — script-defined struct types. Returns
    // (returnTypeDefTok, perParamTypeDefToks); 0 / null when not script structs.
    static (int retTok, int[]? paramToks) ReadSigScriptStructTokens(byte[] sig)
    {
        if (sig.Length < 3) return (0, null);
        int idx = 1;
        int paramCount = DecompressIntAdv(sig, ref idx);
        while (idx < sig.Length && (sig[idx] == 0x1F || sig[idx] == 0x20))
        { idx++; DecompressIntAdv(sig, ref idx); }
        if (idx >= sig.Length) return (0, null);

        // Peek the tag, capture a VALUETYPE TypeDef, then advance over the WHOLE type with
        // SkipSigType — a partial skip of a compound type (generic instantiation, byref) misreads
        // every later parameter (see SigParamSTypes).
        int retTok = ScriptStructTypeDefAt(sig, idx);
        if (!SkipSigType(sig, ref idx)) return (retTok, null);

        int[]? paramToks = null;
        for (int pi = 0; pi < paramCount && idx < sig.Length; pi++)
        {
            while (idx < sig.Length && (sig[idx] == 0x1F || sig[idx] == 0x20))
            { idx++; DecompressIntAdv(sig, ref idx); }
            if (idx >= sig.Length) break;
            int tok = ScriptStructTypeDefAt(sig, idx);
            if (tok != 0) (paramToks ??= new int[paramCount])[pi] = tok;
            if (!SkipSigType(sig, ref idx)) break;
        }
        return (retTok, paramToks);
    }

    // The TypeDef token when the sig type at `idx` is VALUETYPE over a type of this assembly (a
    // script-defined struct); 0 for anything else. Does not advance.
    static int ScriptStructTypeDefAt(byte[] sig, int idx)
    {
        if (idx >= sig.Length || sig[idx] != 0x11) return 0;
        int i = idx + 1;
        int coded = DecompressIntAdv(sig, ref i);
        return (coded & 0x03) == 0 ? MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(coded >> 2)) : 0;
    }

    static int DecompressInt(byte[] b, int i)
    {
        if ((b[i] & 0x80) == 0) return b[i];
        if ((b[i] & 0xC0) == 0x80) return ((b[i] & 0x3F) << 8) | b[i + 1];
        return ((b[i] & 0x1F) << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
    }

    // DecompressInt variant that advances the index past the compressed integer.
    static int DecompressIntAdv(byte[] b, ref int idx)
    {
        byte first = b[idx];
        if ((first & 0x80) == 0)   { idx += 1; return first; }
        if ((first & 0xC0) == 0x80) { int v = ((first & 0x3F) << 8) | b[idx + 1]; idx += 2; return v; }
        int v4 = ((first & 0x1F) << 24) | (b[idx + 1] << 16) | (b[idx + 2] << 8) | b[idx + 3];
        idx += 4; return v4;
    }

    // Decode a TypeSpec GENERICINST blob: returns (outerTypeName, firstArgTypeName).
    /// <summary>SType of a Func`N TypeSpec's LAST generic arg — its TResult. Func`N is
    /// <c>0x15 (CLASS) coded-outer argCount T1..Tn TResult</c>; the return is the final arg.
    /// Skips the leading args, then maps the final arg's element-type tag to I4 (integral/bool/
    /// char) or R4 (Single); anything else (String, object, another generic) stays O so only
    /// primitive delegate results get numeric-frame typing. Returns O on any decode failure.</summary>
    // Closed return SType for a TypeSpec member whose signature returns the generic VAR `!n`:
    // decode n from the member sig, then read the instantiation's n-th type argument.
    // Primitives map to their slot type; enums (host TypeRef or script TypeDef) map to I4;
    // everything else — and any decode wobble — stays O, the pre-existing behavior.
    static SType GenericVarReturnSType(
        MetadataReader reader, TypeSpecificationHandle spec, byte[] memberSig, HostBinding? binding)
    {
        try
        {
            int idx = 0;
            byte conv = memberSig[idx++];
            if ((conv & 0x10) != 0) DecompressIntAdv(memberSig, ref idx); // genParamCount
            DecompressIntAdv(memberSig, ref idx);                        // paramCount
            if (idx >= memberSig.Length || memberSig[idx] != 0x13) return SType.O; // ELEMENT_TYPE_VAR
            idx++;
            int varIdx = DecompressIntAdv(memberSig, ref idx);

            var blob = reader.GetBlobBytes(reader.GetTypeSpecification(spec).Signature);
            if (blob.Length < 4 || blob[0] != 0x15) return SType.O; // GENERICINST
            int b = 2; // skip 0x15 and the CLASS/VALUETYPE tag
            DecompressIntAdv(blob, ref b); // generic definition coded index
            int argc = DecompressIntAdv(blob, ref b);
            for (int i = 0; i < argc && b < blob.Length; i++)
            {
                if (i != varIdx) { if (!SkipSigType(blob, ref b)) return SType.O; continue; }
                byte tag = blob[b];
                switch (tag)
                {
                    case 0x02: case 0x03: case 0x04: case 0x05:
                    case 0x06: case 0x07: case 0x08: case 0x09:
                        return SType.I4;
                    case 0x0C:
                        return SType.R4;
                    // Wide type args ride wide slots — this predated I8/R8, so a
                    // Func<long,long>.Invoke result typed O and every consumer (neg/ceq/add/conv)
                    // used the 32-bit op over the boxed long (found by fuzzing under release
                    // codegen: `if (dg(x) == -dg(x))` read the boxed long as 0).
                    case 0x0A: case 0x0B:
                        return SType.I8;
                    case 0x0D:
                        return SType.R8;
                    case 0x11: // VALUETYPE coded index — enum type args ride I4
                    {
                        int c = b + 1;
                        int coded = DecompressIntAdv(blob, ref c);
                        if ((coded & 0x03) == 0 && IsEnumTypeDef(reader, coded >> 2)) return SType.I4;
                        if ((coded & 0x03) == 1 && binding != null
                            && ResolveSigTypeRef(reader, MetadataTokens.TypeReferenceHandle(coded >> 2), binding)
                                is { IsEnum: true }) return SType.I4;
                        return SType.O;
                    }
                    default:
                        return SType.O;
                }
            }
            return SType.O;
        }
        catch { return SType.O; }
    }

    static SType FuncReturnSType(MetadataReader reader, TypeSpecificationHandle handle)
    {
        try
        {
            var blob = reader.GetBlobBytes(reader.GetTypeSpecification(handle).Signature);
            if (blob.Length < 3 || blob[0] != 0x15) return SType.O;
            int idx = 2; // skip 0x15 (GENERICINST) and 0x11/0x12 (CLASS/VALUETYPE)
            DecompressIntAdv(blob, ref idx); // coded index of the open type (Func`N)
            int argCount = DecompressIntAdv(blob, ref idx);
            if (argCount < 1) return SType.O;
            for (int i = 0; i < argCount - 1; i++) // skip T1..Tn, leaving TResult
                if (!SkipSigType(blob, ref idx)) return SType.O;
            if (idx >= blob.Length) return SType.O;
            return blob[idx] switch
            {
                0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => SType.I4, // bool/char/sub-int/int/uint
                0x0C => SType.R4, // Single
                _ => SType.O,
            };
        }
        catch { return SType.O; }
    }

    static (string, string) DecodeGenericInstHeader(MetadataReader reader, TypeSpecificationHandle handle)
    {
        var blob = reader.GetBlobBytes(reader.GetTypeSpecification(handle).Signature);
        if (blob.Length < 3 || blob[0] != 0x15) return ("", "");

        int idx = 2; // skip 0x15 and 0x11/0x12
        int codedIndex = DecompressIntAdv(blob, ref idx);
        int table = codedIndex & 0x03;
        int row   = codedIndex >> 2;

        string outerName;
        if (table == 1)
            outerName = reader.GetString(reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(row)).Name);
        else if (table == 0)
            outerName = reader.GetString(reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(row)).Name);
        else return ("", "");

        int argCount = DecompressIntAdv(blob, ref idx);
        if (argCount < 1 || idx >= blob.Length) return (outerName, "");

        byte argTag = blob[idx++];
        string arg0Name = "";
        switch (argTag)
        {
            case 0x02: arg0Name = "Boolean"; break;
            case 0x03: arg0Name = "Char";   break;
            case 0x04: arg0Name = "SByte";  break;
            case 0x05: arg0Name = "Byte";   break;
            case 0x06: arg0Name = "Int16";  break;
            case 0x07: arg0Name = "UInt16"; break;
            case 0x08: arg0Name = "Int32";  break;
            case 0x09: arg0Name = "UInt32"; break;
            case 0x0A: arg0Name = "Int64";  break;
            case 0x0B: arg0Name = "UInt64"; break;
            case 0x0C: arg0Name = "Single"; break;
            case 0x0D: arg0Name = "Double"; break;
            case 0x0E: arg0Name = "String"; break;
            case 0x11: case 0x12:
            {
                int ac = DecompressIntAdv(blob, ref idx);
                int at = ac & 0x03; int ar = ac >> 2;
                if (at == 1) arg0Name = reader.GetString(reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(ar)).Name);
                else if (at == 0) arg0Name = reader.GetString(reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(ar)).Name);
                break;
            }
        }
        return (outerName, arg0Name);
    }

    // Resolve a TypeSpecification handle to its closed runtime Type (List`1<Vec2> → List<Vec2>).
    // Null when any piece of the signature is unsupported or unresolved.
    static Type? ResolveClosedSpecType(MetadataReader reader, TypeSpecificationHandle handle, HostBinding binding)
    {
        try
        {
            var blob = reader.GetBlobBytes(reader.GetTypeSpecification(handle).Signature);
            int idx = 0;
            return DecodeSigType(reader, blob, ref idx, binding);
        }
        catch { return null; }
    }

    // Decode a MethodSpec signature blob (II.23.2.15) into resolved host Types.
    // Format: 0x0A [GenArgCount] [Type signatures...]
    // Throws ScriptValidationException for unsupported tags or unresolved type-args —
    // the caller should let it propagate so the user sees a friendly load-time error.
    static Type[] DecodeMethodSpecArgs(MetadataReader reader, BlobHandle sigHandle, HostBinding binding, int msToken)
    {
        var blob = reader.GetBlobBytes(sigHandle);
        if (blob.Length < 2 || blob[0] != 0x0A)
            throw new ScriptValidationException($"MethodSpec at 0x{msToken:X8}: malformed signature");
        int idx = 1;
        int argCount = DecompressIntAdv(blob, ref idx);
        var result = new Type[argCount];
        for (int i = 0; i < argCount; i++)
            result[i] = DecodeMethodSpecArg(reader, blob, ref idx, binding, msToken);
        return result;
    }

    static Type DecodeMethodSpecArg(MetadataReader reader, byte[] blob, ref int idx, HostBinding binding, int msToken)
    {
        byte tag = blob[idx++];
        switch (tag)
        {
            case 0x02: return typeof(bool);
            case 0x08: return typeof(int);
            case 0x09: return typeof(uint);
            case 0x0A: return typeof(long);
            case 0x0C: return typeof(float);
            case 0x0D: return typeof(double);
            case 0x0E: return typeof(string);
            case 0x18: return typeof(IntPtr);  // ELEMENT_TYPE_I — native int
            case 0x19: return typeof(UIntPtr); // ELEMENT_TYPE_U
            case 0x1C: return typeof(object);
            case 0x11: case 0x12:
            {
                int coded = DecompressIntAdv(blob, ref idx);
                int table = coded & 0x03;
                int row   = coded >> 2;
                if (table != 1) // only TypeRef supported (TypeDef / TypeSpec out of scope)
                    throw new ScriptValidationException(
                        $"MethodSpec at 0x{msToken:X8}: unsupported type-arg table {table}");
                var trh = reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(row));
                var name = reader.GetString(trh.Name);
                if (binding.TryGetTypeByName(name, out var t))
                    return t!;
                // Unregistered ENUM type-args resolve by full name (Enum.TryParse<DayOfWeek>):
                // an enum carries no callable surface, so it doesn't widen the allowlist.
                var full = trh.Namespace.IsNil ? name : reader.GetString(trh.Namespace) + "." + name;
                if (FindTypeByFullName(full) is { IsEnum: true } et)
                    return et;
                throw new ScriptValidationException(
                    $"MethodSpec at 0x{msToken:X8}: type argument '{name}' is not in the host binding allowlist (call AllowType<{name}>())");
            }
            default:
                throw new ScriptValidationException(
                    $"MethodSpec at 0x{msToken:X8}: unsupported type-arg signature tag 0x{tag:X2}");
        }
    }

    // Decode a standard method signature blob (II.23.2.1) into a param Type[].
    // Format: [conv-byte] [paramCount] [returnType] [paramType * paramCount]
    // Returns null if the blob is malformed or contains an unsupported tag — caller falls through.
    static Type[]? DecodeMethodSigParamTypes(MetadataReader reader, byte[] blob, HostBinding binding)
    {
        if (blob.Length < 3) return null;
        int idx = 0;
        byte conv = blob[idx++];
        if ((conv & 0x10) != 0) return null; // GENERIC — handled via MethodSpec path
        int paramCount = DecompressIntAdv(blob, ref idx);
        // skip return type
        if (!SkipSigType(blob, ref idx)) return null;
        var result = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
        {
            var t = DecodeSigType(reader, blob, ref idx, binding);
            if (t == null) return null;
            result[i] = t;
        }
        return result;
    }

    // Decode one type from a signature blob at idx, advancing idx. Returns null on unsupported tag.
    static Type? DecodeSigType(MetadataReader reader, byte[] blob, ref int idx, HostBinding binding)
    {
        if (idx >= blob.Length) return null;
        byte tag = blob[idx++];
        switch (tag)
        {
            case 0x01: return typeof(void);
            case 0x02: return typeof(bool);
            case 0x03: return typeof(char);
            case 0x04: return typeof(sbyte);
            case 0x05: return typeof(byte);
            case 0x06: return typeof(short);
            case 0x07: return typeof(ushort);
            case 0x08: return typeof(int);
            case 0x09: return typeof(uint);
            case 0x0A: return typeof(long);
            case 0x0B: return typeof(ulong);
            case 0x0C: return typeof(float);
            case 0x0D: return typeof(double);
            case 0x0E: return typeof(string);
            case 0x18: return typeof(IntPtr);  // ELEMENT_TYPE_I — native int
            case 0x19: return typeof(UIntPtr); // ELEMENT_TYPE_U
            case 0x1C: return typeof(object);
            case 0x11: case 0x12: // VALUETYPE / CLASS + coded TypeDefOrRef
            {
                int coded = DecompressIntAdv(blob, ref idx);
                int table = coded & 0x03;
                int row   = coded >> 2;
                if (table != 1) return null; // only TypeRef supported
                return ResolveSigTypeRef(reader, MetadataTokens.TypeReferenceHandle(row), binding);
            }
            case 0x10: // BYREF — recurse then MakeByRefType
            {
                var inner = DecodeSigType(reader, blob, ref idx, binding);
                return inner?.MakeByRefType();
            }
            case 0x15: // GENERICINST: (CLASS|VALUETYPE) coded-index argCount arg* — e.g. List<int>
            {
                if (idx >= blob.Length) return null;
                idx++; // CLASS or VALUETYPE tag of the generic definition
                int gcoded = DecompressIntAdv(blob, ref idx);
                Type? def = (gcoded & 0x03) == 1
                    ? ResolveSigTypeRef(reader, MetadataTokens.TypeReferenceHandle(gcoded >> 2), binding)
                    : null;
                // A registered CLOSED instantiation (e.g. an AllowType'd List<float>) shares the
                // metadata short name "List`1" — normalize to the open definition before re-closing.
                if (def is { IsGenericType: true, IsGenericTypeDefinition: false })
                    def = def.GetGenericTypeDefinition();
                int argc = DecompressIntAdv(blob, ref idx);
                var args = new Type[argc];
                for (int i = 0; i < argc; i++)
                {
                    var a = DecodeSigType(reader, blob, ref idx, binding);
                    if (a == null) return null; // caller aborts the whole decode; position is moot
                    args[i] = a;
                }
                if (def == null || !def.IsGenericTypeDefinition ||
                    def.GetGenericArguments().Length != argc) return null;
                try { return def.MakeGenericType(args); } catch { return null; }
            }
            case 0x1D: // SZARRAY — recurse then MakeArrayType
            {
                var inner = DecodeSigType(reader, blob, ref idx, binding);
                return inner?.MakeArrayType();
            }
            default: return null; // unsupported tag — fall through
        }
    }

    // Classify a `callvirt X::Invoke(...)` whose token didn't resolve to a host or script method as a
    // SCRIPT-TYPED DELEGATE invoke — the delegate type mentions a script type, so it has no runtime
    // System.Type and its value is a ScriptDelegate (see the invoke_script_delegate op). Returns the
    // Invoke arity + return classification the lowerer needs to type the result slot. False (leaving the
    // call to fall through to notSupported) unless: the member is named "Invoke", its parent is a generic
    // instantiation that does NOT fully resolve to a host Type (i.e. it names a script type), and that
    // instantiation's open generic definition is a delegate family (System.Action`N / System.Func`N / a
    // custom host generic delegate). retSType mirrors the host call_host dst typing: I4/R4 for a numeric
    // Func result, O otherwise; sdVoid true for Action.
    static bool TryClassifyScriptDelegateInvoke(
        ParsedAssembly asm, int tok, out int argc, out bool isVoid, out SType retSType)
    {
        argc = 0; isVoid = true; retSType = SType.O;
        if (asm.HostSurface == null) return false;
        try
        {
            var h = MetadataTokens.EntityHandle(tok);
            if (h.Kind != HandleKind.MemberReference) return false;
            var mr = asm.Reader.GetMemberReference((MemberReferenceHandle)h);
            if (asm.Reader.GetString(mr.Name) != "Invoke") return false;
            if (mr.Parent.Kind != HandleKind.TypeSpecification) return false;
            var spec = (TypeSpecificationHandle)mr.Parent;
            // Fully host-resolvable delegate types are handled by the normal host path — this
            // classifier is only for a delegate type with no runtime Type (it names a script type).
            if (ResolveClosedSpecType(asm.Reader, spec, asm.HostSurface) != null) return false;
            // ...and positively names a script type: a host delegate the binding merely failed to
            // resolve (nested host type, type outside the binding) keeps its host shim path.
            if (!TypeSpecNamesScriptType(asm.Reader, spec)) return false;
            if (!TypeSpecOpenDefIsDelegate(asm.Reader, spec)) return false;

            var sig = asm.Reader.GetBlobBytes(mr.Signature);
            argc = DecompressInt(sig, 1); // paramCount, after the calling-convention byte
            (isVoid, retSType, _) = SigReturn(sig);
            // A Func<...,TResult>::Invoke returns the generic VAR `!n`; resolve it against the
            // TypeSpec's type args (SigReturn maps the raw VAR tag to O). Void (Action) stays void.
            // UPGRADE only, like the call_host path: a concrete return (Predicate<T> → bool,
            // Comparison<T> → int) is already typed by SigReturn, and GenericVarReturnSType answers O
            // for any non-VAR return — overwriting bool with O made `if (p(n))` a null test on a box.
            if (!isVoid && retSType == SType.O)
            {
                var d = GenericVarReturnSType(asm.Reader, spec, sig, asm.HostSurface);
                if (d != SType.O) retSType = d;
            }
            return true;
        }
        catch { return false; }
    }

    // True when a closed generic instantiation (TypeSpec) names a type DEFINED IN THE SCRIPT anywhere
    // in its type arguments — `Action<Node>`, `Func<List<Node>, int>`. This is the positive test for
    // "script-typed delegate": "the host could not resolve it" is not the same thing (a host nested
    // type or a type outside the binding also fails to resolve, and must keep its host path).
    static bool TypeSpecNamesScriptType(MetadataReader reader, TypeSpecificationHandle spec)
    {
        try
        {
            var blob = reader.GetBlobBytes(reader.GetTypeSpecification(spec).Signature);
            int idx = 0;
            return SigTypeNamesScriptType(blob, ref idx);
        }
        catch { return false; }
    }

    // The ctor MemberRef of a `newobj D::.ctor(object, IntPtr)` delegate construction: true when D is
    // a generic instantiation naming a script type (see TypeSpecNamesScriptType).
    static bool CtorParentNamesScriptType(ParsedAssembly asm, int ctorTok)
    {
        try
        {
            var h = MetadataTokens.EntityHandle(ctorTok);
            if (h.Kind != HandleKind.MemberReference) return false;
            var mr = asm.Reader.GetMemberReference((MemberReferenceHandle)h);
            return mr.Parent.Kind == HandleKind.TypeSpecification
                && TypeSpecNamesScriptType(asm.Reader, (TypeSpecificationHandle)mr.Parent);
        }
        catch { return false; }
    }

    // Walk one sig type (II.23.2.12) — the same grammar DecodeSigType decodes — answering whether a
    // TypeDef (table tag 0: a type of this assembly, i.e. a script type) appears anywhere in it.
    // Returns false and stops at any tag it does not know, so an unknown shape is never classified.
    static bool SigTypeNamesScriptType(byte[] blob, ref int idx)
    {
        if (idx >= blob.Length) return false;
        byte tag = blob[idx++];
        switch (tag)
        {
            case 0x01: case 0x02: case 0x03: case 0x04: case 0x05: case 0x06: case 0x07:
            case 0x08: case 0x09: case 0x0A: case 0x0B: case 0x0C: case 0x0D: case 0x0E:
            case 0x18: case 0x19: case 0x1C:
                return false;                                    // primitives / string / object
            case 0x13: case 0x1E: DecompressIntAdv(blob, ref idx); return false; // VAR / MVAR
            case 0x11: case 0x12:                                // VALUETYPE / CLASS coded TypeDefOrRef
                return (DecompressIntAdv(blob, ref idx) & 0x03) == 0;
            case 0x10: case 0x1D: case 0x45:                     // BYREF / SZARRAY / PINNED — recurse
                return SigTypeNamesScriptType(blob, ref idx);
            case 0x15:                                           // GENERICINST
            {
                if (idx >= blob.Length) return false;
                idx++;                                           // CLASS or VALUETYPE of the open def
                bool any = (DecompressIntAdv(blob, ref idx) & 0x03) == 0; // a script generic def
                int argc = DecompressIntAdv(blob, ref idx);
                for (int i = 0; i < argc; i++)
                    if (SigTypeNamesScriptType(blob, ref idx)) any = true;
                return any;
            }
            default: return false;
        }
    }

    // True when a TypeSpec generic instantiation's OPEN generic definition is a delegate type — even
    // when the closed instantiation itself is unresolvable (a script type arg). Resolves only the open
    // definition (System.Action`1, System.Func`2, a custom host delegate), which is always a host type.
    static bool TypeSpecOpenDefIsDelegate(MetadataReader reader, TypeSpecificationHandle spec)
    {
        var blob = reader.GetBlobBytes(reader.GetTypeSpecification(spec).Signature);
        if (blob.Length < 3 || blob[0] != 0x15) return false; // GENERICINST
        int coded = DecompressInt(blob, 2);                   // [1] is the CLASS/VALUETYPE tag
        if ((coded & 0x03) != 1) return false;                // open def must be a TypeRef (host type)
        var tr = reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(coded >> 2));
        var name = reader.GetString(tr.Name);
        var full = tr.Namespace.IsNil ? name : reader.GetString(tr.Namespace) + "." + name;
        var open = FindTypeByFullName(full);
        // Every delegate type derives directly from MulticastDelegate (reliable for open generic defs,
        // where typeof(Delegate).IsAssignableFrom can be finicky).
        return open != null && (open.BaseType == typeof(MulticastDelegate) || open.IsSubclassOf(typeof(Delegate)));
    }

    // Resolve the runtime Type a ctor MemberRef constructs — used by delegate-creation lowering,
    // where the delegate type is only named by the newobj token's parent. TypeRef parents cover
    // custom delegates and System.Action; TypeSpec parents cover generic instantiations
    // (Func`2<int,int>, EventCallback`1<ClickEvent>) via the full recursive sig decode.
    static Type? ResolveCtorParentType(ParsedAssembly asm, int ctorTok)
    {
        if (asm.HostSurface == null) return null;
        try
        {
            var h = MetadataTokens.EntityHandle(ctorTok);
            if (h.Kind != HandleKind.MemberReference) return null;
            var mr = asm.Reader.GetMemberReference((MemberReferenceHandle)h);
            switch (mr.Parent.Kind)
            {
                case HandleKind.TypeReference:
                    return ResolveSigTypeRef(asm.Reader, (TypeReferenceHandle)mr.Parent, asm.HostSurface);
                case HandleKind.TypeSpecification:
                {
                    var blob = asm.Reader.GetBlobBytes(
                        asm.Reader.GetTypeSpecification((TypeSpecificationHandle)mr.Parent).Signature);
                    int idx = 0;
                    return DecodeSigType(asm.Reader, blob, ref idx, asm.HostSurface);
                }
                default:
                    return null;
            }
        }
        catch { return null; }
    }

    // Resolve the receiver type of a MemberRef whose parent is a TypeRef. The namespace-qualified
    // name goes through the (policy-gated) auto-bind FIRST: simple names collide across namespaces
    // (UnityEngine.Application vs UnityEngine.WSA/Device.Application), and resolving against the
    // wrong twin either loses the member (unbound) or — worse — silently binds a same-named member
    // of the wrong type. Nested TypeRefs (nil namespace) and names the auto-bind policy declines
    // (or bindings without a resolver) keep the registered simple-name behavior unchanged.
    static bool TryResolveReceiverTypeRef(MetadataReader reader, TypeReferenceHandle handle,
        string simpleName, HostBinding binding, out Type? type)
    {
        var tr = reader.GetTypeReference(handle);
        if (!tr.Namespace.IsNil)
        {
            var fullName = reader.GetString(tr.Namespace) + "." + simpleName;
            if (binding.TryAutoBindType(fullName) && binding.TryGetTypeByFullName(fullName, out type))
                return true;
        }
        return binding.TryGetTypeByName(simpleName, out type);
    }

    // Resolve a sig-blob TypeRef to a host Type for SIGNATURE MATCHING only (no registration
    // implied): registered simple names first, then the namespace-qualified name — covering types
    // the auto-bind policy deliberately never registers (List`1, System.Uri) and generic
    // definitions living outside corelib (InputFeatureUsage`1) — without a Type for them, every
    // same-arity overload set that differs only by such a parameter resolves ambiguous and lands
    // in UnboundHostMembers.
    static Type? ResolveSigTypeRef(MetadataReader reader, TypeReferenceHandle handle, HostBinding binding)
    {
        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        if (binding.TryGetTypeByName(name, out var t) && t != null) return t;
        return FindTypeByFullName(tr.Namespace.IsNil
            ? name : reader.GetString(tr.Namespace) + "." + name);
    }

    // Full-name type lookup across corelib and every loaded assembly, for signature matching only.
    // Deliberately NOT the auto-bind resolver: no policy applies (registering nothing, we only need
    // Type identity to pick the right overload), and results — including misses — are cached for
    // the process (a type loaded later than its first sig miss stays unresolved; overload matching
    // then degrades to the name+arity fallback exactly as before this lookup existed).
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> _sigTypeByFullName = new();

    static Type? FindTypeByFullName(string fullName) =>
        _sigTypeByFullName.GetOrAdd(fullName, static fn =>
        {
            try { var t = Type.GetType(fn, throwOnError: false); if (t != null) return t; } catch { }
            // UNITY_6000_5_OR_NEWER: AppDomain.GetAssemblies can return already-unloaded assemblies
            // in the editor (UAC0005, an error on 6000.6+); the pure-dotnet builds keep AppDomain.
#if UNITY_6000_5_OR_NEWER
            foreach (var asm in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
#else
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
#endif
            {
                try { var t = asm.GetType(fn, throwOnError: false); if (t != null) return t; } catch { }
            }
            return null;
        });

    // Advance idx past one type in a sig blob without returning a Type. Returns false on error.
    static bool SkipSigType(byte[] blob, ref int idx)
    {
        if (idx >= blob.Length) return false;
        byte tag = blob[idx++];
        switch (tag)
        {
            case 0x01: case 0x02: case 0x03: case 0x04: case 0x05:
            case 0x06: case 0x07: case 0x08: case 0x09: case 0x0A:
            case 0x0B: case 0x0C: case 0x0D: case 0x0E: case 0x18:
            case 0x19: case 0x1C:
                return true; // primitives/native int/string/object — no extra bytes
            case 0x11: case 0x12:
                DecompressIntAdv(blob, ref idx); // coded index
                return true;
            case 0x10: case 0x1D:
                return SkipSigType(blob, ref idx); // wrapper: skip inner type
            case 0x13: case 0x1E: // VAR / MVAR: generic parameter reference + index
                DecompressIntAdv(blob, ref idx);
                return true;
            case 0x15: // GENERICINST: (CLASS|VALUETYPE) coded-index argCount arg*
            {
                // A generic RETURN type (e.g. AsyncOperationHandle<GameObject>) must be skippable
                // even though DecodeSigType can't produce it — otherwise param decoding never runs
                // and multi-overload members (Addressables.InstantiateAsync/5) resolve ambiguous.
                if (idx >= blob.Length) return false;
                idx++; // CLASS or VALUETYPE tag
                DecompressIntAdv(blob, ref idx); // coded TypeDefOrRef
                int n = DecompressIntAdv(blob, ref idx);
                for (int i = 0; i < n; i++)
                    if (!SkipSigType(blob, ref idx)) return false;
                return true;
            }
            default: return false; // unsupported
        }
    }

    // Pick the open generic overload whose MemberRef parameter signature matches the call.
    // sigIdx points at the RETURN type inside the GENERIC-calling-convention MemberRef blob
    // (conv byte, genParamCount and paramCount already consumed). Each candidate is closed with
    // typeArgs and its parameters compared structurally against the blob. Null = none matched.
    static MethodInfo? SelectOpenGenericOverload(MetadataReader reader, List<MethodInfo> candidates,
        byte[] sig, int sigIdx, Type[] typeArgs)
    {
        foreach (var cand in candidates)
        {
            MethodInfo closed;
            try { closed = cand.MakeGenericMethod(typeArgs); }
            catch { continue; }
            var ps = closed.GetParameters();
            int idx = sigIdx;
            if (!SkipSigType(sig, ref idx)) continue; // return type
            bool ok = true;
            for (int i = 0; i < ps.Length && ok; i++)
                ok = SigTypeMatches(reader, sig, ref idx, ps[i].ParameterType, typeArgs);
            if (ok) return cand;
        }
        return null;
    }

    // Structural comparison of one signature-blob type against a resolved CLR type, advancing idx.
    // TypeRef comparisons use short names (good enough to split overloads; full-name resolution
    // isn't available for unregistered refs). VAR/MVAR compare against the instantiation's args.
    static bool SigTypeMatches(MetadataReader reader, byte[] blob, ref int idx, Type expected, Type[] typeArgs)
    {
        if (idx >= blob.Length) return false;
        byte tag = blob[idx++];
        switch (tag)
        {
            case 0x02: return expected == typeof(bool);
            case 0x03: return expected == typeof(char);
            case 0x04: return expected == typeof(sbyte);
            case 0x05: return expected == typeof(byte);
            case 0x06: return expected == typeof(short);
            case 0x07: return expected == typeof(ushort);
            case 0x08: return expected == typeof(int);
            case 0x09: return expected == typeof(uint);
            case 0x0A: return expected == typeof(long);
            case 0x0B: return expected == typeof(ulong);
            case 0x0C: return expected == typeof(float);
            case 0x0D: return expected == typeof(double);
            case 0x0E: return expected == typeof(string);
            case 0x1C: return expected == typeof(object);
            case 0x11: case 0x12: // VALUETYPE / CLASS + coded TypeDefOrRef
            {
                int coded = DecompressIntAdv(blob, ref idx);
                if ((coded & 0x03) != 1) return false; // only TypeRef comparable here
                var name = reader.GetString(reader.GetTypeReference(
                    MetadataTokens.TypeReferenceHandle(coded >> 2)).Name);
                return expected.Name == name;
            }
            case 0x13: case 0x1E: // VAR / MVAR n → n-th type argument of this instantiation
            {
                int n = DecompressIntAdv(blob, ref idx);
                return n < typeArgs.Length && expected == typeArgs[n];
            }
            case 0x10: // BYREF
                return expected.IsByRef && SigTypeMatches(reader, blob, ref idx, expected.GetElementType()!, typeArgs);
            case 0x1D: // SZARRAY
                return expected.IsArray && SigTypeMatches(reader, blob, ref idx, expected.GetElementType()!, typeArgs);
            case 0x15: // GENERICINST: outer definition name + each type arg
            {
                if (idx >= blob.Length) return false;
                idx++; // CLASS|VALUETYPE tag
                int coded = DecompressIntAdv(blob, ref idx);
                string outer = (coded & 0x03) == 1
                    ? reader.GetString(reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(coded >> 2)).Name)
                    : "";
                int n = DecompressIntAdv(blob, ref idx);
                if (!expected.IsGenericType || expected.Name != outer) return false;
                var eargs = expected.GetGenericArguments();
                if (eargs.Length != n) return false;
                for (int i = 0; i < n; i++)
                    if (!SigTypeMatches(reader, blob, ref idx, eargs[i], typeArgs)) return false;
                return true;
            }
            default: return false;
        }
    }

    static MethodInfo? FindMethod(Type type, string name, int paramCount)
    {
        foreach (var m in type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly))
            if (m.Name == name && m.GetParameters().Length == paramCount)
                return m;
        return null;
    }

    // Resolve flat-struct (Vt) layouts for a HostEntry's params/return/receiver, given
    // the binding. Called for hostCalls and hostCtors after construction in Parse.
    static void FillStructInfo(HostEntry he, HostBinding? binding, bool isCtor)
    {
        if (binding == null) return;
        var ps = he.Binding.Params;
        if (ps != null && ps.Length > 0)
        {
            var arr = new HostBinding.StructLayout?[ps.Length];
            bool any = false;
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                if (pt.IsByRef) pt = pt.GetElementType()!;
                if (pt.IsValueType && binding.TryGetStructLayout(pt, out var lay))
                { arr[i] = lay; any = true; }
            }
            if (any) he.ArgStructs = arr;
        }
        var m = (MethodBase?)he.ResolvedMethod ?? he.Binding.Method;
        if (isCtor)
        {
            // Ctor's "return" type is its declaring type.
            var declT = he.Binding.DeclaringType;
            if (declT != null && declT.IsValueType && binding.TryGetStructLayout(declT, out var declLay))
                he.ReturnStruct = declLay;
        }
        else if (m is MethodInfo mi)
        {
            var rt = mi.ReturnType;
            if (rt.IsValueType && rt != typeof(void) && binding.TryGetStructLayout(rt, out var retLay))
                he.ReturnStruct = retLay;
            // Receiver: for a value-type instance method, the receiver is a struct
            // (rarely useful in our subset but tracked for completeness).
            if (he.Binding.HasThis && mi.DeclaringType != null && mi.DeclaringType.IsValueType
                && binding.TryGetStructLayout(mi.DeclaringType, out var recvLay))
                he.ReceiverStruct = recvLay;
        }
    }


    // Walks one ParsedMethod's IL, simulates the eval stack symbolically to determine
    // operand types, allocates frame slots, and emits uint[] IR. All methods in the
    // assembly are lowered at Load time and cached on ParsedMethod.Lowered.
    internal static class IrLowerer
    {
        // Returns a list of (methodName, reason) for any method that failed to lower.
        // Empty list means full success. Load throws when this is non-empty.
        // lenient=true demotes EVERY per-method lowering failure to a skipped stub (as cold
        // enumerator members already are) instead of failing the whole Load — a method that can't
        // lower is left with Lowered==null + a LoweringSkipReason, and the rest load normally. Hot
        // reload uses this so one unsupported method (e.g. SetupUI referencing a member missing on
        // the running build) doesn't poison every other reloaded method in the same file. Strict
        // (default) stays all-or-nothing for the fuzzer/tests, where a failure is a bug to surface.
        // Environment kill switches for the optimizer, read once per Load (LowerAll refreshes
        // them) rather than once per method. Not `static readonly`: the fuzz self-diff and
        // LoweringConfigSelfDiffTests flip them between Loads in one process.
#if UNITY_6000_5_OR_NEWER
        [NoAutoStaticsCleanup]
#endif
        static class Knobs
        {
            public static bool NoOpt;
            public static string SkipPass = "";

            public static void Refresh()
            {
                NoOpt    = Environment.GetEnvironmentVariable("ILINTERPRETER_NOOPT") != null;
                SkipPass = Environment.GetEnvironmentVariable("ILINTERPRETER_SKIP_PASS") ?? "";
            }
        }

        public static List<(string Name, string Reason)> LowerAll(ParsedAssembly asm, bool lenient = false)
        {
            Knobs.Refresh();
            var failures = new List<(string, string)>();
            foreach (var m in asm.ByToken.Values)
            {
                if (m.IsAbstractStub) continue; // interface stub: no IL; dispatch anchor only
                try { m.Lowered = LowerMethod(m, asm); }
                catch (NotSupportedException ex)
                {
                    // A cold enumerator member (iterator Reset — body is `throw new
                    // NotSupportedException()`) becomes a skipped stub instead of failing the
                    // whole Load: no driver ever calls it. Invoking it anyway throws the reason.
                    if (m.IsColdEnumeratorMember || lenient) { m.LoweringSkipReason = ex.Message; continue; }
                    failures.Add((m.Name, ex.Message));
                }
                catch (Exception ex)
                {
                    // A lowering crash must still name the METHOD: a raw IndexOutOfRangeException
                    // escaping Load reads as "Index was outside the bounds of the array" with
                    // nothing to act on. The stack rides along so a bug report is actionable.
                    var reason = $"internal error while lowering (an unsupported construct — please report): {ex}";
                    if (m.IsColdEnumeratorMember || lenient) { m.LoweringSkipReason = reason; continue; }
                    failures.Add((m.Name, reason));
                }
            }

            // A lowered method whose call sites target a skipped stub would jump through a null
            // Lowered at runtime — demote such callers to failures too (cascading; defensive:
            // in practice cold members are leaf Reset bodies nothing calls).
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var m in asm.ByToken.Values)
                {
                    var callees = m.Lowered?.CalleeByTokIdx;
                    if (callees == null) continue;
                    foreach (var callee in callees)
                    {
                        // Interface stubs legitimately have no IL — the executor resolves the
                        // real implementation from the receiver at dispatch time.
                        if (callee == null || callee.Lowered != null || callee.IsAbstractStub) continue;
                        var reason = $"calls '{callee.Name}', which could not be lowered: {callee.LoweringSkipReason}";
                        m.Lowered = null!;
                        if (m.IsColdEnumeratorMember || lenient) m.LoweringSkipReason = reason;
                        else failures.Add((m.Name, reason));
                        changed = true;
                        break;
                    }
                }
            }
            return failures;
        }

        // Concatenates every lowered method's IR into ParsedAssembly.IrBlob with branch targets
        // rewritten blob-absolute, so the Vm can execute script-to-script calls iteratively (frame
        // push + ip jump) under one `fixed` instead of recursing into Run(). Each method's region
        // gets a trailing ret_void sentinel: it preserves both the old "fell off the end" return
        // (void methods) and the lowerer's past-end fall-through branch targets, and stops
        // execution from running into the next method's code.
        public static void BuildIrBlob(ParsedAssembly asm)
        {
            int total = 0;
            foreach (var m in asm.ByToken.Values)
                if (m.Lowered != null) total += m.Lowered.Ir.Length + 1;
            var blob = new uint[total];
            int off = 0;
            foreach (var m in asm.ByToken.Values)
            {
                var lm = m.Lowered;
                if (lm == null) continue; // skipped stub (cold enumerator member)
                var ir = lm.Ir;
                int n  = ir.Length;
                lm.IrStart = off;
                Array.Copy(ir, 0, blob, off, n);

                // PatchInsnBranchTargets computes newTarget = t - shift[t]; a constant -off shift
                // relocates every target (including past-end t == n, which lands on the sentinel).
                var shift = new int[n + 1];
                for (int i = 0; i <= n; i++) shift[i] = -off;
                int pos = 0;
                while (pos < n)
                {
                    var op = (Op)ir[pos];
                    int w = OpWidthForCoalesce(op, ir, pos);
                    if (w <= 0)
                        throw new NotSupportedException($"IR blob: unknown width for op {op} in '{m.Name}'");
                    PatchInsnBranchTargets(blob, off + pos, op, shift, ir, pos);
                    pos += w;
                }
                blob[off + n] = (uint)Op.ret_void;
                off += n + 1;
            }
            asm.IrBlob = blob;
        }

        // Simple (unqualified) type name of a TypeRef/TypeDef token, for matching a
        // `constrained.` prefix's type against a flat struct layout's TypeName. Null for
        // TypeSpecs (generic instantiations — never a registered flat struct) or bad tokens.
        static string TokenSimpleTypeName(ParsedAssembly asm, int token)
        {
            try
            {
                var handle = MetadataTokens.Handle(token);
                switch (handle.Kind)
                {
                    case HandleKind.TypeReference:
                        return asm.Reader.GetString(
                            asm.Reader.GetTypeReference((TypeReferenceHandle)handle).Name);
                    case HandleKind.TypeDefinition:
                        return asm.Reader.GetString(
                            asm.Reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name);
                    default:
                        return null!;
                }
            }
            catch (Exception)
            {
                return null!;
            }
        }

        public static LoweredMethod LowerMethod(ParsedMethod method, ParsedAssembly asm)
        {
            var il        = method.IlBytes;
            var ir        = new List<uint>(il.Length * 2);
            var irToIl    = new List<int>(il.Length * 2);
            var strings   = new List<string>();
            var tokens    = new List<int>();

            // Frame layout: [args (one slot each, 0..ArgCount-1),
            //                locals (variable slot count — struct locals occupy ceil(size/4) slots),
            //                eval-stack slots (above)]
            int frameSize = method.ArgCount;
            // Local slot index → first frame-slot index. Filler slots (struct continuation) are
            // not directly addressable but exist in slotTypes/structLayouts to keep AllocSlot monotonic.
            var localFrameSlot = new int[method.LocalCount];

            // Per-slot type table — grows alongside frameSize.
            // Arg slots: slot 0 is `this` for instance methods; then explicit params from ArgSTypes.
            // A flat-struct arg (Vt) occupies ceil(size/4) slots, so args after it shift —
            // argFrameSlot maps arg index → frame slot (identity when no Vt args).
            var slotTypes = new List<SType>(frameSize + 32);
            // Parallel list of per-Vt-slot struct layouts. null for non-Vt slots and filler slots.
            var slotStructs = new List<HostBinding.StructLayout?>(frameSize + 32);
            var argFrameSlot = new int[method.ArgCount];
            bool argIndirect = false;
            {
                int sigParamBase = method.IsStatic ? 0 : 1; // slot 0 is `this` for instance methods
                for (int i = 0; i < method.ArgCount; i++)
                {
                    int sigIdx = i - sigParamBase;
                    SType t = (sigIdx >= 0 && sigIdx < method.ArgSTypes.Length) ? method.ArgSTypes[sigIdx] : SType.O;
                    // `this` of a flat script struct is ALSO Vt (instance methods on flat structs).
                    HostBinding.StructLayout? alay = null;
                    if (sigIdx >= 0 && method.ArgStructLayouts != null && sigIdx < method.ArgStructLayouts.Length)
                        alay = method.ArgStructLayouts[sigIdx];
                    else if (sigIdx < 0 && method.ThisStructLayout != null) { alay = method.ThisStructLayout; t = SType.Vt; }
                    argFrameSlot[i] = slotTypes.Count;
                    if (t == SType.Vt && alay != null)
                    {
                        int slotsNeeded = (alay.Size + 3) / 4;
                        slotTypes.Add(SType.Vt);
                        slotStructs.Add(alay);
                        for (int k = 1; k < slotsNeeded; k++) { slotTypes.Add(SType.O); slotStructs.Add(null); }
                        argIndirect = true;
                    }
                    else
                    {
                        var at = t == SType.Vt ? SType.O : t; // Vt without layout: defensive O
                        slotTypes.Add(at);
                        slotStructs.Add(null);
                        if (at is SType.I8 or SType.R8)
                        {
                            // Wide arg: continuation cell, and later args shift — argFrameSlot maps.
                            slotTypes.Add(SType.O);
                            slotStructs.Add(null);
                            argIndirect = true;
                        }
                    }
                }
                frameSize = slotTypes.Count;
            }
            // Allocate locals — struct locals get Vt + filler slots whenever a layout exists for
            // the local's type. Vt-typed operator dispatchers chain through Vt slots without
            // boxing; only the crossing into a boxed host API pays a single box_vt.
            var localStrs = method.LocalStructLayouts;
            // A local with a FROZEN declared type (LocalSTypes) is typed authoritatively and never
            // re-inferred/retyped — this restores the "one slot ⇒ one type ⇒ one backing store"
            // invariant that last-wins inference broke, so a fast op is never emitted over a slot
            // whose value is actually boxed. Null entries fall through to inference as before.
            var localDeclared = new bool[method.LocalCount];
            for (int i = 0; i < method.LocalCount; i++)
            {
                var lay = localStrs?[i];
                bool useVt = lay != null;
                localFrameSlot[i] = frameSize;
                if (useVt)
                {
                    int slotsNeeded = (lay!.Size + 3) / 4;
                    slotTypes.Add(SType.Vt);
                    slotStructs.Add(lay);
                    for (int k = 1; k < slotsNeeded; k++)
                    {
                        slotTypes.Add(SType.O); // filler slot: never addressed directly
                        slotStructs.Add(null);
                    }
                    frameSize += slotsNeeded;
                }
                else if (method.LocalSTypes?[i] is { } declSt)
                {
                    slotTypes.Add(declSt);   // frozen: declared type, not inferred
                    slotStructs.Add(null);
                    localDeclared[i] = true;
                    frameSize++;
                    if (declSt is SType.I8 or SType.R8)
                    {
                        slotTypes.Add(SType.O); // wide local: continuation cell
                        slotStructs.Add(null);
                        frameSize++;
                    }
                }
                else
                {
                    slotTypes.Add(SType.O); // patched by pre-scan
                    slotStructs.Add(null);
                    frameSize++;
                }
            }
            // Pre-scan: determine local types from first stloc in each slot.
            // Skips struct-typed and declared-typed locals (already classified above).
            PreClassifyLocals(il, method, slotTypes, localFrameSlot, localStrs, localDeclared);

            // Exception clauses: finally is lowered via the push_cont/br_cont continuation chain;
            // catch/filter would need real exception dispatch — fail with the reason instead
            // of a bare opcode number. (Fault clauses were dropped at Parse: their handlers only
            // run under exception dispatch, which the interpreter never does.)
            if (method.HasUnsupportedEhClauses)
                throw new NotSupportedException(
                    $"IR lowering: '{method.Name}' contains a try/catch (or filter) clause — " +
                    "only try/finally is supported by the interpreter");

            // Pre-scan: collect all IL offsets that are branch targets.
            // Used to clear ensuredScriptSlots at basic-block boundaries.
            var ilBranchTargets = CollectIlBranchTargets(il);
            // Finally handler entries are reached via the lowered leave's br — treat them as
            // basic-block boundaries too.
            if (method.FinallyRegions != null)
                foreach (var r in method.FinallyRegions)
                    ilBranchTargets.Add(r.HandlerStart);
            // Fault handlers are never entered (no exception dispatch) but their bodies still
            // lower — mark each entry a basic-block boundary so the linear scan doesn't carry
            // stale eval-stack slot state into the dead region.
            if (method.FaultHandlerStarts != null)
                foreach (var h in method.FaultHandlerStarts)
                    ilBranchTargets.Add(h);

            // Symbolic eval stack — each entry is (frameSlot, SType)
            var evalStack  = new (int slot, SType type)[64];
            int sp         = 0;

            // Address tracking: indexed by frame slot number (slots are allocated monotonically).
            // addrTagBySlot[s]: -2 = normal slot; >= 0 = "frame-slot address" (points to that frame slot);
            //                    -1 = "field address" (use addrFldObjBySlot[s] + addrFldTokBySlot[s]);
            //                    -3 = "array element address" (addrFldObjBySlot[s] = arr slot, addrFldTokBySlot[s] = idx slot).
            // Size 512 is a generous upper bound; slots never exceed a few dozen in practice.
            var addrTagBySlot    = new int[512];
            var addrFldObjBySlot = new int[512];
            var addrFldTokBySlot = new int[512];
            // Byte-offset accumulation for addresses that point INSIDE flat struct bytes:
            //   tag >= 0  → addrByteOffBySlot = offset within the frame Vt slot (0 = whole slot);
            //   tag == -1 → addrFldOffBySlot >= 0 = composed byte offset into the parent
            //               ScriptObject's PrimBytes (-1 = classic token-addressed field phantom).
            // Composition happens along INLINE segments (nested blittable structs); at a
            // reference boundary (O field / array element) the chain REBASES onto the loaded ref.
            var addrByteOffBySlot = new int[512];
            var addrFldOffBySlot  = new int[512];
            // For -3 (array element) phantoms whose ARRAY has flat struct elements: the element
            // layout (consumers materialize via ldelem_vt + write back via stelem_vt).
            var addrElemLayoutBySlot = new HostBinding.StructLayout?[512];
            for (int i = 0; i < 512; i++) { addrTagBySlot[i] = -2; addrFldOffBySlot[i] = -1; }
            // newarr_vt element layouts, resolved at lowering (sparse, keyed by tokIdx).
            Dictionary<int, HostBinding.StructLayout>? layoutByTokIdx = null;
            // Primitive typed-array element types (bool/char/byte/…), resolved at lowering.
            Dictionary<int, Type>? primArrayElemTypeByTokIdx = null;
            // Delegate-creation sites (ldftn + newobj), resolved at lowering (sparse, by tokIdx).
            Dictionary<int, DelegateSite>? delegateSiteByTokIdx = null;

            // Per-local and per-arg type tracking (updated on store, read on load)
            var localTypes = new SType[method.LocalCount]; // synced from slotTypes
            var argTypes   = new SType[method.ArgCount];   // synced from slotTypes

            for (int i = 0; i < method.ArgCount;   i++) argTypes[i]   = slotTypes[argFrameSlot[i]];
            for (int i = 0; i < method.LocalCount; i++) localTypes[i] = slotTypes[localFrameSlot[i]];

            // Parameters holding a BOXED script struct (an O ScriptObject: the struct has a
            // string/long/double field, so it has no flat layout). PushArg clones these on load for
            // struct copy semantics. Index space is the lowerer's (slot 0 = `this` on instance
            // methods, never cloned: struct `this` is byref); ArgScriptStructTypeDefs is per
            // signature parameter. Flat struct params are Vt, byref params are not recorded.
            var argIsBoxedSc = new bool[method.ArgCount];
            if (method.ArgScriptStructTypeDefs is { } argScTdsL)
            {
                int thisOff = method.IsStatic ? 0 : 1;
                for (int i = thisOff; i < method.ArgCount; i++)
                {
                    int sigIdx = i - thisOff;
                    argIsBoxedSc[i] = sigIdx < argScTdsL.Length && argScTdsL[sigIdx] != 0 && argTypes[i] == SType.O;
                }
            }

            // A script-defined struct local is always an O ScriptObject. PreClassifyLocals infers a
            // local's type from the first value stored into it, and a generic `ldelem <T>` (from
            // `s = arr[i]`) can be mis-inferred as I4 — which would then suppress clone-on-load and
            // corrupt struct value semantics. Force these locals back to O.
            if (method.LocalIsScriptStruct != null)
                for (int i = 0; i < method.LocalCount; i++)
                    if (method.LocalIsScriptStruct[i])
                    {
                        localTypes[i] = SType.O;
                        if (localFrameSlot[i] < slotTypes.Count) slotTypes[localFrameSlot[i]] = SType.O;

                        // CLR localsinit: a struct local is usable without any initobj — Roslyn
                        // relies on it for local-function DISPLAY STRUCTS (`V_0.mult = 3` straight
                        // into the uninitialized local, then `call Scale(ref V_0)`). The O slot
                        // starts null here, so create the ScriptObject in the prologue.
                        if (method.LocalScriptStructTypeDefs != null
                            && method.LocalScriptStructTypeDefs[i] is var lsTok && lsTok != 0)
                        {
                            int lsTokIdx = tokens.Count; tokens.Add(lsTok);
                            ir.Add((uint)Op.initobj_script); irToIl.Add(0);
                            ir.Add((uint)localFrameSlot[i]); irToIl.Add(0);
                            ir.Add((uint)lsTokIdx); irToIl.Add(0);
                        }
                    }

            // IL offset → IR word index (for branch patching)
            var ilToIrIp = new Dictionary<int, int>();
            // Forward branches that need patching: (ir_word_index_of_target, il_target_offset)
            var patchList = new List<(int irIdx, int ilTarget)>();
            // Branch-target → authoritative eval-stack depth delivered by its incoming edge(s).
            // Recorded at every FORWARD branch (the delivered depth after the branch's own stack
            // effect). In structured IL all edges into a join agree on depth, so when the linear
            // walk reaches a target this value is authoritative: it drops any dead carry left on the
            // stack after a preceding unconditional branch (e.g. a ternary then-arm result at the
            // ELSE label) while preserving values that live *beneath* a short-circuit region (e.g.
            // the `==` left operand while its right side is a `&&`/`||` chain).
            var branchTargetDepth = new Dictionary<int, int>();
            // Record a forward branch's delivered depth at its target (backward edges — loop headers —
            // are already past in the linear walk, so they never need reconciling here).
            void RecordJoinDepth(int src, int target, int depth)
            {
                if (target > src) branchTargetDepth[target] = depth;
            }
            // Ternary / short-circuit merge: END offset -> the fresh slot BOTH branches converge into.
            // Each branch moves its result into this slot at its own tail; the join reads it. Using a
            // fresh slot (never a branch's own value slot) is essential — the value slot may be a real
            // local, or the two branches may leave their results in different slots.
            var mergeSlotForEnd   = new Dictionary<int, (int slot, SType type)>();

            // O slots known to hold a non-null ScriptObject in the current basic block.
            // Cleared at branch targets and after any branch/call instruction.
            // Allows the Stfld lowerer to skip redundant ensure_script ops.
            var ensuredScriptSlots = new HashSet<int>();

            // Tracks whether the immediately preceding instruction was a `dup`. Roslyn emits
            // `dup; brtrue`/`dup; brfalse` only for the null-coalescing / null-conditional idioms
            // (`a ?? b`, `a?.b`), where the value left under the popped condition is a genuine
            // forward-join carry. A plain conditional branch with no preceding dup (if/while/&&/||)
            // leaves only base operands beneath, which must NOT be merged.
            bool lastWasDup = false;

            // Type token of a pending `constrained.` prefix, consumed by the next call lowering.
            // Disambiguates a Vt receiver: constrained to the slot's OWN struct type → box the
            // whole struct; constrained to a primitive → extract the field at the byte offset.
            int constrainedTok = 0;

            // Method token of a pending ldftn/ldvirtftn, consumed by the delegate-ctor Newobj that
            // C# always emits immediately after (`ldnull|recv[; dup]; ld(virt)ftn M; newobj D`).
            // Anything else between them is unsupported IL for this lowerer.
            int pendingFtnTok = 0;
            bool pendingFtnVirt = false; // ldvirtftn (vs ldftn): resolve the override at delegate creation

            // Byref locals carry an ADDRESS. Roslyn spills a managed pointer (&x.f from ldflda,
            // &a[i] from ldelema, &local from ldloca) into a local when control flow sits between
            // the address and its use — e.g. `x.f op= (cond switch {...})` or `a[i].Mut((cond)
            // switch {...})`, where the switch-expr's branches force the spill. That address is a
            // lowering-time PHANTOM (per-slot addr* metadata, no runtime value), so a plain
            // stloc/ldloc dropped it and the later stind/call saw a non-address operand. Carry the
            // phantom through the local by stashing its metadata ON THE LOCAL'S FRAME SLOT (the
            // addr* arrays are per-slot and stable) and restoring a fresh phantom on load.
            // (found by fuzzing: switch-expr in a compound-assign RHS / struct-array Mut arg.)
            bool TryStoreLocalPhantom(int localIdx)
            {
                int src = evalStack[sp - 1].Item1;
                if (addrTagBySlot[src] == -2) return false; // ordinary value → normal stloc
                int dst = localFrameSlot[localIdx];
                addrTagBySlot[dst]        = addrTagBySlot[src];
                addrFldObjBySlot[dst]     = addrFldObjBySlot[src];
                addrFldTokBySlot[dst]     = addrFldTokBySlot[src];
                addrByteOffBySlot[dst]    = addrByteOffBySlot[src];
                addrFldOffBySlot[dst]     = addrFldOffBySlot[src];
                addrElemLayoutBySlot[dst] = addrElemLayoutBySlot[src];
                sp--; // consume; the phantom is metadata-only, no runtime mov
                return true;
            }
            bool TryLoadLocalPhantom(int localIdx)
            {
                int srcSlot = localFrameSlot[localIdx];
                if (addrTagBySlot[srcSlot] == -2) return false;
                int ph = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                addrTagBySlot[ph]        = addrTagBySlot[srcSlot];
                addrFldObjBySlot[ph]     = addrFldObjBySlot[srcSlot];
                addrFldTokBySlot[ph]     = addrFldTokBySlot[srcSlot];
                addrByteOffBySlot[ph]    = addrByteOffBySlot[srcSlot];
                addrFldOffBySlot[ph]     = addrFldOffBySlot[srcSlot];
                addrElemLayoutBySlot[ph] = addrElemLayoutBySlot[srcSlot];
                evalStack[sp++] = (ph, SType.O);
                return true;
            }

            int ip = 0;
            while (ip < il.Length)
            {
                // The addr phantom tables are indexed by frame slot and evalStack by stack depth;
                // both start at a size that covers ordinary methods, but generated/pathological
                // code can allocate thousands of temp slots (found by fuzzing: IndexOutOfRange on
                // addrTagBySlot). Grow here, once per instruction — no single instruction
                // allocates anywhere near 256 slots, and the locals are re-read by every helper
                // call below, so Array.Resize's new arrays propagate.
                if (frameSize + 256 > addrTagBySlot.Length)
                {
                    int oldCap = addrTagBySlot.Length;
                    int newCap = Math.Max(oldCap * 2, frameSize + 512);
                    Array.Resize(ref addrTagBySlot, newCap);
                    Array.Resize(ref addrFldObjBySlot, newCap);
                    Array.Resize(ref addrFldTokBySlot, newCap);
                    Array.Resize(ref addrByteOffBySlot, newCap);
                    Array.Resize(ref addrFldOffBySlot, newCap);
                    Array.Resize(ref addrElemLayoutBySlot, newCap);
                    // Same defaults as the initial fill: -2 = normal slot, -1 = token-addressed.
                    for (int i = oldCap; i < newCap; i++) { addrTagBySlot[i] = -2; addrFldOffBySlot[i] = -1; }
                }
                if (sp + 8 > evalStack.Length)
                    Array.Resize(ref evalStack, evalStack.Length * 2);

                int instrStart = ip;
                // Basic-block boundary: clear ensuredScriptSlots when the current instruction
                // is a branch target (we may have arrived via a backward edge or a jump).
                if (ilBranchTargets.Contains(instrStart)) ensuredScriptSlots.Clear();
                // Reconcile the abstract eval-stack depth with the depth the branch edges deliver.
                // The base slots are identical on every edge (they were computed before the diamond),
                // so the current walk's evalStack entries remain valid — only the depth needs fixing.
                if (branchTargetDepth.TryGetValue(instrStart, out var joinDepth))
                    sp = joinDepth;
                // Ternary/merge join: the second branch's result is on top; move it into the merge
                // slot M so both paths converge on M. Done BEFORE recording ilToIrIp[ip] so the first
                // branch's `br` (patched to this offset) lands AFTER the mov, not on top of it.
                if (mergeSlotForEnd.TryGetValue(instrStart, out var mergeJoin))
                {
                    if (sp > 0)
                    {
                        var (joinTop, _) = evalStack[sp - 1];
                        if (joinTop != mergeJoin.slot)
                            Emit3(ir, irToIl, mergeJoin.type == SType.Vt ? Op.mov_vt : Op.mov,
                                  mergeJoin.slot, joinTop, instrStart);
                        evalStack[sp - 1] = (mergeJoin.slot, mergeJoin.type);
                    }
                    mergeSlotForEnd.Remove(instrStart);
                }
                // Record the mapping BEFORE emitting IR words
                int irIp = ir.Count;
                ilToIrIp[ip] = irIp;

                byte b = il[ip++];
                ILOpCode op = b == 0xFE ? (ILOpCode)(0xFE00 | il[ip++]) : (ILOpCode)b;

                // A loaded function pointer is only modeled as the immediate operand of a
                // delegate ctor; any other consumer would see a placeholder slot.
                if (pendingFtnTok != 0 && op != ILOpCode.Newobj)
                    throw new NotSupportedException(
                        $"IR lowering: ldftn in '{method.Name}' at IL+0x{instrStart:X4} is not part of a delegate construction");

                bool afterDup = lastWasDup;
                lastWasDup = false;

                switch (op)
                {
                    case ILOpCode.Nop: break;

                    case ILOpCode.Pop:
                        sp--;
                        break;

                    case ILOpCode.Dup:
                    {
                        lastWasDup = true;
                        var (src, t) = evalStack[sp - 1];
                        // Phantom-address slots aren't real values — duplicate by sharing the
                        // tag without emitting a mov. Required for `arr[i] += v` (ldelema; dup;
                        // ldind; add; stind) so both ldind and stind see the same array address.
                        if (addrTagBySlot[src] != -2)
                        {
                            int phDup = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            addrTagBySlot[phDup]     = addrTagBySlot[src];
                            addrFldObjBySlot[phDup]  = addrFldObjBySlot[src];
                            addrFldTokBySlot[phDup]  = addrFldTokBySlot[src];
                            addrByteOffBySlot[phDup] = addrByteOffBySlot[src];
                            addrFldOffBySlot[phDup]  = addrFldOffBySlot[src];
                            addrElemLayoutBySlot[phDup] = addrElemLayoutBySlot[src];
                            evalStack[sp++] = (phDup, SType.O);
                            break;
                        }
                        // CLR `dup` of a FLAT struct is a full value copy. A 4-byte `mov` copied
                        // only the first word, leaving every later field poisoned/stale — release
                        // codegen's `ldloc <struct>; dup` (e.g. a `(v.f, v.f)` tuple) then read a
                        // garbage field (found by fuzzing under release+poison). Copy all the bytes
                        // via mov_vt over a layout-carrying dst slot.
                        if (t == SType.Vt && slotStructs[src] is { } dupLay)
                        {
                            int vdst = AllocStructSlot(ref frameSize, slotTypes, slotStructs, dupLay);
                            evalStack[sp++] = (vdst, SType.Vt);
                            Emit3(ir, irToIl, Op.mov_vt, vdst, src, instrStart);
                            break;
                        }
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, t, null);
                        evalStack[sp++] = (dst, t);
                        // A boxed script-struct rides an O slot, so a plain mov would alias the two
                        // copies — release codegen's `ldloc; dup; stloc` then let a write through one
                        // corrupt the other. clone_sc clones only script-structs and passes
                        // references through, so it is correct for every O value (strings/arrays/
                        // class instances keep their identity).
                        Emit3(ir, irToIl, t == SType.O ? Op.clone_sc : Op.mov, dst, src, instrStart);
                        break;
                    }

                    // --- Constants ---
                    case ILOpCode.Ldc_i4_m1: PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, -1, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_0:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 0, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_1:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 1, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_2:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 2, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_3:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 3, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_4:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 4, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_5:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 5, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_6:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 6, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_7:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 7, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_8:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, 8, SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4_s:  PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, (int)(sbyte)il[ip++], SType.I4, Op.ldc_i4, instrStart); break;
                    case ILOpCode.Ldc_i4:
                    {
                        int imm = BitConverter.ToInt32(il, ip); ip += 4;
                        PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, imm, SType.I4, Op.ldc_i4, instrStart);
                        break;
                    }
                    case ILOpCode.Ldc_r4:
                    {
                        // Store float bits as int (bit-identical, read back with UInt32BitsToSingle in executor)
                        int imm = (int)BitConverter.ToUInt32(il, ip); ip += 4;
                        PushConst(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, imm, SType.R4, Op.ldc_r4, instrStart);
                        break;
                    }
                    case ILOpCode.Ldc_r8:
                    {
                        // Real double constant into a wide R8 slot (bits split across two IR words).
                        ulong bits = BitConverter.ToUInt64(il, ip); ip += 8;
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.R8, null);
                        evalStack[sp++] = (dst, SType.R8);
                        ir.Add((uint)Op.ldc_r8); irToIl.Add(instrStart);
                        ir.Add((uint)dst); irToIl.Add(instrStart);
                        ir.Add((uint)(bits & 0xFFFFFFFF)); irToIl.Add(instrStart);
                        ir.Add((uint)(bits >> 32)); irToIl.Add(instrStart);
                        break;
                    }
                    case ILOpCode.Ldc_i8:
                    {
                        ulong bits = BitConverter.ToUInt64(il, ip); ip += 8;
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I8, null);
                        evalStack[sp++] = (dst, SType.I8);
                        ir.Add((uint)Op.ldc_i8); irToIl.Add(instrStart);
                        ir.Add((uint)dst); irToIl.Add(instrStart);
                        ir.Add((uint)(bits & 0xFFFFFFFF)); irToIl.Add(instrStart);
                        ir.Add((uint)(bits >> 32)); irToIl.Add(instrStart);
                        break;
                    }
                    case ILOpCode.Ldnull:
                    {
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp++] = (dst, SType.O);
                        Emit2(ir, irToIl, Op.ldnull, dst, instrStart);
                        break;
                    }
                    case ILOpCode.Ldstr:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        string s = asm.Reader.GetUserString(MetadataTokens.UserStringHandle(tok & 0x00FFFFFF));
                        int strIdx = strings.Count; strings.Add(s);
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp++] = (dst, SType.O);
                        Emit3(ir, irToIl, Op.ldstr, dst, strIdx, instrStart);
                        break;
                    }

                    // --- Locals ---
                    case ILOpCode.Ldloc_0: if (!TryLoadLocalPhantom(0)) PushLocal(ir, irToIl, evalStack, ref sp, ref frameSize, 0, localFrameSlot, localTypes, slotTypes, slotStructs, method.LocalIsScriptStruct, instrStart); break;
                    case ILOpCode.Ldloc_1: if (!TryLoadLocalPhantom(1)) PushLocal(ir, irToIl, evalStack, ref sp, ref frameSize, 1, localFrameSlot, localTypes, slotTypes, slotStructs, method.LocalIsScriptStruct, instrStart); break;
                    case ILOpCode.Ldloc_2: if (!TryLoadLocalPhantom(2)) PushLocal(ir, irToIl, evalStack, ref sp, ref frameSize, 2, localFrameSlot, localTypes, slotTypes, slotStructs, method.LocalIsScriptStruct, instrStart); break;
                    case ILOpCode.Ldloc_3: if (!TryLoadLocalPhantom(3)) PushLocal(ir, irToIl, evalStack, ref sp, ref frameSize, 3, localFrameSlot, localTypes, slotTypes, slotStructs, method.LocalIsScriptStruct, instrStart); break;
                    case ILOpCode.Ldloc_s: { int li = il[ip++]; if (!TryLoadLocalPhantom(li)) PushLocal(ir, irToIl, evalStack, ref sp, ref frameSize, li, localFrameSlot, localTypes, slotTypes, slotStructs, method.LocalIsScriptStruct, instrStart); break; }
                    case ILOpCode.Ldloc:   { int li = ReadU16(il, ref ip); if (!TryLoadLocalPhantom(li)) PushLocal(ir, irToIl, evalStack, ref sp, ref frameSize, li, localFrameSlot, localTypes, slotTypes, slotStructs, method.LocalIsScriptStruct, instrStart); break; }
                    case ILOpCode.Stloc_0: if (!TryStoreLocalPhantom(0)) { addrTagBySlot[localFrameSlot[0]] = -2; StoreLocal(ir, irToIl, evalStack, ref sp, 0, localFrameSlot, localTypes, slotTypes, slotStructs, localDeclared, ref frameSize, instrStart); } break;
                    case ILOpCode.Stloc_1: if (!TryStoreLocalPhantom(1)) { addrTagBySlot[localFrameSlot[1]] = -2; StoreLocal(ir, irToIl, evalStack, ref sp, 1, localFrameSlot, localTypes, slotTypes, slotStructs, localDeclared, ref frameSize, instrStart); } break;
                    case ILOpCode.Stloc_2: if (!TryStoreLocalPhantom(2)) { addrTagBySlot[localFrameSlot[2]] = -2; StoreLocal(ir, irToIl, evalStack, ref sp, 2, localFrameSlot, localTypes, slotTypes, slotStructs, localDeclared, ref frameSize, instrStart); } break;
                    case ILOpCode.Stloc_3: if (!TryStoreLocalPhantom(3)) { addrTagBySlot[localFrameSlot[3]] = -2; StoreLocal(ir, irToIl, evalStack, ref sp, 3, localFrameSlot, localTypes, slotTypes, slotStructs, localDeclared, ref frameSize, instrStart); } break;
                    case ILOpCode.Stloc_s: { int li = il[ip++]; if (!TryStoreLocalPhantom(li)) { addrTagBySlot[localFrameSlot[li]] = -2; StoreLocal(ir, irToIl, evalStack, ref sp, li, localFrameSlot, localTypes, slotTypes, slotStructs, localDeclared, ref frameSize, instrStart); } break; }
                    case ILOpCode.Stloc:   { int li = ReadU16(il, ref ip); if (!TryStoreLocalPhantom(li)) { addrTagBySlot[localFrameSlot[li]] = -2; StoreLocal(ir, irToIl, evalStack, ref sp, li, localFrameSlot, localTypes, slotTypes, slotStructs, localDeclared, ref frameSize, instrStart); } break; }

                    // ldloca / ldloca.s — push a phantom address of the local's frame slot.
                    // The consuming instruction (ldind.*, stind.*, call .ctor) resolves it.
                    case ILOpCode.Ldloca_s:
                    case ILOpCode.Ldloca:
                    {
                        int localIdx = op == ILOpCode.Ldloca_s ? il[ip++] : ReadU16(il, ref ip);
                        int frameSlot = localFrameSlot[localIdx];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null); // phantom O slot
                        addrTagBySlot[dst] = frameSlot; // frame-slot address
                        evalStack[sp++] = (dst, SType.O);
                        break;
                    }
                    // ldarga / ldarga.s — push a phantom address of the arg's frame slot.
                    case ILOpCode.Ldarga_s:
                    case ILOpCode.Ldarga:
                    {
                        int argIdx = op == ILOpCode.Ldarga_s ? il[ip++] : ReadU16(il, ref ip);
                        int frameSlot = argFrameSlot[argIdx];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null); // phantom O slot
                        addrTagBySlot[dst] = frameSlot; // frame-slot address
                        evalStack[sp++] = (dst, SType.O);
                        break;
                    }

                    // --- Args ---
                    case ILOpCode.Ldarg_0: PushArg(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, argIsBoxedSc, instrStart, 0, argTypes, argFrameSlot); break;
                    case ILOpCode.Ldarg_1: PushArg(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, argIsBoxedSc, instrStart, 1, argTypes, argFrameSlot); break;
                    case ILOpCode.Ldarg_2: PushArg(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, argIsBoxedSc, instrStart, 2, argTypes, argFrameSlot); break;
                    case ILOpCode.Ldarg_3: PushArg(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, argIsBoxedSc, instrStart, 3, argTypes, argFrameSlot); break;
                    case ILOpCode.Ldarg_s: PushArg(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, argIsBoxedSc, instrStart, il[ip++], argTypes, argFrameSlot); break;
                    case ILOpCode.Ldarg:   PushArg(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, argIsBoxedSc, instrStart, ReadU16(il, ref ip), argTypes, argFrameSlot); break;
                    case ILOpCode.Starg_s: StoreArg(ir, irToIl, evalStack, ref sp, il[ip++], argTypes, argFrameSlot, slotTypes, slotStructs, ref frameSize, instrStart); break;
                    case ILOpCode.Starg:   StoreArg(ir, irToIl, evalStack, ref sp, ReadU16(il, ref ip), argTypes, argFrameSlot, slotTypes, slotStructs, ref frameSize, instrStart); break;

                    // --- Arithmetic ---
                    case ILOpCode.Add: EmitBinop(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.add_i4, Op.add_r4, Op.add_i4_nn, Op.add_r4_nn, instrStart); break;
                    case ILOpCode.Sub: EmitBinop(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.sub_i4, Op.sub_r4, Op.sub_i4_nn, Op.sub_r4_nn, instrStart); break;
                    case ILOpCode.Mul: EmitBinop(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.mul_i4, Op.mul_r4, Op.mul_i4_nn, Op.mul_r4_nn, instrStart); break;
                    case ILOpCode.Div: EmitBinop(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.div_i4, Op.div_r4, Op.div_i4_nn, Op.div_r4_nn, instrStart); break;
                    case ILOpCode.Rem: EmitBinop(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.rem_i4, Op.rem_r4, Op.rem_i4_nn, Op.rem_r4_nn, instrStart); break;
                    case ILOpCode.Div_un: EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.div_un_i4, Op.div_un_i4_nn, instrStart); break;
                    case ILOpCode.Rem_un: EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.rem_un_i4, Op.rem_un_i4_nn, instrStart); break;
                    case ILOpCode.Neg: EmitUnop(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.neg_i4, Op.neg_r4, Op.neg_i4_n, Op.neg_r4_n, instrStart); break;
                    case ILOpCode.And: EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.and_i4, Op.and_i4_nn, instrStart); break;
                    case ILOpCode.Or:  EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.or_i4,  Op.or_i4_nn,  instrStart); break;
                    case ILOpCode.Xor: EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.xor_i4, Op.xor_i4_nn, instrStart); break;
                    case ILOpCode.Not: EmitUnopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.not_i4, Op.not_i4_n, instrStart); break;
                    case ILOpCode.Shl: EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.shl_i4, Op.shl_i4_nn, instrStart); break;
                    case ILOpCode.Shr: EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.shr_i4, Op.shr_i4_nn, instrStart); break;
                    case ILOpCode.Shr_un: EmitBinopI4(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.shr_un_i4, Op.shr_un_i4_nn, instrStart); break;

                    // --- Comparisons ---
                    case ILOpCode.Ceq:
                    {
                        var (s2, t2) = evalStack[--sp];
                        var (s1, t1) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        Op cop;
                        if (t1 == SType.R8 || t2 == SType.R8)
                            cop = Op.ceq_r8;
                        else if (t1 == SType.I8 || t2 == SType.I8)
                            cop = Op.ceq_i8;
                        else if (t1 == SType.R4 || t2 == SType.R4)
                            cop = (t1 == SType.R4 && t2 == SType.R4 && slotTypes[s1] == SType.R4 && slotTypes[s2] == SType.R4) ? Op.ceq_r4_nn : Op.ceq_r4;
                        else if (t1 == SType.I4 || t2 == SType.I4)
                            // ceq_i4 reads each operand from numStack or refStack based on slotT at runtime,
                            // so it handles mixed O/I4 cases (e.g. boxed int from a non-flat host call vs. ldc.i4).
                            cop = (slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4) ? Op.ceq_i4_nn : Op.ceq_i4;
                        else
                            cop = Op.ceq_o;
                        evalStack[sp - 1] = (dst, SType.I4);
                        Emit4(ir, irToIl, cop, dst, s1, s2, instrStart);
                        break;
                    }
                    case ILOpCode.Cgt:
                    {
                        var (s2, t2) = evalStack[--sp];
                        var (s1, t1) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        Op cop;
                        if (t1 == SType.R8 || t2 == SType.R8)
                            cop = Op.cgt_r8;
                        else if (t1 == SType.I8 || t2 == SType.I8)
                            cop = Op.cgt_i8;
                        else if (t1 == SType.R4 || t2 == SType.R4)
                            cop = (t1 == SType.R4 && t2 == SType.R4 && slotTypes[s1] == SType.R4 && slotTypes[s2] == SType.R4) ? Op.cgt_r4_nn : Op.cgt_r4;
                        else
                            cop = (slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4) ? Op.cgt_i4_nn : Op.cgt_i4;
                        evalStack[sp - 1] = (dst, SType.I4);
                        Emit4(ir, irToIl, cop, dst, s1, s2, instrStart);
                        break;
                    }
                    case ILOpCode.Cgt_un:
                    {
                        var (s2, t2) = evalStack[--sp];
                        var (s1, t1) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        Op cop;
                        if (t1 == SType.R8 || t2 == SType.R8)
                            cop = Op.cgt_un_r8;
                        else if (t1 == SType.I8 || t2 == SType.I8)
                            cop = Op.cgt_un_i8;
                        else if (t1 == SType.R4 || t2 == SType.R4)
                            cop = Op.cgt_un_r4; // float `<=` lowers to `cgt.un` — unordered greater-than
                        else if (t1 == SType.O)
                            cop = Op.cgt_un_o;
                        else
                            cop = (slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4) ? Op.cgt_un_i4_nn : Op.cgt_un_i4;
                        evalStack[sp - 1] = (dst, SType.I4);
                        Emit4(ir, irToIl, cop, dst, s1, s2, instrStart);
                        break;
                    }
                    case ILOpCode.Clt:
                    {
                        var (s2, t2) = evalStack[--sp];
                        var (s1, t1) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        Op cop;
                        if (t1 == SType.R8 || t2 == SType.R8)
                            cop = Op.clt_r8;
                        else if (t1 == SType.I8 || t2 == SType.I8)
                            cop = Op.clt_i8;
                        else if (t1 == SType.R4 || t2 == SType.R4)
                            cop = (t1 == SType.R4 && t2 == SType.R4 && slotTypes[s1] == SType.R4 && slotTypes[s2] == SType.R4) ? Op.clt_r4_nn : Op.clt_r4;
                        else
                            cop = (slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4) ? Op.clt_i4_nn : Op.clt_i4;
                        evalStack[sp - 1] = (dst, SType.I4);
                        Emit4(ir, irToIl, cop, dst, s1, s2, instrStart);
                        break;
                    }
                    case ILOpCode.Clt_un:
                    {
                        var (s2, t2) = evalStack[--sp];
                        var (s1, t1) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        evalStack[sp - 1] = (dst, SType.I4);
                        if (t1 == SType.R8 || t2 == SType.R8)
                        {
                            Emit4(ir, irToIl, Op.clt_un_r8, dst, s1, s2, instrStart);
                            break;
                        }
                        if (t1 == SType.I8 || t2 == SType.I8)
                        {
                            Emit4(ir, irToIl, Op.clt_un_i8, dst, s1, s2, instrStart);
                            break;
                        }
                        if (t1 == SType.R4 || t2 == SType.R4)
                        {
                            Emit4(ir, irToIl, Op.clt_un_r4, dst, s1, s2, instrStart); // float `>=` lowers to `clt.un`
                            break;
                        }
                        bool both_i4 = t1 == SType.I4 && t2 == SType.I4 && slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4;
                        Emit4(ir, irToIl, both_i4 ? Op.clt_un_i4_nn : Op.clt_un_i4, dst, s1, s2, instrStart);
                        break;
                    }

                    // Box is a no-op: values already live unboxed in the frame; boxing
                    // happens at host-call boundaries.
                    case ILOpCode.Box:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        // Materialize a REAL, correctly-typed boxed object for primitive value types.
                        // Leaving box a no-op kept the value in its I4/R4 slot: the target `object`
                        // local was then classified I4 (so `object a=5; object b=5; a==b` compared by
                        // value, not reference) and bool/char boxed as int (Debug.Log(true) -> "1").
                        // Reference types are already O (box is identity); flat structs box on store.
                        int tc = BoxPrimTypeCode(asm, tok);
                        if (tc >= 0)
                        {
                            var (src, _) = evalStack[sp - 1];
                            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            evalStack[sp - 1] = (dst, SType.O);
                            Emit4(ir, irToIl, Op.box_prim, dst, src, tc, instrStart);
                        }
                        // HOST enum: box as the real enum type, not the underlying int —
                        // `$"state={m}"` / Debug.Log(m) must render the member name. Script-
                        // declared enums (TypeDefs) have no runtime Type and keep the int form.
                        else if (ResolveHostEnumType(asm, tok) is { } enumType)
                        {
                            int tokIdx = tokens.Count; tokens.Add(tok);
                            asm.TokenTypes[tok] = enumType; // executor resolves through TokenTypes
                            var (src, _) = evalStack[sp - 1];
                            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            evalStack[sp - 1] = (dst, SType.O);
                            Emit4(ir, irToIl, Op.box_enum, dst, src, tokIdx, instrStart);
                        }
                        break;
                    }

                    // --- Castclass / isinst / unbox.any / ldtoken ---
                    case ILOpCode.Castclass:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        var (src, _) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp - 1] = (dst, SType.O);
                        Emit4(ir, irToIl, Op.castclass, dst, src, tokIdx, instrStart);
                        break;
                    }
                    case ILOpCode.Isinst:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        var (src, _) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp - 1] = (dst, SType.O);
                        Emit4(ir, irToIl, Op.isinst, dst, src, tokIdx, instrStart);
                        break;
                    }
                    case ILOpCode.Unbox_any:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        var (src, _) = evalStack[sp - 1];
                        // Type the result by the TARGET, not the boxed source: `(int)(object)x` must
                        // yield an I4 value, not an O. Keeping the source's O tag reclassified the
                        // destination local as a ref slot (e.g. `int acc2 = (int)(object)k` made acc2
                        // O-typed, so later int reads saw 0). Primitives -> I4/R4; ref/struct/enum -> O.
                        var targetSt = PrimitiveSTypeForTypeToken(asm, tok) ?? SType.O;
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, targetSt, null);
                        evalStack[sp - 1] = (dst, targetSt);
                        Emit4(ir, irToIl, Op.unbox_any, dst, src, tokIdx, instrStart);
                        break;
                    }
                    case ILOpCode.Ldtoken:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp++] = (dst, SType.O);
                        Emit3(ir, irToIl, Op.ldtoken, dst, tokIdx, instrStart);
                        break;
                    }

                    // --- Conversions ---
                    case ILOpCode.Conv_i4:
                    case ILOpCode.Conv_u4:
                    {
                        var (src, st) = evalStack[sp - 1];
                        if (st == SType.I4) break; // already int
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        evalStack[sp - 1] = (dst, SType.I4);
                        // An O source is a BOXED numeric (a generic host/delegate call result —
                        // `(int)dg(x)` where dg is Func<long,long>): a coercing mov reads it via
                        // CoerceBoxedI4 (truncates boxed long/double). conv_r4_i4 would misread the
                        // box as raw float bits (found by fuzzing under release codegen).
                        var cvOp = st == SType.I8 ? Op.conv_i4_i8
                                 : st == SType.R8 ? Op.conv_i4_r8
                                 : st == SType.O ? Op.mov
                                 : Op.conv_r4_i4;
                        Emit3(ir, irToIl, cvOp, dst, src, instrStart);
                        break;
                    }
                    case ILOpCode.Conv_i8:
                    case ILOpCode.Conv_u8:
                    {
                        var (src, st) = evalStack[sp - 1];
                        if (st == SType.I8) break; // already 64-bit
                        // conv.i8 sign-extends an i4; conv.u8 zero-extends it. From float sources
                        // only the SIGNED form is supported — (ulong)someFloat stays loud.
                        bool zext = op == ILOpCode.Conv_u8;
                        Op cvOp;
                        if (st == SType.R4) { if (zext) goto notSupported; cvOp = Op.conv_i8_r4; }
                        else if (st == SType.R8) { if (zext) goto notSupported; cvOp = Op.conv_i8_r8; }
                        // O source is a boxed numeric (generic call result): a coercing mov reads
                        // it via RdI8/CoerceBoxedI8, which keeps the full 64 bits — conv_i8_i4 would
                        // truncate a boxed long through int first (see Conv_i4).
                        else if (st == SType.O) cvOp = Op.mov;
                        else cvOp = zext ? Op.conv_i8_u4 : Op.conv_i8_i4;
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I8, null);
                        evalStack[sp - 1] = (dst, SType.I8);
                        Emit3(ir, irToIl, cvOp, dst, src, instrStart);
                        break;
                    }
                    case ILOpCode.Conv_r_un:
                    {
                        // Unsigned int → floating: ulong/uint reinterpreted unsigned, result R8
                        // (a following conv.r4 narrows it).
                        var (src, st) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.R8, null);
                        evalStack[sp - 1] = (dst, SType.R8);
                        if (st == SType.I8)
                            Emit3(ir, irToIl, Op.conv_r8_u8, dst, src, instrStart);
                        else
                        {
                            // u4 source: zero-extend to I8 first, then unsigned-convert.
                            int wide = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I8, null);
                            Emit3(ir, irToIl, Op.conv_i8_u4, wide, src, instrStart);
                            Emit3(ir, irToIl, Op.conv_r8_u8, dst, wide, instrStart);
                        }
                        break;
                    }
                    case ILOpCode.Conv_i1:
                    {
                        var (src2, _) = evalStack[sp - 1];
                        int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        evalStack[sp - 1] = (dst2, SType.I4);
                        Emit3(ir, irToIl, Op.conv_i4_i1, dst2, src2, instrStart);
                        break;
                    }
                    case ILOpCode.Conv_u1:
                    {
                        var (src2, _) = evalStack[sp - 1];
                        int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        evalStack[sp - 1] = (dst2, SType.I4);
                        Emit3(ir, irToIl, Op.conv_i4_u1, dst2, src2, instrStart);
                        break;
                    }
                    case ILOpCode.Conv_i2:
                    {
                        var (src2, _) = evalStack[sp - 1];
                        int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        evalStack[sp - 1] = (dst2, SType.I4);
                        Emit3(ir, irToIl, Op.conv_i4_i2, dst2, src2, instrStart);
                        break;
                    }
                    case ILOpCode.Conv_u2:
                    {
                        var (src2, _) = evalStack[sp - 1];
                        int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        evalStack[sp - 1] = (dst2, SType.I4);
                        Emit3(ir, irToIl, Op.conv_i4_u2, dst2, src2, instrStart);
                        break;
                    }
                    case ILOpCode.Conv_r4:
                    {
                        var (src, st) = evalStack[sp - 1];
                        if (st == SType.R4) break;
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.R4, null);
                        evalStack[sp - 1] = (dst, SType.R4);
                        var cvOp = st == SType.I8 ? Op.conv_r4_i8
                                 : st == SType.R8 ? Op.conv_r4_r8
                                 : st == SType.O ? Op.mov // boxed numeric → R4 via CoerceBoxedR4 (see Conv_i4)
                                 : Op.conv_i4_r4;
                        Emit3(ir, irToIl, cvOp, dst, src, instrStart);
                        break;
                    }
                    case ILOpCode.Conv_r8:
                    {
                        // Honest double now that R8 slots exist (this retires the double-as-float
                        // divergence the analyzer guarded with MR002).
                        var (src, st) = evalStack[sp - 1];
                        if (st == SType.R8) break;
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.R8, null);
                        evalStack[sp - 1] = (dst, SType.R8);
                        var cvOp = st == SType.I8 ? Op.conv_r8_i8
                                 : st == SType.R4 ? Op.conv_r8_r4
                                 : st == SType.O ? Op.mov // boxed numeric → R8 via RdR8/CoerceBoxedR8 (see Conv_i4)
                                 : Op.conv_r8_i4;
                        Emit3(ir, irToIl, cvOp, dst, src, instrStart);
                        break;
                    }

                    // --- Branches ---
                    case ILOpCode.Br_s:
                    case ILOpCode.Br:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Br_s);
                        // Ternary / short-circuit value pattern: a FORWARD br that is the tail of a
                        // value-producing arm — `<then>; br END; ELSE: <else>; END:`. The top-of-stack
                        // is this arm's result; move it into a fresh merge slot M so the other arm can
                        // converge on the same M at END.
                        //
                        // The arm is genuine when the label that follows the br (`ip`) is the ELSE
                        // label — i.e. an earlier branch already delivered depth `sp-1` there, so ELSE
                        // drops exactly this one result. When the recorded depth is `sp` instead, the
                        // value on top is NOT an arm result but a live operand carried beneath a
                        // short-circuit region (the `v0 == (… || …)` case): merging/dropping it would
                        // corrupt the stack, so we leave it alone and let RecordJoinDepth carry it.
                        //
                        // It is ALSO genuine — regardless of the ELSE-adjacency heuristic above — when
                        // `target` is already a known merge join (an earlier sibling arm already
                        // converged there): every further forward edge into an established merge point
                        // must converge its own top-of-stack into the same slot too. This matters for a
                        // degenerate zero-displacement `br` (target == ip, e.g. when Roslyn collapses two
                        // constant-valued ternary arms into one shared tail): such a br still carries a
                        // value that MUST be merged, but its "label right after it" is the merge point
                        // itself rather than a distinct ELSE label, so the ELSE-adjacency check alone
                        // (which additionally requires target > ip) never fires for it. Without this,
                        // the arm's value is silently dropped and the merge slot keeps a stale/default
                        // value when that arm is the one actually taken at runtime.
                        if (target >= ip && sp > 0 &&
                            (mergeSlotForEnd.ContainsKey(target) ||
                             (branchTargetDepth.TryGetValue(ip, out var elseDepth) && elseDepth == sp - 1)))
                        {
                            MergeTopIntoJoinSlot(ir, irToIl, evalStack, sp, ref frameSize, slotTypes, slotStructs, mergeSlotForEnd, target, instrStart);
                        }
                        RecordJoinDepth(instrStart, target, sp); // br leaves the stack unchanged
                        int patchIdx = ir.Count + 1;
                        Emit2(ir, irToIl, Op.br, -1, instrStart);
                        patchList.Add((patchIdx, target));
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    // leave empties the eval stack, then runs every enclosing finally whose try
                    // region contains this instruction but not the target (innermost first),
                    // then continues at the target. Lowered as a continuation chain: push the
                    // final target and the outer handlers' entries, branch to the innermost
                    // handler; each handler's endfinally (br_cont) pops the next address.
                    case ILOpCode.Leave_s:
                    case ILOpCode.Leave:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Leave_s);
                        sp = 0; // leave semantics: the eval stack is emptied
                        var chain = FinallyChain(method.FinallyRegions, instrStart, target);
                        RecordJoinDepth(instrStart, target, 0);
                        if (chain.Count == 0)
                        {
                            int patchIdx = ir.Count + 1;
                            Emit2(ir, irToIl, Op.br, -1, instrStart);
                            patchList.Add((patchIdx, target));
                        }
                        else
                        {
                            // Push order: final target first, then outer→inner-but-one handlers,
                            // so pops run inner handler → … → outer handler → target.
                            int pi = ir.Count + 1;
                            Emit2(ir, irToIl, Op.push_cont, -1, instrStart);
                            patchList.Add((pi, target));
                            for (int k = chain.Count - 1; k >= 1; k--)
                            {
                                pi = ir.Count + 1;
                                Emit2(ir, irToIl, Op.push_cont, -1, instrStart);
                                patchList.Add((pi, chain[k]));
                            }
                            RecordJoinDepth(instrStart, chain[0], 0);
                            pi = ir.Count + 1;
                            Emit2(ir, irToIl, Op.br, -1, instrStart);
                            patchList.Add((pi, chain[0]));
                        }
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Endfinally: // also ends the handler when reached by fall-through
                    {
                        ir.Add((uint)Op.br_cont); irToIl.Add(instrStart);
                        sp = 0;
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    case ILOpCode.Brtrue_s:
                    case ILOpCode.Brtrue:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Brtrue_s);
                        var (cond, ct) = evalStack[--sp];
                        // Value left on the stack after the condition is a forward-join carry ONLY in
                        // the `dup; brtrue L` idiom (`a ?? b`, `a?.b`): converge it with the
                        // fall-through's value into one slot; the join reconciles the other edge. With
                        // no preceding dup the remaining top is just a base operand (e.g. the `==` left
                        // operand under a `&&`/`||` chain) and must be left untouched.
                        if (afterDup && sp > 0 && target > ip)
                            MergeTopIntoJoinSlot(ir, irToIl, evalStack, sp, ref frameSize, slotTypes, slotStructs, mergeSlotForEnd, target, instrStart);
                        RecordJoinDepth(instrStart, target, sp); // condition already popped
                        var bop = ct == SType.O ? Op.brtrue_o : ct == SType.I8 ? Op.brtrue_i8 : Op.brtrue_i4;
                        if (ct == SType.R8) goto notSupported; // no IL brtrue on floats
                        int patchIdx = ir.Count + 2;
                        Emit3(ir, irToIl, bop, cond, -1, instrStart);
                        patchList.Add((patchIdx, target));
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Brfalse_s:
                    case ILOpCode.Brfalse:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Brfalse_s);
                        var (cond, ct) = evalStack[--sp];
                        if (afterDup && sp > 0 && target > ip)
                            MergeTopIntoJoinSlot(ir, irToIl, evalStack, sp, ref frameSize, slotTypes, slotStructs, mergeSlotForEnd, target, instrStart);
                        RecordJoinDepth(instrStart, target, sp); // condition already popped
                        var bop = ct == SType.O ? Op.brfalse_o : ct == SType.I8 ? Op.brfalse_i8 : Op.brfalse_i4;
                        if (ct == SType.R8) goto notSupported;
                        int patchIdx = ir.Count + 2;
                        Emit3(ir, irToIl, bop, cond, -1, instrStart);
                        patchList.Add((patchIdx, target));
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    // Compare-and-branch (lower to ceq+brtrue pattern)
                    case ILOpCode.Beq_s: case ILOpCode.Beq:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Beq_s);
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.ceq_i4, Op.ceq_r4, Op.ceq_o, Op.ceq_i4_nn, Op.ceq_r4_nn, Op.brtrue_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Bne_un_s: case ILOpCode.Bne_un:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Bne_un_s);
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.ceq_i4, Op.ceq_r4, Op.ceq_o, Op.ceq_i4_nn, Op.ceq_r4_nn, Op.brfalse_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Blt_s: case ILOpCode.Blt:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Blt_s);
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.clt_i4, Op.clt_r4, Op.clt_i4, Op.clt_i4_nn, Op.clt_r4_nn, Op.brtrue_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Blt_un_s: case ILOpCode.Blt_un:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Blt_un_s);
                        // Unordered: branch when a < b OR either is NaN, so the float compare must be the
                        // unordered clt_un_r4 (used for both the typed and _nn slots — it reads operand types itself).
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.clt_un_i4, Op.clt_un_r4, Op.clt_un_i4, Op.clt_un_i4_nn, Op.clt_un_r4, Op.brtrue_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Bgt_s: case ILOpCode.Bgt:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Bgt_s);
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.cgt_i4, Op.cgt_r4, Op.cgt_i4, Op.cgt_i4_nn, Op.cgt_r4_nn, Op.brtrue_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Bgt_un_s: case ILOpCode.Bgt_un:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Bgt_un_s);
                        // Unordered: branch when a > b OR either is NaN, so the float compare must be the
                        // unordered cgt_un_r4 (used for both the typed and _nn slots — it reads operand types itself).
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.cgt_un_i4, Op.cgt_un_r4, Op.cgt_un_o, Op.cgt_un_i4_nn, Op.cgt_un_r4, Op.brtrue_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Ble_s: case ILOpCode.Ble:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Ble_s);
                        // !(a > b) — negating through brfalse swaps ordered/unordered, so the ORDERED ble
                        // (no branch on NaN) needs the UNORDERED cgt_un_r4: !(a > b OR NaN) is false for NaN.
                        // (cgt_un_r4 serves both the typed and _nn slots — it reads operand types itself.)
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.cgt_i4, Op.cgt_un_r4, Op.cgt_i4, Op.cgt_i4_nn, Op.cgt_un_r4, Op.brfalse_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Ble_un_s: case ILOpCode.Ble_un:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Ble_un_s);
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.cgt_un_i4, Op.cgt_r4, Op.cgt_un_o, Op.cgt_un_i4_nn, Op.cgt_r4_nn, Op.brfalse_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Bge_s: case ILOpCode.Bge:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Bge_s);
                        // !(a < b) — negating through brfalse swaps ordered/unordered, so the ORDERED bge
                        // (no branch on NaN) needs the UNORDERED clt_un_r4: !(a < b OR NaN) is false for NaN.
                        // (clt_un_r4 serves both the typed and _nn slots — it reads operand types itself.)
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.clt_i4, Op.clt_un_r4, Op.clt_i4, Op.clt_i4_nn, Op.clt_un_r4, Op.brfalse_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }
                    case ILOpCode.Bge_un_s: case ILOpCode.Bge_un:
                    {
                        int target = ReadBranchTarget(il, ref ip, op == ILOpCode.Bge_un_s);
                        EmitCmpBranch(ir, irToIl, evalStack, ref sp, ref frameSize, slotTypes, slotStructs, Op.clt_un_i4, Op.clt_r4, Op.clt_un_i4, Op.clt_un_i4_nn, Op.clt_r4_nn, Op.brfalse_i4, patchList, target, instrStart, branchTargetDepth);
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    // --- Switch ---
                    case ILOpCode.Switch:
                    {
                        int n = BitConverter.ToInt32(il, ip); ip += 4;
                        int tableStart = ip;
                        ip += 4 * n;  // skip offset table
                        int afterSwitch = ip; // base for relative offsets

                        var (valSlot, _) = evalStack[--sp];
                        // Emit: [switch_i4, val, n, default_patch_idx, ip0, ip1, ...]; targets patched afterwards.
                        int irSwitchStart = ir.Count;
                        ir.Add((uint)Op.switch_i4);
                        irToIl.Add(instrStart);
                        ir.Add((uint)valSlot);
                        irToIl.Add(instrStart);
                        ir.Add((uint)n);
                        irToIl.Add(instrStart);
                        // placeholder for default (fall-through = afterSwitch → patched via patchList)
                        int defaultPatchIdx = ir.Count;
                        ir.Add(unchecked((uint)-1));
                        irToIl.Add(instrStart);
                        patchList.Add((defaultPatchIdx, afterSwitch));
                        // per-case targets — the switch value is already popped, so every edge
                        // (cases + default fall-through) delivers the current base depth.
                        RecordJoinDepth(instrStart, afterSwitch, sp);
                        for (int ci = 0; ci < n; ci++)
                        {
                            int off = BitConverter.ToInt32(il, tableStart + ci * 4);
                            int target = afterSwitch + off;
                            int patchIdx2 = ir.Count;
                            ir.Add(unchecked((uint)-1));
                            irToIl.Add(instrStart);
                            patchList.Add((patchIdx2, target));
                            RecordJoinDepth(instrStart, target, sp);
                        }
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    // --- Ret ---
                    case ILOpCode.Throw:
                    {
                        var (thrSrc, thrSt) = evalStack[--sp];
                        if (thrSt != SType.O) goto notSupported; // exceptions are always O refs
                        Emit2(ir, irToIl, Op.throw_o, thrSrc, instrStart);
                        // Unreachable code follows until the next branch target; the linear walk's
                        // join-depth bookkeeping handles it like the code after an unconditional br.
                        sp = 0;
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    case ILOpCode.Ret:
                    {
                        if (sp > 0)
                        {
                            var (src, st) = evalStack[--sp];
                            if (method.ReturnSType == SType.Vt && method.ReturnStructLayout != null)
                            {
                                // Flat-struct return. A boxed (O) value — e.g. from a path the
                                // lowerer left boxed — is unboxed into a Vt temp first so ret_vt
                                // always reads real frame bytes.
                                if (st != SType.Vt)
                                {
                                    int vtmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, method.ReturnStructLayout);
                                    Emit3(ir, irToIl, Op.unbox_vt, vtmp, src, instrStart);
                                    src = vtmp;
                                }
                                Emit2(ir, irToIl, Op.ret_vt, src, instrStart);
                            }
                            else
                            {
                                // A Vt value returned through a non-Vt signature (defensive): box it.
                                if (st == SType.Vt)
                                {
                                    int boxed = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                    Emit3(ir, irToIl, Op.box_vt, boxed, src, instrStart);
                                    src = boxed;
                                }
                                var retOp = st == SType.R4 ? Op.ret_r4 : st == SType.I4 ? Op.ret_i4
                                    : st == SType.I8 ? Op.ret_i8 : st == SType.R8 ? Op.ret_r8 : Op.ret_o;
                                Emit2(ir, irToIl, retOp, src, instrStart);
                            }
                        }
                        else
                        {
                            ir.Add((uint)Op.ret_void);
                            irToIl.Add(instrStart);
                        }
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    // --- Method calls ---
                    // `constrained. T` prefixes a callvirt on a byref receiver. Boxed (O-slot)
                    // struct receivers already give constrained semantics through plain virtual
                    // dispatch. FLAT (Vt-slot) receivers need the type token: constrained to the
                    // slot's own struct type, the whole struct must box before the call — the
                    // receiver resolution below consumes the remembered token for that.
                    case ILOpCode.Constrained:
                        constrainedTok = BitConverter.ToInt32(il, ip);
                        ip += 4;
                        break;

                    case ILOpCode.Call:
                    case ILOpCode.Callvirt:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        // A plain `call` on a member that also has a virtual binding (`base.ToString()`)
                        // must bind statically: use the non-virtual entry Parse registered beside it.
                        if (op == ILOpCode.Call && asm.HostCalls.ContainsKey(tok | NonVirtualCallBit))
                            tok |= NonVirtualCallBit;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        int ctok = constrainedTok; constrainedTok = 0; // consume the prefix, if any
                        // `callvirt` dispatches on the receiver's runtime type; a plain `call` binds
                        // statically. C# emits `call` (not callvirt) for `base.M()`, so a base call to a
                        // virtual method must stay statically bound — hence the opcode is load-bearing.
                        bool isCallvirt = op == ILOpCode.Callvirt;

                        // `f(args)` where f's TYPE names a script type (e.g. Action<Node>) compiles to
                        // `callvirt <ScriptTypedDelegate>::Invoke(args)`. Its Invoke is a TypeSpec MemberRef
                        // that the binding ALSO resolves to a host DynamicInvoke shim (Action`1::Invoke keyed
                        // by short name), so it would otherwise take the CLR call_host path and fault trying
                        // to cast the ScriptDelegate to System.Delegate. Intercept BEFORE resolution — but
                        // only when the delegate type mentions a script type (TryClassify... returns false
                        // for a host-typed Action<int>, which keeps its working CLR delegate + shim path).
                        if (isCallvirt && !asm.ByToken.ContainsKey(tok)
                            && TryClassifyScriptDelegateInvoke(asm, tok, out int sdArgc, out bool sdVoid, out SType sdRet))
                        {
                            // The receiver (the delegate value) sits below the explicit args; re-enter the
                            // VM on the delegate's target. Executor boxes each arg by slot type (call_host's
                            // slow-path marshalling). A null receiver at runtime is a real null-deref (C#
                            // short-circuits `?.Invoke` before this op), reported by the executor.
                            int spBase = sp - sdArgc - 1;
                            int recvSlot = evalStack[spBase].slot;
                            if (recvSlot < 0 || addrTagBySlot[recvSlot] != -2)
                                goto notSupported; // delegate value must be a plain O reference
                            // Explicit args must be plain values (no ldloca/ldflda byref phantoms — ref/out
                            // delegate params are unsupported, matching the CLR adapter path).
                            for (int k = 0; k < sdArgc; k++)
                            {
                                int aSlot = evalStack[spBase + 1 + k].slot;
                                if (aSlot >= 0 && addrTagBySlot[aSlot] != -2) goto notSupported;
                            }
                            int dst = sdVoid ? -1
                                : (sdRet is SType.I4 or SType.R4 or SType.I8 or SType.R8)
                                    ? AllocSlot(ref frameSize, slotTypes, slotStructs, sdRet, null)
                                    : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            ir.Add((uint)Op.invoke_script_delegate); irToIl.Add(instrStart);
                            ir.Add((uint)(sdVoid ? -1 : dst)); irToIl.Add(instrStart);
                            ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                            ir.Add((uint)recvSlot); irToIl.Add(instrStart);
                            ir.Add((uint)sdArgc); irToIl.Add(instrStart);
                            for (int k = 0; k < sdArgc; k++)
                            { ir.Add((uint)evalStack[spBase + 1 + k].slot); irToIl.Add(instrStart); }
                            sp = spBase;
                            if (!sdVoid) evalStack[sp++] = (dst, sdRet);
                            ensuredScriptSlots.Clear();
                            break;
                        }

                        if (asm.ByToken.TryGetValue(tok, out var callee))
                        {
                            // Detect ldloca.s + call .ctor (script struct ctor) pattern:
                            // if this is a .ctor and the first arg slot is a frame-addr phantom,
                            // emit newobj_script writing into the existing local slot.
                            int argc = callee.ArgCount;
                            int spBase = sp - argc;
                            if (callee.Name == ".ctor" && argc >= 1 && addrTagBySlot[evalStack[spBase].slot] >= 0)
                            {
                                int destSlot = addrTagBySlot[evalStack[spBase].slot];
                                int destOff  = addrByteOffBySlot[evalStack[spBase].slot];
                                addrTagBySlot[evalStack[spBase].slot] = -2; // consume the addr tag
                                int explicitArgc = argc - 1; // exclude `this`
                                if (slotTypes[destSlot] == SType.Vt && destOff == 0)
                                {
                                    // FLAT struct ctor: zero the destination and run the ctor body
                                    // with `this` = the Vt slot itself. The VM copies the bytes into
                                    // the callee's Vt arg0 and copies them back on ret (VtThisWb).
                                    Emit2(ir, irToIl, Op.initobj, destSlot, instrStart);
                                    ir.Add((uint)Op.call_script); irToIl.Add(instrStart);
                                    ir.Add(unchecked((uint)-1)); irToIl.Add(instrStart);
                                    ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                                    ir.Add((uint)argc); irToIl.Add(instrStart);
                                    ir.Add((uint)destSlot); irToIl.Add(instrStart); // `this`
                                    for (int k = 0; k < explicitArgc; k++)
                                    { ir.Add((uint)evalStack[spBase + 1 + k].slot); irToIl.Add(instrStart); }
                                    sp = spBase; // ctor is void, no push
                                    ensuredScriptSlots.Clear();
                                    break;
                                }
                                ir.Add((uint)Op.newobj_script); irToIl.Add(instrStart);
                                ir.Add((uint)destSlot); irToIl.Add(instrStart);
                                ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                                ir.Add((uint)explicitArgc); irToIl.Add(instrStart);
                                for (int k = 0; k < explicitArgc; k++)
                                { ir.Add((uint)evalStack[spBase + 1 + k].slot); irToIl.Add(instrStart); }
                                sp = spBase; // ctor is void, no push
                            }
                            else
                            {
                                // Instance callee: resolve a phantom receiver (ldloca/ldflda/ldelema)
                                // to a real slot — otherwise `this` arrives null. Flat receivers with
                                // a composed offset (or field/element receivers) are materialized into
                                // a temp with a write-back after the call (copy-in/copy-out byref
                                // semantics).
                                int wbKind = 0; // 0=none, 1=stfld_vt_vt, 2=stfld_sc_vt
                                int wbA = 0, wbB = 0, wbTemp = 0;
                                if (!callee.IsStatic && argc >= 1)
                                {
                                    var (rSlot, _) = evalStack[spBase];
                                    int rTag = addrTagBySlot[rSlot];
                                    if (rTag >= 0)
                                    {
                                        int rOff = addrByteOffBySlot[rSlot];
                                        addrTagBySlot[rSlot] = -2;
                                        if (rOff == 0 || slotTypes[rTag] != SType.Vt)
                                        {
                                            evalStack[spBase] = (rTag, slotTypes[rTag]);
                                        }
                                        else if (callee.ThisStructLayout != null)
                                        {
                                            int tmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, callee.ThisStructLayout);
                                            Emit4(ir, irToIl, Op.ldfld_vt_vt, tmp, rTag, rOff, instrStart);
                                            evalStack[spBase] = (tmp, SType.Vt);
                                            wbKind = 1; wbA = rTag; wbB = rOff; wbTemp = tmp;
                                        }
                                    }
                                    else if (rTag == -1 && addrFldOffBySlot[rSlot] >= 0 && callee.ThisStructLayout != null)
                                    {
                                        int parentR = addrFldObjBySlot[rSlot];
                                        int offR    = addrFldOffBySlot[rSlot];
                                        addrTagBySlot[rSlot] = -2;
                                        int tmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, callee.ThisStructLayout);
                                        Emit4(ir, irToIl, Op.ldfld_sc_vt, tmp, parentR, offR, instrStart);
                                        evalStack[spBase] = (tmp, SType.Vt);
                                        wbKind = 2; wbA = parentR; wbB = offR; wbTemp = tmp;
                                    }
                                    else if (rTag == -1)
                                    {
                                        // Heap script-struct field receiver: load the ScriptObject
                                        // reference — mutations through it propagate, no write-back.
                                        int parentR = addrFldObjBySlot[rSlot];
                                        int fTokIdx = addrFldTokBySlot[rSlot];
                                        int fTok = tokens[fTokIdx];
                                        addrTagBySlot[rSlot] = -2;
                                        int tmp = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                        if (asm.FieldSlots.TryGetValue(fTok, out var rfs))
                                            Emit4(ir, irToIl, Op.ldfld_sc_o, tmp, parentR, rfs.Item1.FieldOffsets[rfs.Item2], instrStart);
                                        else
                                            Emit4(ir, irToIl, Op.ldfld_o, tmp, parentR, fTokIdx, instrStart);
                                        evalStack[spBase] = (tmp, SType.O);
                                    }
                                    else if (rTag == -3 && addrElemLayoutBySlot[rSlot] != null
                                             && callee.ThisStructLayout != null)
                                    {
                                        // arr[i].Method(): materialize the flat element, call with
                                        // `this` = the temp, memcpy the element back after (wbKind 4).
                                        int arrS = addrFldObjBySlot[rSlot];
                                        int idxS = addrFldTokBySlot[rSlot];
                                        addrTagBySlot[rSlot] = -2;
                                        int tmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, addrElemLayoutBySlot[rSlot]!);
                                        Emit4(ir, irToIl, Op.ldelem_vt, tmp, arrS, idxS, instrStart);
                                        evalStack[spBase] = (tmp, SType.Vt);
                                        wbKind = 4; wbA = arrS; wbB = idxS; wbTemp = tmp;
                                    }
                                    else if (rTag == -3)
                                    {
                                        int arrS = addrFldObjBySlot[rSlot];
                                        int idxS = addrFldTokBySlot[rSlot];
                                        addrTagBySlot[rSlot] = -2;
                                        int tmp = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                        Emit4(ir, irToIl, Op.ldelem_o, tmp, arrS, idxS, instrStart);
                                        evalStack[spBase] = (tmp, SType.O);
                                    }
                                }
                                // Normal script → script call; return type comes from callee's signature
                                bool isVoid = callee.IsVoid;
                                SType retSType = callee.ReturnSType;
                                int dst = isVoid ? -1
                                    : retSType == SType.Vt && callee.ReturnStructLayout != null
                                        ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, callee.ReturnStructLayout)
                                        : AllocSlot(ref frameSize, slotTypes, slotStructs, retSType == SType.Vt ? SType.O : retSType, null);
                                // Resolve ldloca-phantom args BEFORE emitting: a local function's
                                // captured environment arrives as `ref <>c__DisplayClass` — pass the
                                // ScriptObject reference itself, so callee field writes propagate.
                                // Any other byref arg shape stays loud (emitting the phantom slot
                                // raw would read null/garbage at runtime — silent wrong).
                                for (int k = 0; k < argc; k++)
                                {
                                    int aSlot = evalStack[spBase + k].slot;
                                    if (aSlot < 0 || addrTagBySlot[aSlot] == -2) continue;
                                    int aTag = addrTagBySlot[aSlot];
                                    if (aTag >= 0)
                                    {
                                        // Frame-local environment (ldloca in a plain method).
                                        if (slotTypes[aTag] != SType.O || addrByteOffBySlot[aSlot] != 0)
                                            goto notSupported;
                                        addrTagBySlot[aSlot] = -2;
                                        evalStack[spBase + k] = (aTag, SType.O);
                                    }
                                    else if (aTag == -1 && addrFldOffBySlot[aSlot] < 0)
                                    {
                                        // Token mode — the addr names a whole O field; a composed
                                        // byte offset (>= 0) would point INSIDE a nested struct,
                                        // which falls through to the loud path below.
                                        // State-machine-hoisted environment (ldflda in MoveNext):
                                        // load the ScriptObject field — same reference, mutations
                                        // propagate — exactly like the heap receiver path above.
                                        int aParent = addrFldObjBySlot[aSlot];
                                        int aTokIdx = addrFldTokBySlot[aSlot];
                                        int aTok = tokens[aTokIdx];
                                        addrTagBySlot[aSlot] = -2;
                                        int tmp = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                        if (asm.FieldSlots.TryGetValue(aTok, out var afs))
                                            Emit4(ir, irToIl, Op.ldfld_sc_o, tmp, aParent, afs.Item1.FieldOffsets[afs.Item2], instrStart);
                                        else
                                            Emit4(ir, irToIl, Op.ldfld_o, tmp, aParent, aTokIdx, instrStart);
                                        evalStack[spBase + k] = (tmp, SType.O);
                                    }
                                    else
                                        goto notSupported;
                                }
                                // [call_script, dst, tok_idx, argc, arg0, arg1, ...]
                                // callvirt on a virtual script method dispatches on the RECEIVER's
                                // runtime type; plain `call` (base.M()) stays statically bound.
                                bool virtDispatch = op == ILOpCode.Callvirt && callee.IsVirtual && !callee.IsStatic;
                                ir.Add((uint)(virtDispatch ? Op.call_script_virt : Op.call_script)); irToIl.Add(instrStart);
                                ir.Add((uint)(isVoid ? -1 : dst)); irToIl.Add(instrStart);
                                ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                                ir.Add((uint)argc); irToIl.Add(instrStart);
                                for (int k = 0; k < argc; k++)
                                { ir.Add((uint)evalStack[spBase + k].slot); irToIl.Add(instrStart); }
                                if (wbKind == 1) Emit4(ir, irToIl, Op.stfld_vt_vt, wbA, wbB, wbTemp, instrStart);
                                else if (wbKind == 2) Emit4(ir, irToIl, Op.stfld_sc_vt, wbA, wbB, wbTemp, instrStart);
                                else if (wbKind == 4) Emit4(ir, irToIl, Op.stelem_vt, wbA, wbB, wbTemp, instrStart);
                                sp = spBase;
                                if (!isVoid) evalStack[sp++] = (dst, retSType);
                            }
                        }
                        else if (asm.HostCalls.TryGetValue(tok, out var hostEntry))
                        {
                            // Detect byref params — these go through the slow boxed path with
                            // post-call write-back. The Fast / FastFlat shortcuts are bypassed
                            // (none of the registered Fast templates take byref args today).
                            var ps = hostEntry.Binding.Params;
                            int byRefArgCount = 0;
                            int explicitArgc = hostEntry.Binding.ParamCount;
                            if (ps != null)
                            {
                                int nByref = ps.Length;
                                if (nByref > explicitArgc) nByref = explicitArgc;
                                for (int k = 0; k < nByref; k++)
                                    if (ps[k].ParameterType.IsByRef) byRefArgCount++;
                            }
                            if (byRefArgCount > 0)
                            {
                                EmitCallHostByref(ir, irToIl, evalStack, ref sp, ref frameSize,
                                    slotTypes, slotStructs, addrTagBySlot, addrFldObjBySlot, addrFldTokBySlot, addrByteOffBySlot,
                                    asm, tokens, hostEntry, tokIdx, ps!, instrStart);
                                break;
                            }

                            int argc = hostEntry.Binding.ParamCount;
                            bool isVoid2 = hostEntry.IsVoid;
                            // Dst-slot picker. Three flat paths (all gated on
                            // Binding.FastIsFlat — when true the entry's Fast closure writes
                            // results to numFrame, so dst must be a numFrame-resident SType):
                            //   - Vt return: AllocStructSlot, Fast writes struct bytes
                            //   - primitive (R4/I4) return: AllocSlot of that numeric type, Fast
                            //     writes the primitive bytes (no float/int box)
                            //   - everything else: O slot, boxed Fast / Invoke path
                            // Flat dst whenever the return type HAS a layout — even on the slow
                            // Invoke path the executor unboxes the boxed return straight into the
                            // Vt slot (one boundary box), so downstream consumers (field reads,
                            // operator args, further calls) stay flat instead of re-boxing.
                            bool useFlatVt = hostEntry.ReturnStruct != null;
                            SType retSt = SType.O;
                            if (!isVoid2 && (hostEntry.ResolvedMethod ?? hostEntry.Binding.Method) is MethodInfo mi5)
                            {
                                var rt5 = mi5.ReturnType;
                                if (rt5 == typeof(float))                                                      retSt = SType.R4;
                                else if (rt5 == typeof(int) || rt5 == typeof(bool) || rt5 == typeof(byte)
                                         || rt5 == typeof(sbyte) || rt5 == typeof(short) || rt5 == typeof(ushort)) retSt = SType.I4;
                            }
                            else if (!isVoid2)
                            {
                                // String-keyed shim (no MethodInfo): type the result slot from the
                                // call site's metadata signature. Leaving numeric returns O boxed
                                // them, so `a.Length != a.Length` reference-compared two boxes.
                                retSt = hostEntry.SigRetSType;
                            }
                            // Optional explicit override (e.g. AllowBcl AttachFlatFloat sets it to R4
                            // because Math.Sqrt is C# (double)→double but we narrow to float at the closure).
                            // default(SType) == SType.O means "unset" — leaves the Method-derived retSt alone.
                            if (!isVoid2 && hostEntry.Binding.FlatReturnSType != SType.O)
                                retSt = hostEntry.Binding.FlatReturnSType;
                            // Numeric returns get a NUMERIC dst even on the slow Invoke path — the
                            // executor unboxes into it (WrObj), and Entry.Invoke normalizes sub-int
                            // returns to int boxes. Leaving them O made downstream arithmetic pick
                            // the int fallback over boxed floats: `r.width + r.height` returned 0.
                            bool useFlatNum = !useFlatVt && (retSt == SType.R4 || retSt == SType.I4
                                || retSt == SType.I8 || retSt == SType.R8);
                            int dst;
                            if (isVoid2)         dst = -1;
                            else if (useFlatVt)  dst = AllocStructSlot(ref frameSize, slotTypes, slotStructs, hostEntry.ReturnStruct!);
                            else if (useFlatNum) dst = AllocSlot(ref frameSize, slotTypes, slotStructs, retSt, null);
                            else                 dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            // willCallFlat: when true, the executor's Fast closure expects Vt args
                            // to be flat (no box_vt boundary). Void calls with FastIsFlat also need
                            // this — their closure unboxes args directly from numFrame.
                            bool willCallFlat = hostEntry.Binding.FastIsFlat && (useFlatVt || useFlatNum || isVoid2);
                            int recvSlotRaw = hostEntry.Binding.HasThis ? (int)evalStack[sp - argc - 1].slot : -1;
                            // Resolve receiver address phantoms to the actual value slot:
                            // • frame-slot address (addrTagBySlot >= 0): the local slot IS the value
                            // • field address (addrTagBySlot == -1): emit ldfld to load the field value
                            int recvSlot = recvSlotRaw;
                            // Write-back for a mutating value-type instance method called through a field
                            // address (`h.P.Scale()`): the receiver is materialised into a boxed slot, the
                            // call mutates that box in place, and we must store it back into the field
                            // afterwards — otherwise the mutation is lost. Only for value-type receivers.
                            bool recvWriteBack = false;
                            int wbFldObj = 0, wbFldToki = 0, wbFldOff = 0; bool wbSc = false; SType wbFldSt = SType.O;
                            int recvVtOff = 0; // composed byte offset when the receiver address points inside a Vt slot
                            if (recvSlotRaw >= 0)
                            {
                                int addrTag = addrTagBySlot[recvSlotRaw];
                                if (addrTag >= 0)
                                {
                                    // Local address → use actual local slot (value is in numFrame or refFrame)
                                    recvSlot = addrTag;
                                    recvVtOff = addrByteOffBySlot[recvSlotRaw];
                                }
                                else if (addrTag == -3)
                                {
                                    // Array-element receiver (`a[i].ToString()`): mirror the script-call
                                    // resolution — materialize the element. Flat struct elements load
                                    // into a Vt temp; scalar/ref elements box through ldelem_o (its
                                    // executor dispatches on the array's runtime type). Previously this
                                    // tag was unhandled here and the executor read a never-written O
                                    // phantom slot: null receiver (found by fuzzing).
                                    int arrS2 = addrFldObjBySlot[recvSlotRaw];
                                    int idxS2 = addrFldTokBySlot[recvSlotRaw];
                                    addrTagBySlot[recvSlotRaw] = -2;
                                    if (addrElemLayoutBySlot[recvSlotRaw] is { } elay)
                                    {
                                        int tmp2 = AllocStructSlot(ref frameSize, slotTypes, slotStructs, elay);
                                        Emit4(ir, irToIl, Op.ldelem_vt, tmp2, arrS2, idxS2, instrStart);
                                        recvSlot = tmp2;
                                        // a[i].fK: the receiver is a primitive field at a byte offset
                                        // inside the materialized element — extracted below.
                                        recvVtOff = addrByteOffBySlot[recvSlotRaw];
                                    }
                                    else
                                    {
                                        int tmp2 = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                        Emit4(ir, irToIl, Op.ldelem_o, tmp2, arrS2, idxS2, instrStart);
                                        recvSlot = tmp2;
                                    }
                                }
                                else if (addrTag == -1)
                                {
                                    // Field address → emit ldfld to materialise the value into a new slot
                                    int fldObj  = addrFldObjBySlot[recvSlotRaw];
                                    int fldToki = addrFldTokBySlot[recvSlotRaw];
                                    int fldTok  = tokens[fldToki];
                                    asm.FieldSTypes.TryGetValue(fldTok, out var fldSt);
                                    bool isScField = asm.FieldSlots.TryGetValue(fldTok, out var fldFs);
                                    // A Vt field (a flat HOST struct stored inline in PrimBytes —
                                    // e.g. Roslyn SPILLS a struct receiver into an iterator
                                    // state-machine field around a switch expression's branches)
                                    // needs a layout-carrying struct slot and a byte-range copy;
                                    // the ldfld_sc_o fallback read RefSlots at a PrimBytes offset
                                    // and threw IndexOutOfRange (found by fuzzing).
                                    var fldVtLay = isScField && fldSt == SType.Vt
                                        ? fldFs.Item1.VtFieldLayouts?[fldFs.Item2] : null;
                                    recvSlot = fldVtLay != null
                                        ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, fldVtLay)
                                        : AllocSlot(ref frameSize, slotTypes, slotStructs, fldSt, null);
                                    if (isScField)
                                    {
                                        // Prefer the COMPOSED offset when the phantom accumulated one
                                        // (`o.sf.f1`: sf's base + f1's offset) — recomputing from the
                                        // token alone gives f1's offset within sf, dropping sf's base
                                        // and reading the wrong PrimBytes (found by fuzzing).
                                        int composed = addrFldOffBySlot[recvSlotRaw];
                                        int fldOff = composed >= 0 ? composed : fldFs.Item1.FieldOffsets[fldFs.Item2];
                                        var fldOp2 = fldVtLay != null ? Op.ldfld_sc_vt
                                            : fldSt == SType.I4 ? Op.ldfld_sc_i4 : fldSt == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                                        Emit4(ir, irToIl, fldOp2, recvSlot, fldObj, fldOff, instrStart);
                                        wbFldOff = fldOff;
                                    }
                                    else
                                    {
                                        var fldLoadOp = fldSt == SType.I4 ? Op.ldfld_i4 : fldSt == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                                        Emit4(ir, irToIl, fldLoadOp, recvSlot, fldObj, fldToki, instrStart);
                                    }
                                    // A mutating instance method on a boxed value-type receiver needs its
                                    // result written back to the field. Reference-type receivers mutate in
                                    // place; flat (Vt) receivers take a different path and are left as-is.
                                    if (fldSt == SType.O && hostEntry.Binding.HasThis
                                        && hostEntry.Binding.Method?.DeclaringType?.IsValueType == true)
                                    {
                                        recvWriteBack = true; wbFldObj = fldObj; wbFldToki = fldToki; wbSc = isScField; wbFldSt = fldSt;
                                    }
                                }
                            }

                            // `constrained. <host enum>` on an I4-slot receiver — an enum
                            // local/field calling ToString/GetHashCode/HasFlag. RdObj would box
                            // the receiver as INT and ToString would render "1", not the member
                            // name; box as the real enum type (the box_enum op covers explicit
                            // `box` IL, this is the callvirt-receiver path). Script enums keep
                            // the int form — no runtime Type exists.
                            if (ctok != 0 && hostEntry.Binding.HasThis
                                && recvSlot >= 0 && recvSlot < slotTypes.Count && slotTypes[recvSlot] == SType.I4
                                && ResolveHostEnumType(asm, ctok) is { } cEnumType)
                            {
                                int ctokIdx = tokens.Count; tokens.Add(ctok);
                                asm.TokenTypes[ctok] = cEnumType;
                                int boxedEnumRecv = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                Emit4(ir, irToIl, Op.box_enum, boxedEnumRecv, recvSlot, ctokIdx, instrStart);
                                recvSlot = boxedEnumRecv;
                            }

                            // Receiver inside a FLAT struct slot with no Vt-aware entry (no
                            // ReceiverStruct, no AccessorOffset — typically an inherited virtual
                            // like Object.ToString reached via `constrained.`). RdObj on a Vt slot
                            // yields null, so the value must be materialized first. Two shapes:
                            //  • constrained to the slot's OWN struct type (`"impact" + quaternion`
                            //    → constrained callvirt Object.ToString when the struct overrides
                            //    it): the receiver is the WHOLE struct — box it (box_vt) and
                            //    dispatch virtually on the box. Without the constrained-type check
                            //    the field extraction below misread the struct's first field as
                            //    the receiver (Quaternion printed as its x component).
                            //  • constrained to a PRIMITIVE field (`v.f0.ToString()`, or a struct
                            //    array element's field): extract the primitive at its byte offset
                            //    into a typed slot (found by fuzzing: ToString returned "").
                            if (hostEntry.ReceiverStruct == null && hostEntry.Binding.AccessorOffset < 0
                                && recvSlot >= 0 && recvSlot < slotTypes.Count
                                && slotTypes[recvSlot] == SType.Vt && slotStructs[recvSlot] is { } lay0)
                            {
                                if (recvVtOff == 0 && ctok != 0
                                    && TokenSimpleTypeName(asm, ctok) == lay0.TypeName)
                                {
                                    int boxedRecv = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                    Emit3(ir, irToIl, Op.box_vt, boxedRecv, recvSlot, instrStart);
                                    recvSlot = boxedRecv;
                                }
                                else
                                {
                                    foreach (var fld in lay0.Fields.Values)
                                    {
                                        if (fld.Offset != recvVtOff || fld.St is not (SType.I4 or SType.R4)) continue;
                                        int prim = AllocSlot(ref frameSize, slotTypes, slotStructs, fld.St, null);
                                        Emit4(ir, irToIl, fld.St == SType.I4 ? Op.ldfld_vt_i4 : Op.ldfld_vt_r4,
                                            prim, recvSlot, recvVtOff, instrStart);
                                        recvSlot = prim;
                                        recvVtOff = 0;
                                        break;
                                    }
                                }
                            }
                            int spBase = sp - argc - (hostEntry.Binding.HasThis ? 1 : 0);

                            // Verified trivial accessor on a FLAT receiver: `r.width` / `r.width = v`
                            // becomes a direct byte read/write of the backing field — no host call,
                            // no receiver box. (The accessor IL was verified at registration.)
                            if (hostEntry.Binding.AccessorOffset >= 0 && recvSlot >= 0
                                && recvSlot < slotTypes.Count && slotTypes[recvSlot] == SType.Vt)
                            {
                                int accOff = recvVtOff + hostEntry.Binding.AccessorOffset;
                                if (argc == 0 && !isVoid2)
                                {
                                    int accDst;
                                    if (hostEntry.Binding.AccessorSt == SType.Vt)
                                    {
                                        // Struct-typed backing field (Bounds.center → Vector3):
                                        // byte-range copy into a Vt slot sized by the field layout.
                                        accDst = AllocStructSlot(ref frameSize, slotTypes, slotStructs,
                                            hostEntry.Binding.AccessorVtLayout!);
                                        Emit4(ir, irToIl, Op.ldfld_vt_vt, accDst, recvSlot, accOff, instrStart);
                                    }
                                    else
                                    {
                                        accDst = AllocSlot(ref frameSize, slotTypes, slotStructs, hostEntry.Binding.AccessorSt, null);
                                        Emit4(ir, irToIl,
                                            hostEntry.Binding.AccessorSt == SType.I4 ? Op.ldfld_vt_i4 : Op.ldfld_vt_r4,
                                            accDst, recvSlot, accOff, instrStart);
                                    }
                                    sp = spBase;
                                    evalStack[sp++] = (accDst, hostEntry.Binding.AccessorSt);
                                    ensuredScriptSlots.Clear();
                                    break;
                                }
                                if (argc == 1 && isVoid2)
                                {
                                    int accSrc = evalStack[sp - 1].slot;
                                    if (hostEntry.Binding.AccessorSt == SType.Vt)
                                    {
                                        // Struct value into a struct-typed backing field. A boxed
                                        // source (O slot from a host return that wasn't flattened)
                                        // unboxes into a Vt temp first, so stfld_vt_vt always
                                        // copies real frame bytes at the field layout's size.
                                        if (evalStack[sp - 1].type != SType.Vt)
                                        {
                                            int vtmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs,
                                                hostEntry.Binding.AccessorVtLayout!);
                                            Emit3(ir, irToIl, Op.unbox_vt, vtmp, accSrc, instrStart);
                                            accSrc = vtmp;
                                        }
                                        Emit4(ir, irToIl, Op.stfld_vt_vt, recvSlot, accOff, accSrc, instrStart);
                                    }
                                    else
                                        Emit4(ir, irToIl,
                                            hostEntry.Binding.AccessorSt == SType.I4 ? Op.stfld_vt_i4 : Op.stfld_vt_r4,
                                            recvSlot, accOff, accSrc, instrStart);
                                    sp = spBase;
                                    ensuredScriptSlots.Clear();
                                    break;
                                }
                            }

                            // Vt receiver addressed at a NONZERO byte offset — a method call on a
                            // flat struct nested inside another flat struct (`w.p.Scale(3)` where p
                            // sits at offset 4 of w): call_host encodes only a slot, so the executor
                            // would box the WRONG bytes (from the slot start). Materialize the
                            // sub-struct into its own Vt temp and copy it back after the call (the
                            // callee may mutate the receiver).
                            bool recvVtWb = false; int recvVtWbBase = -1;
                            if (hostEntry.Binding.HasThis && recvSlot >= 0 && recvVtOff != 0
                                && recvSlot < slotTypes.Count && slotTypes[recvSlot] == SType.Vt
                                && hostEntry.ReceiverStruct != null)
                            {
                                int rTmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, hostEntry.ReceiverStruct);
                                Emit4(ir, irToIl, Op.ldfld_vt_vt, rTmp, recvSlot, recvVtOff, instrStart);
                                recvVtWb = true; recvVtWbBase = recvSlot;
                                recvSlot = rTmp;
                            }

                            // The operator-inlining decision must come BEFORE the boundary boxing:
                            // EmitInlinedOp synthesizes per-field arithmetic over the FLAT arg
                            // slots, so its args must not be boxed away first.
                            bool inlineOp = hostEntry.Binding.IsInlineableOp && useFlatVt
                                && hostEntry.ReturnStruct != null
                                && CanInlineOp(hostEntry, argc, hostEntry.ReturnStruct, isCtor: false);

                            // Boundary box: any Vt-slot arg gets converted to a fresh O slot via
                            // box_vt before the call — UNLESS we'll dispatch to FastFlat, which
                            // reads Vt arg bytes directly from numFrame (or the op inlines).
                            int argBase = sp - argc;
                            if (!willCallFlat && !inlineOp)
                            {
                                for (int k = 0; k < argc; k++)
                                {
                                    if (evalStack[argBase + k].type == SType.Vt)
                                    {
                                        int boxed = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                        Emit3(ir, irToIl, Op.box_vt, boxed, evalStack[argBase + k].slot, instrStart);
                                        evalStack[argBase + k] = (boxed, SType.O);
                                    }
                                }
                            }

                            // Pure operator returning Vt: synthesize per-field arithmetic instead
                            // of calling through call_host.
                            if (inlineOp)
                            {
                                EmitInlinedOp(ir, irToIl, evalStack, argBase, argc, dst,
                                    hostEntry.ReturnStruct!, hostEntry, instrStart, isCtor: false);
                                sp = spBase;
                                evalStack[sp++] = (dst, SType.Vt);
                                break;
                            }

                            ir.Add((uint)Op.call_host); irToIl.Add(instrStart);
                            ir.Add((uint)(isVoid2 ? -1 : dst)); irToIl.Add(instrStart);
                            ir.Add((uint)(hostEntry.Binding.HasThis ? recvSlot : -1)); irToIl.Add(instrStart);
                            ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                            ir.Add((uint)argc); irToIl.Add(instrStart);
                            for (int k = 0; k < argc; k++)
                            {
                                ir.Add((uint)evalStack[argBase + k].slot); irToIl.Add(instrStart);
                            }
                            // Nested-offset Vt receiver: copy the (possibly mutated) temp back into
                            // its byte range of the outer struct.
                            if (recvVtWb)
                            {
                                ir.Add((uint)Op.stfld_vt_vt); irToIl.Add(instrStart);
                                ir.Add((uint)recvVtWbBase); irToIl.Add(instrStart);
                                ir.Add((uint)recvVtOff); irToIl.Add(instrStart);
                                ir.Add((uint)recvSlot); irToIl.Add(instrStart);
                            }
                            // Mutating value-type method via field address: store the mutated receiver box
                            // back into the field (see recvWriteBack above).
                            if (recvWriteBack)
                            {
                                if (wbSc)
                                {
                                    var wbOp = wbFldSt == SType.I4 ? Op.stfld_sc_i4 : wbFldSt == SType.R4 ? Op.stfld_sc_r4 : Op.stfld_sc_o;
                                    ir.Add((uint)wbOp); irToIl.Add(instrStart);
                                    ir.Add((uint)wbFldObj); irToIl.Add(instrStart);
                                    ir.Add((uint)wbFldOff); irToIl.Add(instrStart);
                                    ir.Add((uint)recvSlot); irToIl.Add(instrStart);
                                }
                                else
                                {
                                    var wbOp = wbFldSt == SType.I4 ? Op.stfld_i4 : wbFldSt == SType.R4 ? Op.stfld_r4 : Op.stfld_o;
                                    ir.Add((uint)wbOp); irToIl.Add(instrStart);
                                    ir.Add((uint)wbFldObj); irToIl.Add(instrStart);
                                    ir.Add((uint)wbFldToki); irToIl.Add(instrStart);
                                    ir.Add((uint)recvSlot); irToIl.Add(instrStart);
                                }
                            }
                            sp = spBase;
                            if (!isVoid2) evalStack[sp++] = (dst, useFlatVt ? SType.Vt : useFlatNum ? retSt : SType.O);
                        }
                        else if (asm.HostCtors.TryGetValue(tok, out var callCtor))
                        {
                            // ldloca.s + call hostCtor pattern: create host object, store into local slot
                            int callCtorArgc = callCtor.Binding.ParamCount; // explicit params only
                            int spBase2 = sp - callCtorArgc - 1; // -1 for the this-addr
                            int thisAddrSlot = evalStack[spBase2].slot;
                            if (addrTagBySlot[thisAddrSlot] >= 0)
                            {
                                int destSlot = addrTagBySlot[thisAddrSlot];
                                addrTagBySlot[thisAddrSlot] = -2; // consume addr tag
                                ir.Add((uint)Op.newobj_host); irToIl.Add(instrStart);
                                ir.Add((uint)destSlot); irToIl.Add(instrStart);
                                ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                                ir.Add((uint)callCtorArgc); irToIl.Add(instrStart);
                                int argBase2 = sp - callCtorArgc;
                                for (int k = 0; k < callCtorArgc; k++)
                                { ir.Add((uint)evalStack[argBase2 + k].slot); irToIl.Add(instrStart); }
                                sp = spBase2;
                            }
                            else
                                goto notSupported;
                        }
                        else
                        {
                            // unresolved — treat as not-supported so IL path handles it
                            goto notSupported;
                        }
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    // --- Newobj ---
                    case ILOpCode.Ldftn:
                    {
                        pendingFtnTok = BitConverter.ToInt32(il, ip); ip += 4;
                        // Placeholder for the native fn pointer; the delegate newobj pops it.
                        evalStack[sp++] = (-1, SType.I4);
                        break;
                    }
                    case ILOpCode.Ldvirtftn:
                    {
                        pendingFtnTok = BitConverter.ToInt32(il, ip); ip += 4;
                        pendingFtnVirt = true;
                        // Pops the dup'd receiver copy, pushes the fn pointer placeholder — the
                        // original receiver stays beneath for the delegate newobj.
                        evalStack[sp - 1] = (-1, SType.I4);
                        break;
                    }

                    case ILOpCode.Newobj:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);

                        if (pendingFtnTok != 0)
                        {
                            // Delegate construction: `recv|null; ftn; newobj D::.ctor(object, IntPtr)`.
                            int ftnTok = pendingFtnTok; pendingFtnTok = 0;
                            bool ftnVirt = pendingFtnVirt; pendingFtnVirt = false;
                            var delegateType = ResolveCtorParentType(asm, tok);
                            // A delegate TYPE that names a script type (e.g. Action<Node>) has NO runtime
                            // System.Type — ResolveCtorParentType returns null because MakeGenericType needs
                            // a real Type for the script-class arg. We can't build a CLR delegate over it (a
                            // CLR Invoke(Node) can't carry a ScriptObject), so lower to an internal
                            // ScriptDelegate over the interpreted target and invoke it by VM re-entry. A
                            // host-resolvable delegate type keeps the CLR ScriptDelegateAdapter path below.
                            // Positive test: the type ARGUMENTS must name a script type. A host delegate the
                            // binding merely failed to resolve (nested host type, type outside the binding)
                            // is not script-typed and keeps the unresolvable-type rejection below.
                            bool scriptTyped = delegateType == null && CtorParentNamesScriptType(asm, tok);
                            if (!scriptTyped && !typeof(Delegate).IsAssignableFrom(delegateType))
                                throw new NotSupportedException(
                                    $"IR lowering: delegate construction in '{method.Name}' at IL+0x{instrStart:X4}" +
                                    $" over an unresolvable delegate type (token 0x{tok:X8})");
                            var site = new DelegateSite { DelegateType = delegateType! };
                            if (asm.ByToken.TryGetValue(ftnTok, out var scriptTarget))
                            {
                                site.ScriptMethod = scriptTarget;
                                // ldvirtftn on a virtual script method: CLR resolves the override
                                // ONCE at delegate creation from the receiver's runtime type —
                                // binding the base statically ran the wrong body (found by fuzzing
                                // probes). Plain ldftn (e.g. a `base.M` group) stays static.
                                site.VirtualResolve = ftnVirt && scriptTarget.IsVirtual && !scriptTarget.IsStatic;
                            }
                            else if (!scriptTyped && asm.HostCalls.TryGetValue(ftnTok, out var hostTarget)
                                     && (hostTarget.ResolvedMethod ?? hostTarget.Binding.Method as MethodInfo) is { } mi)
                                site.HostMethod = mi;
                            else
                            {
                                asm.TokenNames.TryGetValue(ftnTok, out var ftnName);
                                throw new NotSupportedException(scriptTyped
                                    ? $"IR lowering: a delegate whose type names a script type in '{method.Name}' " +
                                      $"at IL+0x{instrStart:X4} targets '{ftnName ?? $"token 0x{ftnTok:X8}"}', which is " +
                                      "not an interpreted method — script-typed delegates are supported only over " +
                                      "script methods and lambdas"
                                    : $"IR lowering: method group '{ftnName ?? $"token 0x{ftnTok:X8}"}' in " +
                                      $"'{method.Name}' at IL+0x{instrStart:X4} did not resolve to a host or script method");
                            }
                            (delegateSiteByTokIdx ??= new Dictionary<int, DelegateSite>())[tokIdx] = site;

                            int recvSlot = evalStack[sp - 2].slot;
                            if (recvSlot < 0 || addrTagBySlot[recvSlot] != -2)
                                goto notSupported; // phantom/placeholder receiver — not a plain O value
                            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            // Always new_delegate; the executor picks the ScriptDelegate path when the
                            // site's DelegateType is null (scriptTyped) — no separate op needed.
                            ir.Add((uint)Op.new_delegate); irToIl.Add(instrStart);
                            ir.Add((uint)dst); irToIl.Add(instrStart);
                            ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                            ir.Add((uint)recvSlot); irToIl.Add(instrStart);
                            sp -= 2;
                            evalStack[sp++] = (dst, SType.O);
                            ensuredScriptSlots.Clear();
                            break;
                        }

                        if (asm.CtorToType.TryGetValue(tok, out var newObjDesc))
                        {
                            // Script-defined type
                            int argc = asm.ByToken.TryGetValue(tok, out var ctorM) ? ctorM.ArgCount - 1 : 0;
                            if (newObjDesc.FlatLayout != null)
                            {
                                // FLAT struct `new S(...)` as an expression: zeroed Vt temp +
                                // ctor call with `this` = the temp (VtThisWb copies mutations back).
                                int vdst = AllocStructSlot(ref frameSize, slotTypes, slotStructs, newObjDesc.FlatLayout);
                                Emit2(ir, irToIl, Op.initobj, vdst, instrStart);
                                int argBaseF = sp - argc;
                                ir.Add((uint)Op.call_script); irToIl.Add(instrStart);
                                ir.Add(unchecked((uint)-1)); irToIl.Add(instrStart);
                                ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                                ir.Add((uint)(argc + 1)); irToIl.Add(instrStart);
                                ir.Add((uint)vdst); irToIl.Add(instrStart); // `this`
                                for (int k = 0; k < argc; k++)
                                { ir.Add((uint)evalStack[argBaseF + k].slot); irToIl.Add(instrStart); }
                                sp = argBaseF;
                                evalStack[sp++] = (vdst, SType.Vt);
                                ensuredScriptSlots.Clear();
                                break;
                            }
                            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            ir.Add((uint)Op.newobj_script); irToIl.Add(instrStart);
                            ir.Add((uint)dst); irToIl.Add(instrStart);
                            ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                            ir.Add((uint)argc); irToIl.Add(instrStart);
                            int argBase = sp - argc;
                            for (int k = 0; k < argc; k++)
                            {
                                ir.Add((uint)evalStack[argBase + k].slot); irToIl.Add(instrStart);
                            }
                            sp = argBase;
                            evalStack[sp++] = (dst, SType.O);
                        }
                        else if (asm.HostCtors.TryGetValue(tok, out var hostCtor))
                        {
                            int argc = hostCtor.Binding.ParamCount;
                            // A host ctor whose declaring type is a flat-registered struct and that
                            // has a flat-write Fast → allocate dst as Vt and let the executor write
                            // the struct bytes directly. Otherwise stay on O.
                            // Same slow-path-flat reasoning as call_host's useFlatVt above.
                            bool useFlat = hostCtor.ReturnStruct != null;
                            int dst = useFlat
                                ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, hostCtor.ReturnStruct!)
                                : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            int argBase = sp - argc;
                            // Inline the ctor when field order is verified and the dst is flat Vt.
                            if (hostCtor.Binding.IsInlineableOp && useFlat && hostCtor.ReturnStruct != null
                                && CanInlineOp(hostCtor, argc, hostCtor.ReturnStruct, isCtor: true))
                            {
                                EmitInlinedOp(ir, irToIl, evalStack, argBase, argc, dst,
                                    hostCtor.ReturnStruct, hostCtor, instrStart, isCtor: true);
                                sp = argBase;
                                evalStack[sp++] = (dst, SType.Vt);
                                break;
                            }
                            ir.Add((uint)Op.newobj_host); irToIl.Add(instrStart);
                            ir.Add((uint)dst); irToIl.Add(instrStart);
                            ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                            ir.Add((uint)argc); irToIl.Add(instrStart);
                            for (int k = 0; k < argc; k++)
                            {
                                ir.Add((uint)evalStack[argBase + k].slot); irToIl.Add(instrStart);
                            }
                            sp = argBase;
                            evalStack[sp++] = (dst, useFlat ? SType.Vt : SType.O);
                        }
                        else
                        {
                            goto notSupported;
                        }
                        ensuredScriptSlots.Clear();
                        break;
                    }

                    // --- Field access ---
                    case ILOpCode.Ldfld:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        var (obj, _) = evalStack[--sp];
                        // Resolve address phantoms to actual object slots
                        int actualObj;
                        int recvOff = 0;   // composed byte offset within a Vt receiver slot
                        int heapOff = -1;  // composed PrimBytes offset for a heap receiver (-1 = token mode)
                        if (addrTagBySlot[obj] >= 0)
                        {
                            // Frame-slot address (ldloca.s / composed inline chain)
                            actualObj = addrTagBySlot[obj];
                            recvOff   = addrByteOffBySlot[obj];
                        }
                        else if (addrTagBySlot[obj] == -1 && addrFldOffBySlot[obj] >= 0)
                        {
                            // Composed inline address on a heap ScriptObject: parent + byte offset.
                            actualObj = addrFldObjBySlot[obj];
                            heapOff   = addrFldOffBySlot[obj];
                        }
                        else if (addrTagBySlot[obj] == -1)
                        {
                            // Field-address phantom (ldflda): materialise the intermediate field first
                            int midFldObj  = addrFldObjBySlot[obj];
                            int midFldToki = addrFldTokBySlot[obj];
                            int midFldTok  = tokens[midFldToki];
                            asm.FieldSTypes.TryGetValue(midFldTok, out var midFst);
                            if (midFst == SType.Vt && asm.FieldSlots.TryGetValue(midFldTok, out var midFsVt))
                            {
                                // Vt field on a script class — allocate a Vt slot and emit ldfld_sc_vt.
                                var midVtLay = midFsVt.Item1.VtFieldLayouts?[midFsVt.Item2];
                                int midSlotVt = midVtLay != null
                                    ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, midVtLay)
                                    : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                int midOffVt = midFsVt.Item1.FieldOffsets[midFsVt.Item2];
                                Emit4(ir, irToIl, Op.ldfld_sc_vt, midSlotVt, midFldObj, midOffVt, instrStart);
                                actualObj = midSlotVt;
                            }
                            else
                            {
                                int midSlot = AllocSlot(ref frameSize, slotTypes, slotStructs, midFst, null);
                                if (asm.FieldSlots.TryGetValue(midFldTok, out var midFs))
                                {
                                    int midOff = midFs.Item1.FieldOffsets[midFs.Item2];
                                    var midOp2 = midFst == SType.I4 ? Op.ldfld_sc_i4 : midFst == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                                    Emit4(ir, irToIl, midOp2, midSlot, midFldObj, midOff, instrStart);
                                }
                                else
                                {
                                    var midOp = midFst == SType.I4 ? Op.ldfld_i4 : midFst == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                                    Emit4(ir, irToIl, midOp, midSlot, midFldObj, midFldToki, instrStart);
                                }
                                actualObj = midSlot;
                            }
                        }
                        else if (addrTagBySlot[obj] == -3 && addrElemLayoutBySlot[obj] != null)
                        {
                            // Flat array element: memcpy it into a Vt temp — the read flows through
                            // the Vt-receiver field branch below (no write-back needed for loads).
                            int arrSlot = addrFldObjBySlot[obj];
                            int idxSlot = addrFldTokBySlot[obj];
                            int midVt = AllocStructSlot(ref frameSize, slotTypes, slotStructs, addrElemLayoutBySlot[obj]!);
                            Emit4(ir, irToIl, Op.ldelem_vt, midVt, arrSlot, idxSlot, instrStart);
                            actualObj = midVt;
                            recvOff   = addrByteOffBySlot[obj];
                        }
                        else if (addrTagBySlot[obj] == -3)
                        {
                            // Array-element address (ldelema): materialise the element via ldelem.
                            int arrSlot = addrFldObjBySlot[obj];
                            int idxSlot = addrFldTokBySlot[obj];
                            int midSlot = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            Emit4(ir, irToIl, Op.ldelem_o, midSlot, arrSlot, idxSlot, instrStart);
                            actualObj = midSlot;
                        }
                        else
                        {
                            actualObj = obj;
                        }
                        // HOST-struct field read through a composed heap offset: the flat struct
                        // lives inline in a ScriptObject's PrimBytes — e.g. a Vector3 hoisted into
                        // an iterator state-machine field (`target.y` after a yield). Without this
                        // the read fell through to the boxed host-field path with the ScriptObject
                        // as receiver ("Field y ... is not a field on ScriptObject").
                        if (heapOff >= 0 && asm.HostFields.TryGetValue(tok, out var hfheap)
                            && hfheap.DeclaringStruct != null)
                        {
                            if (hfheap.PrimitiveSt == SType.I4 || hfheap.PrimitiveSt == SType.R4)
                            {
                                // Primitive field: materialize the declaring struct into a Vt temp
                                // so the flat fast path below applies (widening kinds included).
                                int midVtH = AllocStructSlot(ref frameSize, slotTypes, slotStructs, hfheap.DeclaringStruct);
                                Emit4(ir, irToIl, Op.ldfld_sc_vt, midVtH, actualObj, heapOff, instrStart);
                                actualObj = midVtH;
                                recvOff = 0;
                                heapOff = -1;
                            }
                            else if (hfheap.FieldTypeName != null && asm.HostSurface != null
                                && asm.HostSurface.TryGetStructLayout(hfheap.FieldTypeName, out var nLayH)
                                && nLayH != null)
                            {
                                // NESTED struct field (`pose.P` value read): copy its byte range
                                // straight out at the summed offset.
                                int dstNH = AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLayH);
                                evalStack[sp++] = (dstNH, SType.Vt);
                                Emit4(ir, irToIl, Op.ldfld_sc_vt, dstNH, actualObj, heapOff + hfheap.ByteOffset, instrStart);
                                break;
                            }
                        }
                        // Flat-struct fast path: receiver is a Vt slot AND the field is a registered
                        // primitive on that struct → emit ldfld_vt_* with the byte offset, bypassing
                        // the boxed Get delegate entirely.
                        if (heapOff < 0 && slotTypes[actualObj] == SType.Vt
                            && asm.HostFields.TryGetValue(tok, out var hfvt)
                            && hfvt.DeclaringStruct != null
                            && (hfvt.PrimitiveSt == SType.I4 || hfvt.PrimitiveSt == SType.R4))
                        {
                            int dstv = AllocSlot(ref frameSize, slotTypes, slotStructs, hfvt.PrimitiveSt, null);
                            evalStack[sp++] = (dstv, hfvt.PrimitiveSt);
                            Emit4(ir, irToIl, LdFldVtOpFor(hfvt), dstv, actualObj, recvOff + hfvt.ByteOffset, instrStart);
                            break;
                        }
                        // Flat script-struct receiver (Vt frame slot): field reads are direct
                        // byte-offset loads; nested blittable struct fields memcpy a sub-range.
                        if (heapOff < 0 && slotTypes[actualObj] == SType.Vt
                            && asm.FieldSlots.TryGetValue(tok, out var vfs))
                        {
                            var vfst = vfs.Item1.FieldTypes[vfs.Item2];
                            int vOff = recvOff + vfs.Item1.FieldOffsets[vfs.Item2];
                            if (vfst == SType.Vt)
                            {
                                var nLay = vfs.Item1.VtFieldLayouts?[vfs.Item2];
                                int dstn = nLay != null
                                    ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLay)
                                    : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                evalStack[sp++] = (dstn, SType.Vt);
                                Emit4(ir, irToIl, Op.ldfld_vt_vt, dstn, actualObj, vOff, instrStart);
                            }
                            else
                            {
                                var pst = vfst == SType.R4 ? SType.R4 : SType.I4;
                                int dstn = AllocSlot(ref frameSize, slotTypes, slotStructs, pst, null);
                                evalStack[sp++] = (dstn, pst);
                                Emit4(ir, irToIl, pst == SType.I4 ? Op.ldfld_vt_i4 : Op.ldfld_vt_r4,
                                    dstn, actualObj, vOff, instrStart);
                            }
                            break;
                        }
                        // Heap receiver with a COMPOSED inline offset (nested blittable struct
                        // inside a heap ScriptObject): direct PrimBytes access at the summed offset.
                        if (heapOff >= 0 && asm.FieldSlots.TryGetValue(tok, out var cfs))
                        {
                            var cfst = cfs.Item1.FieldTypes[cfs.Item2];
                            int cOff = heapOff + cfs.Item1.FieldOffsets[cfs.Item2];
                            if (cfst == SType.Vt)
                            {
                                var nLay = cfs.Item1.VtFieldLayouts?[cfs.Item2];
                                int dstn = nLay != null
                                    ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLay)
                                    : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                evalStack[sp++] = (dstn, SType.Vt);
                                Emit4(ir, irToIl, Op.ldfld_sc_vt, dstn, actualObj, cOff, instrStart);
                            }
                            else
                            {
                                var pst = cfst == SType.R4 ? SType.R4 : SType.I4;
                                int dstn = AllocSlot(ref frameSize, slotTypes, slotStructs, pst, null);
                                evalStack[sp++] = (dstn, pst);
                                Emit4(ir, irToIl, pst == SType.I4 ? Op.ldfld_sc_i4 : Op.ldfld_sc_r4,
                                    dstn, actualObj, cOff, instrStart);
                            }
                            break;
                        }
                        asm.FieldSTypes.TryGetValue(tok, out var fst);
                        if (asm.FieldSlots.TryGetValue(tok, out var fsLo))
                        {
                            int off = fsLo.Item1.FieldOffsets[fsLo.Item2];
                            if (fst == SType.Vt)
                            {
                                // Host-struct Vt field stored inline in PrimBytes; copy bytes out.
                                var vtLay = fsLo.Item1.VtFieldLayouts?[fsLo.Item2];
                                int dst2 = vtLay != null
                                    ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, vtLay)
                                    : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                evalStack[sp++] = (dst2, fst);
                                Emit4(ir, irToIl, Op.ldfld_sc_vt, dst2, actualObj, off, instrStart);
                            }
                            else
                            {
                                int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, fst, null);
                                evalStack[sp++] = (dst2, fst);
                                var op2 = fst == SType.I4 ? Op.ldfld_sc_i4 : fst == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                                Emit4(ir, irToIl, op2, dst2, actualObj, off, instrStart);
                            }
                        }
                        else
                        {
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, fst, null);
                            evalStack[sp++] = (dst2, fst);
                            var ldfldOp = fst == SType.I4 ? Op.ldfld_i4 : fst == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                            Emit4(ir, irToIl, ldfldOp, dst2, actualObj, tokIdx, instrStart);
                        }
                        // Wide (long/double) script field: the load above fetched the BOXED value
                        // into an O slot. Unbox into a correctly-typed wide slot — mov with a wide
                        // dst reads the src via RdI8/RdR8, which coerce boxed values — so downstream
                        // arithmetic takes the i8/r8 path instead of the truncating i4 one.
                        if (fst == SType.O && asm.FieldWide.TryGetValue(tok, out var wfst))
                        {
                            var (boxSlot, _) = evalStack[sp - 1];
                            int wdst = AllocSlot(ref frameSize, slotTypes, slotStructs, wfst, null);
                            Emit3(ir, irToIl, Op.mov, wdst, boxSlot, instrStart);
                            evalStack[sp - 1] = (wdst, wfst);
                        }
                        // Value-load of a script-struct field: clone so the pushed value is an
                        // independent copy (struct copy semantics). Field mutation uses ldflda, which
                        // takes a different path and is unaffected.
                        if (fst == SType.O && asm.FieldIsScriptStruct.Contains(tok))
                        {
                            var (fldSlot, _) = evalStack[sp - 1];
                            int cl = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            Emit3(ir, irToIl, Op.clone_sc, cl, fldSlot, instrStart);
                            evalStack[sp - 1] = (cl, SType.O);
                        }
                        break;
                    }
                    case ILOpCode.Ldflda:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        var (obj, _) = evalStack[--sp];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null); // phantom slot
                        asm.FieldSTypes.TryGetValue(tok, out var fldaFst);
                        bool isScFldA = asm.FieldSlots.TryGetValue(tok, out var fldaFs);
                        int inTag = addrTagBySlot[obj];

                        // Address of a field INSIDE flat struct bytes: accumulate the byte offset
                        // instead of materializing — this is what makes nested chains like
                        // `a.inner.x = v` (ldloca a; ldflda inner; stfld x) work. HOST flat-struct
                        // fields (Pose.P where Pose is a registered flat struct) compose the same
                        // way via the FieldEntry byte offset — without this a chain rooted at a
                        // hoisted iterator field fell to the token-mode phantom and consumers
                        // reflected on a null or ScriptObject receiver.
                        HostBinding.FieldEntry? hfA = null;
                        bool isHostFlatA = !isScFldA
                            && asm.HostFields.TryGetValue(tok, out hfA) && hfA!.DeclaringStruct != null;
                        if ((isScFldA && (fldaFst == SType.Vt || fldaFst == SType.I4 || fldaFst == SType.R4))
                            || isHostFlatA)
                        {
                            int fOff = isScFldA
                                ? fldaFs.Item1.FieldOffsets[fldaFs.Item2]
                                : hfA!.ByteOffset;
                            // "Is the addressed field itself a struct?" — gates the heap-receiver
                            // branch below. For host fields, anything non-primitive is a nested struct.
                            bool fldaIsVt = isScFldA
                                ? fldaFst == SType.Vt
                                : hfA!.PrimitiveSt != SType.I4 && hfA.PrimitiveSt != SType.R4;
                            if (inTag >= 0 && slotTypes[inTag] == SType.Vt)
                            {
                                // base = frame Vt slot (+ prior offset)
                                addrTagBySlot[dst]     = inTag;
                                addrByteOffBySlot[dst] = addrByteOffBySlot[obj] + fOff;
                                addrFldTokBySlot[dst]  = tokIdx;
                                evalStack[sp++] = (dst, SType.O);
                                break;
                            }
                            if (inTag == -1 && addrFldOffBySlot[obj] >= 0)
                            {
                                // base = heap parent + prior PrimBytes offset
                                addrTagBySlot[dst]     = -1;
                                addrFldObjBySlot[dst]  = addrFldObjBySlot[obj];
                                addrFldOffBySlot[dst]  = addrFldOffBySlot[obj] + fOff;
                                addrFldTokBySlot[dst]  = tokIdx;
                                evalStack[sp++] = (dst, SType.O);
                                break;
                            }
                            if (inTag == -1 && addrFldOffBySlot[obj] < 0)
                            {
                                // base = token-addressed field phantom. Composition is only valid
                                // when the OUTER field is itself inline (sc-Vt); resolve its offset.
                                int outTok = tokens[addrFldTokBySlot[obj]];
                                asm.FieldSTypes.TryGetValue(outTok, out var outFst);
                                if (outFst == SType.Vt && asm.FieldSlots.TryGetValue(outTok, out var outFs))
                                {
                                    addrTagBySlot[dst]     = -1;
                                    addrFldObjBySlot[dst]  = addrFldObjBySlot[obj];
                                    addrFldOffBySlot[dst]  = outFs.Item1.FieldOffsets[outFs.Item2] + fOff;
                                    addrFldTokBySlot[dst]  = tokIdx;
                                    evalStack[sp++] = (dst, SType.O);
                                    break;
                                }
                            }
                            if (inTag == -2 && slotTypes[obj] == SType.Vt)
                            {
                                // base = a Vt VALUE slot on the stack (r-value receiver)
                                addrTagBySlot[dst]     = obj;
                                addrByteOffBySlot[dst] = fOff;
                                addrFldTokBySlot[dst]  = tokIdx;
                                evalStack[sp++] = (dst, SType.O);
                                break;
                            }
                            if (inTag == -3 && addrElemLayoutBySlot[obj] != null)
                            {
                                // Address of a field inside a FLAT ARRAY ELEMENT: keep the element
                                // address, accumulate the byte offset (consumers materialize the
                                // element and write it back).
                                addrTagBySlot[dst]        = -3;
                                addrFldObjBySlot[dst]     = addrFldObjBySlot[obj];
                                addrFldTokBySlot[dst]     = addrFldTokBySlot[obj];
                                addrElemLayoutBySlot[dst] = addrElemLayoutBySlot[obj];
                                addrByteOffBySlot[dst]    = addrByteOffBySlot[obj] + fOff;
                                evalStack[sp++] = (dst, SType.O);
                                break;
                            }
                            if (fldaIsVt)
                            {
                                // Address of an INLINE (Vt) struct field on a HEAP receiver (class
                                // instance or heap-struct local): offset-mode phantom rooted at the
                                // OBJECT slot, so consumers read/write PrimBytes in place —
                                // materializing would copy and lose mutations (h.pos.x = 3).
                                int baseObj;
                                if (inTag == -3)
                                {
                                    // BOXED array element (no flat layout): `obj` is the ldelema
                                    // placeholder, never a real object. Materialize the element via
                                    // ldelem_o — it returns the ScriptObject reference the array
                                    // holds, so PrimBytes reads/writes at the field offset hit the
                                    // live element in place (found by fuzzing: `a[i].n.f0` NRE'd on
                                    // a null placeholder receiver).
                                    int arrS = addrFldObjBySlot[obj], idxS = addrFldTokBySlot[obj];
                                    baseObj = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                    Emit4(ir, irToIl, Op.ldelem_o, baseObj, arrS, idxS, instrStart);
                                }
                                else baseObj = inTag >= 0 ? inTag : obj;
                                addrTagBySlot[dst]    = -1;
                                addrFldObjBySlot[dst] = baseObj;
                                addrFldOffBySlot[dst] = fOff;
                                addrFldTokBySlot[dst] = tokIdx;
                                evalStack[sp++] = (dst, SType.O);
                                break;
                            }
                        }

                        // Classic path: address of a field on a (heap) object — resolve ldloca
                        // bases to the actual slot; consumers materialize via the token.
                        int actualObj;
                        if (inTag == -3)
                        {
                            // Field address inside a BOXED (non-flat) array element — e.g.
                            // `sa[i].f0 += 1` where the struct has a ref-slot field (string/long/
                            // double). Materialize the element: it's a ScriptObject, a reference
                            // INTO the array, so writes through it stick. Rooting the phantom at
                            // the raw -3 slot handed consumers a never-written null receiver
                            // (found by fuzzing).
                            int arrSlotA = addrFldObjBySlot[obj];
                            int idxSlotA = addrFldTokBySlot[obj];
                            actualObj = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            Emit4(ir, irToIl, Op.ldelem_o, actualObj, arrSlotA, idxSlotA, instrStart);
                        }
                        else if (inTag == -1 && addrFldOffBySlot[obj] < 0)
                        {
                            // Field address chained on an unmaterialized token-mode FIELD phantom
                            // whose outer field is boxed (O storage — a script struct with ref
                            // fields): materialize the outer field; the loaded ScriptObject is the
                            // same reference the outer field's ref slot holds, so writes stick.
                            int outObjA  = addrFldObjBySlot[obj];
                            int outTokiA = addrFldTokBySlot[obj];
                            int outTokA  = tokens[outTokiA];
                            actualObj = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            if (asm.FieldSlots.TryGetValue(outTokA, out var outFsA))
                                Emit4(ir, irToIl, Op.ldfld_sc_o, actualObj, outObjA,
                                    outFsA.Item1.FieldOffsets[outFsA.Item2], instrStart);
                            else
                                Emit4(ir, irToIl, Op.ldfld_o, actualObj, outObjA, outTokiA, instrStart);
                        }
                        else
                            actualObj = inTag >= 0 ? inTag : obj;
                        addrTagBySlot[dst]    = -1; // field address
                        addrFldObjBySlot[dst] = actualObj;
                        addrFldTokBySlot[dst] = tokIdx;
                        evalStack[sp++] = (dst, SType.O);
                        break;
                    }

                    case ILOpCode.Stfld:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        var (src, srcT) = evalStack[--sp];
                        var (obj, _) = evalStack[--sp];
                        int tag = addrTagBySlot[obj];
                        int recvOffS = tag >= 0 ? addrByteOffBySlot[obj] : 0;
                        int heapOffS = tag == -1 ? addrFldOffBySlot[obj] : -1;
                        // If obj is a frame-slot addr (from ldloca.s), use the actual local slot as receiver.
                        // If obj is a field-address phantom (ldflda on a struct class-field), materialise the
                        // field into a temp slot. ScriptObject is a reference type, so writing through the
                        // reference propagates to the parent's field slot automatically — no write-back needed.
                        // If obj is an array-element phantom (ldelema on a struct array), materialise the
                        // element the same way.
                        int actualObj;
                        if (tag >= 0)
                        {
                            actualObj = tag; // ldloca.s → resolve to actual local
                        }
                        else if (tag == -1 && heapOffS >= 0)
                        {
                            // Composed inline address on a heap ScriptObject: use the parent directly.
                            actualObj = addrFldObjBySlot[obj];
                        }
                        else if (tag == -1)
                        {
                            // ldflda phantom: load the struct reference stored in the parent class field
                            int midFldObj  = addrFldObjBySlot[obj];
                            int midFldToki = addrFldTokBySlot[obj];
                            int midTok     = tokens[midFldToki];
                            asm.FieldSTypes.TryGetValue(midTok, out var midFst);
                            if (midFst == SType.Vt && asm.FieldSlots.TryGetValue(midTok, out var midFs2Vt))
                            {
                                // Vt field on a script class — allocate Vt slot and emit ldfld_sc_vt.
                                var midVtLay2 = midFs2Vt.Item1.VtFieldLayouts?[midFs2Vt.Item2];
                                int midSlotVt2 = midVtLay2 != null
                                    ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, midVtLay2)
                                    : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                int midOff2Vt = midFs2Vt.Item1.FieldOffsets[midFs2Vt.Item2];
                                Emit4(ir, irToIl, Op.ldfld_sc_vt, midSlotVt2, midFldObj, midOff2Vt, instrStart);
                                actualObj = midSlotVt2;
                            }
                            else
                            {
                                int midSlot = AllocSlot(ref frameSize, slotTypes, slotStructs, midFst, null);
                                if (asm.FieldSlots.TryGetValue(midTok, out var midFs2))
                                {
                                    int midOff2 = midFs2.Item1.FieldOffsets[midFs2.Item2];
                                    var midOp2 = midFst == SType.I4 ? Op.ldfld_sc_i4 : midFst == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                                    Emit4(ir, irToIl, midOp2, midSlot, midFldObj, midOff2, instrStart);
                                }
                                else
                                {
                                    var midOp = midFst == SType.I4 ? Op.ldfld_i4 : midFst == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                                    Emit4(ir, irToIl, midOp, midSlot, midFldObj, midFldToki, instrStart);
                                }
                                actualObj = midSlot;
                            }
                        }
                        else if (tag == -3 && addrElemLayoutBySlot[obj] != null)
                        {
                            // Flat array element write (`arr[i].f = v`): materialize → mutate the
                            // Vt temp → memcpy the whole element back. Handled inline because the
                            // write-back must follow the field store.
                            int arrSlotW = addrFldObjBySlot[obj];
                            int idxSlotW = addrFldTokBySlot[obj];
                            var elemLayW = addrElemLayoutBySlot[obj]!;
                            int elemTmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, elemLayW);
                            Emit4(ir, irToIl, Op.ldelem_vt, elemTmp, arrSlotW, idxSlotW, instrStart);
                            int baseOffW = addrByteOffBySlot[obj];
                            // Host-struct element field (arr[i].x on Vector3[]): FieldEntry byte offset.
                            if (asm.HostFields.TryGetValue(tok, out var whf) && whf.DeclaringStruct != null
                                && (whf.PrimitiveSt == SType.I4 || whf.PrimitiveSt == SType.R4))
                            {
                                Emit4(ir, irToIl, StFldVtOpFor(whf),
                                    elemTmp, baseOffW + whf.ByteOffset, src, instrStart);
                                Emit4(ir, irToIl, Op.stelem_vt, arrSlotW, idxSlotW, elemTmp, instrStart);
                                break;
                            }
                            if (asm.FieldSlots.TryGetValue(tok, out var wfs))
                            {
                                var wfst = wfs.Item1.FieldTypes[wfs.Item2];
                                int wOff = baseOffW + wfs.Item1.FieldOffsets[wfs.Item2];
                                if (wfst == SType.Vt)
                                {
                                    if (srcT != SType.Vt)
                                    {
                                        var nLayW = wfs.Item1.VtFieldLayouts?[wfs.Item2];
                                        if (nLayW != null)
                                        {
                                            int vtmpW = AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLayW);
                                            Emit3(ir, irToIl, Op.unbox_vt, vtmpW, src, instrStart);
                                            src = vtmpW;
                                        }
                                    }
                                    Emit4(ir, irToIl, Op.stfld_vt_vt, elemTmp, wOff, src, instrStart);
                                }
                                else
                                {
                                    Emit4(ir, irToIl, wfst == SType.R4 ? Op.stfld_vt_r4 : Op.stfld_vt_i4,
                                        elemTmp, wOff, src, instrStart);
                                }
                            }
                            Emit4(ir, irToIl, Op.stelem_vt, arrSlotW, idxSlotW, elemTmp, instrStart);
                            break;
                        }
                        else if (tag == -3)
                        {
                            // ldelema phantom: load the struct reference stored in the array element
                            int arrSlot = addrFldObjBySlot[obj];
                            int idxSlot = addrFldTokBySlot[obj];
                            int midSlot = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            Emit4(ir, irToIl, Op.ldelem_o, midSlot, arrSlot, idxSlot, instrStart);
                            actualObj = midSlot;
                        }
                        else
                        {
                            actualObj = obj;
                        }
                        // Auto-init: if this stfld writes into an O slot (script-defined struct local
                        // addressed via ldloca.s), emit ensure_script before the first field write.
                        // Roslyn omits initobj when the struct is "definitely assigned" by consecutive
                        // field stores, but our runtime O slot starts null — so the first stfld on
                        // any execution path would NRE without this guard.
                        // Optimization: skip if the slot is already known initialized in this basic block
                        // (tracked in ensuredScriptSlots). Cleared at block boundaries and after calls/branches.
                        // Same for a BOXED host struct local (`S s; s.x = 1; s.y = 2;` with no
                        // initobj): the field's declaring TypeRef token has a synthetic default
                        // ctor (see the parse-time hostCtors registration), and ensure_script
                        // creates the box through it when the slot is still null.
                        if (tag >= 0 && slotTypes[actualObj] == SType.O
                            && (asm.FieldToTypeDef.TryGetValue(tok, out int fieldTypeDef)
                                || (asm.HostFieldDeclTypeTok.TryGetValue(tok, out fieldTypeDef)
                                    && asm.HostCtors.ContainsKey(fieldTypeDef))))
                        {
                            if (!ensuredScriptSlots.Contains(actualObj))
                            {
                                int typeDefTokIdx2 = tokens.Count; tokens.Add(fieldTypeDef);
                                ir.Add((uint)Op.ensure_script); irToIl.Add(instrStart);
                                ir.Add((uint)actualObj); irToIl.Add(instrStart);
                                ir.Add((uint)typeDefTokIdx2); irToIl.Add(instrStart);
                            }
                            ensuredScriptSlots.Add(actualObj);
                        }
                        // HOST-struct field write through a composed heap offset (the mirror of the
                        // Ldfld handling above — a flat host struct inlined in a ScriptObject's
                        // PrimBytes, e.g. `target.y = v` on a hoisted iterator local).
                        if (heapOffS >= 0 && asm.HostFields.TryGetValue(tok, out var hfheapS)
                            && hfheapS.DeclaringStruct != null)
                        {
                            if (hfheapS.PrimitiveSt == SType.I4 || hfheapS.PrimitiveSt == SType.R4)
                            {
                                // Primitive field: the temp is a copy, so read-modify-WRITE-BACK —
                                // copy the struct out, store the field into the temp (widening/
                                // truncating kinds included), copy the whole struct back.
                                int midVtS = AllocStructSlot(ref frameSize, slotTypes, slotStructs, hfheapS.DeclaringStruct);
                                Emit4(ir, irToIl, Op.ldfld_sc_vt, midVtS, actualObj, heapOffS, instrStart);
                                Emit4(ir, irToIl, StFldVtOpFor(hfheapS), midVtS, hfheapS.ByteOffset, src, instrStart);
                                Emit4(ir, irToIl, Op.stfld_sc_vt, actualObj, heapOffS, midVtS, instrStart);
                                break;
                            }
                            if (srcT == SType.Vt)
                            {
                                // NESTED struct field (`pose.P = v`): write the source's byte range
                                // (stfld_sc_vt sizes from the SRC slot's layout) at the summed offset.
                                Emit4(ir, irToIl, Op.stfld_sc_vt, actualObj, heapOffS + hfheapS.ByteOffset, src, instrStart);
                                break;
                            }
                        }
                        // Flat-struct fast path: stfld of a primitive into a Vt slot.
                        if (heapOffS < 0 && slotTypes[actualObj] == SType.Vt
                            && asm.HostFields.TryGetValue(tok, out var hfvtS)
                            && hfvtS.DeclaringStruct != null
                            && (hfvtS.PrimitiveSt == SType.I4 || hfvtS.PrimitiveSt == SType.R4))
                        {
                            Emit4(ir, irToIl, StFldVtOpFor(hfvtS), actualObj, recvOffS + hfvtS.ByteOffset, src, instrStart);
                            break;
                        }
                        // Flat script-struct receiver (Vt frame slot).
                        if (heapOffS < 0 && slotTypes[actualObj] == SType.Vt
                            && asm.FieldSlots.TryGetValue(tok, out var vfsS))
                        {
                            var vfst = vfsS.Item1.FieldTypes[vfsS.Item2];
                            int vOff = recvOffS + vfsS.Item1.FieldOffsets[vfsS.Item2];
                            if (vfst == SType.Vt)
                            {
                                if (srcT != SType.Vt)
                                {
                                    var nLay = vfsS.Item1.VtFieldLayouts?[vfsS.Item2];
                                    if (nLay != null)
                                    {
                                        int vtmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLay);
                                        Emit3(ir, irToIl, Op.unbox_vt, vtmp, src, instrStart);
                                        src = vtmp;
                                    }
                                }
                                Emit4(ir, irToIl, Op.stfld_vt_vt, actualObj, vOff, src, instrStart);
                            }
                            else
                            {
                                Emit4(ir, irToIl, vfst == SType.R4 ? Op.stfld_vt_r4 : Op.stfld_vt_i4,
                                    actualObj, vOff, src, instrStart);
                            }
                            break;
                        }
                        // Heap receiver with a COMPOSED inline offset.
                        if (heapOffS >= 0 && asm.FieldSlots.TryGetValue(tok, out var cfsS))
                        {
                            var cfst = cfsS.Item1.FieldTypes[cfsS.Item2];
                            int cOff = heapOffS + cfsS.Item1.FieldOffsets[cfsS.Item2];
                            if (cfst == SType.Vt)
                            {
                                if (srcT != SType.Vt)
                                {
                                    var nLay = cfsS.Item1.VtFieldLayouts?[cfsS.Item2];
                                    if (nLay != null)
                                    {
                                        int vtmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLay);
                                        Emit3(ir, irToIl, Op.unbox_vt, vtmp, src, instrStart);
                                        src = vtmp;
                                    }
                                }
                                Emit4(ir, irToIl, Op.stfld_sc_vt, actualObj, cOff, src, instrStart);
                            }
                            else
                            {
                                Emit4(ir, irToIl, cfst == SType.R4 ? Op.stfld_sc_r4 : Op.stfld_sc_i4,
                                    actualObj, cOff, src, instrStart);
                            }
                            break;
                        }
                        asm.FieldSTypes.TryGetValue(tok, out var sfSType);
                        if (asm.FieldSlots.TryGetValue(tok, out var sfScFs))
                        {
                            int sfOff = sfScFs.Item1.FieldOffsets[sfScFs.Item2];
                            // Vt-typed destination field (inline in PrimBytes): emit stfld_sc_vt.
                            if (sfSType == SType.Vt)
                            {
                                // The runtime stfld_sc_vt copies the struct bytes out of the source's
                                // Vt frame slot. If the value instead arrived boxed in an O slot (e.g.
                                // a host call/ctor whose return wasn't flattened — common when a struct
                                // type is registered after its consumers), unbox it into a Vt temp using
                                // the field's layout first, so the store always reads real frame bytes.
                                if (srcT != SType.Vt)
                                {
                                    var fieldLay = sfScFs.Item1.VtFieldLayouts?[sfScFs.Item2];
                                    if (fieldLay != null)
                                    {
                                        int vtmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, fieldLay);
                                        Emit3(ir, irToIl, Op.unbox_vt, vtmp, src, instrStart);
                                        src = vtmp;
                                    }
                                }
                                ir.Add((uint)Op.stfld_sc_vt); irToIl.Add(instrStart);
                                ir.Add((uint)actualObj); irToIl.Add(instrStart);
                                ir.Add((uint)sfOff); irToIl.Add(instrStart);
                                ir.Add((uint)src); irToIl.Add(instrStart);
                                break;
                            }
                            // Vt source into a non-Vt receiver field (script struct field boxed in
                            // RefSlots): box it first.
                            if (srcT == SType.Vt)
                            {
                                int boxed = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                Emit3(ir, irToIl, Op.box_vt, boxed, src, instrStart);
                                src = boxed;
                            }
                            var scOp = sfSType == SType.I4 ? Op.stfld_sc_i4 : sfSType == SType.R4 ? Op.stfld_sc_r4 : Op.stfld_sc_o;
                            ir.Add((uint)scOp); irToIl.Add(instrStart);
                            ir.Add((uint)actualObj); irToIl.Add(instrStart);
                            ir.Add((uint)sfOff); irToIl.Add(instrStart);
                            ir.Add((uint)src); irToIl.Add(instrStart);
                        }
                        else
                        {
                            // Non-script-class field (host field). If src is Vt, box it first.
                            if (srcT == SType.Vt)
                            {
                                int boxed = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                Emit3(ir, irToIl, Op.box_vt, boxed, src, instrStart);
                                src = boxed;
                            }
                            var sfldOp = sfSType == SType.I4 ? Op.stfld_i4 : sfSType == SType.R4 ? Op.stfld_r4 : Op.stfld_o;
                            ir.Add((uint)sfldOp); irToIl.Add(instrStart);
                            ir.Add((uint)actualObj); irToIl.Add(instrStart);
                            ir.Add((uint)tokIdx); irToIl.Add(instrStart);
                            ir.Add((uint)src); irToIl.Add(instrStart);
                        }
                        break;
                    }
                    case ILOpCode.Ldsfld:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        // Host STRUCT field (the Vector3.one shape): unbox the Get() result into
                        // a flat Vt slot. The O-slot form handed a BOXED struct to flat consumers
                        // (inlined operators, Vt args, component reads), which read the numeric
                        // frame and saw zeros (found by fuzzing: HVec.One + v summed only v).
                        if (asm.HostFields.TryGetValue(tok, out var sfe) && sfe.FieldTypeName != null
                            && asm.HostSurface != null
                            && asm.HostSurface.TryGetStructLayout(sfe.FieldTypeName, out var sLay) && sLay != null)
                        {
                            int dstv = AllocStructSlot(ref frameSize, slotTypes, slotStructs, sLay);
                            evalStack[sp++] = (dstv, SType.Vt);
                            Emit3(ir, irToIl, Op.ldsfld_struct, dstv, tokIdx, instrStart);
                            break;
                        }
                        // SCRIPT static of primitive type: load into a typed numeric slot, not O.
                        // The boxed O form broke every consumer that keys off the slot type —
                        // `if (s_hp == 0 && …)` compiles to brtrue on the load, and brtrue_o
                        // tested the BOX for null (boxed 0 is non-null → wrong branch).
                        if (ScriptStaticPrimitiveSType(asm, tok) is { } sst)
                        {
                            int dstn = AllocSlot(ref frameSize, slotTypes, slotStructs, sst, null);
                            evalStack[sp++] = (dstn, sst);
                            Emit3(ir, irToIl, sst == SType.I4 ? Op.ldsfld_i4 : Op.ldsfld_r4,
                                dstn, tokIdx, instrStart);
                            break;
                        }
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp++] = (dst, SType.O);
                        Emit3(ir, irToIl, Op.ldsfld_o, dst, tokIdx, instrStart);
                        // Wide (long/double) SCRIPT static: reads come from the boxed store — unbox
                        // into a wide slot so arithmetic over two statics doesn't fall into the
                        // truncating i4 path (mirrors the Ldfld wide unbox; found by fuzzing).
                        // Statics aren't in FieldWide (the field-slot pass walks instance fields
                        // only), so classify from the signature blob.
                        if (ScriptStaticWideSType(asm, tok) is { } wsst)
                        {
                            int wdst = AllocSlot(ref frameSize, slotTypes, slotStructs, wsst, null);
                            Emit3(ir, irToIl, Op.mov, wdst, dst, instrStart);
                            evalStack[sp - 1] = (wdst, wsst);
                        }
                        break;
                    }
                    case ILOpCode.Ldsflda:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        // Address of a static field. C# emits this for member calls THROUGH the
                        // address — `str + s_count` becomes `ldsflda s_count; call int.ToString()`.
                        // For IMMUTABLE field types (primitives, enums, string) the address is
                        // equivalent to the value. Script statics load into TYPED slots like
                        // Ldsfld does (an enum static then hits the constrained-enum boxing on
                        // the call); host prims/strings load boxed. Mutable struct statics keep
                        // failing — a value copy would silently drop their mutations.
                        if (ScriptStaticPrimitiveSType(asm, tok) is { } ast)
                        {
                            int tdst = AllocSlot(ref frameSize, slotTypes, slotStructs, ast, null);
                            evalStack[sp++] = (tdst, ast);
                            Emit3(ir, irToIl, ast == SType.I4 ? Op.ldsfld_i4 : Op.ldsfld_r4,
                                tdst, tokIdx, instrStart);
                            break;
                        }
                        if (!StaticFieldIsImmutableValue(asm, tok))
                            goto notSupported;
                        int adst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp++] = (adst, SType.O);
                        Emit3(ir, irToIl, Op.ldsfld_o, adst, tokIdx, instrStart);
                        // Wide SCRIPT static addressed for a member call (`s_wl.ToString()`):
                        // unbox like Ldsfld — a never-written static's boxed store is null, and a
                        // null receiver rendered "" where C# prints 0 (found by fuzzing). The mov
                        // coerces null to a typed 0; the constrained call re-boxes from the wide slot.
                        if (ScriptStaticWideSType(asm, tok) is { } wast)
                        {
                            int wadst = AllocSlot(ref frameSize, slotTypes, slotStructs, wast, null);
                            Emit3(ir, irToIl, Op.mov, wadst, adst, instrStart);
                            evalStack[sp - 1] = (wadst, wast);
                        }
                        break;
                    }
                    case ILOpCode.Stsfld:
                    {
                        int tok = BitConverter.ToInt32(il, ip); ip += 4;
                        int tokIdx = tokens.Count; tokens.Add(tok);
                        var (src, srcT) = evalStack[--sp];
                        // Primitive script static: emit an unboxed typed store (mirrors Ldsfld). The old
                        // unconditional stsfld_o boxed every primitive static write.
                        if (srcT != SType.Vt && ScriptStaticPrimitiveSType(asm, tok) is { } sst)
                        {
                            Emit3(ir, irToIl, sst == SType.I4 ? Op.stsfld_i4 : Op.stsfld_r4, tokIdx, src, instrStart);
                            break;
                        }
                        // Host static fields take a boxed value (FieldEntry.Set); box a flat struct first.
                        if (srcT == SType.Vt)
                        {
                            int boxed = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            Emit3(ir, irToIl, Op.box_vt, boxed, src, instrStart);
                            src = boxed;
                        }
                        Emit3(ir, irToIl, Op.stsfld_o, tokIdx, src, instrStart);
                        break;
                    }
                    // --- Arrays ---
                    case ILOpCode.Newarr:
                    {
                        int elemTok = BitConverter.ToInt32(il, ip); ip += 4;
                        int elemTokIdx = tokens.Count; tokens.Add(elemTok);
                        var (lenSlot, _) = evalStack[--sp];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp++] = (dst, SType.O);

                        // Typed backing when the element type is statically int/float: the executor
                        // allocates int[]/float[] and element accesses skip boxing entirely.
                        // Flat script-struct elements get a ScriptVtArray (single byte[] backing,
                        // no per-element ScriptObject). Host flat structs (Vector3[]) still use the
                        // boxed object?[] path — the lowerer has no binding access here; follow-up.
                        var arrOp = Op.newarr;
                        var elemHandle2 = MetadataTokens.EntityHandle(elemTok);
                        if (elemHandle2.Kind == HandleKind.TypeReference)
                        {
                            var elemRef2 = asm.Reader.GetTypeReference((TypeReferenceHandle)elemHandle2);
                            if (asm.Reader.GetString(elemRef2.Namespace) == "System")
                            {
                                var elemName2 = asm.Reader.GetString(elemRef2.Name);
                                if (elemName2 == "Int32") arrOp = Op.newarr_i4;
                                else if (elemName2 == "Single") arrOp = Op.newarr_r4;
                                // Other primitives keep Op.newarr but get a real typed runtime
                                // array (bool[]/char[]/…) via the side table, so element reads box
                                // with the correct type (element ToString/identity were wrong when
                                // object?[]-backed: bool boxed as int, defaults null).
                                else if (PrimArrayElemType(elemName2) is { } pt)
                                    (primArrayElemTypeByTokIdx ??= new Dictionary<int, Type>())[elemTokIdx] = pt;
                            }
                        }
                        // Host ENUM elements need a real typed array too: Roslyn skips storing
                        // zero-valued members into `{ ... }` initializers (newarr zeroes them),
                        // so an object?[] backing left NULLS where C# guarantees the zero member.
                        if (arrOp == Op.newarr
                            && (primArrayElemTypeByTokIdx == null || !primArrayElemTypeByTokIdx.ContainsKey(elemTokIdx))
                            && ResolveHostEnumType(asm, elemTok) is { } enumElem2)
                            (primArrayElemTypeByTokIdx ??= new Dictionary<int, Type>())[elemTokIdx] = enumElem2;
                        if (arrOp == Op.newarr && FlatElemLayout(asm, elemTok) is { } elemFlat2)
                        {
                            // Flat elements — script structs AND flat host structs (new Vector3[n]).
                            // Also fixes uninitialized-element access: object?[] held nulls where C#
                            // guarantees zeroed structs; the byte backing is zeroed by construction.
                            arrOp = Op.newarr_vt;
                            (layoutByTokIdx ??= new Dictionary<int, HostBinding.StructLayout>())[elemTokIdx] = elemFlat2;
                        }

                        // Check for eager InitializeArray pattern (dup; ldtoken; call InitializeArray)
                        // If present, we can't lower it cleanly — fall through to not-supported
                        if (ip + 11 <= il.Length && il[ip] == 0x25 && il[ip + 1] == 0xD0)
                        {
                            if (il[ip + 6] == 0x28)
                            {
                                int callTok2 = BitConverter.ToInt32(il, ip + 7);
                                if (asm.TokenNames.TryGetValue(callTok2, out var calleeName2)
                                    && calleeName2 == "RuntimeHelpers.InitializeArray/2")
                                {
                                    // Skip the dup+ldtoken+call pattern and emit with init flag
                                    int fieldTok = BitConverter.ToInt32(il, ip + 2);
                                    int fieldTokIdx = tokens.Count; tokens.Add(fieldTok);
                                    ip += 11;
                                    // Emit: [newarr*, dst, lenSlot, elemTokIdx, fieldTokIdx] (5 words)
                                    ir.Add((uint)arrOp); irToIl.Add(instrStart);
                                    ir.Add((uint)dst); irToIl.Add(instrStart);
                                    ir.Add((uint)lenSlot); irToIl.Add(instrStart);
                                    ir.Add((uint)elemTokIdx); irToIl.Add(instrStart);
                                    ir.Add((uint)fieldTokIdx); irToIl.Add(instrStart);
                                    break;
                                }
                            }
                        }

                        // Normal newarr (no initializer): fieldTokIdx = 0xFFFFFFFF
                        ir.Add((uint)arrOp); irToIl.Add(instrStart);
                        ir.Add((uint)dst); irToIl.Add(instrStart);
                        ir.Add((uint)lenSlot); irToIl.Add(instrStart);
                        ir.Add((uint)elemTokIdx); irToIl.Add(instrStart);
                        ir.Add(unchecked((uint)-1)); irToIl.Add(instrStart);
                        break;
                    }
                    case ILOpCode.Ldlen:
                    {
                        var (src, _) = evalStack[sp - 1];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
                        evalStack[sp - 1] = (dst, SType.I4);
                        Emit3(ir, irToIl, Op.ldlen, dst, src, instrStart);
                        break;
                    }
                    // Generic `ldelem <T>` — emitted by Roslyn only for value-type elements (reference
                    // types use ldelem.ref). In this representation a struct element is an O
                    // ScriptObject, so the destination is O, and the loaded value must be cloned for
                    // struct copy semantics. clone_sc is a safe pass-through for any non-ScriptObject.
                    case ILOpCode.Ldelem:
                    {
                        int ldeTok = BitConverter.ToInt32(il, ip); ip += 4;
                        var (idxSlot, _) = evalStack[--sp];
                        var (arrSlot, _) = evalStack[sp - 1];
                        if (FlatElemLayout(asm, ldeTok) is { } ldeFlat)
                        {
                            // Flat element: memcpy out of the ScriptVtArray backing — value
                            // semantics without a clone allocation.
                            int vdst = AllocStructSlot(ref frameSize, slotTypes, slotStructs, ldeFlat);
                            evalStack[sp - 1] = (vdst, SType.Vt);
                            Emit4(ir, irToIl, Op.ldelem_vt, vdst, arrSlot, idxSlot, instrStart);
                            break;
                        }
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        evalStack[sp - 1] = (dst, SType.O);
                        Emit4(ir, irToIl, Op.ldelem_o, dst, arrSlot, idxSlot, instrStart);
                        int cl = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                        Emit3(ir, irToIl, Op.clone_sc, cl, dst, instrStart);
                        evalStack[sp - 1] = (cl, SType.O);
                        break;
                    }
                    case ILOpCode.Ldelem_i:
                    case ILOpCode.Ldelem_i1:
                    case ILOpCode.Ldelem_u1:
                    case ILOpCode.Ldelem_i2:
                    case ILOpCode.Ldelem_u2:
                    case ILOpCode.Ldelem_i4:
                    case ILOpCode.Ldelem_u4:
                    case ILOpCode.Ldelem_i8:
                    case ILOpCode.Ldelem_r4:
                    case ILOpCode.Ldelem_r8:
                    case ILOpCode.Ldelem_ref:
                    {
                        var (idxSlot, _) = evalStack[--sp];
                        var (arrSlot, _) = evalStack[sp - 1];
                        SType et = op == ILOpCode.Ldelem_r4 ? SType.R4
                                 : op == ILOpCode.Ldelem_r8 ? SType.R8
                                 : op == ILOpCode.Ldelem_i8 ? SType.I8
                                 : op == ILOpCode.Ldelem_ref ? SType.O : SType.I4;
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, et, null);
                        evalStack[sp - 1] = (dst, et);
                        Emit4(ir, irToIl, Op.ldelem_o, dst, arrSlot, idxSlot, instrStart);
                        break;
                    }
                    case ILOpCode.Stelem:
                    case ILOpCode.Stelem_i:
                    case ILOpCode.Stelem_i1:
                    case ILOpCode.Stelem_i2:
                    case ILOpCode.Stelem_i4:
                    case ILOpCode.Stelem_i8:
                    case ILOpCode.Stelem_r4:
                    case ILOpCode.Stelem_r8:
                    case ILOpCode.Stelem_ref:
                    {
                        int steTok = 0;
                        if (op == ILOpCode.Stelem) { steTok = BitConverter.ToInt32(il, ip); ip += 4; }
                        var (src, srcTE) = evalStack[--sp];
                        var (idxSlot, _) = evalStack[--sp];
                        var (arrSlot, _) = evalStack[--sp];
                        if (steTok != 0 && FlatElemLayout(asm, steTok) is { } steFlat)
                        {
                            // Flat element: memcpy into the ScriptVtArray backing. A boxed (O)
                            // source is unboxed into a Vt temp first.
                            if (srcTE != SType.Vt)
                            {
                                int vtmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, steFlat);
                                Emit3(ir, irToIl, Op.unbox_vt, vtmp, src, instrStart);
                                src = vtmp;
                            }
                            Emit4(ir, irToIl, Op.stelem_vt, arrSlot, idxSlot, src, instrStart);
                            break;
                        }
                        // [stelem_o, arr, idx, src]
                        ir.Add((uint)Op.stelem_o); irToIl.Add(instrStart);
                        ir.Add((uint)arrSlot); irToIl.Add(instrStart);
                        ir.Add((uint)idxSlot); irToIl.Add(instrStart);
                        ir.Add((uint)src); irToIl.Add(instrStart);
                        break;
                    }

                    // ldelema — push a phantom array-element address. Consumed in-place by
                    // ldind/stind (read or write the element directly), or as a byref call arg
                    // (then call_host_byref reads/writes through the array slot post-call).
                    case ILOpCode.Ldelema:
                    {
                        int lmaTok = BitConverter.ToInt32(il, ip); ip += 4;
                        var (idxSlot, _) = evalStack[--sp];
                        var (arrSlot, _) = evalStack[--sp];
                        int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null); // phantom
                        addrTagBySlot[dst]    = -3; // array-element address
                        addrFldObjBySlot[dst] = arrSlot;
                        addrFldTokBySlot[dst] = idxSlot; // reuse slot — points to index slot, not a token
                        if (FlatElemLayout(asm, lmaTok) is { } lmaFlat)
                            addrElemLayoutBySlot[dst] = lmaFlat; // flat elem: consumers ldelem_vt/stelem_vt
                        // .NET bounds-checks AT ldelema; the phantom defers the check to whatever
                        // consumes the address, so an UNCONSUMED element address (unused ref/local,
                        // common under release codegen) skipped it — `var _ = a[oob]` returned
                        // normally instead of throwing (found by fuzzing the unmasked-index axis).
                        Emit3(ir, irToIl, Op.arr_bounds_check, arrSlot, idxSlot, instrStart);
                        evalStack[sp++] = (dst, SType.O);
                        break;
                    }

                    // initobj — zero-initialize the value-type at the address. For our model:
                    //   - phantom frame-slot address pointing to a Vt slot: zero the slot's bytes.
                    //   - phantom frame-slot address pointing to an O slot (script struct): emit newobj_script
                    //     with 0 explicit args to create a zero-initialized ScriptObject.
                    //   - phantom field address: stfld of null/zero.
                    //   - phantom array-element address: stelem of null/zero.
                    case ILOpCode.Initobj:
                    {
                        int typeDefTok = BitConverter.ToInt32(il, ip);
                        ip += 4; // skip type token
                        var (addrSlot, _) = evalStack[--sp];
                        int tag = addrTagBySlot[addrSlot];
                        if (tag >= 0)
                        {
                            int targetSlot = tag;
                            SType ts = slotTypes[targetSlot];
                            // Roslyn REUSES one temp local across consecutive struct initializers
                            // (`F(new V{...}, new V{...})`): the first arg's eval-stack entry still
                            // points at the slot this initobj is about to zero. CLR stack semantics
                            // say that entry is a COPY — materialize it before overwriting.
                            ProtectStackAliases(ir, irToIl, evalStack, sp, ref frameSize,
                                slotTypes, slotStructs, targetSlot, instrStart);
                            if (ts == SType.Vt)
                            {
                                ir.Add((uint)Op.initobj); irToIl.Add(instrStart);
                                ir.Add((uint)targetSlot); irToIl.Add(instrStart);
                            }
                            else if (ts == SType.I4 || ts == SType.R4)
                            {
                                Emit3(ir, irToIl, Op.ldc_i4, targetSlot, 0, instrStart);
                            }
                            else if (asm.TypeDefToType.ContainsKey(typeDefTok))
                            {
                                // Script-defined struct/class O slot.
                                // If there is an explicit 0-arg ctor, call it via newobj_script.
                                // Otherwise (default struct ctor with RVA=0), zero-init without a call.
                                if (asm.TypeDefToCtorTok.TryGetValue(typeDefTok, out int ctorTok))
                                {
                                    int ctorTokIdx = tokens.Count; tokens.Add(ctorTok);
                                    ir.Add((uint)Op.newobj_script); irToIl.Add(instrStart);
                                    ir.Add((uint)targetSlot); irToIl.Add(instrStart);
                                    ir.Add((uint)ctorTokIdx); irToIl.Add(instrStart);
                                    ir.Add(0u); irToIl.Add(instrStart); // argc = 0 explicit args
                                }
                                else
                                {
                                    // No body ctor — emit initobj_script with TypeDef token.
                                    int typeDefTokIdx = tokens.Count; tokens.Add(typeDefTok);
                                    ir.Add((uint)Op.initobj_script); irToIl.Add(instrStart);
                                    ir.Add((uint)targetSlot); irToIl.Add(instrStart);
                                    ir.Add((uint)typeDefTokIdx); irToIl.Add(instrStart);
                                }
                                // Slot is now initialized: subsequent stfld in this block won't need ensure_script.
                                ensuredScriptSlots.Add(targetSlot);
                            }
                            else if (PrimitiveSTypeForTypeToken(asm, typeDefTok) is { } pst)
                            {
                                // `initobj System.Int32` on a local PreClassifyLocals never saw a
                                // store to (Roslyn rewrites `stloc <const 0>` into `ldloca; initobj`
                                // when the constant is default(T)) — the slot defaulted to O. Retype
                                // it numeric and zero it; otherwise the value read null (found by
                                // fuzzing: `(byte)(p*0)` folds to 0, so `(...).ToString()` saw null).
                                slotTypes[targetSlot] = pst;
                                Emit3(ir, irToIl, pst == SType.R4 ? Op.ldc_r4 : Op.ldc_i4, targetSlot, 0, instrStart);
                            }
                            else if (asm.HostCtors.ContainsKey(typeDefTok))
                            {
                                // Boxed host struct local: materialize the default box so the
                                // initializer's stfld sequence has a target (registered at parse
                                // time, keyed by the type token).
                                int hostTypeTokIdx = tokens.Count; tokens.Add(typeDefTok);
                                ir.Add((uint)Op.newobj_host); irToIl.Add(instrStart);
                                ir.Add((uint)targetSlot); irToIl.Add(instrStart);
                                ir.Add((uint)hostTypeTokIdx); irToIl.Add(instrStart);
                                ir.Add(0u); irToIl.Add(instrStart); // argc = 0
                                ensuredScriptSlots.Add(targetSlot);
                            }
                            else
                            {
                                Emit2(ir, irToIl, Op.ldnull, targetSlot, instrStart);
                            }
                        }
                        else if (tag == -1)
                        {
                            // field-address initobj: store null / 0 / zero-Vt into the field.
                            int objSlot = addrFldObjBySlot[addrSlot];
                            int tokIdx2 = addrFldTokBySlot[addrSlot];
                            int tok2 = tokens[tokIdx2];
                            asm.FieldSTypes.TryGetValue(tok2, out var fst2);
                            // Vt (flat host struct) field inline in the ScriptObject's PrimBytes —
                            // Roslyn zero-inits hoisted iterator locals through their field address
                            // (`ldarg.0; ldflda '<v>5__N'; initobj Vec2`). Zero a Vt temp and copy
                            // its bytes in. The reference path below would emit stfld_sc_o with the
                            // field's BYTE offset as a RefSlots INDEX (found live: RefSlots[24] out
                            // of bounds on an iterator state machine).
                            if (fst2 == SType.Vt && asm.FieldSlots.TryGetValue(tok2, out var vtFsI)
                                && vtFsI.Item1.VtFieldLayouts?[vtFsI.Item2] is { } vtLayI)
                            {
                                int zeroVt = AllocStructSlot(ref frameSize, slotTypes, slotStructs, vtLayI);
                                ir.Add((uint)Op.initobj); irToIl.Add(instrStart);
                                ir.Add((uint)zeroVt); irToIl.Add(instrStart);
                                // A composed phantom carries the summed PrimBytes offset; a plain
                                // field phantom resolves it from the descriptor.
                                int vtOffI = addrFldOffBySlot[addrSlot] >= 0
                                    ? addrFldOffBySlot[addrSlot]
                                    : vtFsI.Item1.FieldOffsets[vtFsI.Item2];
                                Emit4(ir, irToIl, Op.stfld_sc_vt, objSlot, vtOffI, zeroVt, instrStart);
                                break;
                            }
                            // Same shape one level deeper: initobj on a HOST struct member of a
                            // hoisted flat struct (`pose.P = default` lowers to
                            // `ldflda '<pose>'; ldflda Pose::P; initobj Vec2`). The composed
                            // phantom's offset already sums the whole chain; zero the nested
                            // struct's byte range there.
                            if (addrFldOffBySlot[addrSlot] >= 0
                                && asm.HostFields.TryGetValue(tok2, out var hfI) && hfI.DeclaringStruct != null
                                && hfI.PrimitiveSt != SType.I4 && hfI.PrimitiveSt != SType.R4
                                && hfI.FieldTypeName != null && asm.HostSurface != null
                                && asm.HostSurface.TryGetStructLayout(hfI.FieldTypeName, out var nLayI)
                                && nLayI != null)
                            {
                                int zeroVtH = AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLayI);
                                ir.Add((uint)Op.initobj); irToIl.Add(instrStart);
                                ir.Add((uint)zeroVtH); irToIl.Add(instrStart);
                                Emit4(ir, irToIl, Op.stfld_sc_vt, objSlot, addrFldOffBySlot[addrSlot], zeroVtH, instrStart);
                                break;
                            }
                            int zeroSlot = AllocSlot(ref frameSize, slotTypes, slotStructs, fst2, null);
                            if (fst2 == SType.I4 || fst2 == SType.R4)
                                Emit3(ir, irToIl, Op.ldc_i4, zeroSlot, 0, instrStart);
                            // initobj on a SCRIPT-STRUCT field (boxed O storage — e.g. a struct
                            // local with a ref-slot field hoisted into an iterator state machine):
                            // C# semantics are a fresh ZEROED instance, not null — a null field
                            // NREs the next read (found by fuzzing: crash-e049406a).
                            else if (asm.TypeDefToType.TryGetValue(typeDefTok, out var initFldType)
                                     && initFldType.IsScriptStructValue)
                                EmitFreshScriptStruct(ir, irToIl, tokens, asm, typeDefTok, zeroSlot, instrStart);
                            else
                                Emit2(ir, irToIl, Op.ldnull, zeroSlot, instrStart);
                            if (asm.FieldSlots.TryGetValue(tok2, out var sfScFsI))
                            {
                                int sfOffI = sfScFsI.Item1.FieldOffsets[sfScFsI.Item2];
                                var scOpI = fst2 == SType.I4 ? Op.stfld_sc_i4 : fst2 == SType.R4 ? Op.stfld_sc_r4 : Op.stfld_sc_o;
                                ir.Add((uint)scOpI); irToIl.Add(instrStart);
                                ir.Add((uint)objSlot); irToIl.Add(instrStart);
                                ir.Add((uint)sfOffI); irToIl.Add(instrStart);
                                ir.Add((uint)zeroSlot); irToIl.Add(instrStart);
                            }
                            else
                            {
                                var sfldOp = fst2 == SType.I4 ? Op.stfld_i4 : fst2 == SType.R4 ? Op.stfld_r4 : Op.stfld_o;
                                ir.Add((uint)sfldOp); irToIl.Add(instrStart);
                                ir.Add((uint)objSlot); irToIl.Add(instrStart);
                                ir.Add((uint)tokIdx2); irToIl.Add(instrStart);
                                ir.Add((uint)zeroSlot); irToIl.Add(instrStart);
                            }
                        }
                        else if (tag == -3)
                        {
                            // array-element initobj: stelem of null/0 — except a SCRIPT-STRUCT
                            // element (`sa[i] = default` on a boxed struct), which must become a
                            // fresh zeroed instance, not null (same rule as the field branch above).
                            int arrSlot = addrFldObjBySlot[addrSlot];
                            int idxSlot = addrFldTokBySlot[addrSlot];
                            int zeroSlot = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            if (asm.TypeDefToType.TryGetValue(typeDefTok, out var initElemType)
                                && initElemType.IsScriptStructValue)
                                EmitFreshScriptStruct(ir, irToIl, tokens, asm, typeDefTok, zeroSlot, instrStart);
                            else
                                Emit2(ir, irToIl, Op.ldnull, zeroSlot, instrStart);
                            ir.Add((uint)Op.stelem_o); irToIl.Add(instrStart);
                            ir.Add((uint)arrSlot); irToIl.Add(instrStart);
                            ir.Add((uint)idxSlot); irToIl.Add(instrStart);
                            ir.Add((uint)zeroSlot); irToIl.Add(instrStart);
                        }
                        else
                            goto notSupported;
                        break;
                    }

                    // ldind.* — dereference an address. The address may be:
                    //   • a frame-slot address (addrTagBySlot[s] >= 0): read from that slot
                    //   • a field address (addrTagBySlot[s] == -1): emit ldfld
                    //   • an array-element address (addrTagBySlot[s] == -3): emit ldelem
                    case ILOpCode.Ldind_i1: case ILOpCode.Ldind_u1: case ILOpCode.Ldind_i2:
                    case ILOpCode.Ldind_u2: case ILOpCode.Ldind_i4: case ILOpCode.Ldind_u4:
                    case ILOpCode.Ldind_i:  case ILOpCode.Ldind_r4: case ILOpCode.Ldind_r8:
                    case ILOpCode.Ldind_i8: case ILOpCode.Ldind_ref:
                    {
                        var (addrSlot, _) = evalStack[--sp];
                        int tag = addrTagBySlot[addrSlot];
                        // Honest suffix typing: r8/i8 dereferences produce wide slots. Typing r8 as
                        // R4 silently read doubles as floats through ldelema (found by fuzzing).
                        SType ldindT = op == ILOpCode.Ldind_r4 ? SType.R4
                                     : op == ILOpCode.Ldind_r8 ? SType.R8
                                     : op == ILOpCode.Ldind_i8 ? SType.I8
                                     : op == ILOpCode.Ldind_ref ? SType.O : SType.I4;
                        bool wideInd = ldindT is SType.I8 or SType.R8;
                        if (tag == -1 && addrFldOffBySlot[addrSlot] >= 0)
                        {
                            // Composed inline address on a heap ScriptObject: direct PrimBytes access.
                            // Flat storage holds only 4-byte-or-narrower cells — a wide dereference
                            // here has no 8-byte cell to read; keep it loud rather than truncate.
                            if (wideInd) goto notSupported;
                            int objSlot = addrFldObjBySlot[addrSlot];
                            int cOff = addrFldOffBySlot[addrSlot];
                            var pst = ldindT == SType.R4 ? SType.R4 : SType.I4;
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, pst, null);
                            evalStack[sp++] = (dst2, pst);
                            Emit4(ir, irToIl, pst == SType.I4 ? Op.ldfld_sc_i4 : Op.ldfld_sc_r4, dst2, objSlot, cOff, instrStart);
                        }
                        else if (tag == -1)
                        {
                            // field address: emit ldfld
                            int objSlot = addrFldObjBySlot[addrSlot];
                            int tokIdx2 = addrFldTokBySlot[addrSlot];
                            int tok2 = tokens[tokIdx2];
                            asm.FieldSTypes.TryGetValue(tok2, out var fst2);
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, fst2, null);
                            evalStack[sp++] = (dst2, fst2);
                            if (asm.FieldSlots.TryGetValue(tok2, out var fsLo2))
                            {
                                int off2 = fsLo2.Item1.FieldOffsets[fsLo2.Item2];
                                var ldfldOp2 = fst2 == SType.I4 ? Op.ldfld_sc_i4 : fst2 == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                                Emit4(ir, irToIl, ldfldOp2, dst2, objSlot, off2, instrStart);
                            }
                            else
                            {
                                var ldfldOp2 = fst2 == SType.I4 ? Op.ldfld_i4 : fst2 == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                                Emit4(ir, irToIl, ldfldOp2, dst2, objSlot, tokIdx2, instrStart);
                            }
                            // Wide script field through an address (compound assign lowers to
                            // ldflda + ldind.i8/r8): unbox the boxed ref-slot value into a wide
                            // slot, same as the plain Ldfld path.
                            if (fst2 == SType.O && asm.FieldWide.TryGetValue(tok2, out var wfst2))
                            {
                                int wdst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, wfst2, null);
                                Emit3(ir, irToIl, Op.mov, wdst2, dst2, instrStart);
                                evalStack[sp - 1] = (wdst2, wfst2);
                            }
                        }
                        else if (tag == -3 && addrElemLayoutBySlot[addrSlot] != null)
                        {
                            // Flat array element field read via address: materialize the element
                            // and read the field at the composed offset. Flat layouts hold no wide
                            // cells (see the composed-address branch above) — keep wide loud.
                            if (wideInd) goto notSupported;
                            int arrSlot = addrFldObjBySlot[addrSlot];
                            int idxSlot = addrFldTokBySlot[addrSlot];
                            int elemTmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, addrElemLayoutBySlot[addrSlot]!);
                            Emit4(ir, irToIl, Op.ldelem_vt, elemTmp, arrSlot, idxSlot, instrStart);
                            var pst = ldindT == SType.R4 ? SType.R4 : SType.I4;
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, pst, null);
                            evalStack[sp++] = (dst2, pst);
                            Emit4(ir, irToIl, pst == SType.I4 ? Op.ldfld_vt_i4 : Op.ldfld_vt_r4,
                                dst2, elemTmp, addrByteOffBySlot[addrSlot], instrStart);
                        }
                        else if (tag == -3)
                        {
                            // array element address: emit ldelem (chooses dst type from ldind suffix)
                            int arrSlot = addrFldObjBySlot[addrSlot];
                            int idxSlot = addrFldTokBySlot[addrSlot];
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, ldindT, null);
                            evalStack[sp++] = (dst2, ldindT);
                            Emit4(ir, irToIl, Op.ldelem_o, dst2, arrSlot, idxSlot, instrStart);
                        }
                        else if (tag >= 0)
                        {
                            int targetSlot = tag;
                            SType tgt = slotTypes[targetSlot];
                            if (tgt == SType.Vt)
                            {
                                // Address inside a flat struct's bytes: read the field at the
                                // composed byte offset — a plain mov reads word 0 regardless of
                                // the addressed field and silently misreads offsets > 0.
                                // Flat layouts hold no wide cells — keep wide dereferences loud.
                                if (wideInd) goto notSupported;
                                int vOff2 = addrByteOffBySlot[addrSlot];
                                var pst = ldindT == SType.R4 ? SType.R4 : SType.I4;
                                int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, pst, null);
                                evalStack[sp++] = (dst2, pst);
                                Emit4(ir, irToIl, pst == SType.I4 ? Op.ldfld_vt_i4 : Op.ldfld_vt_r4, dst2, targetSlot, vOff2, instrStart);
                            }
                            else
                            {
                                int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, tgt, null);
                                evalStack[sp++] = (dst2, tgt);
                                Emit3(ir, irToIl, Op.mov, dst2, targetSlot, instrStart);
                            }
                        }
                        else
                        {
                            // normal slot (e.g. ldind after non-phantom): copy it
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, slotTypes[addrSlot], null);
                            evalStack[sp++] = (dst2, slotTypes[addrSlot]);
                            Emit3(ir, irToIl, Op.mov, dst2, addrSlot, instrStart);
                        }
                        break;
                    }

                    // stind.* — write through an address. The address may be:
                    //   • a frame-slot address (addrTagBySlot[s] >= 0): mov val → that slot
                    //   • a field address (addrTagBySlot[s] == -1): emit stfld
                    //   • an array-element address (addrTagBySlot[s] == -3): emit stelem
                    case ILOpCode.Stind_ref: case ILOpCode.Stind_i1: case ILOpCode.Stind_i2:
                    case ILOpCode.Stind_i4:  case ILOpCode.Stind_r4: case ILOpCode.Stind_r8:
                    case ILOpCode.Stind_i8:
                    {
                        var (val, _) = evalStack[--sp];
                        var (addrSlot, _) = evalStack[--sp];
                        int tag = addrTagBySlot[addrSlot];
                        // Wide stores can't land in flat (PrimBytes/Vt) storage — those cells are
                        // 4 bytes; a stfld_sc_i4/r4 there would silently truncate. Keep them loud
                        // on the flat branches; the boxed and array branches handle them typed.
                        bool wideSt = op == ILOpCode.Stind_i8 || op == ILOpCode.Stind_r8;

                        if (tag == -1 && addrFldOffBySlot[addrSlot] >= 0)
                        {
                            // Composed inline address on a heap ScriptObject: write PrimBytes direct.
                            if (wideSt) goto notSupported;
                            int objSlot = addrFldObjBySlot[addrSlot];
                            int cOff = addrFldOffBySlot[addrSlot];
                            bool isR4s = op == ILOpCode.Stind_r4;
                            Emit4(ir, irToIl, isR4s ? Op.stfld_sc_r4 : Op.stfld_sc_i4, objSlot, cOff, val, instrStart);
                        }
                        else if (tag == -1)
                        {
                            // field address: emit stfld
                            int objSlot = addrFldObjBySlot[addrSlot];
                            int tokIdx2 = addrFldTokBySlot[addrSlot];
                            int tok2 = tokens[tokIdx2];
                            asm.FieldSTypes.TryGetValue(tok2, out var fst2);
                            if (asm.FieldSlots.TryGetValue(tok2, out var sfScFs2))
                            {
                                int sfOff2 = sfScFs2.Item1.FieldOffsets[sfScFs2.Item2];
                                var scOp2 = fst2 == SType.I4 ? Op.stfld_sc_i4 : fst2 == SType.R4 ? Op.stfld_sc_r4 : Op.stfld_sc_o;
                                ir.Add((uint)scOp2); irToIl.Add(instrStart);
                                ir.Add((uint)objSlot); irToIl.Add(instrStart);
                                ir.Add((uint)sfOff2); irToIl.Add(instrStart);
                                ir.Add((uint)val); irToIl.Add(instrStart);
                            }
                            else
                            {
                                var sfldOp = fst2 == SType.I4 ? Op.stfld_i4 : fst2 == SType.R4 ? Op.stfld_r4 : Op.stfld_o;
                                ir.Add((uint)sfldOp); irToIl.Add(instrStart);
                                ir.Add((uint)objSlot); irToIl.Add(instrStart);
                                ir.Add((uint)tokIdx2); irToIl.Add(instrStart);
                                ir.Add((uint)val); irToIl.Add(instrStart);
                            }
                        }
                        else if (tag == -3 && addrElemLayoutBySlot[addrSlot] != null)
                        {
                            // Flat array element field write via address: read-modify-write the
                            // whole element (materialize, patch the field, memcpy back).
                            if (wideSt) goto notSupported;
                            int arrSlot = addrFldObjBySlot[addrSlot];
                            int idxSlot = addrFldTokBySlot[addrSlot];
                            int elemTmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, addrElemLayoutBySlot[addrSlot]!);
                            Emit4(ir, irToIl, Op.ldelem_vt, elemTmp, arrSlot, idxSlot, instrStart);
                            bool isR4st = op == ILOpCode.Stind_r4;
                            Emit4(ir, irToIl, isR4st ? Op.stfld_vt_r4 : Op.stfld_vt_i4,
                                elemTmp, addrByteOffBySlot[addrSlot], val, instrStart);
                            Emit4(ir, irToIl, Op.stelem_vt, arrSlot, idxSlot, elemTmp, instrStart);
                        }
                        else if (tag == -3)
                        {
                            int arrSlot = addrFldObjBySlot[addrSlot];
                            int idxSlot = addrFldTokBySlot[addrSlot];
                            ir.Add((uint)Op.stelem_o); irToIl.Add(instrStart);
                            ir.Add((uint)arrSlot); irToIl.Add(instrStart);
                            ir.Add((uint)idxSlot); irToIl.Add(instrStart);
                            ir.Add((uint)val); irToIl.Add(instrStart);
                        }
                        else if (tag >= 0)
                        {
                            if (slotTypes[tag] == SType.Vt)
                            {
                                // Write the field at the composed byte offset within the flat
                                // struct's bytes (a plain mov would clobber word 0 AND retarget
                                // nothing at offsets > 0).
                                if (wideSt) goto notSupported;
                                bool isR4s2 = op == ILOpCode.Stind_r4;
                                Emit4(ir, irToIl, isR4s2 ? Op.stfld_vt_r4 : Op.stfld_vt_i4, tag, addrByteOffBySlot[addrSlot], val, instrStart);
                            }
                            else
                            {
                                // frame-slot address: mov val → target slot
                                Emit3(ir, irToIl, Op.mov, tag, val, instrStart);
                            }
                        }
                        else
                        {
                            // Shouldn't happen — no valid stind target
                            throw new NotSupportedException(
                                $"IR lowering: stind with non-address operand in '{method.Name}' at IL+0x{instrStart:X4}");
                        }
                        break;
                    }

                    // ldobj — load a value-type from an address. Like ldind but for whole structs.
                    case ILOpCode.Ldobj:
                    {
                        ip += 4; // skip type token
                        var (addrSlot2, _) = evalStack[--sp];
                        int tag = addrTagBySlot[addrSlot2];
                        if (tag == -1)
                        {
                            // field address: emit ldfld
                            int objSlot = addrFldObjBySlot[addrSlot2];
                            int tokIdx2 = addrFldTokBySlot[addrSlot2];
                            int tok2 = tokens[tokIdx2];
                            asm.FieldSTypes.TryGetValue(tok2, out var fst2);
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, fst2, null);
                            evalStack[sp++] = (dst2, fst2);
                            if (asm.FieldSlots.TryGetValue(tok2, out var fsLo3))
                            {
                                int off3 = fsLo3.Item1.FieldOffsets[fsLo3.Item2];
                                var ldfldOp2 = fst2 == SType.I4 ? Op.ldfld_sc_i4 : fst2 == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                                Emit4(ir, irToIl, ldfldOp2, dst2, objSlot, off3, instrStart);
                            }
                            else
                            {
                                var ldfldOp2 = fst2 == SType.I4 ? Op.ldfld_i4 : fst2 == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                                Emit4(ir, irToIl, ldfldOp2, dst2, objSlot, tokIdx2, instrStart);
                            }
                        }
                        else if (tag == -3 && addrElemLayoutBySlot[addrSlot2] != null)
                        {
                            int arrSlot = addrFldObjBySlot[addrSlot2];
                            int idxSlot = addrFldTokBySlot[addrSlot2];
                            int dst2 = AllocStructSlot(ref frameSize, slotTypes, slotStructs, addrElemLayoutBySlot[addrSlot2]!);
                            evalStack[sp++] = (dst2, SType.Vt);
                            Emit4(ir, irToIl, Op.ldelem_vt, dst2, arrSlot, idxSlot, instrStart);
                        }
                        else if (tag == -3)
                        {
                            int arrSlot = addrFldObjBySlot[addrSlot2];
                            int idxSlot = addrFldTokBySlot[addrSlot2];
                            int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                            evalStack[sp++] = (dst2, SType.O);
                            Emit4(ir, irToIl, Op.ldelem_o, dst2, arrSlot, idxSlot, instrStart);
                        }
                        else if (tag >= 0)
                        {
                            // frame-slot address: mov / mov_vt from that slot (ldfld_vt_vt when the
                            // address points INSIDE the flat struct at a composed offset).
                            SType tgt = slotTypes[tag];
                            if (tgt == SType.Vt)
                            {
                                int vOff3 = addrByteOffBySlot[addrSlot2];
                                if (vOff3 > 0)
                                {
                                    int lTok = tokens[addrFldTokBySlot[addrSlot2]];
                                    HostBinding.StructLayout? nLay = null;
                                    if (asm.FieldSlots.TryGetValue(lTok, out var lfs))
                                        nLay = lfs.Item1.VtFieldLayouts?[lfs.Item2];
                                    int dst3 = nLay != null
                                        ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, nLay)
                                        : AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                                    evalStack[sp++] = (dst3, SType.Vt);
                                    Emit4(ir, irToIl, Op.ldfld_vt_vt, dst3, tag, vOff3, instrStart);
                                }
                                else
                                {
                                    var lay = slotStructs[tag];
                                    int dst2 = AllocStructSlot(ref frameSize, slotTypes, slotStructs, lay!);
                                    evalStack[sp++] = (dst2, SType.Vt);
                                    Emit3(ir, irToIl, Op.mov_vt, dst2, tag, instrStart);
                                }
                            }
                            else
                            {
                                int dst2 = AllocSlot(ref frameSize, slotTypes, slotStructs, tgt, null);
                                evalStack[sp++] = (dst2, tgt);
                                Emit3(ir, irToIl, Op.mov, dst2, tag, instrStart);
                            }
                        }
                        else
                            goto notSupported;
                        break;
                    }

                    // stobj — store a value-type to an address. Like stind but for whole structs.
                    case ILOpCode.Stobj:
                    {
                        ip += 4; // skip type token
                        var (val, _) = evalStack[--sp];
                        var (addrSlot2, _) = evalStack[--sp];
                        int tag = addrTagBySlot[addrSlot2];
                        if (tag == -1)
                        {
                            int objSlot = addrFldObjBySlot[addrSlot2];
                            int tokIdx2 = addrFldTokBySlot[addrSlot2];
                            int tok2 = tokens[tokIdx2];
                            asm.FieldSTypes.TryGetValue(tok2, out var fst2);
                            var sfldOp = fst2 == SType.I4 ? Op.stfld_i4 : fst2 == SType.R4 ? Op.stfld_r4 : Op.stfld_o;
                            ir.Add((uint)sfldOp); irToIl.Add(instrStart);
                            ir.Add((uint)objSlot); irToIl.Add(instrStart);
                            ir.Add((uint)tokIdx2); irToIl.Add(instrStart);
                            ir.Add((uint)val); irToIl.Add(instrStart);
                        }
                        else if (tag == -3 && addrElemLayoutBySlot[addrSlot2] != null)
                        {
                            int arrSlot = addrFldObjBySlot[addrSlot2];
                            int idxSlot = addrFldTokBySlot[addrSlot2];
                            int vSrc = val;
                            if (slotTypes[val] != SType.Vt)
                            {
                                int vtmpO = AllocStructSlot(ref frameSize, slotTypes, slotStructs, addrElemLayoutBySlot[addrSlot2]!);
                                Emit3(ir, irToIl, Op.unbox_vt, vtmpO, val, instrStart);
                                vSrc = vtmpO;
                            }
                            Emit4(ir, irToIl, Op.stelem_vt, arrSlot, idxSlot, vSrc, instrStart);
                        }
                        else if (tag == -3)
                        {
                            int arrSlot = addrFldObjBySlot[addrSlot2];
                            int idxSlot = addrFldTokBySlot[addrSlot2];
                            ir.Add((uint)Op.stelem_o); irToIl.Add(instrStart);
                            ir.Add((uint)arrSlot); irToIl.Add(instrStart);
                            ir.Add((uint)idxSlot); irToIl.Add(instrStart);
                            ir.Add((uint)val); irToIl.Add(instrStart);
                        }
                        else if (tag >= 0)
                        {
                            // frame-slot address: mov / mov_vt val → target slot (sub-range write
                            // when the address carries a composed offset into the flat bytes).
                            int vOffS = addrByteOffBySlot[addrSlot2];
                            if (slotTypes[tag] == SType.Vt && slotTypes[val] == SType.Vt)
                            {
                                if (vOffS > 0) Emit4(ir, irToIl, Op.stfld_vt_vt, tag, vOffS, val, instrStart);
                                else Emit3(ir, irToIl, Op.mov_vt, tag, val, instrStart);
                            }
                            else
                                Emit3(ir, irToIl, Op.mov, tag, val, instrStart);
                        }
                        else
                            goto notSupported;
                        break;
                    }

                    // cpobj is in Rejected (validator bans it as unsafe), so we never see it
                    // here. If a valid script ever needs struct-to-struct copy, Roslyn emits
                    // ldobj + stobj instead, both of which are handled above.

                    default:
                        goto notSupported;
                }
                continue;

            notSupported:
                // Name the opcode: "ldftn" tells the user it's a method group / delegate
                // creation; "0xFE06" tells them nothing.
                throw new NotSupportedException(
                    $"IR lowering: opcode {op.ToString().ToLowerInvariant()} (0x{(int)op:X4}) in '{method.Name}' at IL+0x{instrStart:X4} not yet supported");
            }

            // Patch forward branch targets
            var irArray = ir.ToArray();
            foreach (var (pIdx, ilTarget) in patchList)
            {
                if (ilToIrIp.TryGetValue(ilTarget, out int irTarget))
                    irArray[pIdx] = (uint)irTarget;
                else if (ilTarget == il.Length)
                    irArray[pIdx] = (uint)irArray.Length; // legit branch to method end -> ret sentinel
                else
                    // Out of range, or mid-instruction (not an IL boundary). Rejecting instead of the
                    // old silent "jump past end = return" rewrite, which turned corrupt/hostile control
                    // flow into silently-wrong control flow with no diagnostic.
                    throw new NotSupportedException(
                        $"branch target IL+0x{ilTarget:X4} in '{method.Name}' is out of range or not at an instruction boundary");
            }

            var slotTypesArr = slotTypes.ToArray();
            var irToIlArr    = irToIl.ToArray();
            var tokensArr = tokens.ToArray();
            // Wide (I8/R8) slots span two cells under one index; the pass tables (rename,
            // coalesce, retarget) reason per-cell and would merge another value into a wide
            // slot's continuation cell. Until the tables learn wide widths, a method whose
            // frame contains any wide slot runs unoptimized — correctness first.
            bool hasWideSlots = false;
            for (int wi = 0; wi < slotTypesArr.Length; wi++)
                if (slotTypesArr[wi] is SType.I8 or SType.R8) { hasWideSlots = true; break; }
            if (!hasWideSlots && !Knobs.NoOpt) {
            var skip = Knobs.SkipPass;
            if (!skip.Contains("frl")) ForwardRedundantLoads(ref irArray, ref irToIlArr, slotTypesArr, tokensArr);
            if (!skip.Contains("fci")) FoldConstantImmediates(ref irArray, ref irToIlArr, slotTypesArr);
            if (!skip.Contains("cdm")) CoalesceDestMovs(ref irArray, ref irToIlArr, slotTypesArr);
            if (!skip.Contains("fcb")) FoldCompareBranch(ref irArray, ref irToIlArr, slotTypesArr);
            if (!skip.Contains("ffl")) FoldForIntLoop(ref irArray, ref irToIlArr, slotTypesArr);
            if (!skip.Contains("hlc")) HoistLoopConsts(ref irArray, ref irToIlArr, slotTypesArr, method.ArgCount);
            }

            // Pre-resolve fast delegates per tokIdx so call_host can skip the dict
            // lookup. Setup-time cost; eliminates ~37 ns/iter from mixed bench
            // (call_host is dispatched 3+ times per inner step there).
            FastCallDelegate?[]? hostFastByTokIdx = null;
            bool[]? hostFastVtRecvByTokIdx = null;
            bool[]? hostFastWideOkByTokIdx = null;
            ParsedMethod?[]? calleeByTokIdx = null;
            for (int t = 0; t < tokensArr.Length; t++)
            {
                int tok = tokensArr[t];
                if (asm.HostCalls.TryGetValue(tok, out var he) && he.Binding.Fast != null)
                {
                    if (hostFastByTokIdx == null) hostFastByTokIdx = new FastCallDelegate?[tokensArr.Length];
                    hostFastByTokIdx[t] = he.Binding.Fast;
                    if (he.Binding.FastVtRecv)
                    {
                        if (hostFastVtRecvByTokIdx == null) hostFastVtRecvByTokIdx = new bool[tokensArr.Length];
                        hostFastVtRecvByTokIdx[t] = true;
                    }
                    if (he.Binding.FastWideOk)
                    {
                        if (hostFastWideOkByTokIdx == null) hostFastWideOkByTokIdx = new bool[tokensArr.Length];
                        hostFastWideOkByTokIdx[t] = true;
                    }
                }
                if (asm.ByToken.TryGetValue(tok, out var callee))
                {
                    if (calleeByTokIdx == null) calleeByTokIdx = new ParsedMethod?[tokensArr.Length];
                    calleeByTokIdx[t] = callee;
                }
            }

            int refClearLen = 0;
            for (int s = slotTypesArr.Length - 1; s >= 0; s--)
                if (slotTypesArr[s] == SType.O) { refClearLen = s + 1; break; }

            // A frame larger than either VM arena would write past its backing block on the first
            // slot store. Reject at lowering (a clean load failure) instead of corrupting memory at
            // invoke. Both arenas are indexed by the same slot number, but the reference arena is
            // smaller, so a ref-heavy frame can exceed it while the numeric count still fits.
            // Reachable from a pathological method body or a crafted LOCAL_SIG.
            if (frameSize > Vm.NumSlots || refClearLen > Vm.RefSlots)
                throw new NotSupportedException(
                    $"method '{method.Name}' needs {frameSize} frame slots and {refClearLen} reference slots, " +
                    $"over the {Vm.NumSlots}/{Vm.RefSlots}-slot arena limits");

            return new LoweredMethod
            {
                Ir            = irArray,
                FrameSize     = frameSize,
                IrToIlOffset  = irToIlArr,
                Strings       = strings.ToArray(),
                Tokens        = tokensArr,
                SlotTypes     = slotTypesArr,
                StructLayouts = slotStructs.ToArray(),
                HostFastByTokIdx = hostFastByTokIdx,
                HostFastVtRecvByTokIdx = hostFastVtRecvByTokIdx,
                HostFastWideOkByTokIdx = hostFastWideOkByTokIdx,
                CalleeByTokIdx   = calleeByTokIdx,
                RefClearLen      = refClearLen,
                ArgSlot          = argIndirect ? argFrameSlot : null,
                LayoutByTokIdx   = BuildLayoutByTokIdx(layoutByTokIdx, tokensArr.Length),
                PrimArrayElemTypeByTokIdx = BuildPrimArrayTypes(primArrayElemTypeByTokIdx, tokensArr.Length),
                DelegateSiteByTokIdx = BuildDelegateSites(delegateSiteByTokIdx, tokensArr.Length),
            };
        }

        // Op selection for flat host-struct field access: sub-4-byte integer fields
        // (Color32.r-class) need widening loads / truncating stores; everything else uses
        // the plain 4-byte cell ops.
        static Op LdFldVtOpFor(HostBinding.FieldEntry fe)
        {
            if (fe.PrimitiveSt == SType.R4) return Op.ldfld_vt_r4;
            switch (fe.PrimitiveKind)
            {
                case 1:  return Op.ldfld_vt_u1;
                case 2:  return Op.ldfld_vt_i1;
                case 3:  return Op.ldfld_vt_u2;
                case 4:  return Op.ldfld_vt_i2;
                default: return Op.ldfld_vt_i4;
            }
        }

        static Op StFldVtOpFor(HostBinding.FieldEntry fe)
        {
            if (fe.PrimitiveSt == SType.R4) return Op.stfld_vt_r4;
            switch (fe.PrimitiveKind)
            {
                case 1: case 2: return Op.stfld_vt_b1;
                case 3: case 4: return Op.stfld_vt_b2;
                default:        return Op.stfld_vt_i4;
            }
        }

        // Runtime Type for a `box` token that names a HOST enum (TypeRef parent), null otherwise.
        // Script-declared enums are TypeDefs — no runtime Type exists, so they keep the int form.
        static Type? ResolveHostEnumType(ParsedAssembly asm, int tok)
        {
            if (asm.HostSurface == null) return null;
            try
            {
                var h = MetadataTokens.EntityHandle(tok);
                if (h.Kind != HandleKind.TypeReference) return null;
                var t = ResolveSigTypeRef(asm.Reader, (TypeReferenceHandle)h, asm.HostSurface);
                return t is { IsEnum: true } ? t : null;
            }
            catch { return null; }
        }

        // Slot type for a SCRIPT static field (FieldDef) whose signature is an I4/R4-mapped
        // primitive; null for host fields, reference/struct/wide types (those stay boxed O).
        // Enums included: a script enum static's underlying value rides the I4 slot.
        internal static SType? ScriptStaticPrimitiveSType(ParsedAssembly asm, int tok)
        {
            var h = MetadataTokens.EntityHandle(tok);
            if (h.Kind != HandleKind.FieldDefinition) return null;
            try
            {
                var blob = asm.Reader.GetBlobBytes(
                    asm.Reader.GetFieldDefinition((FieldDefinitionHandle)h).Signature);
                if (blob.Length < 2 || blob[0] != 0x06) return null;
                switch (blob[1])
                {
                    case 0x02: case 0x03: case 0x04: case 0x05: case 0x06:
                    case 0x07: case 0x08: case 0x09: // bool..uint
                        return SType.I4;
                    case 0x0C: // float
                        return SType.R4;
                    case 0x11: // VALUETYPE coded-index — a script ENUM static maps to I4 (its
                    {          // value flows as the underlying int everywhere else already)
                        int idx = 2;
                        int coded = DecompressIntAdv(blob, ref idx);
                        if ((coded & 0x03) == 0 && IsEnumTypeDef(asm.Reader, coded >> 2))
                            return SType.I4;
                        return null;
                    }
                    default:
                        return null;
                }
            }
            catch { return null; }
        }

        // I8/R8 when a script static field is long/ulong/double — the wide sibling of
        // ScriptStaticPrimitiveSType (which deliberately leaves wides on the boxed O path;
        // the Ldsfld lowering unboxes them into a wide slot after the boxed load).
        internal static SType? ScriptStaticWideSType(ParsedAssembly asm, int tok)
        {
            var h = MetadataTokens.EntityHandle(tok);
            if (h.Kind != HandleKind.FieldDefinition) return null;
            try
            {
                var blob = asm.Reader.GetBlobBytes(
                    asm.Reader.GetFieldDefinition((FieldDefinitionHandle)h).Signature);
                if (blob.Length < 2 || blob[0] != 0x06) return null;
                return blob[1] switch
                {
                    0x0A or 0x0B => SType.I8, // long/ulong
                    0x0D => SType.R8,         // double
                    _ => null,
                };
            }
            catch { return null; }
        }

        // Emit IR that fills `dstSlot` (an O slot) with a fresh zero-initialized instance of the
        // script struct `typeDefTok` — via its 0-arg ctor when one exists (newobj_script), else
        // initobj_script. Used by the initobj lowering wherever C#'s default(T)/new T() semantics
        // require a usable zeroed instance rather than null (O-stored script structs).
        static void EmitFreshScriptStruct(List<uint> ir, List<int> irToIl, List<int> tokens,
            ParsedAssembly asm, int typeDefTok, int dstSlot, int instrStart)
        {
            if (asm.TypeDefToCtorTok.TryGetValue(typeDefTok, out int ctorTok))
            {
                int ctorTokIdx = tokens.Count; tokens.Add(ctorTok);
                ir.Add((uint)Op.newobj_script); irToIl.Add(instrStart);
                ir.Add((uint)dstSlot); irToIl.Add(instrStart);
                ir.Add((uint)ctorTokIdx); irToIl.Add(instrStart);
                ir.Add(0u); irToIl.Add(instrStart); // argc = 0 explicit args
            }
            else
            {
                int tdTokIdx = tokens.Count; tokens.Add(typeDefTok);
                ir.Add((uint)Op.initobj_script); irToIl.Add(instrStart);
                ir.Add((uint)dstSlot); irToIl.Add(instrStart);
                ir.Add((uint)tdTokIdx); irToIl.Add(instrStart);
            }
        }

        // True when a static field's type is immutable-by-value (primitive or string), so its
        // address can be lowered as a value load (see the Ldsflda case). Script FieldDefs decode
        // the field signature blob; host MemberRefs consult the registered FieldEntry.
        static bool StaticFieldIsImmutableValue(ParsedAssembly asm, int tok)
        {
            var h = MetadataTokens.EntityHandle(tok);
            if (h.Kind == HandleKind.FieldDefinition)
            {
                try
                {
                    var blob = asm.Reader.GetBlobBytes(
                        asm.Reader.GetFieldDefinition((FieldDefinitionHandle)h).Signature);
                    // Field sig: FIELD (0x06) then the type. 0x02..0x0D = bool..double, 0x0E = string.
                    return blob.Length >= 2 && blob[0] == 0x06 && blob[1] >= 0x02 && blob[1] <= 0x0E;
                }
                catch { return false; }
            }
            if (asm.HostFields.TryGetValue(tok, out var fe))
                return fe.PrimitiveSt is SType.I4 or SType.R4 || fe.FieldTypeName == "String";
            return false;
        }

        // Flat layout for an array-element type token: script TypeDefs use the descriptor's
        // synthesized FlatLayout; host TypeRefs resolve by simple name through the binding.
        static HostBinding.StructLayout? FlatElemLayout(ParsedAssembly asm, int tok)
        {
            if (asm.TypeDefToType.TryGetValue(tok, out var d)) return d.FlatLayout;
            var h = MetadataTokens.EntityHandle(tok);
            if (h.Kind == HandleKind.TypeReference && asm.HostSurface != null)
            {
                string name;
                try { name = asm.Reader.GetString(asm.Reader.GetTypeReference((TypeReferenceHandle)h).Name); }
                catch (Exception) { return null; }
                if (asm.HostSurface.TryGetStructLayout(name, out var lay)) return lay;
            }
            return null;
        }

        static HostBinding.StructLayout?[]? BuildLayoutByTokIdx(
            Dictionary<int, HostBinding.StructLayout>? sparse, int tokenCount)
        {
            if (sparse == null) return null;
            var arr = new HostBinding.StructLayout?[tokenCount];
            foreach (var kv in sparse) arr[kv.Key] = kv.Value;
            return arr;
        }

        static Type?[]? BuildPrimArrayTypes(Dictionary<int, Type>? sparse, int tokenCount)
        {
            if (sparse == null) return null;
            var arr = new Type?[tokenCount];
            foreach (var kv in sparse) arr[kv.Key] = kv.Value;
            return arr;
        }

        static DelegateSite?[]? BuildDelegateSites(Dictionary<int, DelegateSite>? sparse, int tokenCount)
        {
            if (sparse == null) return null;
            var arr = new DelegateSite?[tokenCount];
            foreach (var kv in sparse) arr[kv.Key] = kv.Value;
            return arr;
        }

        // Primitive element types that need a real typed runtime array (I4-mapped but whose boxed
        // identity differs from int, or that C# zero-inits differently than object?[] null). Int32
        // and Single are handled by newarr_i4/newarr_r4 and are intentionally not here.
        static Type? PrimArrayElemType(string elemName) => elemName switch
        {
            "Boolean" => typeof(bool),
            "Char"    => typeof(char),
            "Byte"    => typeof(byte),
            "SByte"   => typeof(sbyte),
            "Int16"   => typeof(short),
            "UInt16"  => typeof(ushort),
            "Int64"   => typeof(long),
            "UInt64"  => typeof(ulong),
            "Double"  => typeof(double),
            _ => null,
        };

        // Constant-immediate fold: `ldc_X tmp; binop_X_nn dst, src, tmp` with a single-use
        // tmp folds into `binop_X_nk dst, src, K` (K inline in the IR word) — one dispatch
        // less per pair. RHS-constant only (no _kn opcodes exist); commutative ops also
        // accept tmp on the LEFT via operand swap. Skipped when a branch target lands on
        // the binop (the ldc would be lost on that edge). Runs BEFORE CoalesceDestMovs so
        // the trailing `mov local <- tmp_dst` is still available for coalescing.
        static void FoldConstantImmediates(ref uint[] ir, ref int[] irToIl, SType[] slotTypes)
        {
            int n = ir.Length;
            if (n == 0) return;

            // Walk IR, record (ip, op, width).
            var ips    = new List<int>(n / 3);
            var ops    = new List<Op>(n / 3);
            var widths = new List<int>(n / 3);
            int pos = 0;
            while (pos < n)
            {
                var op = (Op)ir[pos];
                int w = OpWidthForCoalesce(op, ir, pos);
                if (w <= 0) return;
                ips.Add(pos); ops.Add(op); widths.Add(w);
                pos += w;
            }
            int insnCount = ips.Count;

            int slotCount = slotTypes.Length;
            var reads  = new int[slotCount];
            var writes = new int[slotCount];
            for (int i = 0; i < insnCount; i++)
                AccumulateSlotUses(ir, ips[i], ops[i], widths[i], reads, writes, slotCount);

            var isBranchTarget = new bool[n + 1];
            for (int i = 0; i < insnCount; i++)
                MarkInsnBranchTargets(ir, ips[i], ops[i], isBranchTarget);

            var deleted = new bool[insnCount];
            int deletedWords = 0;
            for (int i = 0; i < insnCount - 1; i++)
            {
                if (deleted[i]) continue;
                var ldcOp = ops[i];
                if (ldcOp != Op.ldc_i4 && ldcOp != Op.ldc_r4) continue;

                int ldcIp   = ips[i];
                int tmpSlot = (int)ir[ldcIp + 1];
                uint kBits  = ir[ldcIp + 2];

                if (writes[tmpSlot] != 1 || reads[tmpSlot] != 1) continue;

                // Find the next *non-deleted* instruction.
                int j = i + 1;
                while (j < insnCount && deleted[j]) j++;
                if (j >= insnCount) continue;

                var binOp = ops[j];
                int binIp = ips[j];
                if (widths[j] != 4) continue;
                if (isBranchTarget[binIp]) continue;

                int s1 = (int)ir[binIp + 2];
                int s2 = (int)ir[binIp + 3];
                bool tmpRight = (s2 == tmpSlot);
                bool tmpLeft  = (s1 == tmpSlot);
                if (!tmpRight && !tmpLeft) continue;

                if (!TryFoldNN(ldcOp, binOp, tmpRight, out Op nkOp)) continue;

                // Apply: rewrite binop in place, mark ldc deleted.
                ir[binIp] = (uint)nkOp;
                if (tmpLeft)
                {
                    // Commutative swap: move src1 into the operand-slot position
                    // (word 2), put k in word 3. Original encoding was [op, dst,
                    // tmp_left, src_right] — we want [nk, dst, src_right, k].
                    ir[binIp + 2] = (uint)s2;
                }
                ir[binIp + 3] = kBits;

                deleted[i] = true;
                deletedWords += widths[i];

                // Maintain reads/writes so a future pass over the same IR sees
                // the correct counts (we don't iterate to fixpoint, but defensive).
                reads[tmpSlot] = 0; writes[tmpSlot] = 0;
            }

            if (deletedWords == 0) return;

            // Compact (same shift-table machinery as CoalesceDestMovs).
            var deletedWord = new bool[n];
            for (int i = 0; i < insnCount; i++)
            {
                if (!deleted[i]) continue;
                for (int k = 0; k < widths[i]; k++) deletedWord[ips[i] + k] = true;
            }
            var shift = new int[n + 1];
            int run = 0;
            for (int oldIp = 0; oldIp <= n; oldIp++)
            {
                shift[oldIp] = run;
                if (oldIp < n && deletedWord[oldIp]) run++;
            }

            int newLen = n - deletedWords;
            var newIr   = new uint[newLen];
            var newToIl = new int[newLen];
            int dstIdx = 0;
            for (int i = 0; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                int oldIp = ips[i];
                int w = widths[i];
                for (int k = 0; k < w; k++)
                {
                    newIr[dstIdx + k]   = ir[oldIp + k];
                    newToIl[dstIdx + k] = irToIl[oldIp + k];
                }
                PatchInsnBranchTargets(newIr, dstIdx, ops[i], shift, ir, oldIp);
                dstIdx += w;
            }

            ir     = newIr;
            irToIl = newToIl;
        }

        // Map an (ldc, binop_nn) pair to the matching binop_nk. Returns false if
        // we don't have an _nk variant for this combination — no fold.
        // tmpRight=true means the constant is on the right (no swap needed); for
        // commutative ops we may also fold tmpLeft via operand swap.
        static bool TryFoldNN(Op ldcOp, Op binOp, bool tmpRight, out Op nkOp)
        {
            nkOp = default;
            // Type compatibility: ldc_i4 with i4 binop, ldc_r4 with r4 binop.
            bool isI4Ldc = ldcOp == Op.ldc_i4;
            switch (binOp)
            {
                case Op.add_i4_nn: if (!isI4Ldc) return false; nkOp = Op.add_i4_nk; return true;       // commutative
                case Op.add_r4_nn: if (isI4Ldc)  return false; nkOp = Op.add_r4_nk; return true;       // commutative
                case Op.mul_r4_nn: if (isI4Ldc)  return false; nkOp = Op.mul_r4_nk; return true;       // commutative
                case Op.ceq_i4_nn: if (!isI4Ldc) return false; nkOp = Op.ceq_i4_nk; return true;       // commutative
                case Op.mul_i4_nn: if (!isI4Ldc) return false; nkOp = Op.mul_i4_nk; return true;       // commutative
                case Op.and_i4_nn: if (!isI4Ldc) return false; nkOp = Op.and_i4_nk; return true;       // commutative
                case Op.or_i4_nn:  if (!isI4Ldc) return false; nkOp = Op.or_i4_nk;  return true;       // commutative
                case Op.xor_i4_nn: if (!isI4Ldc) return false; nkOp = Op.xor_i4_nk; return true;       // commutative
                case Op.ceq_r4_nn: if (isI4Ldc)  return false; nkOp = Op.ceq_r4_nk; return true;       // commutative
                // Non-commutative: tmp must be on the right.
                case Op.sub_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.sub_i4_nk; return true;
                case Op.sub_r4_nn: if (!tmpRight ||  isI4Ldc) return false; nkOp = Op.sub_r4_nk; return true;
                case Op.clt_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.clt_i4_nk; return true;
                case Op.clt_r4_nn: if (!tmpRight ||  isI4Ldc) return false; nkOp = Op.clt_r4_nk; return true;
                case Op.cgt_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.cgt_i4_nk; return true;
                case Op.cgt_r4_nn: if (!tmpRight ||  isI4Ldc) return false; nkOp = Op.cgt_r4_nk; return true;
                case Op.div_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.div_i4_nk; return true;
                case Op.rem_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.rem_i4_nk; return true;
                case Op.shl_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.shl_i4_nk; return true;
                case Op.shr_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.shr_i4_nk; return true;
                case Op.shr_un_i4_nn: if (!tmpRight || !isI4Ldc) return false; nkOp = Op.shr_un_i4_nk; return true;
                case Op.div_r4_nn: if (!tmpRight ||  isI4Ldc) return false; nkOp = Op.div_r4_nk; return true;
                default: return false;
            }
        }

        // --- Redundant-load forwarding ---
        //
        // Within each basic block, tracks for each (objSlot, resolvedFieldToken)
        // pair which slot currently holds the field's value:
        //   ldfld_X dst, obj, tok       → cache[(obj, tok)] = dst
        //   stfld_X obj, tok, src       → cache[(obj, tok)] = src
        //
        // On a subsequent matching ldfld_X with cache hit (and matching SType),
        // the second load is redundant: rewrite all reads of its dst slot to
        // the cached value slot, delete the load. Slot rename is global,
        // applied via a slotMap[] table; safe because the deleted load was the
        // sole writer of its dst within the live region (we're conservative
        // with cache invalidation).
        //
        // Cache invalidation events (cleared aggressively):
        //   - Any call (host/script/byref) — could mutate any field.
        //   - Any newobj — constructor side effects.
        //   - Any stsfld_*, stelem_* — written field/elem could alias.
        //   - Any branch — basic-block boundary.
        //   - A target landing on the next instruction also resets the block.
        //
        // Subset is corpus-driven: ldfld_o, ldfld_r4, ldfld_i4 (with matching
        // stfld variants for store-to-load forwarding). Other field-bearing
        // ops (ldelem, ldfld_struct, ldsfld) deferred — no bench evidence.
        /// <summary>Removes cached (obj, field) forwards for <paramref name="fieldKey"/> held under
        /// any object slot other than <paramref name="objS"/> — a store through one slot must not
        /// leave stale forwards alive for other slots that may alias the same object.</summary>
        static void DropAliasedField(Dictionary<(int, int), int> cache, int objS, int fieldKey)
        {
            List<(int, int)>? stale = null;
            foreach (var kv in cache)
                if (kv.Key.Item2 == fieldKey && kv.Key.Item1 != objS) (stale ??= new()).Add(kv.Key);
            if (stale != null) foreach (var k in stale) cache.Remove(k);
        }

        static void ForwardRedundantLoads(ref uint[] ir, ref int[] irToIl,
            SType[] slotTypes, int[] tokens)
        {
            int n = ir.Length;
            if (n == 0) return;

            var ips    = new List<int>(n / 3);
            var ops    = new List<Op>(n / 3);
            var widths = new List<int>(n / 3);
            int pos = 0;
            while (pos < n)
            {
                var op = (Op)ir[pos];
                int w = OpWidthForCoalesce(op, ir, pos);
                if (w <= 0) return;
                ips.Add(pos); ops.Add(op); widths.Add(w);
                pos += w;
            }
            int insnCount = ips.Count;

            var isBranchTarget = new bool[n + 1];
            for (int i = 0; i < insnCount; i++)
                MarkInsnBranchTargets(ir, ips[i], ops[i], isBranchTarget);

            int slotCount = slotTypes.Length;
            var slotMap = new int[slotCount];
            for (int i = 0; i < slotCount; i++) slotMap[i] = i;

            var deleted = new bool[insnCount];
            int deletedWords = 0;

            // (objSlot, resolvedTok) → valueSlot. Cleared at block boundaries
            // and on any side effect.
            var cache = new Dictionary<(int, int), int>();

            for (int i = 0; i < insnCount; i++)
            {
                int ip = ips[i];
                var op = ops[i];

                // Block boundary: an instruction that's a branch target starts
                // a new block (we may have entered via either fall-through or
                // jump — conservative).
                if (i > 0 && isBranchTarget[ip]) cache.Clear();

                // Invalidate cache entries whose object slot OR cached value slot is overwritten by
                // this instruction. Without this, store-to-load forwarding (stfld caches the SOURCE
                // slot) or load forwarding (keyed by the OBJECT slot) survives a later reassignment of
                // that slot and forwards a stale value — e.g. `bx.p = s1; s1 = other; read bx.p`.
                if (WritesFrameDstAt1(op))
                {
                    int wdst = (int)ir[ip + 1];
                    if (wdst >= 0)
                    {
                        List<(int, int)>? drop = null;
                        foreach (var kv in cache)
                            if (kv.Value == wdst || kv.Key.Item1 == wdst) (drop ??= new()).Add(kv.Key);
                        if (drop != null) foreach (var k in drop) cache.Remove(k);
                    }
                }

                switch (op)
                {
                    case Op.ldfld_o:
                    case Op.ldfld_r4:
                    case Op.ldfld_i4:
                    {
                        int dst    = (int)ir[ip + 1];
                        int objS   = (int)ir[ip + 2];
                        int tokIdx = (int)ir[ip + 3];
                        if ((uint)tokIdx >= (uint)tokens.Length) break;
                        int resolvedTok = tokens[tokIdx];
                        var key = (objS, resolvedTok);
                        if (cache.TryGetValue(key, out int valueSlot)
                            && (uint)valueSlot < (uint)slotCount
                            && slotTypes[valueSlot] == slotTypes[dst])
                        {
                            // Forward: subsequent reads of dst become reads of
                            // valueSlot; this load is redundant.
                            slotMap[dst] = valueSlot;
                            deleted[i] = true;
                            deletedWords += widths[i];
                        }
                        else
                        {
                            cache[key] = dst;
                        }
                        break;
                    }

                    case Op.ldfld_sc_o:
                    case Op.ldfld_sc_r4:
                    case Op.ldfld_sc_i4:
                    {
                        int dst    = (int)ir[ip + 1];
                        int objS   = (int)ir[ip + 2];
                        int off    = (int)ir[ip + 3];
                        // Negated keys avoid collision with raw token values; the ref/prim split
                        // (~(2*off) vs ~(2*off+1)) keeps a stfld_sc_o REF-SLOT index from aliasing
                        // a stfld_sc_i4 PRIM BYTE offset of the same number — that collision
                        // forwarded a field read to an unrelated O field's source slot (found by
                        // fuzzing: a hoisted char at ref slot 4 hijacked the int at prim byte 4).
                        var key = (objS, op == Op.ldfld_sc_o ? ~(2 * off + 1) : ~(2 * off));
                        if (cache.TryGetValue(key, out int valueSlot)
                            && (uint)valueSlot < (uint)slotCount
                            && slotTypes[valueSlot] == slotTypes[dst])
                        {
                            slotMap[dst] = valueSlot;
                            deleted[i] = true;
                            deletedWords += widths[i];
                        }
                        else
                        {
                            cache[key] = dst;
                        }
                        break;
                    }

                    case Op.stfld_o:
                    case Op.stfld_r4:
                    case Op.stfld_i4:
                    {
                        int objS   = (int)ir[ip + 1];
                        int tokIdx = (int)ir[ip + 2];
                        int srcS   = (int)ir[ip + 3];
                        if ((uint)tokIdx >= (uint)tokens.Length) break;
                        int resolvedTok = tokens[tokIdx];
                        // Alias hazard: another slot may reference the SAME object (`C0 b = a;`),
                        // so a store through one slot must drop every cached entry for the same
                        // field under any OTHER object slot — else `a.f0 = 5; C0 b = a;
                        // b.f0 = 9; read a.f0` forwards the stale 5 (found by fuzzing).
                        DropAliasedField(cache, objS, resolvedTok);
                        // The stored value lives in srcS; subsequent loads of
                        // (objS, tok) can forward to it.
                        cache[(objS, resolvedTok)] = srcS;
                        break;
                    }

                    case Op.stfld_sc_o:
                    case Op.stfld_sc_r4:
                    case Op.stfld_sc_i4:
                    {
                        int objS = (int)ir[ip + 1];
                        int off  = (int)ir[ip + 2];
                        int srcS = (int)ir[ip + 3];
                        int koff = op == Op.stfld_sc_o ? ~(2 * off + 1) : ~(2 * off); // see ldfld_sc key split
                        DropAliasedField(cache, objS, koff); // same alias hazard as stfld_o above
                        cache[(objS, koff)] = srcS;
                        break;
                    }

                    case Op.call_script:
                    case Op.call_script_virt:
                    case Op.invoke_script_delegate: // re-enters the VM → may mutate any field/static/array
                    case Op.call_host:
                    case Op.call_host_byref:
                    case Op.newobj_script:
                    case Op.newobj_host:
                    case Op.stsfld_i4: case Op.stsfld_r4:
                    case Op.stsfld_o:  case Op.stsfld_struct:
                    case Op.stelem_i4: case Op.stelem_r4:
                    case Op.stelem_o:  case Op.stelem_struct:
                    case Op.stfld_struct:
                    case Op.box_vt: case Op.unbox_vt: case Op.mov_vt:
                    case Op.stfld_vt_i4: case Op.stfld_vt_r4:
                    case Op.stfld_vt_b1: case Op.stfld_vt_b2:
                    // Multi-byte flat-struct stores overwrite RANGES that can cover any cached
                    // (obj, offset) entry of the same object — e.g. `bx.p = s1` (stfld_sc_vt)
                    // rewrites every field of p, so a cached forward of bx.p.n.v goes stale.
                    case Op.stfld_sc_vt: case Op.stfld_vt_vt: case Op.stelem_vt:
                        cache.Clear();
                        break;

                    // stfld_sc_* only update the cache (handled above); no full clear needed.

                    case Op.br:
                    case Op.brtrue_i4: case Op.brfalse_i4:
                    case Op.brtrue_o:  case Op.brfalse_o:
                    case Op.blt_i4_nn: case Op.blt_i4_nk:
                    case Op.bgt_r4_nn: case Op.bgt_r4_nk:
                    case Op.beq_i4_nn: case Op.beq_i4_nk:
                    case Op.bne_i4_nn: case Op.bne_i4_nk:
                    case Op.for_i4_nk:
                    case Op.switch_i4:
                    case Op.push_cont: case Op.br_cont:
                    case Op.ret_void: case Op.ret_i4: case Op.ret_r4: case Op.ret_o: case Op.ret_vt:
                    case Op.throw_o:
                        // End-of-block: anything past here gets a fresh cache.
                        cache.Clear();
                        break;
                }
            }

            if (deletedWords == 0) return;

            // Apply slotMap globally to all slot operands.
            int Resolve(int s) { while (slotMap[s] != s) s = slotMap[s]; return s; }

            for (int i = 0; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                RenameSlots(ir, ips[i], ops[i], Resolve);
            }

            // Compact + branch-target patch.
            var deletedWord = new bool[n];
            for (int i = 0; i < insnCount; i++)
            {
                if (!deleted[i]) continue;
                for (int k = 0; k < widths[i]; k++) deletedWord[ips[i] + k] = true;
            }
            var shift = new int[n + 1];
            int run = 0;
            for (int oldIp = 0; oldIp <= n; oldIp++)
            {
                shift[oldIp] = run;
                if (oldIp < n && deletedWord[oldIp]) run++;
            }

            int newLen = n - deletedWords;
            var newIr   = new uint[newLen];
            var newToIl = new int[newLen];
            int dstIdx = 0;
            for (int i = 0; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                int oldIp = ips[i];
                int w = widths[i];
                for (int k = 0; k < w; k++)
                {
                    newIr[dstIdx + k]   = ir[oldIp + k];
                    newToIl[dstIdx + k] = irToIl[oldIp + k];
                }
                PatchInsnBranchTargets(newIr, dstIdx, ops[i], shift, ir, oldIp);
                dstIdx += w;
            }

            ir     = newIr;
            irToIl = newToIl;
        }

        // For each slot operand of `op` at `ip` in `ir`, apply `resolve` and write
        // back. Mirrors AccumulateSlotUses' positions; keep both in sync.
        static void RenameSlots(uint[] ir, int ip, Op op, Func<int, int> resolve)
        {
            void RW(int wOff)
            {
                int s = (int)ir[ip + wOff];
                if (s < 0) return; // sentinel (-1 for void-call dst, no-receiver, etc.)
                ir[ip + wOff] = (uint)resolve(s);
            }

            if (Is4WordArithForCoalesce(op))
            {
                RW(1); RW(2);
                if (!IsNkForm(op)) RW(3);
                return;
            }
            if (IsFusedCmpBranch(op))
            {
                RW(1);
                if (!IsFusedCmpBranchNk(op)) RW(2);
                return;
            }
            if (op == Op.for_i4_nk) { RW(1); return; }

            switch (op)
            {
                case Op.nop: case Op.ret_void: case Op.br:
                case Op.push_cont: case Op.br_cont: return;

                case Op.arr_bounds_check: RW(1); RW(2); return; // both words are read slots, no dst

                case Op.mov: case Op.box: case Op.mov_vt: case Op.box_vt: case Op.unbox_vt:
                case Op.clone_sc:
                case Op.ldlen:
                case Op.neg_i4: case Op.neg_i4_n: case Op.neg_r4: case Op.neg_r4_n:
                case Op.not_i4: case Op.not_i4_n:
                case Op.conv_i4_r4: case Op.conv_r4_i4:
                case Op.conv_i4_i1: case Op.conv_i4_u1:
                case Op.conv_i4_i2: case Op.conv_i4_u2:
                    RW(1); RW(2); return;

                case Op.ldc_i4: case Op.ldc_r4: case Op.ldstr:
                case Op.ldsfld_i4: case Op.ldsfld_r4: case Op.ldsfld_o: case Op.ldsfld_struct:
                case Op.ldtoken:
                case Op.ldnull: case Op.initobj: case Op.initobj_script: case Op.ensure_script:
                    RW(1); return;

                case Op.stsfld_i4: case Op.stsfld_r4: case Op.stsfld_o: case Op.stsfld_struct:
                    RW(2); return;

                case Op.ret_i4: case Op.ret_r4: case Op.ret_o: case Op.ret_vt:
                case Op.throw_o:
                    RW(1); return;

                case Op.brtrue_i4: case Op.brfalse_i4: case Op.brtrue_o: case Op.brfalse_o:
                    RW(1); return;

                case Op.ldfld_i4: case Op.ldfld_r4: case Op.ldfld_o:
                case Op.ldfld_struct: case Op.ldflda:
                case Op.ldfld_vt_i4: case Op.ldfld_vt_r4: case Op.ldfld_vt_vt:
                case Op.ldfld_vt_u1: case Op.ldfld_vt_i1: case Op.ldfld_vt_u2: case Op.ldfld_vt_i2:
                case Op.ldfld_sc_i4: case Op.ldfld_sc_r4: case Op.ldfld_sc_o: case Op.ldfld_sc_vt:
                    RW(1); RW(2); return;

                case Op.stfld_i4: case Op.stfld_r4: case Op.stfld_o: case Op.stfld_struct:
                case Op.stfld_vt_i4: case Op.stfld_vt_r4: case Op.stfld_vt_vt:
                case Op.stfld_vt_b1: case Op.stfld_vt_b2:
                case Op.stfld_sc_i4: case Op.stfld_sc_r4: case Op.stfld_sc_o: case Op.stfld_sc_vt:
                    RW(1); RW(3); return;

                case Op.ldelem_i4: case Op.ldelem_r4: case Op.ldelem_o: case Op.ldelem_struct:
                case Op.ldelem_vt:
                    RW(1); RW(2); RW(3); return;

                case Op.stelem_i4: case Op.stelem_r4: case Op.stelem_o: case Op.stelem_struct:
                case Op.stelem_vt:
                    RW(1); RW(2); RW(3); return;

                case Op.castclass: case Op.unbox_any: case Op.isinst: case Op.box_prim:
                case Op.box_enum:
                    RW(1); RW(2); return;

                case Op.newarr: case Op.newarr_i4: case Op.newarr_r4: case Op.newarr_vt:
                    RW(1); RW(2); return;

                case Op.new_delegate:
                    RW(1); RW(3); return; // dst, receiver slot (word 2 is a tokIdx)

                case Op.call_script:
                case Op.call_script_virt:
                case Op.newobj_script:
                case Op.newobj_host:
                {
                    RW(1);
                    int argc = (int)ir[ip + 3];
                    for (int k = 0; k < argc; k++) RW(4 + k);
                    return;
                }

                case Op.call_host:
                {
                    RW(1); RW(2);
                    int argc = (int)ir[ip + 4];
                    for (int k = 0; k < argc; k++) RW(5 + k);
                    return;
                }

                case Op.invoke_script_delegate:
                {
                    RW(1); RW(3); // dst, delegate receiver slot (word 2 is a tokIdx)
                    int argc = (int)ir[ip + 4];
                    for (int k = 0; k < argc; k++) RW(5 + k);
                    return;
                }

                case Op.call_host_byref:
                {
                    RW(1); RW(2);
                    int argc = (int)ir[ip + 4];
                    for (int k = 0; k < argc; k++) RW(5 + k);
                    int wbBase = ip + 5 + argc;
                    int wbCount = (int)ir[wbBase];
                    for (int k = 0; k < wbCount; k++)
                    {
                        int kind = (int)ir[wbBase + 1 + k * 4 + 1];
                        // word offsets relative to ip:
                        int t1Off = wbBase + 1 + k * 4 + 2 - ip;
                        int t2Off = wbBase + 1 + k * 4 + 3 - ip;
                        if (kind == 0)      { RW(t1Off); }
                        else if (kind == 1) { RW(t1Off); }
                        else                { RW(t1Off); RW(t2Off); }
                    }
                    return;
                }

                case Op.switch_i4:
                    RW(1); return;
            }
        }

        // --- Numeric for-loop super-instruction ---
        //
        // Detects the canonical Roslyn-emitted for-loop tail
        //   add_i4_nk slot[N], slot[N], 1
        //   blt_i4_nk slot[N], K_limit, body_top
        // plus the matching "test-at-bottom" loop entry
        //   ldc_i4 slot[N] = K_init
        //   br -> blt_ip
        // and fuses the add+blt into one `for_i4_nk slot[N], K_limit, body_top`
        // dispatch. Redirects the entry br from blt_ip to body_top so the first
        // body iteration isn't skipped (the executor's increment-then-test
        // would otherwise consume the first slot value).
        //
        // Safety:
        // - Step is hardcoded to 1 (the add's K must be exactly 1).
        // - K_init < K_limit must hold statically — without the entry test we
        //   can't guarantee zero-iter loops behave correctly.
        // - The induction slot is single-write across the loop entry pattern
        //   (the ldc + the add). This rules out odd cases where the body
        //   re-initializes the counter.
        //
        // Not handled:
        // - Reverse loops (`i--`).
        // - Step != 1.
        // - r4 induction (rare in idiomatic C#, accumulates error).
        // - Slot-valued limit (`for (int i=0; i<n; i++)` where n is a local).
        //   Bench corpus has no instances; add `for_i4_n` on demand.
        static void FoldForIntLoop(ref uint[] ir, ref int[] irToIl, SType[] slotTypes)
        {
            int n = ir.Length;
            if (n == 0) return;

            var ips    = new List<int>(n / 3);
            var ops    = new List<Op>(n / 3);
            var widths = new List<int>(n / 3);
            int pos = 0;
            while (pos < n)
            {
                var op = (Op)ir[pos];
                int w = OpWidthForCoalesce(op, ir, pos);
                if (w <= 0) return;
                ips.Add(pos); ops.Add(op); widths.Add(w);
                pos += w;
            }
            int insnCount = ips.Count;

            // Map IP → instruction index for lookup.
            var ipToIdx = new Dictionary<int, int>(insnCount);
            for (int i = 0; i < insnCount; i++) ipToIdx[ips[i]] = i;

            // Branch-target set: which IPs are jumped to by some branch?
            var isBranchTarget = new bool[n + 1];
            for (int i = 0; i < insnCount; i++)
                MarkInsnBranchTargets(ir, ips[i], ops[i], isBranchTarget);

            var deleted = new bool[insnCount];
            int deletedWords = 0;

            for (int i = 1; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                if (ops[i] != Op.blt_i4_nk) continue;
                int bltIp = ips[i];
                int slotN  = (int)ir[bltIp + 1];
                int kLimit = (int)ir[bltIp + 2];
                int bodyTop = (int)ir[bltIp + 3];
                if (bodyTop >= bltIp) continue;          // must be a backward branch
                if (!ipToIdx.ContainsKey(bodyTop)) continue;

                // Preceding instruction must be add_i4_nk slot[N], slot[N], 1.
                int j = i - 1;
                if (deleted[j] || ops[j] != Op.add_i4_nk) continue;
                int addIp = ips[j];
                if ((int)ir[addIp + 1] != slotN) continue;       // dst == N
                if ((int)ir[addIp + 2] != slotN) continue;       // src == N (in-place)
                if ((int)ir[addIp + 3] != 1) continue;           // step == 1

                // Two cases:
                //   (a) Test-at-bottom loop: an entry `br -> bltIp` exists. We must
                //       redirect it to body_top so the for_i4_nk doesn't consume the
                //       first body iteration. Requires statically provable
                //       K_init < K_limit (otherwise we'd execute the body once when
                //       the original loop wouldn't have run at all).
                //   (b) Test-at-top / do-while loop: nothing branches to the blt. The
                //       loop is entered by fall-through and exited by fall-through past
                //       the blt. for_i4_nk has identical semantics to add+blt at the
                //       loop tail, so the fuse is unconditionally safe.
                bool needRedirect = isBranchTarget[bltIp];
                if (needRedirect)
                {
                    if (!ipToIdx.TryGetValue(bodyTop, out int topIdx)) continue;
                    if (topIdx < 2) continue;
                    int brIdx = topIdx - 1;
                    if (deleted[brIdx] || ops[brIdx] != Op.br) continue;
                    int brIp = ips[brIdx];
                    if ((int)ir[brIp + 1] != bltIp) continue;     // br must target the blt

                    int initIdx = brIdx - 1;
                    if (initIdx < 0 || deleted[initIdx]) continue;
                    if (ops[initIdx] != Op.ldc_i4) continue;
                    int initIp = ips[initIdx];
                    if ((int)ir[initIp + 1] != slotN) continue;
                    int kInit = (int)ir[initIp + 2];
                    if (kInit >= kLimit) continue;                // entry-check guard

                    // Redirect entry br to body_top.
                    ir[brIp + 1] = (uint)bodyTop;
                }

                // Apply: rewrite add → for_i4_nk in place; mark blt for deletion.
                ir[addIp + 0] = (uint)Op.for_i4_nk;
                ir[addIp + 1] = (uint)slotN;
                ir[addIp + 2] = (uint)kLimit;
                ir[addIp + 3] = (uint)bodyTop;
                ops[j] = Op.for_i4_nk;

                deleted[i] = true;
                deletedWords += widths[i];
            }

            if (deletedWords == 0) return;

            var deletedWord = new bool[n];
            for (int i = 0; i < insnCount; i++)
            {
                if (!deleted[i]) continue;
                for (int k = 0; k < widths[i]; k++) deletedWord[ips[i] + k] = true;
            }
            var shift = new int[n + 1];
            int run = 0;
            for (int oldIp = 0; oldIp <= n; oldIp++)
            {
                shift[oldIp] = run;
                if (oldIp < n && deletedWord[oldIp]) run++;
            }

            int newLen = n - deletedWords;
            var newIr   = new uint[newLen];
            var newToIl = new int[newLen];
            int dstIdx = 0;
            for (int i = 0; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                int oldIp = ips[i];
                int w = widths[i];
                for (int k = 0; k < w; k++)
                {
                    newIr[dstIdx + k]   = ir[oldIp + k];
                    newToIl[dstIdx + k] = irToIl[oldIp + k];
                }
                PatchInsnBranchTargets(newIr, dstIdx, ops[i], shift, ir, oldIp);
                dstIdx += w;
            }

            ir     = newIr;
            irToIl = newToIl;
        }

        // --- Loop-invariant constant hoisting ---
        //
        // A `ldc_i4 / ldc_r4 / ldstr` inside a loop re-executes every iteration even though it
        // always produces the same value into the same slot. Profiling showed constant reloads at
        // 10–38% of all dispatches on loop-heavy bodies (`i & 255`, `Translate(0.01f, 0f, -0.01f)`).
        // This pass moves qualifying ldcs into a prologue emitted once at IR entry.
        //
        // A candidate ldc must satisfy:
        // - writes[dst] == 1: this ldc is the slot's only writer in the whole method. C# definite
        //   assignment then guarantees the write dominates every read, so "execute once up front"
        //   observes the same values (re-executions were idempotent).
        // - dst >= argCount: arg slots are populated by the CALLER before Run enters the IR;
        //   a prologue write would clobber the incoming argument.
        // - the ldc sits inside a loop body — between a backward branch and its target. Hoisting
        //   straight-line or cold-branch constants would ADD a dispatch per call, not remove one.
        //
        // Branch targets all shift by the prologue length; the shared shift-table mechanism handles
        // that with negative entries (newTarget = old - shift[old]).
        static void HoistLoopConsts(ref uint[] ir, ref int[] irToIl, SType[] slotTypes, int argCount)
        {
            int n = ir.Length;
            if (n == 0) return;

            var ips    = new List<int>(n / 3);
            var ops    = new List<Op>(n / 3);
            var widths = new List<int>(n / 3);
            int pos = 0;
            while (pos < n)
            {
                var op = (Op)ir[pos];
                int w = OpWidthForCoalesce(op, ir, pos);
                if (w <= 0) return;
                ips.Add(pos); ops.Add(op); widths.Add(w);
                pos += w;
            }
            int insnCount = ips.Count;

            int slotCount = slotTypes.Length;
            var reads  = new int[slotCount];
            var writes = new int[slotCount];
            for (int i = 0; i < insnCount; i++)
                AccumulateSlotUses(ir, ips[i], ops[i], widths[i], reads, writes, slotCount);

            // Mark loop-body words: for every backward branch at ip with target t <= ip, the
            // whole [t, ip] range is (over-approximately) a loop body. Difference array over words.
            var loopDelta = new int[n + 2];
            void MarkLoop(int from, int to) { if (to <= from) { loopDelta[to]++; loopDelta[from + 1]--; } }
            for (int i = 0; i < insnCount; i++)
            {
                int ip = ips[i];
                switch (ops[i])
                {
                    case Op.br: case Op.push_cont: MarkLoop(ip, (int)ir[ip + 1]); break;
                    case Op.brtrue_i4: case Op.brfalse_i4:
                    case Op.brtrue_o:  case Op.brfalse_o: MarkLoop(ip, (int)ir[ip + 2]); break;
                    case Op.blt_i4_nn: case Op.blt_i4_nk:
                    case Op.bgt_r4_nn: case Op.bgt_r4_nk:
                    case Op.beq_i4_nn: case Op.beq_i4_nk:
                    case Op.bne_i4_nn: case Op.bne_i4_nk:
                    case Op.for_i4_nk: MarkLoop(ip, (int)ir[ip + 3]); break;
                    case Op.switch_i4:
                    {
                        int cnt = (int)ir[ip + 2];
                        MarkLoop(ip, (int)ir[ip + 3]);
                        for (int k = 0; k < cnt; k++) MarkLoop(ip, (int)ir[ip + 4 + k]);
                        break;
                    }
                }
            }
            var inLoop = new bool[n];
            int depth = 0;
            for (int w2 = 0; w2 < n; w2++) { depth += loopDelta[w2]; inLoop[w2] = depth > 0; }

            var hoisted = new List<int>(); // instruction indexes to move
            var deleted = new bool[insnCount];
            int hoistWords = 0;
            for (int i = 0; i < insnCount; i++)
            {
                var op = ops[i];
                if (op != Op.ldc_i4 && op != Op.ldc_r4 && op != Op.ldstr) continue;
                int ip  = ips[i];
                if (!inLoop[ip]) continue;
                int dst = (int)ir[ip + 1];
                if (dst < argCount) continue;
                if ((uint)dst >= (uint)slotCount) continue;
                if (writes[dst] != 1 || reads[dst] == 0) continue;
                hoisted.Add(i);
                deleted[i] = true;
                hoistWords += widths[i];
            }
            if (hoisted.Count == 0) return;

            // shift[t] = (deleted words strictly before t) - prologueLen, so
            // newTarget = t - shift[t] lands on the surviving instruction, offset by the prologue.
            var deletedWord = new bool[n];
            foreach (var i in hoisted)
                for (int k = 0; k < widths[i]; k++) deletedWord[ips[i] + k] = true;
            var shift = new int[n + 1];
            int run = 0;
            for (int oldIp = 0; oldIp <= n; oldIp++)
            {
                shift[oldIp] = run - hoistWords;
                if (oldIp < n && deletedWord[oldIp]) run++;
            }

            int newLen = n; // deleted words re-emitted verbatim in the prologue
            var newIr   = new uint[newLen];
            var newToIl = new int[newLen];
            int dstIdx = 0;
            foreach (var i in hoisted)
            {
                int oldIp = ips[i];
                for (int k = 0; k < widths[i]; k++)
                {
                    newIr[dstIdx + k]   = ir[oldIp + k];
                    newToIl[dstIdx + k] = irToIl[oldIp + k];
                }
                dstIdx += widths[i];
            }
            for (int i = 0; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                int oldIp = ips[i];
                int w = widths[i];
                for (int k = 0; k < w; k++)
                {
                    newIr[dstIdx + k]   = ir[oldIp + k];
                    newToIl[dstIdx + k] = irToIl[oldIp + k];
                }
                PatchInsnBranchTargets(newIr, dstIdx, ops[i], shift, ir, oldIp);
                dstIdx += w;
            }

            ir     = newIr;
            irToIl = newToIl;
        }

        // --- Fused compare-and-branch ---
        //
        // Detects `compare_X_form dst, ...; brtrue/brfalse dst, target` where
        // dst is single-use (the brtrue/brfalse is its only reader) and folds
        // into one fused `bX_form ...args, target`. Runs AFTER CoalesceDestMovs
        // so that the compare's dst is already the local slot the branch
        // reads — pattern detection becomes a clean two-instruction window.
        //
        // Mappings (only ones with bench-corpus evidence):
        //   clt_i4_nn + brtrue → blt_i4_nn      ceq_i4_nn + brtrue → beq_i4_nn
        //   clt_i4_nk + brtrue → blt_i4_nk      ceq_i4_nk + brtrue → beq_i4_nk
        //   cgt_r4_nn + brtrue → bgt_r4_nn      ceq_i4_nn + brfalse → bne_i4_nn
        //   cgt_r4_nk + brtrue → bgt_r4_nk      ceq_i4_nk + brfalse → bne_i4_nk
        //
        // Other polarity combinations (clt+brfalse, cgt+brfalse, etc.) are
        // left as two ops — adding "bge"/"ble" etc. would double the opcode
        // count for patterns we don't have. Add on demand.
        static void FoldCompareBranch(ref uint[] ir, ref int[] irToIl, SType[] slotTypes)
        {
            int n = ir.Length;
            if (n == 0) return;

            var ips    = new List<int>(n / 3);
            var ops    = new List<Op>(n / 3);
            var widths = new List<int>(n / 3);
            int pos = 0;
            while (pos < n)
            {
                var op = (Op)ir[pos];
                int w = OpWidthForCoalesce(op, ir, pos);
                if (w <= 0) return;
                ips.Add(pos); ops.Add(op); widths.Add(w);
                pos += w;
            }
            int insnCount = ips.Count;

            int slotCount = slotTypes.Length;
            var reads  = new int[slotCount];
            var writes = new int[slotCount];
            for (int i = 0; i < insnCount; i++)
                AccumulateSlotUses(ir, ips[i], ops[i], widths[i], reads, writes, slotCount);

            var isBranchTarget = new bool[n + 1];
            for (int i = 0; i < insnCount; i++)
                MarkInsnBranchTargets(ir, ips[i], ops[i], isBranchTarget);

            var deleted = new bool[insnCount];
            int deletedWords = 0;
            for (int i = 0; i < insnCount - 1; i++)
            {
                if (deleted[i]) continue;
                int cmpIp = ips[i];
                if (widths[i] != 4) continue;
                int dstSlot = (int)ir[cmpIp + 1];
                if ((uint)dstSlot >= (uint)slotCount) continue; // -1 sentinel (e.g. zero-arg void call_script)
                if (writes[dstSlot] != 1 || reads[dstSlot] != 1) continue;

                int j = i + 1;
                if (j >= insnCount || deleted[j]) continue;
                var brOp = ops[j];
                if (brOp != Op.brtrue_i4 && brOp != Op.brfalse_i4) continue;
                int brIp = ips[j];
                if (isBranchTarget[brIp]) continue;
                if ((int)ir[brIp + 1] != dstSlot) continue;

                bool brIfTrue = (brOp == Op.brtrue_i4);
                if (!TryFoldCompareBranch(ops[i], brIfTrue, out Op fusedOp)) continue;

                uint target = ir[brIp + 2];

                // Rewrite cmp in place: [op, s1, s2_or_k, target_ip]
                ir[cmpIp]     = (uint)fusedOp;
                ir[cmpIp + 1] = ir[cmpIp + 2]; // slide src1 from word 2 → word 1
                ir[cmpIp + 2] = ir[cmpIp + 3]; // slide src2/k from word 3 → word 2
                ir[cmpIp + 3] = target;        // target at word 3

                // Update cached op so PatchInsnBranchTargets sees the new opcode and
                // applies the shift to word 3 (not the original cmp's word 3, which
                // was an operand and shouldn't be shifted).
                ops[i] = fusedOp;

                deleted[j] = true;
                deletedWords += widths[j];

                reads[dstSlot] = 0; writes[dstSlot] = 0;
            }

            if (deletedWords == 0) return;

            // Compact + branch-target patch.
            var deletedWord = new bool[n];
            for (int i = 0; i < insnCount; i++)
            {
                if (!deleted[i]) continue;
                for (int k = 0; k < widths[i]; k++) deletedWord[ips[i] + k] = true;
            }
            var shift = new int[n + 1];
            int run = 0;
            for (int oldIp = 0; oldIp <= n; oldIp++)
            {
                shift[oldIp] = run;
                if (oldIp < n && deletedWord[oldIp]) run++;
            }

            int newLen = n - deletedWords;
            var newIr   = new uint[newLen];
            var newToIl = new int[newLen];
            int dstIdx = 0;
            for (int i = 0; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                int oldIp = ips[i];
                int w = widths[i];
                for (int k = 0; k < w; k++)
                {
                    newIr[dstIdx + k]   = ir[oldIp + k];
                    newToIl[dstIdx + k] = irToIl[oldIp + k];
                }
                PatchInsnBranchTargets(newIr, dstIdx, ops[i], shift, ir, oldIp);
                dstIdx += w;
            }

            ir     = newIr;
            irToIl = newToIl;
        }

        // True when the op writes a frame slot at ir[ip+1] (its dst). False for control flow, stores
        // (which write memory, not a frame slot), and returns. Used by ForwardRedundantLoads to know
        // which cached slots a reassignment invalidates. Calls/newobj also write a dst but clear the
        // whole cache separately, so treating them as writers here is harmless.
        static bool WritesFrameDstAt1(Op op) => op switch
        {
            Op.nop or Op.br
            or Op.brtrue_i4 or Op.brfalse_i4 or Op.brtrue_o or Op.brfalse_o
            or Op.ret_void or Op.ret_i4 or Op.ret_r4 or Op.ret_o or Op.throw_o
            or Op.stfld_i4 or Op.stfld_r4 or Op.stfld_o or Op.stfld_struct
            or Op.stfld_sc_i4 or Op.stfld_sc_r4 or Op.stfld_sc_o or Op.stfld_sc_vt
            or Op.stfld_vt_i4 or Op.stfld_vt_r4 or Op.stfld_vt_b1 or Op.stfld_vt_b2
            or Op.stelem_i4 or Op.stelem_r4 or Op.stelem_o or Op.stelem_struct
            or Op.stsfld_i4 or Op.stsfld_r4 or Op.stsfld_o or Op.stsfld_struct
            or Op.switch_i4
            or Op.blt_i4_nn or Op.blt_i4_nk or Op.bgt_r4_nn or Op.bgt_r4_nk
            or Op.beq_i4_nn or Op.beq_i4_nk or Op.bne_i4_nn or Op.bne_i4_nk
            or Op.push_cont or Op.br_cont => false,
            _ => true,
        };

        static bool TryFoldCompareBranch(Op cmpOp, bool brIfTrue, out Op fused)
        {
            fused = default;
            switch (cmpOp)
            {
                case Op.clt_i4_nn: if (brIfTrue) { fused = Op.blt_i4_nn; return true; } return false;
                case Op.clt_i4_nk: if (brIfTrue) { fused = Op.blt_i4_nk; return true; } return false;
                case Op.cgt_r4_nn: if (brIfTrue) { fused = Op.bgt_r4_nn; return true; } return false;
                case Op.cgt_r4_nk: if (brIfTrue) { fused = Op.bgt_r4_nk; return true; } return false;
                case Op.ceq_i4_nn: fused = brIfTrue ? Op.beq_i4_nn : Op.bne_i4_nn; return true;
                case Op.ceq_i4_nk: fused = brIfTrue ? Op.beq_i4_nk : Op.bne_i4_nk; return true;
                default: return false;
            }
        }

        // Helpers shared by all three peephole passes.
        static void MarkInsnBranchTargets(uint[] ir, int ip, Op op, bool[] isBranchTarget)
        {
            switch (op)
            {
                case Op.br:
                case Op.push_cont:
                    MarkTarget(isBranchTarget, ir[ip + 1]); break;
                case Op.brtrue_i4: case Op.brfalse_i4:
                case Op.brtrue_o:  case Op.brfalse_o:
                    MarkTarget(isBranchTarget, ir[ip + 2]); break;
                case Op.blt_i4_nn: case Op.blt_i4_nk:
                case Op.bgt_r4_nn: case Op.bgt_r4_nk:
                case Op.beq_i4_nn: case Op.beq_i4_nk:
                case Op.bne_i4_nn: case Op.bne_i4_nk:
                case Op.for_i4_nk:
                    MarkTarget(isBranchTarget, ir[ip + 3]); break;
                case Op.switch_i4:
                {
                    int cnt = (int)ir[ip + 2];
                    MarkTarget(isBranchTarget, ir[ip + 3]);
                    for (int k = 0; k < cnt; k++)
                        MarkTarget(isBranchTarget, ir[ip + 4 + k]);
                    break;
                }
            }
        }

        static void PatchInsnBranchTargets(uint[] newIr, int dstIdx, Op op, int[] shift,
            uint[] oldIr, int oldIp)
        {
            switch (op)
            {
                case Op.br:
                case Op.push_cont:
                    newIr[dstIdx + 1] = (uint)((int)oldIr[oldIp + 1] - shift[(int)oldIr[oldIp + 1]]);
                    break;
                case Op.brtrue_i4: case Op.brfalse_i4:
                case Op.brtrue_o:  case Op.brfalse_o:
                case Op.brtrue_i8: case Op.brfalse_i8:
                    newIr[dstIdx + 2] = (uint)((int)oldIr[oldIp + 2] - shift[(int)oldIr[oldIp + 2]]);
                    break;
                case Op.blt_i4_nn: case Op.blt_i4_nk:
                case Op.bgt_r4_nn: case Op.bgt_r4_nk:
                case Op.beq_i4_nn: case Op.beq_i4_nk:
                case Op.bne_i4_nn: case Op.bne_i4_nk:
                case Op.for_i4_nk:
                    newIr[dstIdx + 3] = (uint)((int)oldIr[oldIp + 3] - shift[(int)oldIr[oldIp + 3]]);
                    break;
                case Op.switch_i4:
                {
                    int cnt = (int)oldIr[oldIp + 2];
                    int defT = (int)oldIr[oldIp + 3];
                    newIr[dstIdx + 3] = (uint)(defT - shift[defT]);
                    for (int k = 0; k < cnt; k++)
                    {
                        int t = (int)oldIr[oldIp + 4 + k];
                        newIr[dstIdx + 4 + k] = (uint)(t - shift[t]);
                    }
                    break;
                }
            }
        }

        // --- Dest-coalescing peephole ---
        //
        // Eliminates `mov dst=X, src=tmp` where `tmp` is a single-use temp produced by
        // the immediately-preceding instruction. Rewrites that instruction's dst to X
        // and deletes the mov. Run after branch-target patching so target IPs are
        // word-indexed; we re-patch them after compaction.
        //
        // Safety:
        // - tmp has exactly 1 read (the candidate mov) and 1 write (prev op) globally.
        // - slotTypes[tmp] == slotTypes[X], and neither is Vt — guarantees the prev
        //   op's frame write (numFrame vs refFrame, byte vs ref) is valid for X.
        // - The mov's IP is not a branch target.
        // - Prev op is in a conservative whitelist (IsRetargetable) of ops that write
        //   their dst at words[1] with no other side effects on dst.
        static void CoalesceDestMovs(ref uint[] ir, ref int[] irToIl, SType[] slotTypes)
        {
            int n = ir.Length;
            if (n == 0) return;

            // Walk IR once: record (ip, op, width) per instruction. Bail out if any
            // op has unknown width — leaves IR untouched.
            var ips    = new List<int>(n / 3);
            var ops    = new List<Op>(n / 3);
            var widths = new List<int>(n / 3);
            int pos = 0;
            while (pos < n)
            {
                var op = (Op)ir[pos];
                int w = OpWidthForCoalesce(op, ir, pos);
                if (w <= 0) return;
                ips.Add(pos); ops.Add(op); widths.Add(w);
                pos += w;
            }
            int insnCount = ips.Count;

            // Per-slot read/write counts. Conservative: any value in a "slot position"
            // is counted, even if -1 (sentinel for void/static); -1 just bumps a phantom
            // entry that nothing else references.
            int slotCount = slotTypes.Length;
            var reads  = new int[slotCount];
            var writes = new int[slotCount];
            for (int i = 0; i < insnCount; i++)
                AccumulateSlotUses(ir, ips[i], ops[i], widths[i], reads, writes, slotCount);

            // Set of branch-target word IPs. Branches target alive-instruction IPs; we
            // refuse to delete a mov that anything jumps to.
            var isBranchTarget = new bool[n + 1];
            for (int i = 0; i < insnCount; i++)
                MarkInsnBranchTargets(ir, ips[i], ops[i], isBranchTarget);

            // Identify mov instructions to coalesce.
            var deleted = new bool[insnCount];
            int deletedWords = 0;
            for (int i = 1; i < insnCount; i++)
            {
                if (ops[i] != Op.mov) continue;
                int movIp = ips[i];
                if (isBranchTarget[movIp]) continue;

                int dstSlot = (int)ir[movIp + 1];
                int srcSlot = (int)ir[movIp + 2];
                if ((uint)dstSlot >= (uint)slotCount || (uint)srcSlot >= (uint)slotCount) continue;

                var stSrc = slotTypes[srcSlot];
                var stDst = slotTypes[dstSlot];
                if (stSrc == SType.Vt || stDst == SType.Vt) continue;
                if (stSrc != stDst) continue;

                if (writes[srcSlot] != 1 || reads[srcSlot] != 1) continue;

                // Prev instruction must define srcSlot in retargetable position.
                var prevOp = ops[i - 1];
                if (deleted[i - 1]) continue;
                if (!IsRetargetable(prevOp)) continue;
                int prevDstWord = ips[i - 1] + 1;
                if ((int)ir[prevDstWord] != srcSlot) continue;

                // Apply rewrite + mark deletion.
                ir[prevDstWord] = (uint)dstSlot;
                deleted[i] = true;
                deletedWords += widths[i];

                // Update counts so a chained mov (X <- Y where Y was just rewritten
                // away) sees the new state. srcSlot loses its lone read+write; dstSlot
                // gains one of each (the rewritten prev op now reads/writes dst).
                reads[srcSlot] = 0; writes[srcSlot] = 0;
                writes[dstSlot]++;
            }

            if (deletedWords == 0) return;

            // Build old-IP → new-IP shift table for branch-target patching.
            // shift[oldIp] = total deleted words at positions strictly less than oldIp.
            var deletedWord = new bool[n];
            for (int i = 0; i < insnCount; i++)
            {
                if (!deleted[i]) continue;
                for (int k = 0; k < widths[i]; k++) deletedWord[ips[i] + k] = true;
            }
            var shift = new int[n + 1];
            int run = 0;
            for (int oldIp = 0; oldIp <= n; oldIp++)
            {
                shift[oldIp] = run;
                if (oldIp < n && deletedWord[oldIp]) run++;
            }

            // Compact: copy alive instructions, patching branch targets via shift.
            int newLen = n - deletedWords;
            var newIr   = new uint[newLen];
            var newToIl = new int[newLen];
            int dstIdx = 0;
            for (int i = 0; i < insnCount; i++)
            {
                if (deleted[i]) continue;
                int oldIp = ips[i];
                int w = widths[i];
                for (int k = 0; k < w; k++)
                {
                    newIr[dstIdx + k]   = ir[oldIp + k];
                    newToIl[dstIdx + k] = irToIl[oldIp + k];
                }
                switch (ops[i])
                {
                    case Op.br:
                    case Op.push_cont:
                        newIr[dstIdx + 1] = (uint)((int)ir[oldIp + 1] - shift[(int)ir[oldIp + 1]]);
                        break;
                    case Op.brtrue_i4: case Op.brfalse_i4:
                    case Op.brtrue_o:  case Op.brfalse_o:
                        newIr[dstIdx + 2] = (uint)((int)ir[oldIp + 2] - shift[(int)ir[oldIp + 2]]);
                        break;
                    case Op.switch_i4:
                    {
                        int cnt = (int)ir[oldIp + 2];
                        int defT = (int)ir[oldIp + 3];
                        newIr[dstIdx + 3] = (uint)(defT - shift[defT]);
                        for (int k = 0; k < cnt; k++)
                        {
                            int t = (int)ir[oldIp + 4 + k];
                            newIr[dstIdx + 4 + k] = (uint)(t - shift[t]);
                        }
                        break;
                    }
                }
                dstIdx += w;
            }

            ir      = newIr;
            irToIl  = newToIl;
        }

        static void MarkTarget(bool[] arr, uint target)
        {
            int t = (int)target;
            if (t >= 0 && t < arr.Length) arr[t] = true;
        }

        static bool IsNkForm(Op op) => op switch
        {
            Op.add_i4_nk or Op.add_r4_nk or Op.sub_i4_nk or Op.sub_r4_nk
            or Op.mul_r4_nk
            or Op.clt_i4_nk or Op.clt_r4_nk or Op.cgt_i4_nk or Op.cgt_r4_nk
            or Op.ceq_i4_nk
            or Op.mul_i4_nk or Op.div_i4_nk or Op.rem_i4_nk
            or Op.and_i4_nk or Op.or_i4_nk or Op.xor_i4_nk
            or Op.shl_i4_nk or Op.shr_i4_nk or Op.shr_un_i4_nk
            or Op.div_r4_nk or Op.ceq_r4_nk => true,
            _ => false,
        };

        static bool IsFusedCmpBranch(Op op) => op switch
        {
            Op.blt_i4_nn or Op.blt_i4_nk
            or Op.bgt_r4_nn or Op.bgt_r4_nk
            or Op.beq_i4_nn or Op.beq_i4_nk
            or Op.bne_i4_nn or Op.bne_i4_nk => true,
            _ => false,
        };

        // True when the second operand at word 2 is an inline immediate, not a slot.
        static bool IsFusedCmpBranchNk(Op op) => op switch
        {
            Op.blt_i4_nk or Op.bgt_r4_nk or Op.beq_i4_nk or Op.bne_i4_nk => true,
            _ => false,
        };

        // Whitelist of ops whose only effect (on the frame) is writing words[1] as a slot index.
        // call_host_byref and Vt-dst ops are excluded conservatively (byref write-back touches
        // slots beyond dst; Vt dsts span multiple numeric words — the coalesce pass's "types
        // equal, neither Vt" check would reject them anyway).
        // Calls and newobjs ARE retargetable: their dst is written exactly once, type-routed by
        // slotT (numeric or ref frame), after all argument reads — the profile showed 12-25% of
        // dispatches were `call tmp; mov local <- tmp` pairs this unlocks.
        static bool IsRetargetable(Op op)
        {
            if (Is4WordArithForCoalesce(op)) return true;
            return op switch
            {
                Op.call_host or Op.call_script or Op.call_script_virt or Op.newobj_host
                    or Op.newobj_script or Op.new_delegate or Op.invoke_script_delegate => true,
                Op.ldc_i4 or Op.ldc_r4 or Op.ldnull or Op.ldstr => true,
                Op.neg_i4 or Op.neg_i4_n or Op.neg_r4 or Op.neg_r4_n
                    or Op.not_i4 or Op.not_i4_n => true,
                Op.conv_i4_r4 or Op.conv_r4_i4 or Op.conv_i4_i1 or Op.conv_i4_u1
                    or Op.conv_i4_i2 or Op.conv_i4_u2 => true,
                Op.ldfld_i4 or Op.ldfld_r4 or Op.ldfld_o or Op.ldflda => true,
                Op.ldfld_sc_i4 or Op.ldfld_sc_r4 or Op.ldfld_sc_o or Op.ldfld_sc_vt => true,
                Op.ldsfld_i4 or Op.ldsfld_r4 or Op.ldsfld_o => true,
                Op.ldlen => true,
                Op.ldelem_i4 or Op.ldelem_r4 or Op.ldelem_o => true,
                Op.ldtoken => true,
                Op.castclass or Op.unbox_any or Op.isinst => true,
                Op.box or Op.box_prim or Op.box_enum => true,
                Op.ldfld_vt_i4 or Op.ldfld_vt_r4 => true, // dst is numeric, not Vt
                Op.ldfld_vt_u1 or Op.ldfld_vt_i1 or Op.ldfld_vt_u2 or Op.ldfld_vt_i2 => true,
                Op.box_vt => true,                         // dst is O
                _ => false,
            };
        }

        // Mirrors the executor's per-op ip-advance. Returns -1 for ops we don't
        // recognise (causes the pass to bail out, leaving IR untouched).
        internal static int OpWidthForCoalesce(Op op, uint[] ir, int ip)
        {
            if (Is4WordArithForCoalesce(op)) return 4;
            if (IsFusedCmpBranch(op))         return 4;
            if (op == Op.for_i4_nk)           return 4;
            return op switch
            {
                Op.nop or Op.ret_void or Op.br_cont => 1,
                Op.br or Op.push_cont or Op.ret_i4 or Op.ret_r4 or Op.ret_o or Op.ret_vt or Op.throw_o or Op.ldnull or Op.initobj
                    or Op.ret_i8 or Op.ret_r8 => 2,

                // 64-bit family — 4-word binops/constants, 3-word convs/unaries/branches. These
                // never reach the optimizer passes (wide frames skip them) but the blob relocator
                // widths must be exact.
                Op.ldc_i8 or Op.ldc_r8
                    or Op.add_i8 or Op.sub_i8 or Op.mul_i8 or Op.div_i8 or Op.rem_i8
                    or Op.div_un_i8 or Op.rem_un_i8
                    or Op.and_i8 or Op.or_i8 or Op.xor_i8
                    or Op.shl_i8 or Op.shr_i8 or Op.shr_un_i8
                    or Op.add_r8 or Op.sub_r8 or Op.mul_r8 or Op.div_r8 or Op.rem_r8
                    or Op.ceq_i8 or Op.cgt_i8 or Op.clt_i8 or Op.cgt_un_i8 or Op.clt_un_i8
                    or Op.ceq_r8 or Op.cgt_r8 or Op.clt_r8 or Op.cgt_un_r8 or Op.clt_un_r8 => 4,

                Op.neg_i8 or Op.not_i8 or Op.neg_r8
                    or Op.conv_i8_i4 or Op.conv_i8_u4 or Op.conv_i4_i8
                    or Op.conv_i8_r4 or Op.conv_r4_i8 or Op.conv_i8_r8 or Op.conv_r8_i8
                    or Op.conv_r8_r4 or Op.conv_r4_r8 or Op.conv_r8_i4 or Op.conv_i4_r8
                    or Op.conv_r8_u8
                    or Op.brtrue_i8 or Op.brfalse_i8 => 3,

                Op.arr_bounds_check => 3,

                Op.mov or Op.box or Op.mov_vt or Op.box_vt or Op.unbox_vt or Op.clone_sc
                    or Op.ldlen or Op.neg_i4 or Op.neg_i4_n or Op.neg_r4 or Op.neg_r4_n
                    or Op.not_i4 or Op.not_i4_n
                    or Op.conv_i4_r4 or Op.conv_r4_i4 or Op.conv_i4_i1 or Op.conv_i4_u1
                    or Op.conv_i4_i2 or Op.conv_i4_u2
                    or Op.initobj_script or Op.ensure_script or Op.ldtoken
                    or Op.ldc_i4 or Op.ldc_r4 or Op.ldstr
                    or Op.ldsfld_i4 or Op.ldsfld_r4 or Op.ldsfld_o or Op.ldsfld_struct
                    or Op.stsfld_i4 or Op.stsfld_r4 or Op.stsfld_o or Op.stsfld_struct
                    or Op.brtrue_i4 or Op.brfalse_i4 or Op.brtrue_o or Op.brfalse_o => 3,

                Op.ldfld_i4 or Op.ldfld_r4 or Op.ldfld_o or Op.ldfld_struct or Op.ldflda
                    or Op.stfld_i4 or Op.stfld_r4 or Op.stfld_o or Op.stfld_struct
                    or Op.ldelem_i4 or Op.ldelem_r4 or Op.ldelem_o or Op.ldelem_struct or Op.ldelem_vt
                    or Op.stelem_i4 or Op.stelem_r4 or Op.stelem_o or Op.stelem_struct or Op.stelem_vt
                    or Op.ldfld_vt_i4 or Op.ldfld_vt_r4 or Op.stfld_vt_i4 or Op.stfld_vt_r4
                    or Op.ldfld_vt_u1 or Op.ldfld_vt_i1 or Op.ldfld_vt_u2 or Op.ldfld_vt_i2
                    or Op.stfld_vt_b1 or Op.stfld_vt_b2
                    or Op.ldfld_vt_vt or Op.stfld_vt_vt
                    or Op.ldfld_sc_i4 or Op.ldfld_sc_r4 or Op.ldfld_sc_o or Op.ldfld_sc_vt
                    or Op.stfld_sc_i4 or Op.stfld_sc_r4 or Op.stfld_sc_o or Op.stfld_sc_vt
                    or Op.castclass or Op.unbox_any or Op.isinst or Op.box_prim or Op.box_enum
                    or Op.new_delegate => 4,

                Op.newarr or Op.newarr_i4 or Op.newarr_r4 or Op.newarr_vt => 5,

                Op.call_script or Op.call_script_virt or Op.newobj_script or Op.newobj_host
                    => ip + 3 < ir.Length ? 4 + (int)ir[ip + 3] : -1,
                Op.call_host
                    => ip + 4 < ir.Length ? 5 + (int)ir[ip + 4] : -1,
                Op.invoke_script_delegate // [op, dst, tokIdx, recv, argc, arg0..] — like call_host
                    => ip + 4 < ir.Length ? 5 + (int)ir[ip + 4] : -1,
                Op.call_host_byref => CallHostByrefWidthForCoalesce(ir, ip),
                Op.switch_i4 => ip + 2 < ir.Length ? 4 + (int)ir[ip + 2] : -1,

                _ => -1,
            };
        }

        static bool Is4WordArithForCoalesce(Op op) => op switch
        {
            Op.add_i4 or Op.add_i4_nn or Op.sub_i4 or Op.sub_i4_nn
            or Op.mul_i4 or Op.mul_i4_nn or Op.div_i4 or Op.div_i4_nn
            or Op.rem_i4 or Op.rem_i4_nn
            or Op.add_r4 or Op.add_r4_nn or Op.sub_r4 or Op.sub_r4_nn
            or Op.mul_r4 or Op.mul_r4_nn or Op.div_r4 or Op.div_r4_nn
            or Op.rem_r4 or Op.rem_r4_nn
            or Op.and_i4 or Op.and_i4_nn or Op.or_i4 or Op.or_i4_nn
            or Op.xor_i4 or Op.xor_i4_nn
            or Op.shl_i4 or Op.shl_i4_nn or Op.shr_i4 or Op.shr_i4_nn
            or Op.shr_un_i4 or Op.shr_un_i4_nn
            or Op.div_un_i4 or Op.div_un_i4_nn or Op.rem_un_i4 or Op.rem_un_i4_nn
            or Op.ceq_i4 or Op.ceq_i4_nn or Op.cgt_i4 or Op.cgt_i4_nn
            or Op.clt_i4 or Op.clt_i4_nn or Op.cgt_un_i4 or Op.cgt_un_i4_nn
            or Op.clt_un_i4 or Op.clt_un_i4_nn
            or Op.ceq_r4 or Op.ceq_r4_nn or Op.cgt_r4 or Op.cgt_r4_nn
            or Op.clt_r4 or Op.clt_r4_nn or Op.cgt_un_r4 or Op.clt_un_r4
            or Op.ceq_o or Op.cgt_un_o
            // _nk forms also encode as [op, dst, src, k] — 4 words. dst at words[1].
            or Op.add_i4_nk or Op.add_r4_nk or Op.sub_i4_nk or Op.sub_r4_nk
            or Op.mul_r4_nk
            or Op.clt_i4_nk or Op.clt_r4_nk or Op.cgt_i4_nk or Op.cgt_r4_nk
            or Op.ceq_i4_nk
            or Op.mul_i4_nk or Op.div_i4_nk or Op.rem_i4_nk
            or Op.and_i4_nk or Op.or_i4_nk or Op.xor_i4_nk
            or Op.shl_i4_nk or Op.shr_i4_nk or Op.shr_un_i4_nk
            or Op.div_r4_nk or Op.ceq_r4_nk => true,
            _ => false,
        };

        static int CallHostByrefWidthForCoalesce(uint[] ir, int ip)
        {
            if (ip + 4 >= ir.Length) return -1;
            int argc = (int)ir[ip + 4];
            int wbBase = ip + 5 + argc;
            if (wbBase >= ir.Length) return -1;
            int wbCount = (int)ir[wbBase];
            return (wbBase + 1 + wbCount * 4) - ip;
        }

        // Conservative slot read/write accumulation. Counts every value at a known
        // slot position. Tokens, immediates, and IP targets are skipped.
        static void AccumulateSlotUses(uint[] ir, int ip, Op op, int width,
            int[] reads, int[] writes, int slotCount)
        {
            void R(int s) { if ((uint)s < (uint)slotCount) reads[s]++; }
            void W(int s) { if ((uint)s < (uint)slotCount) writes[s]++; }

            if (Is4WordArithForCoalesce(op))
            {
                W((int)ir[ip + 1]); R((int)ir[ip + 2]);
                // _nk forms have an immediate at word 3; everything else has a 2nd slot.
                if (!IsNkForm(op)) R((int)ir[ip + 3]);
                return;
            }
            if (IsFusedCmpBranch(op))
            {
                R((int)ir[ip + 1]);
                if (!IsFusedCmpBranchNk(op)) R((int)ir[ip + 2]);
                // word 3 is target_ip, not a slot
                return;
            }
            if (op == Op.for_i4_nk)
            {
                // induction slot is read AND written; words 2 and 3 are limit + target.
                int s = (int)ir[ip + 1];
                R(s); W(s);
                return;
            }
            switch (op)
            {
                case Op.nop: case Op.ret_void: case Op.br:
                case Op.push_cont: case Op.br_cont: return;

                case Op.mov: case Op.box: case Op.mov_vt: case Op.box_vt: case Op.unbox_vt:
                case Op.clone_sc:
                case Op.ldlen:
                case Op.neg_i4: case Op.neg_i4_n: case Op.neg_r4: case Op.neg_r4_n:
                case Op.not_i4: case Op.not_i4_n:
                case Op.conv_i4_r4: case Op.conv_r4_i4:
                case Op.conv_i4_i1: case Op.conv_i4_u1:
                case Op.conv_i4_i2: case Op.conv_i4_u2:
                    W((int)ir[ip + 1]); R((int)ir[ip + 2]); return;

                case Op.arr_bounds_check: // reads arr + idx, defines nothing (side-effect: throw)
                    R((int)ir[ip + 1]); R((int)ir[ip + 2]); return;

                case Op.ldc_i4: case Op.ldc_r4: case Op.ldstr:
                case Op.ldsfld_i4: case Op.ldsfld_r4: case Op.ldsfld_o: case Op.ldsfld_struct:
                case Op.ldtoken:
                case Op.ldnull: case Op.initobj: case Op.initobj_script: case Op.ensure_script:
                    W((int)ir[ip + 1]); return;

                case Op.stsfld_i4: case Op.stsfld_r4: case Op.stsfld_o: case Op.stsfld_struct:
                    R((int)ir[ip + 2]); return;

                case Op.ret_i4: case Op.ret_r4: case Op.ret_o: case Op.ret_vt:
                case Op.throw_o:
                    R((int)ir[ip + 1]); return;

                case Op.brtrue_i4: case Op.brfalse_i4: case Op.brtrue_o: case Op.brfalse_o:
                    R((int)ir[ip + 1]); return;

                case Op.ldfld_i4: case Op.ldfld_r4: case Op.ldfld_o:
                case Op.ldfld_struct: case Op.ldflda:
                case Op.ldfld_vt_i4: case Op.ldfld_vt_r4: case Op.ldfld_vt_vt:
                case Op.ldfld_vt_u1: case Op.ldfld_vt_i1: case Op.ldfld_vt_u2: case Op.ldfld_vt_i2:
                case Op.ldfld_sc_i4: case Op.ldfld_sc_r4: case Op.ldfld_sc_o: case Op.ldfld_sc_vt:
                    W((int)ir[ip + 1]); R((int)ir[ip + 2]); return;

                case Op.stfld_i4: case Op.stfld_r4: case Op.stfld_o: case Op.stfld_struct:
                case Op.stfld_vt_i4: case Op.stfld_vt_r4: case Op.stfld_vt_vt:
                case Op.stfld_vt_b1: case Op.stfld_vt_b2:
                case Op.stfld_sc_i4: case Op.stfld_sc_r4: case Op.stfld_sc_o: case Op.stfld_sc_vt:
                    R((int)ir[ip + 1]); R((int)ir[ip + 3]); return;

                case Op.ldelem_i4: case Op.ldelem_r4: case Op.ldelem_o: case Op.ldelem_struct:
                case Op.ldelem_vt:
                    W((int)ir[ip + 1]); R((int)ir[ip + 2]); R((int)ir[ip + 3]); return;

                case Op.stelem_i4: case Op.stelem_r4: case Op.stelem_o: case Op.stelem_struct:
                case Op.stelem_vt:
                    R((int)ir[ip + 1]); R((int)ir[ip + 2]); R((int)ir[ip + 3]); return;

                case Op.castclass: case Op.unbox_any: case Op.isinst: case Op.box_prim:
                case Op.box_enum:
                    W((int)ir[ip + 1]); R((int)ir[ip + 2]); return;

                case Op.newarr: case Op.newarr_i4: case Op.newarr_r4: case Op.newarr_vt:
                    W((int)ir[ip + 1]); R((int)ir[ip + 2]); return;

                case Op.new_delegate:
                    W((int)ir[ip + 1]); R((int)ir[ip + 3]); return; // word 2 is a tokIdx

                case Op.call_script:
                case Op.call_script_virt:
                case Op.newobj_script:
                case Op.newobj_host:
                {
                    W((int)ir[ip + 1]);
                    int argc = (int)ir[ip + 3];
                    for (int k = 0; k < argc; k++) R((int)ir[ip + 4 + k]);
                    return;
                }

                case Op.call_host:
                {
                    W((int)ir[ip + 1]); R((int)ir[ip + 2]);
                    int argc = (int)ir[ip + 4];
                    for (int k = 0; k < argc; k++) R((int)ir[ip + 5 + k]);
                    return;
                }

                case Op.invoke_script_delegate:
                {
                    W((int)ir[ip + 1]); R((int)ir[ip + 3]); // dst, delegate receiver (word 2 is a tokIdx)
                    int argc = (int)ir[ip + 4];
                    for (int k = 0; k < argc; k++) R((int)ir[ip + 5 + k]);
                    return;
                }

                case Op.call_host_byref:
                {
                    W((int)ir[ip + 1]); R((int)ir[ip + 2]);
                    int argc = (int)ir[ip + 4];
                    for (int k = 0; k < argc; k++) R((int)ir[ip + 5 + k]);
                    int wbBase = ip + 5 + argc;
                    int wbCount = (int)ir[wbBase];
                    for (int k = 0; k < wbCount; k++)
                    {
                        int kind = (int)ir[wbBase + 1 + k * 4 + 1];
                        int t1   = (int)ir[wbBase + 1 + k * 4 + 2];
                        int t2   = (int)ir[wbBase + 1 + k * 4 + 3];
                        if (kind == 0)      { W(t1); }                    // frame slot writeback
                        else if (kind == 1) { R(t1); }                    // field on object
                        else                { R(t1); R(t2); }             // array[idx]
                    }
                    return;
                }

                case Op.switch_i4:
                    R((int)ir[ip + 1]); return;
            }
        }

        // --- Pre-scan helpers ---

        // Scan the IL bytes and collect all branch target offsets into a HashSet.
        // Used by LowerMethod to detect basic-block boundaries and clear ensuredScriptSlots.
        static HashSet<int> CollectIlBranchTargets(byte[] il)
        {
            var targets = new HashSet<int>();
            int ip = 0;
            while (ip < il.Length)
            {
                byte b = il[ip++];
                ILOpCode op = b == 0xFE ? (ILOpCode)(0xFE00 | il[ip++]) : (ILOpCode)b;
                switch (op)
                {
                    case ILOpCode.Br_s:
                    case ILOpCode.Brtrue_s: case ILOpCode.Brfalse_s:
                    case ILOpCode.Beq_s: case ILOpCode.Bne_un_s:
                    case ILOpCode.Blt_s: case ILOpCode.Blt_un_s:
                    case ILOpCode.Bgt_s: case ILOpCode.Bgt_un_s:
                    case ILOpCode.Ble_s: case ILOpCode.Ble_un_s:
                    case ILOpCode.Bge_s: case ILOpCode.Bge_un_s:
                    case ILOpCode.Leave_s:
                    {
                        int off = (sbyte)il[ip++];
                        targets.Add(ip + off);
                        break;
                    }
                    case ILOpCode.Br:
                    case ILOpCode.Brtrue: case ILOpCode.Brfalse:
                    case ILOpCode.Beq: case ILOpCode.Bne_un:
                    case ILOpCode.Blt: case ILOpCode.Blt_un:
                    case ILOpCode.Bgt: case ILOpCode.Bgt_un:
                    case ILOpCode.Ble: case ILOpCode.Ble_un:
                    case ILOpCode.Bge: case ILOpCode.Bge_un:
                    case ILOpCode.Leave:
                    {
                        int off = BitConverter.ToInt32(il, ip); ip += 4;
                        targets.Add(ip + off);
                        break;
                    }
                    case ILOpCode.Switch:
                    {
                        int n = BitConverter.ToInt32(il, ip); ip += 4;
                        int tableStart = ip;
                        ip += 4 * n;
                        for (int ci = 0; ci < n; ci++)
                        {
                            int off = BitConverter.ToInt32(il, tableStart + ci * 4);
                            targets.Add(ip + off);
                        }
                        break;
                    }
                    default:
                        ip += ILOperandSize(op, il, ip);
                        break;
                }
            }
            return targets;
        }

        // Handler entry points of every finally region a `leave` at leaveOffset must run before
        // reaching `target`: try region contains the leave but not the target. Innermost first
        // (smallest try region), matching the order the CLR runs them on the non-exceptional path.
        static List<int> FinallyChain(
            (int TryStart, int TryEnd, int HandlerStart, int HandlerEnd)[]? regions,
            int leaveOffset, int target)
        {
            var chain = new List<int>();
            if (regions == null) return chain;
            // Sort by try-region size so nesting order is explicit (metadata usually orders
            // inner clauses first, but don't rely on it).
            var hits = new List<(int size, int handlerStart)>();
            foreach (var r in regions)
            {
                if (r.TryStart <= leaveOffset && leaveOffset < r.TryEnd
                    && !(r.TryStart <= target && target < r.TryEnd))
                    hits.Add((r.TryEnd - r.TryStart, r.HandlerStart));
            }
            hits.Sort((a, b) => a.size.CompareTo(b.size));
            foreach (var h in hits) chain.Add(h.handlerStart);
            return chain;
        }

        // Forward-type-flow pre-pass classifying every local's SType before IR emission:
        // a simplified run of the main lowering pass that only tracks types (no IR output),
        // pre-populating slotTypes[] for args+locals so the main pass allocates correctly
        // from the start.
        /// <summary>Maps a metadata type token to the interpreter SType for a system PRIMITIVE
        /// (Int32/Boolean/Byte/…/Char → I4, Single/Double → R4); null for anything else (script
        /// types, reference types, unresolvable). Used by <c>initobj</c> to classify a target the
        /// local-type inference couldn't see a store to.</summary>
        // Box typecode for a primitive value type (else -1). Codes: 0=Int32 1=Boolean 2=Char
        // 3=Byte 4=SByte 5=Int16 6=UInt16 7=Single. Others (UInt32/Int64/Double/enum/struct/ref)
        // return -1 and keep box a no-op (their value-uses already round-trip correctly).
        static int BoxPrimTypeCode(ParsedAssembly asm, int token)
        {
            try
            {
                var h = MetadataTokens.EntityHandle(token);
                string ns, name;
                if (h.Kind == HandleKind.TypeReference)
                { var tr = asm.Reader.GetTypeReference((TypeReferenceHandle)h); ns = asm.Reader.GetString(tr.Namespace); name = asm.Reader.GetString(tr.Name); }
                else if (h.Kind == HandleKind.TypeDefinition)
                { var td = asm.Reader.GetTypeDefinition((TypeDefinitionHandle)h); ns = asm.Reader.GetString(td.Namespace); name = asm.Reader.GetString(td.Name); }
                else return -1;
                if (ns != "System") return -1;
                return name switch
                {
                    "Int32" => 0, "Boolean" => 1, "Char" => 2, "Byte" => 3,
                    "SByte" => 4, "Int16" => 5, "UInt16" => 6, "Single" => 7,
                    "UInt32" => 8, // uint is analyzer-legal; boxing as int flips big values negative
                    "Int64" => 9, "UInt64" => 10, "Double" => 11, // wide slots
                    _ => -1,
                };
            }
            catch { return -1; }
        }

        static SType? PrimitiveSTypeForTypeToken(ParsedAssembly asm, int token)
        {
            try
            {
                var h = MetadataTokens.EntityHandle(token);
                string ns, name;
                if (h.Kind == HandleKind.TypeReference)
                {
                    var tr = asm.Reader.GetTypeReference((TypeReferenceHandle)h);
                    ns = asm.Reader.GetString(tr.Namespace); name = asm.Reader.GetString(tr.Name);
                }
                else if (h.Kind == HandleKind.TypeDefinition)
                {
                    var td = asm.Reader.GetTypeDefinition((TypeDefinitionHandle)h);
                    ns = asm.Reader.GetString(td.Namespace); name = asm.Reader.GetString(td.Name);
                }
                else return null;
                if (ns != "System") return null;
                return name switch
                {
                    "Int32" or "Boolean" or "Byte" or "SByte" or "Int16" or "UInt16"
                        or "UInt32" or "Char" => SType.I4,
                    "Single" => SType.R4,
                    "Int64" or "UInt64" => SType.I8,
                    "Double" => SType.R8,
                    _ => null,
                };
            }
            catch { return null; }
        }

        static void PreClassifyLocals(byte[] il, ParsedMethod m, List<SType> slotTypes,
            int[] localFrameSlot, HostBinding.StructLayout?[]? localStrs, bool[] localDeclared)
        {
            // Simulated eval stack (just types, no slot indices)
            var stack = new SType[64];
            int sp = 0;
            var localTypes = new SType[m.LocalCount]; // O by default; Vt locals seeded below
            var argTypes   = new SType[m.ArgCount];   // O by default
            // Seed Vt locals so a later ldloc returns Vt instead of O.
            if (localStrs != null)
                for (int i = 0; i < m.LocalCount; i++)
                    if (localStrs[i] != null) localTypes[i] = SType.Vt;
            // Seed declared-typed locals from their frozen type so the abstract walk sees them
            // correctly and never re-infers them (PreStoreLocal skips them below).
            for (int i = 0; i < m.LocalCount; i++)
                if (localDeclared[i]) localTypes[i] = slotTypes[localFrameSlot[i]];

            int ip = 0;
            while (ip < il.Length)
            {
                byte b = il[ip++];
                ILOpCode op = b == 0xFE ? (ILOpCode)(0xFE00 | il[ip++]) : (ILOpCode)b;
                switch (op)
                {
                    // Constants
                    case ILOpCode.Ldc_i4_m1: case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1:
                    case ILOpCode.Ldc_i4_2:  case ILOpCode.Ldc_i4_3: case ILOpCode.Ldc_i4_4:
                    case ILOpCode.Ldc_i4_5:  case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                    case ILOpCode.Ldc_i4_8:  if(sp<64)stack[sp++]=SType.I4; break;
                    case ILOpCode.Ldc_i4_s:  ip++; if(sp<64)stack[sp++]=SType.I4; break;
                    case ILOpCode.Ldc_i4:    ip+=4; if(sp<64)stack[sp++]=SType.I4; break;
                    case ILOpCode.Ldc_r4:    ip+=4; if(sp<64)stack[sp++]=SType.R4; break;
                    case ILOpCode.Ldc_r8:    ip+=8; if(sp<64)stack[sp++]=SType.R8; break;
                    case ILOpCode.Ldc_i8:    ip+=8; if(sp<64)stack[sp++]=SType.I8; break;
                    case ILOpCode.Ldnull:    if(sp<64)stack[sp++]=SType.O; break;
                    case ILOpCode.Ldstr:     ip+=4; if(sp<64)stack[sp++]=SType.O; break;

                    // Locals
                    // Bounds-guarded like Ldloc_s below: valid IL never underflows, but this pass
                    // walks unknown opcodes by table — a size mistake there must degrade, not throw.
                    case ILOpCode.Ldloc_0: if(sp<64)stack[sp++]=m.LocalCount>0?localTypes[0]:SType.O; break;
                    case ILOpCode.Ldloc_1: if(sp<64)stack[sp++]=m.LocalCount>1?localTypes[1]:SType.O; break;
                    case ILOpCode.Ldloc_2: if(sp<64)stack[sp++]=m.LocalCount>2?localTypes[2]:SType.O; break;
                    case ILOpCode.Ldloc_3: if(sp<64)stack[sp++]=m.LocalCount>3?localTypes[3]:SType.O; break;
                    case ILOpCode.Ldloc_s: { int i=il[ip++]; if(sp<64)stack[sp++]=i<m.LocalCount?localTypes[i]:SType.O; break; }
                    case ILOpCode.Ldloc:   { int i=il[ip]|(il[ip+1]<<8); ip+=2; if(sp<64)stack[sp++]=i<m.LocalCount?localTypes[i]:SType.O; break; }
                    case ILOpCode.Stloc_0: PreStoreLocal(stack, ref sp, 0, m, localTypes, slotTypes, localFrameSlot, localStrs, localDeclared); break;
                    case ILOpCode.Stloc_1: PreStoreLocal(stack, ref sp, 1, m, localTypes, slotTypes, localFrameSlot, localStrs, localDeclared); break;
                    case ILOpCode.Stloc_2: PreStoreLocal(stack, ref sp, 2, m, localTypes, slotTypes, localFrameSlot, localStrs, localDeclared); break;
                    case ILOpCode.Stloc_3: PreStoreLocal(stack, ref sp, 3, m, localTypes, slotTypes, localFrameSlot, localStrs, localDeclared); break;
                    case ILOpCode.Stloc_s: { int i=il[ip++]; PreStoreLocal(stack, ref sp, i, m, localTypes, slotTypes, localFrameSlot, localStrs, localDeclared); break; }
                    case ILOpCode.Stloc:   { int i=il[ip]|(il[ip+1]<<8); ip+=2; PreStoreLocal(stack, ref sp, i, m, localTypes, slotTypes, localFrameSlot, localStrs, localDeclared); break; }

                    // Args
                    case ILOpCode.Ldarg_0: if(sp<64)stack[sp++]=argTypes.Length>0?argTypes[0]:SType.O; break;
                    case ILOpCode.Ldarg_1: if(sp<64)stack[sp++]=argTypes.Length>1?argTypes[1]:SType.O; break;
                    case ILOpCode.Ldarg_2: if(sp<64)stack[sp++]=argTypes.Length>2?argTypes[2]:SType.O; break;
                    case ILOpCode.Ldarg_3: if(sp<64)stack[sp++]=argTypes.Length>3?argTypes[3]:SType.O; break;
                    case ILOpCode.Ldarg_s: { ip++; if(sp<64)stack[sp++]=SType.O; break; }
                    case ILOpCode.Ldarg:   { ip+=2; if(sp<64)stack[sp++]=SType.O; break; }
                    case ILOpCode.Starg_s: ip++; if(sp>0)sp--; break;
                    case ILOpCode.Starg:   ip+=2; if(sp>0)sp--; break;

                    // Arithmetic (consume 2, push 1 of same type)
                    case ILOpCode.Add: case ILOpCode.Sub: case ILOpCode.Mul: case ILOpCode.Div: case ILOpCode.Rem:
                    {
                        if(sp>=2){SType t2=stack[--sp];SType t1=stack[sp-1];
                            stack[sp-1]=t1==SType.R8||t2==SType.R8?SType.R8
                                :t1==SType.R4||t2==SType.R4?SType.R4
                                :t1==SType.I8||t2==SType.I8?SType.I8:SType.I4;}
                        break;
                    }
                    case ILOpCode.Neg:
                        if(sp>0){SType t=stack[sp-1];stack[sp-1]=t is SType.R4 or SType.R8 or SType.I8?t:SType.I4;} break;
                    case ILOpCode.And: case ILOpCode.Or: case ILOpCode.Xor: case ILOpCode.Shl: case ILOpCode.Shr: case ILOpCode.Shr_un:
                        if(sp>=2){SType t2=stack[--sp];SType t1=stack[sp-1];stack[sp-1]=t1==SType.I8||t2==SType.I8?SType.I8:SType.I4;} break;
                    case ILOpCode.Not:
                        if(sp>0)stack[sp-1]=SType.I4; break;

                    // Comparisons → I4
                    case ILOpCode.Ceq: case ILOpCode.Cgt: case ILOpCode.Clt: case ILOpCode.Cgt_un: case ILOpCode.Clt_un:
                        if(sp>=2){sp--;stack[sp-1]=SType.I4;} break;

                    // Conversions
                    case ILOpCode.Conv_i4: case ILOpCode.Conv_u4:
                    case ILOpCode.Conv_i1: case ILOpCode.Conv_u1:
                    case ILOpCode.Conv_i2: case ILOpCode.Conv_u2:
                        if(sp>0)stack[sp-1]=SType.I4; break;
                    case ILOpCode.Conv_r4:
                        if(sp>0)stack[sp-1]=SType.R4; break;
                    case ILOpCode.Conv_r8: case ILOpCode.Conv_r_un:
                        if(sp>0)stack[sp-1]=SType.R8; break;
                    case ILOpCode.Conv_i8: case ILOpCode.Conv_u8:
                        if(sp>0)stack[sp-1]=SType.I8; break;

                    // Box, dup, pop
                    case ILOpCode.Box: ip+=4; break;
                    case ILOpCode.Dup: if(sp>0&&sp<64){stack[sp]=stack[sp-1];sp++;} break;
                    case ILOpCode.Pop: if(sp>0)sp--; break;

                    // Branches (pop 0, 1, or 2)
                    case ILOpCode.Br_s: ip+=1; sp=0; break;
                    case ILOpCode.Br: ip+=4; sp=0; break;
                    case ILOpCode.Leave_s: ip+=1; sp=0; break;
                    case ILOpCode.Leave: ip+=4; sp=0; break;
                    case ILOpCode.Endfinally: sp=0; break;
                    case ILOpCode.Brtrue_s: case ILOpCode.Brfalse_s: ip+=1; if(sp>0)sp--; break;
                    case ILOpCode.Brtrue: case ILOpCode.Brfalse: ip+=4; if(sp>0)sp--; break;
                    case ILOpCode.Beq_s: case ILOpCode.Bne_un_s: case ILOpCode.Blt_s: case ILOpCode.Bgt_s:
                    case ILOpCode.Ble_s: case ILOpCode.Bge_s: case ILOpCode.Blt_un_s: case ILOpCode.Bgt_un_s:
                    case ILOpCode.Ble_un_s: case ILOpCode.Bge_un_s: ip+=1; if(sp>=2)sp-=2; break;
                    case ILOpCode.Beq: case ILOpCode.Bne_un: case ILOpCode.Blt: case ILOpCode.Bgt:
                    case ILOpCode.Ble: case ILOpCode.Bge: case ILOpCode.Blt_un: case ILOpCode.Bgt_un:
                    case ILOpCode.Ble_un: case ILOpCode.Bge_un: ip+=4; if(sp>=2)sp-=2; break;

                    // Everything else: skip operands, reset stack conservatively (don't corrupt)
                    default:
                    {
                        int sz = ILOperandSize(op, il, ip);
                        ip += sz;
                        // For calls, field access, etc. — conservatively push/pop O values
                        // We don't need perfect accuracy here; only stloc tracking matters.
                        if (op == ILOpCode.Call || op == ILOpCode.Callvirt || op == ILOpCode.Newobj)
                        {
                            // Just clear the stack to be safe — we can't know arg counts without parsing sigs
                            sp = 0;
                        }
                        else if (op == ILOpCode.Ldfld || op == ILOpCode.Ldflda || op == ILOpCode.Ldsfld)
                        {
                            if(sp>0)sp--;
                            if(sp<64)stack[sp++]=SType.O;
                        }
                        else if (op == ILOpCode.Stfld)
                        {
                            if(sp>=2)sp-=2;
                        }
                        else if (op == ILOpCode.Stsfld)
                        {
                            if(sp>0)sp--;
                        }
                        else if (op == ILOpCode.Ldlen || op == ILOpCode.Newarr || op == ILOpCode.Ldtoken)
                        {
                            if(sp>0)sp--;
                            if(sp<64)stack[sp++]=SType.O;
                        }
                        break;
                    }
                }
            }
        }

        // On conflicting stores, keep the most recent type — in practice well-typed C#
        // stores only one type into each local.
        static void UpdateSlotType(List<SType> slotTypes, int slot, SType t, string context = "")
        {
            if (slot < slotTypes.Count && slotTypes[slot] != SType.Vt) // never downgrade a Vt slot
                slotTypes[slot] = t;
        }

        // Stloc handler for the pre-classify pass: writes localTypes[i] = src type, propagates to
        // slotTypes at the local's frame slot. Vt locals are seeded ahead of time and never overwritten.
        static void PreStoreLocal(SType[] stack, ref int sp, int i, ParsedMethod m,
            SType[] localTypes, List<SType> slotTypes, int[] localFrameSlot, HostBinding.StructLayout?[]? localStrs,
            bool[] localDeclared)
        {
            if (sp == 0 || i >= m.LocalCount) { if (sp > 0) sp--; return; }
            SType t = stack[--sp];
            if (i < localDeclared.Length && localDeclared[i]) return; // declared type is frozen — never infer
            if (localStrs != null && localStrs[i] != null) return; // Vt local — keep type
            // A Vt VALUE flowing into a local with no struct layout (e.g. `object o = someStruct;`)
            // BOXES at the store — the local is O. Marking it Vt would make StoreLocal alias the
            // struct bytes (mov_vt with a null layout) and lose box-snapshot semantics.
            if (t == SType.Vt) t = SType.O;
            localTypes[i] = t;
            UpdateSlotType(slotTypes, localFrameSlot[i], t);
        }

        // Returns the operand size in bytes for an IL opcode at position ip (for skipping in pre-scan).
        static int ILOperandSize(ILOpCode op, byte[] il, int ip)
        {
            switch (op)
            {
                case ILOpCode.Ldarg_s: case ILOpCode.Ldloc_s: case ILOpCode.Stloc_s:
                case ILOpCode.Ldarga_s: case ILOpCode.Ldloca_s: case ILOpCode.Starg_s:
                case ILOpCode.Ldc_i4_s: case ILOpCode.Brfalse_s: case ILOpCode.Brtrue_s:
                case ILOpCode.Br_s: case ILOpCode.Beq_s: case ILOpCode.Bge_s: case ILOpCode.Bgt_s:
                case ILOpCode.Ble_s: case ILOpCode.Blt_s: case ILOpCode.Bne_un_s:
                case ILOpCode.Bge_un_s: case ILOpCode.Bgt_un_s: case ILOpCode.Ble_un_s:
                case ILOpCode.Blt_un_s: case ILOpCode.Leave_s:
                case (ILOpCode)0xFE12: // Unaligned prefix (banned opcode)
                    return 1;
                case ILOpCode.Ldarg: case ILOpCode.Ldloc: case ILOpCode.Stloc: case ILOpCode.Ldarga:
                case ILOpCode.Ldloca: case ILOpCode.Starg:
                    return 2;
                case ILOpCode.Ldc_i4: case ILOpCode.Ldc_r4: case ILOpCode.Ldstr: case ILOpCode.Newobj:
                case ILOpCode.Ldfld: case ILOpCode.Ldflda: case ILOpCode.Stfld: case ILOpCode.Ldsfld:
                case ILOpCode.Stsfld: case ILOpCode.Leave: case ILOpCode.Constrained:
                case ILOpCode.Call: case ILOpCode.Callvirt: case ILOpCode.Newarr:
                case ILOpCode.Ldelem: case ILOpCode.Stelem: case ILOpCode.Ldelema: case ILOpCode.Unbox_any:
                case ILOpCode.Box: case ILOpCode.Castclass: case ILOpCode.Isinst: case ILOpCode.Ldtoken:
                case ILOpCode.Br: case ILOpCode.Beq: case ILOpCode.Bge: case ILOpCode.Bgt:
                case ILOpCode.Ble: case ILOpCode.Blt: case ILOpCode.Bne_un: case ILOpCode.Bge_un:
                case ILOpCode.Bgt_un: case ILOpCode.Ble_un: case ILOpCode.Blt_un:
                case ILOpCode.Brfalse: case ILOpCode.Brtrue: case ILOpCode.Initobj:
                // Token-operand opcodes the lowerer rejects later — the size must still be right
                // here, or the walk reads the token bytes as opcodes and desyncs (a misread
                // ldloc.N then indexes past the locals array — the ldftn/delegate-creation crash).
                case ILOpCode.Ldftn: case ILOpCode.Ldvirtftn: case ILOpCode.Ldsflda:
                case ILOpCode.Jmp: case ILOpCode.Calli: case ILOpCode.Cpobj: case ILOpCode.Ldobj:
                case ILOpCode.Stobj: case ILOpCode.Unbox: case ILOpCode.Refanyval:
                case ILOpCode.Mkrefany: case ILOpCode.Sizeof:
                    return 4;
                case ILOpCode.Ldc_r8: case (ILOpCode)0x21: // 0x21=Ldc_i8 (banned, 8-byte operand)
                    return 8;
                case ILOpCode.Switch:
                    if (ip < il.Length) { int n = BitConverter.ToInt32(il, ip); return 4 + n * 4; }
                    return 0;
                default:
                    return 0; // no operand
            }
        }

        // --- Slot allocation helpers ---

        // AllocSlot ALSO grows slotStructs (with null) in lockstep so the two lists stay aligned.
        // For Vt slots, use AllocStructSlot which reserves ceil(size/4) consecutive slots.
        static int AllocSlot(ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, SType t, HostBinding.StructLayout? lay)
        {
            int s = frameSize++;
            slotTypes.Add(t);
            slotStructs.Add(lay);
            if (t is SType.I8 or SType.R8)
            {
                // Wide value: reserve the continuation cell, filler-typed like Vt continuation
                // cells so nothing else is allocated over the value's second half.
                slotTypes.Add(SType.O);
                slotStructs.Add(null);
                frameSize++;
            }
            return s;
        }

        static int AllocStructSlot(ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, HostBinding.StructLayout lay)
        {
            int s = frameSize++;
            slotTypes.Add(SType.Vt);
            slotStructs.Add(lay);
            int filler = (lay.Size + 3) / 4 - 1;
            for (int k = 0; k < filler; k++)
            {
                slotTypes.Add(SType.O);
                slotStructs.Add(null);
                frameSize++;
            }
            return s;
        }

        // Converge the value currently on top of the eval stack into a single "merge slot" for a forward
        // join, so every forward edge into `target` leaves its result in the same frame slot. Used by the
        // ternary/short-circuit `br` path and by conditional branches that carry a value to a forward join
        // — e.g. `a ?? b`, whose `dup; brtrue L` leaves the left operand on the stack on the taken edge.
        // Emits the mov (unconditionally, before the branch) and repoints the eval-stack top at the merge
        // slot; the join reconciles the other edge's value into the same slot via mergeSlotForEnd.
        static void MergeTopIntoJoinSlot(
            List<uint> ir, List<int> irToIl, (int slot, SType type)[] evalStack, int sp,
            ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs,
            Dictionary<int, (int slot, SType type)> mergeSlotForEnd, int target, int instrStart)
        {
            var (firstSlot, firstType) = evalStack[sp - 1];
            int mSlot;
            if (mergeSlotForEnd.TryGetValue(target, out var existing))
                mSlot = existing.slot; // multiple edges merging into one join
            else if (firstType == SType.Vt)
            {
                mSlot = AllocStructSlot(ref frameSize, slotTypes, slotStructs, slotStructs[firstSlot]!);
                mergeSlotForEnd[target] = (mSlot, firstType);
            }
            else
            {
                mSlot = AllocSlot(ref frameSize, slotTypes, slotStructs, firstType, null);
                mergeSlotForEnd[target] = (mSlot, firstType);
            }
            if (mSlot != firstSlot)
                Emit3(ir, irToIl, firstType == SType.Vt ? Op.mov_vt : Op.mov, mSlot, firstSlot, instrStart);
            evalStack[sp - 1] = (mSlot, firstType);
        }

        // Emit call_host_byref. Each byref arg is replaced with a fresh boxed O slot pre-loaded
        // with the target's current value (so MethodInfo.Invoke can write through it), then a
        // writeback table is appended so the executor copies the post-call value back into
        // the original frame slot / field / array element.
        static void EmitCallHostByref(List<uint> ir, List<int> irToIl, (int slot, SType type)[] evalStack,
            ref int sp, ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs,
            int[] addrTagBySlot, int[] addrFldObjBySlot, int[] addrFldTokBySlot, int[] addrByteOffBySlot,
            ParsedAssembly asm, List<int> tokens, HostEntry hostEntry, int tokIdx,
            ParameterInfo[] ps, int instrStart)
        {
            int argc = hostEntry.Binding.ParamCount;
            bool isVoid = hostEntry.IsVoid;
            // Type the result slot like the regular call_host dst-picker: from the resolved
            // MethodInfo's return type, falling back to the MemberRef signature for string-keyed
            // shims. A blanket O dst left bool/int results boxed in the ref frame, and a boxed
            // false condition was then null-tested TRUE under brtrue_o's pure reference semantics
            // (found by fuzzing: TryHalve-shaped bool-returning byref calls). Vt returns keep O.
            SType retSt = SType.O;
            if (!isVoid && (hostEntry.ResolvedMethod ?? hostEntry.Binding.Method) is MethodInfo bmi)
            {
                var brt = bmi.ReturnType;
                if (brt == typeof(float))                                                    retSt = SType.R4;
                else if (brt == typeof(int) || brt == typeof(bool) || brt == typeof(byte)
                         || brt == typeof(sbyte) || brt == typeof(short) || brt == typeof(ushort)) retSt = SType.I4;
            }
            else if (!isVoid && hostEntry.SigRetSType is SType.I4 or SType.R4)
                retSt = hostEntry.SigRetSType;
            int dst = isVoid ? -1 : AllocSlot(ref frameSize, slotTypes, slotStructs, retSt, null);

            // Receiver — same address-resolution rules as the regular call_host path.
            int recvSlotRaw = hostEntry.Binding.HasThis ? evalStack[sp - argc - 1].slot : -1;
            int recvSlot = recvSlotRaw;
            int recvVtOffB = 0; bool recvVtWbB = false; int recvVtWbBaseB = -1;
            if (recvSlotRaw >= 0)
            {
                int addrTag = addrTagBySlot[recvSlotRaw];
                if (addrTag >= 0) { recvSlot = addrTag; recvVtOffB = addrByteOffBySlot[recvSlotRaw]; }
                else if (addrTag == -1)
                {
                    int fldObj = addrFldObjBySlot[recvSlotRaw];
                    int fldToki = addrFldTokBySlot[recvSlotRaw];
                    int fldTok = tokens[fldToki];
                    asm.FieldSTypes.TryGetValue(fldTok, out var fldSt);
                    bool isScFieldB = asm.FieldSlots.TryGetValue(fldTok, out var fldFsB);
                    // Vt field receiver: struct slot + byte-range load (see the call_host site).
                    var fldVtLayB = isScFieldB && fldSt == SType.Vt
                        ? fldFsB.Item1.VtFieldLayouts?[fldFsB.Item2] : null;
                    recvSlot = fldVtLayB != null
                        ? AllocStructSlot(ref frameSize, slotTypes, slotStructs, fldVtLayB)
                        : AllocSlot(ref frameSize, slotTypes, slotStructs, fldSt, null);
                    if (isScFieldB)
                    {
                        int fldOffB = fldFsB.Item1.FieldOffsets[fldFsB.Item2];
                        var fldOpB = fldVtLayB != null ? Op.ldfld_sc_vt
                            : fldSt == SType.I4 ? Op.ldfld_sc_i4 : fldSt == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                        Emit4(ir, irToIl, fldOpB, recvSlot, fldObj, fldOffB, instrStart);
                    }
                    else
                    {
                        var fldLoadOp = fldSt == SType.I4 ? Op.ldfld_i4 : fldSt == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                        Emit4(ir, irToIl, fldLoadOp, recvSlot, fldObj, fldToki, instrStart);
                    }
                }
            }

            // Nested-offset Vt receiver — same materialize + write-back as the call_host path.
            if (hostEntry.Binding.HasThis && recvSlot >= 0 && recvVtOffB != 0
                && recvSlot < slotTypes.Count && slotTypes[recvSlot] == SType.Vt
                && hostEntry.ReceiverStruct != null)
            {
                int rTmpB = AllocStructSlot(ref frameSize, slotTypes, slotStructs, hostEntry.ReceiverStruct);
                Emit4(ir, irToIl, Op.ldfld_vt_vt, rTmpB, recvSlot, recvVtOffB, instrStart);
                recvVtWbB = true; recvVtWbBaseB = recvSlot;
                recvSlot = rTmpB;
            }

            int argBase = sp - argc;
            int spBase = sp - argc - (hostEntry.Binding.HasThis ? 1 : 0);

            // Walk byref args: replace each phantom-addr arg slot with a fresh O slot pre-loaded
            // with the current value at the address. Record the writeback target in parallel arrays.
            // Max 8 byref args is plenty (Physics.Raycast is the worst common case at ~3).
            Span<int>  wbArgIdx = stackalloc int[8];
            Span<int>  wbKind   = stackalloc int[8];
            Span<int>  wbT1     = stackalloc int[8];
            Span<int>  wbT2     = stackalloc int[8];
            int wbCount = 0;
            for (int k = 0; k < argc; k++)
            {
                if (k >= ps.Length || !ps[k].ParameterType.IsByRef) continue;
                int origSlot = evalStack[argBase + k].slot;
                int tag = addrTagBySlot[origSlot];
                int boxed = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                if (tag >= 0)
                {
                    // Pre-load current value (may be irrelevant for `out`). A flat (Vt) source
                    // lives in the numeric frame — box it; a plain mov would read the wrong frame.
                    Emit3(ir, irToIl, slotTypes[tag] == SType.Vt ? Op.box_vt : Op.mov, boxed, tag, instrStart);
                    if (wbCount < 8)
                    { wbArgIdx[wbCount] = k; wbKind[wbCount] = 0; wbT1[wbCount] = tag; wbT2[wbCount] = 0; wbCount++; }
                }
                else if (tag == -1)
                {
                    int fldObj = addrFldObjBySlot[origSlot];
                    int fldToki = addrFldTokBySlot[origSlot];
                    int fldTok = tokens[fldToki];
                    asm.FieldSTypes.TryGetValue(fldTok, out var fldSt);
                    bool isScFieldC = asm.FieldSlots.TryGetValue(fldTok, out var fldFsC);
                    var fldVtLayC = isScFieldC && fldSt == SType.Vt
                        ? fldFsC.Item1.VtFieldLayouts?[fldFsC.Item2] : null;
                    // Pre-load into a slot tagged with the FIELD's type, not O: the field-load ops
                    // write I4/R4 values into the numeric frame, so an O-tagged slot reads back
                    // null and the host call sees 0 (found by fuzzing: `h.Accumulate(ref acc)` on
                    // a hoisted iterator field passed 0 in — the writeback masked the wrong input).
                    // RdObj boxes an I4/R4-tagged slot correctly at call time, like the receiver
                    // resolution above. Vt and O fields keep the O tag — their pre-load lands a
                    // boxed object in the ref frame.
                    if (fldSt == SType.I4 || fldSt == SType.R4)
                        slotTypes[boxed] = fldSt;
                    if (fldVtLayC != null)
                    {
                        // Flat (Vt) script field — e.g. a Vector3 hoisted into an iterator state
                        // machine because an out-arg takes its address: materialize the bytes into
                        // a struct slot, then box for the reflective call. The op picker below
                        // would emit ldfld_sc_o, indexing RefSlots with a PrimBytes byte offset.
                        int tmpVtC = AllocStructSlot(ref frameSize, slotTypes, slotStructs, fldVtLayC);
                        Emit4(ir, irToIl, Op.ldfld_sc_vt, tmpVtC, fldObj, fldFsC.Item1.FieldOffsets[fldFsC.Item2], instrStart);
                        Emit3(ir, irToIl, Op.box_vt, boxed, tmpVtC, instrStart);
                    }
                    else if (isScFieldC)
                    {
                        int fldOffC = fldFsC.Item1.FieldOffsets[fldFsC.Item2];
                        var fldOpC = fldSt == SType.I4 ? Op.ldfld_sc_i4 : fldSt == SType.R4 ? Op.ldfld_sc_r4 : Op.ldfld_sc_o;
                        Emit4(ir, irToIl, fldOpC, boxed, fldObj, fldOffC, instrStart);
                    }
                    else
                    {
                        var fldLoadOp = fldSt == SType.I4 ? Op.ldfld_i4 : fldSt == SType.R4 ? Op.ldfld_r4 : Op.ldfld_o;
                        Emit4(ir, irToIl, fldLoadOp, boxed, fldObj, fldToki, instrStart);
                    }
                    if (wbCount < 8)
                    { wbArgIdx[wbCount] = k; wbKind[wbCount] = 1; wbT1[wbCount] = fldObj; wbT2[wbCount] = fldToki; wbCount++; }
                }
                else if (tag == -3)
                {
                    int arrSlot = addrFldObjBySlot[origSlot];
                    int idxSlot = addrFldTokBySlot[origSlot];
                    Emit4(ir, irToIl, Op.ldelem_o, boxed, arrSlot, idxSlot, instrStart);
                    if (wbCount < 8)
                    { wbArgIdx[wbCount] = k; wbKind[wbCount] = 2; wbT1[wbCount] = arrSlot; wbT2[wbCount] = idxSlot; wbCount++; }
                }
                else
                {
                    // Non-phantom passed by ref — read whatever's there but no writeback target.
                    Emit3(ir, irToIl, slotTypes[origSlot] == SType.Vt ? Op.box_vt : Op.mov, boxed, origSlot, instrStart);
                }
                evalStack[argBase + k] = (boxed, SType.O);
            }

            // Box any leftover Vt args (slow path doesn't read flat).
            for (int k = 0; k < argc; k++)
            {
                if (evalStack[argBase + k].type == SType.Vt)
                {
                    int boxed = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                    Emit3(ir, irToIl, Op.box_vt, boxed, evalStack[argBase + k].slot, instrStart);
                    evalStack[argBase + k] = (boxed, SType.O);
                }
            }

            ir.Add((uint)Op.call_host_byref); irToIl.Add(instrStart);
            ir.Add((uint)(isVoid ? -1 : dst)); irToIl.Add(instrStart);
            ir.Add((uint)(hostEntry.Binding.HasThis ? recvSlot : -1)); irToIl.Add(instrStart);
            ir.Add((uint)tokIdx); irToIl.Add(instrStart);
            ir.Add((uint)argc); irToIl.Add(instrStart);
            for (int k = 0; k < argc; k++)
            { ir.Add((uint)evalStack[argBase + k].slot); irToIl.Add(instrStart); }
            ir.Add((uint)wbCount); irToIl.Add(instrStart);
            for (int k = 0; k < wbCount; k++)
            {
                ir.Add((uint)wbArgIdx[k]); irToIl.Add(instrStart);
                ir.Add((uint)wbKind[k]);   irToIl.Add(instrStart);
                ir.Add((uint)wbT1[k]);     irToIl.Add(instrStart);
                ir.Add((uint)wbT2[k]);     irToIl.Add(instrStart);
            }
            if (recvVtWbB)
                Emit4(ir, irToIl, Op.stfld_vt_vt, recvVtWbBaseB, recvVtOffB, recvSlot, instrStart);
            sp = spBase;
            if (!isVoid) evalStack[sp++] = (dst, retSt);
        }

        // Operator inlining: synthesize per-field arithmetic IR for a pure host-struct operator
        // or constructor. The args on evalStack at [argBase..argBase+argc) are either Vt slots
        // (for struct args) or scalar slots (for float/int args). The result goes into dst (a Vt slot).
        //
        // Strategy: iterate the return struct's fields in byte-offset order, match each to the
        // corresponding field in one of the Vt args, and emit a scalar arithmetic op per field.
        // For scalar args (no struct layout), use the slot directly for all fields.
        // Supported shapes: Vt op Vt, Vt op Scalar, Scalar op Vt, Scalar Scalar Scalar (ctor).
        // Cheap pre-check for EmitInlinedOp, evaluated BEFORE the Vt-arg boxing decision: once
        // the lowerer skips boundary boxing it is committed to inlining, so every precondition
        // must hold up front (a mid-emission bail-out would leave partial IR behind).
        static bool CanInlineOp(HostEntry hostEntry, int argc, HostBinding.StructLayout retLayout, bool isCtor)
        {
            if (retLayout.Fields.Count == 0) return false;
            if (isCtor) return argc == retLayout.Fields.Count;
            var n = hostEntry.Binding.Method?.Name;
            if (n != "op_Addition" && n != "op_Subtraction" && n != "op_Multiply"
                && n != "op_Division" && n != "op_UnaryNegation") return false;
            return n == "op_UnaryNegation" ? argc == 1 : argc == 2;
        }

        // Synthesizes per-field IR for a VERIFIED component-wise operator or field-order ctor
        // (see HostBinding.InspectAndMarkOperators — marking guarantees the IL is exactly the
        // per-field pattern, so this name-driven emission is semantically equivalent).
        static void EmitInlinedOp(List<uint> ir, List<int> irToIl, (int slot, SType type)[] evalStack,
            int argBase, int argc, int dst, HostBinding.StructLayout retLayout,
            HostEntry hostEntry, int instrStart, bool isCtor)
        {
            // Build sorted field list: (byteOffset, SType) sorted by byte offset.
            var fields = new (int Off, SType St)[retLayout.Fields.Count];
            int fi = 0;
            foreach (var kv in retLayout.Fields)
                fields[fi++] = (kv.Value.Offset, kv.Value.St);
            // Sort by offset (Fields dict may be unordered)
            for (int a = 0; a < fields.Length - 1; a++)
                for (int b = a + 1; b < fields.Length; b++)
                    if (fields[b].Off < fields[a].Off) { var tmp = fields[a]; fields[a] = fields[b]; fields[b] = tmp; }

            if (isCtor)
            {
                // Verified field-order ctor: param i is exactly field i's value (arity checked
                // by CanInlineOp before any IR was committed).
                for (int f = 0; f < fields.Length; f++)
                {
                    var stF = fields[f].St == SType.R4 ? Op.stfld_vt_r4 : Op.stfld_vt_i4;
                    ir.Add((uint)stF); irToIl.Add(instrStart);
                    ir.Add((uint)dst); irToIl.Add(instrStart);
                    ir.Add((uint)fields[f].Off); irToIl.Add(instrStart);
                    ir.Add((uint)evalStack[argBase + f].slot); irToIl.Add(instrStart);
                }
                return;
            }

            // For each result field, emit one arithmetic instruction. Struct-vs-scalar per arg is
            // decided from the CALL SITE's slot types, not the entry's ArgStructs — aliased
            // operator pairs ((V3,float) + (float,V3)) share one Entry, so the entry's signature
            // metadata may describe the other overload.
            for (int f = 0; f < fields.Length; f++)
            {
                int dstSubSlot = dst + fields[f].Off / 4;
                var argSubs = new int[argc];
                for (int k = 0; k < argc; k++)
                {
                    int argSlot = evalStack[argBase + k].slot;
                    argSubs[k] = evalStack[argBase + k].type == SType.Vt
                        ? argSlot + fields[f].Off / 4   // struct arg: same-offset component
                        : argSlot;                      // scalar arg: same slot for every field
                }

                string mName = hostEntry.Binding.Method?.Name ?? "";
                Op fieldOp;
                if (mName == "op_Addition")           fieldOp = fields[f].St == SType.R4 ? Op.add_r4_nn : Op.add_i4_nn;
                else if (mName == "op_Subtraction")   fieldOp = fields[f].St == SType.R4 ? Op.sub_r4_nn : Op.sub_i4_nn;
                else if (mName == "op_Multiply")      fieldOp = fields[f].St == SType.R4 ? Op.mul_r4_nn : Op.mul_i4_nn;
                else if (mName == "op_Division")      fieldOp = fields[f].St == SType.R4 ? Op.div_r4_nn : Op.div_i4_nn;
                else                                  fieldOp = fields[f].St == SType.R4 ? Op.neg_r4_n : Op.neg_i4_n; // op_UnaryNegation (CanInlineOp vetted the name)

                if (argc == 1)
                {
                    ir.Add((uint)fieldOp); irToIl.Add(instrStart);
                    ir.Add((uint)dstSubSlot); irToIl.Add(instrStart);
                    ir.Add((uint)argSubs[0]); irToIl.Add(instrStart);
                }
                else
                {
                    ir.Add((uint)fieldOp); irToIl.Add(instrStart);
                    ir.Add((uint)dstSubSlot); irToIl.Add(instrStart);
                    ir.Add((uint)argSubs[0]); irToIl.Add(instrStart);
                    ir.Add((uint)argSubs[1]); irToIl.Add(instrStart);
                }
            }
        }

        // Value-load a local, cloning script-defined structs so the pushed value is an independent
        // copy (struct copy semantics). Address loads (ldloca) never come through here, so in-place
        // field mutation is unaffected.
        static void PushLocal(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp,
            ref int frameSize, int localIdx, int[] localFrameSlot, SType[] localTypes,
            List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs,
            bool[]? localIsScStruct, int instrStart)
        {
            int srcSlot = localFrameSlot[localIdx];
            SType t = localTypes[localIdx];
            if (t == SType.O && localIsScStruct != null && localIdx < localIsScStruct.Length && localIsScStruct[localIdx])
            {
                int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                Emit3(ir, irToIl, Op.clone_sc, dst, srcSlot, instrStart);
                stack[sp++] = (dst, SType.O);
            }
            else
            {
                stack[sp++] = (srcSlot, t);
            }
        }

        // Any pending eval-stack entry that ALIASES the slot about to be stored must be
        // materialized into a temp first: PushLocal/PushArg push the local's own frame slot, so
        // `(a, b) = (b, a)` — `ldloc b; ldloc a; stloc b; stloc a` — left the second-from-top
        // entry pointing AT b's slot when b was overwritten, and a came back as the new b
        // (found by fuzzing). Address phantoms don't match here (their entry is a fresh phantom
        // slot), and a store through an address is genuine aliasing anyway.
        static void ProtectStackAliases(List<uint> ir, List<int> irToIl, (int, SType)[] stack, int sp,
            ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs,
            int dst, int instrStart)
        {
            for (int i = 0; i < sp; i++)
            {
                if (stack[i].Item1 != dst) continue;
                var t = stack[i].Item2;
                if (t == SType.Vt && dst < slotStructs.Count && slotStructs[dst] != null)
                {
                    int tmp = AllocStructSlot(ref frameSize, slotTypes, slotStructs, slotStructs[dst]!);
                    Emit3(ir, irToIl, Op.mov_vt, tmp, dst, instrStart);
                    stack[i] = (tmp, t);
                }
                else
                {
                    int tmp = AllocSlot(ref frameSize, slotTypes, slotStructs, dst < slotTypes.Count ? slotTypes[dst] : t, null);
                    Emit3(ir, irToIl, Op.mov, tmp, dst, instrStart);
                    stack[i] = (tmp, t);
                }
            }
        }

        static void StoreLocal(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp, int localIdx, int[] localFrameSlot, SType[] localTypes, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, bool[] localDeclared, ref int frameSize, int instrStart)
        {
            var (src, t) = stack[--sp];
            int dst = localFrameSlot[localIdx];
            ProtectStackAliases(ir, irToIl, stack, sp, ref frameSize, slotTypes, slotStructs, dst, instrStart);
            // Vt local: byte-copy struct bytes from src into the local slot. Requires the dst
            // slot to actually carry a layout — a Vt-typed slot without one must not alias.
            if (slotTypes[dst] == SType.Vt && t == SType.Vt && slotStructs[dst] != null)
            {
                Emit3(ir, irToIl, Op.mov_vt, dst, src, instrStart);
                return;
            }
            // Vt local being stored from O slot: unbox + flat-write.
            if (slotTypes[dst] == SType.Vt && t == SType.O)
            {
                Emit3(ir, irToIl, Op.unbox_vt, dst, src, instrStart);
                return;
            }
            // Vt source going into a non-Vt local (struct-returning ops produce Vt slots even
            // when the local stays O because no ldloca was taken on it). Box the Vt bytes
            // into a refFrame entry so the O local sees a normal boxed reference.
            if (slotTypes[dst] != SType.Vt && t == SType.Vt)
            {
                localTypes[localIdx] = SType.O;
                if (dst < slotTypes.Count) slotTypes[dst] = SType.O;
                Emit3(ir, irToIl, Op.box_vt, dst, src, instrStart);
                return;
            }
            if (localIdx < localDeclared.Length && localDeclared[localIdx])
            {
                // Declared type is FROZEN — never retype the slot. Op.mov routes by the (fixed) dst
                // slot type at runtime, boxing/unboxing the source to match, so an int stored into a
                // declared `object` local boxes and a boxed value read back unboxes, consistently.
                localTypes[localIdx] = slotTypes[dst];
                Emit3(ir, irToIl, Op.mov, dst, src, instrStart);
                return;
            }
            localTypes[localIdx] = t; // remember what type this local holds (non-Vt locals only)
            if (dst < slotTypes.Count) slotTypes[dst] = t;
            Emit3(ir, irToIl, Op.mov, dst, src, instrStart);
        }

        // Value-load an argument. A boxed script-struct parameter (O ScriptObject) is cloned like
        // PushLocal does for locals, so forwarding it (`Mut(s)`, `f(n, s)`) or storing it hands out
        // an independent copy — the callee's writes to its by-value parameter must not reach the
        // caller's argument. `this` and byref parameters are never in argIsBoxedSc.
        static void PushArg(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp,
            ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs,
            bool[] argIsBoxedSc, int instrStart, int argIdx, SType[] argTypes, int[] argFrameSlot)
        {
            if (argIsBoxedSc[argIdx] && argTypes[argIdx] == SType.O)
            {
                int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.O, null);
                Emit3(ir, irToIl, Op.clone_sc, dst, argFrameSlot[argIdx], instrStart);
                stack[sp++] = (dst, SType.O);
                return;
            }
            stack[sp++] = (argFrameSlot[argIdx], argTypes[argIdx]);
        }

        static void StoreArg(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp,
            int argIdx, SType[] argTypes, int[] argFrameSlot, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, ref int frameSize, int instrStart)
        {
            var (src, t) = stack[--sp];
            int dst = argFrameSlot[argIdx];
            ProtectStackAliases(ir, irToIl, stack, sp, ref frameSize, slotTypes, slotStructs, dst, instrStart);
            // Vt arg slots have a FIXED type (the frame layout is part of the call ABI): copy
            // struct bytes in (or unbox a boxed ScriptObject); never retype the slot.
            if (slotTypes[dst] == SType.Vt)
            {
                Emit3(ir, irToIl, t == SType.Vt ? Op.mov_vt : Op.unbox_vt, dst, src, instrStart);
                return;
            }
            argTypes[argIdx] = t;
            Emit3(ir, irToIl, Op.mov, dst, src, instrStart);
        }

        static void PushConst(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp, ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, int imm, SType t, Op ldc, int instrStart)
        {
            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, t, null);
            stack[sp++] = (dst, t);
            Emit3(ir, irToIl, ldc, dst, imm, instrStart);
        }

        // Map an i4-family op to its wide sibling. Throws for combinations IL can't
        // produce or the subset doesn't support — the LowerAll catch-all keeps it loud.
        static Op Wide64(Op i4Op, bool isR8) => (i4Op, isR8) switch
        {
            (Op.add_i4, false) => Op.add_i8, (Op.add_i4, true) => Op.add_r8,
            (Op.sub_i4, false) => Op.sub_i8, (Op.sub_i4, true) => Op.sub_r8,
            (Op.mul_i4, false) => Op.mul_i8, (Op.mul_i4, true) => Op.mul_r8,
            (Op.div_i4, false) => Op.div_i8, (Op.div_i4, true) => Op.div_r8,
            (Op.rem_i4, false) => Op.rem_i8, (Op.rem_i4, true) => Op.rem_r8,
            (Op.div_un_i4, false) => Op.div_un_i8,
            (Op.rem_un_i4, false) => Op.rem_un_i8,
            (Op.and_i4, false) => Op.and_i8,
            (Op.or_i4,  false) => Op.or_i8,
            (Op.xor_i4, false) => Op.xor_i8,
            (Op.shl_i4, false) => Op.shl_i8,
            (Op.shr_i4, false) => Op.shr_i8,
            (Op.shr_un_i4, false) => Op.shr_un_i8,
            (Op.ceq_i4, false) => Op.ceq_i8, (Op.ceq_i4, true) => Op.ceq_r8,
            (Op.cgt_i4, false) => Op.cgt_i8, (Op.cgt_i4, true) => Op.cgt_r8,
            (Op.clt_i4, false) => Op.clt_i8, (Op.clt_i4, true) => Op.clt_r8,
            (Op.cgt_un_i4, false) => Op.cgt_un_i8, (Op.cgt_un_i4, true) => Op.cgt_un_r8,
            (Op.clt_un_i4, false) => Op.clt_un_i8, (Op.clt_un_i4, true) => Op.clt_un_r8,
            _ => throw new NotSupportedException($"IR lowering: no 64-bit form of {i4Op}"),
        };

        // R8 compare from the R4 op the call site chose — the ordered/unordered intent
        // (NaN semantics through negated branches) lives in that choice, not the I4 op.
        static Op WideCmpR8(Op r4Op) => r4Op switch
        {
            Op.ceq_r4 or Op.ceq_r4_nn => Op.ceq_r8,
            Op.cgt_r4 or Op.cgt_r4_nn => Op.cgt_r8,
            Op.clt_r4 or Op.clt_r4_nn => Op.clt_r8,
            Op.cgt_un_r4 => Op.cgt_un_r8,
            Op.clt_un_r4 => Op.clt_un_r8,
            _ => throw new NotSupportedException($"IR lowering: no R8 form of {r4Op}"),
        };

        static void EmitBinop(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp, ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs,
            Op opI4, Op opR4, Op opI4_nn, Op opR4_nn, int instrStart)
        {
            var (s2, t2) = stack[--sp];
            var (s1, t1) = stack[sp - 1];
            // 64-bit operands (Roslyn emits explicit convs, so pairs arrive homogeneous;
            // a stray I4 operand is widened by the executor's Rd helpers).
            if (t1 is SType.I8 or SType.R8 || t2 is SType.I8 or SType.R8)
            {
                bool isR8w = t1 == SType.R8 || t2 == SType.R8;
                SType wrt = isR8w ? SType.R8 : SType.I8;
                int wdst = AllocSlot(ref frameSize, slotTypes, slotStructs, wrt, null);
                stack[sp - 1] = (wdst, wrt);
                Emit4(ir, irToIl, Wide64(opI4, isR8w), wdst, s1, s2, instrStart);
                return;
            }
            bool isFloat = t1 == SType.R4 || t2 == SType.R4;
            SType rt = isFloat ? SType.R4 : SType.I4;
            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, rt, null);
            stack[sp - 1] = (dst, rt);
            bool both_r4 = t1 == SType.R4 && t2 == SType.R4 && slotTypes[s1] == SType.R4 && slotTypes[s2] == SType.R4;
            bool both_i4 = t1 == SType.I4 && t2 == SType.I4 && slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4;
            Op op = isFloat ? (both_r4 ? opR4_nn : opR4) : (both_i4 ? opI4_nn : opI4);
            Emit4(ir, irToIl, op, dst, s1, s2, instrStart);
        }

        static void EmitBinopI4(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp, ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, Op opI4, Op opI4_nn, int instrStart)
        {
            var (s2, t2) = stack[--sp];
            var (s1, t1) = stack[sp - 1];
            // 64-bit form: and/or/xor take two I8s; shifts take (I8 value, I4 count) — either
            // way the value operand's width decides.
            if (t1 == SType.I8)
            {
                int wdst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I8, null);
                stack[sp - 1] = (wdst, SType.I8);
                Emit4(ir, irToIl, Wide64(opI4, false), wdst, s1, s2, instrStart);
                return;
            }
            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
            stack[sp - 1] = (dst, SType.I4);
            bool both_i4 = t1 == SType.I4 && t2 == SType.I4 && slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4;
            Emit4(ir, irToIl, both_i4 ? opI4_nn : opI4, dst, s1, s2, instrStart);
        }

        static void EmitUnop(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp, ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, Op opI4, Op opR4, Op opI4_n, Op opR4_n, int instrStart)
        {
            var (src, t) = stack[sp - 1];
            if (t is SType.I8 or SType.R8)
            {
                int wdst = AllocSlot(ref frameSize, slotTypes, slotStructs, t, null);
                stack[sp - 1] = (wdst, t);
                Emit3(ir, irToIl, t == SType.R8 ? Op.neg_r8 : Op.neg_i8, wdst, src, instrStart);
                return;
            }
            bool is_r4 = t == SType.R4;
            // The dst must be typed by what the op WRITES (always flat numeric), not by the source:
            // an O-typed src (e.g. a boxed int flowing out of a host call) would otherwise stamp the
            // dst O, and every downstream reader would look at the (never-written) ref slot.
            var rt = is_r4 ? SType.R4 : SType.I4;
            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, rt, null);
            stack[sp - 1] = (dst, rt);
            bool src_n = is_r4 ? slotTypes[src] == SType.R4 : slotTypes[src] == SType.I4;
            Op op = is_r4 ? (src_n ? opR4_n : opR4) : (src_n ? opI4_n : opI4);
            Emit3(ir, irToIl, op, dst, src, instrStart);
        }

        static void EmitUnopI4(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp, ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs, Op opI4, Op opI4_n, int instrStart)
        {
            var (src, t) = stack[sp - 1];
            if (t == SType.I8)
            {
                int wdst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I8, null);
                stack[sp - 1] = (wdst, SType.I8);
                Emit3(ir, irToIl, Op.not_i8, wdst, src, instrStart);
                return;
            }
            int dst = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
            stack[sp - 1] = (dst, SType.I4);
            bool src_n = t == SType.I4 && slotTypes[src] == SType.I4;
            Emit3(ir, irToIl, src_n ? opI4_n : opI4, dst, src, instrStart);
        }

        static void EmitCmpBranch(List<uint> ir, List<int> irToIl, (int, SType)[] stack, ref int sp, ref int frameSize, List<SType> slotTypes, List<HostBinding.StructLayout?> slotStructs,
            Op cmpI4, Op cmpR4, Op cmpO, Op cmpI4_nn, Op cmpR4_nn, Op branch, List<(int, int)> patchList, int ilTarget, int instrStart,
            Dictionary<int, int> branchTargetDepth)
        {
            var (s2, t2) = stack[--sp];
            var (s1, t1) = stack[sp - 1];
            sp--;
            // Both operands are consumed; the compare-branch leaves the base stack. Record the depth
            // it delivers to a forward target so the join reconciles correctly (see branchTargetDepth).
            if (ilTarget > instrStart) branchTargetDepth[ilTarget] = sp;
            int tmp = AllocSlot(ref frameSize, slotTypes, slotStructs, SType.I4, null);
            Op cmp;
            if (t1 == SType.R8 || t2 == SType.R8)
                cmp = WideCmpR8(cmpR4);
            else if (t1 == SType.I8 || t2 == SType.I8)
                cmp = Wide64(cmpI4, false);
            else if (t1 == SType.R4 || t2 == SType.R4)
                cmp = (t1 == SType.R4 && t2 == SType.R4 && slotTypes[s1] == SType.R4 && slotTypes[s2] == SType.R4) ? cmpR4_nn : cmpR4;
            else if (t1 == SType.I4 || t2 == SType.I4)
                // cmpI4 reads each operand from the correct stack at runtime (handles O/I4 mix).
                cmp = (slotTypes[s1] == SType.I4 && slotTypes[s2] == SType.I4) ? cmpI4_nn : cmpI4;
            else
                cmp = cmpO;
            Emit4(ir, irToIl, cmp, tmp, s1, s2, instrStart);
            int patchIdx = ir.Count + 2;
            Emit3(ir, irToIl, branch, tmp, -1, instrStart);
            patchList.Add((patchIdx, ilTarget));
        }

        // Read a branch target from the IL stream: 1-byte sbyte offset for short, 4-byte int for long.
        static int ReadBranchTarget(byte[] il, ref int ip, bool isShort)
        {
            int off = isShort ? (sbyte)il[ip++] : BitConverter.ToInt32(il, ip);
            if (!isShort) ip += 4;
            return ip + off;
        }

        // --- IR emission helpers (all params are int; cast to uint internally) ---

        static void Emit2(List<uint> ir, List<int> irToIl, Op op, int a, int ilOff)
        {
            ir.Add((uint)op); irToIl.Add(ilOff);
            ir.Add((uint)a);  irToIl.Add(ilOff);
        }

        static void Emit3(List<uint> ir, List<int> irToIl, Op op, int a, int b, int ilOff)
        {
            ir.Add((uint)op); irToIl.Add(ilOff);
            ir.Add((uint)a);  irToIl.Add(ilOff);
            ir.Add((uint)b);  irToIl.Add(ilOff);
        }

        static void Emit4(List<uint> ir, List<int> irToIl, Op op, int a, int b, int c, int ilOff)
        {
            ir.Add((uint)op); irToIl.Add(ilOff);
            ir.Add((uint)a);  irToIl.Add(ilOff);
            ir.Add((uint)b);  irToIl.Add(ilOff);
            ir.Add((uint)c);  irToIl.Add(ilOff);
        }
    }

    static (int Line, string Doc) FindSourceLine(ParsedMethod method, int ilOffset)
    {
        var pts = method.SeqPoints;
        if (pts == null) return (-1, "");
        int lo = 0, hi = pts.Length - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (pts[mid].IlOffset <= ilOffset) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result >= 0 ? (pts[result].Line, pts[result].Doc) : (-1, "");
    }

    static string At(ParsedMethod method, int ilOffset)
    {
        var (line, doc) = FindSourceLine(method, ilOffset);
        if (line <= 0) return $" at IL+0x{ilOffset:X4}";
        // Keep the " at line " marker: the VM's host-call rethrow filter matches it to avoid
        // appending a second location when an already-located error bubbles through nested frames.
        return doc.Length > 0 ? $" at line {line} ({doc})" : $" at line {line}";
    }

    static int ReadU16(byte[] il, ref int ip)
    {
        int v = il[ip] | (il[ip + 1] << 8);
        ip += 2;
        return v;
    }

    // Fill `arr` from the FieldDef's RVA blob, decoding bytes per element type.
    // Returns false if the element type is non-primitive or the field has no RVA — caller
    // then leaves the array zero-initialized and lets the InitializeArray no-op binding run.
    // Typed-backing fills for newarr_i4/newarr_r4 array initializers: element type is statically
    // Int32/Single (the lowerer only emits the typed ops for those), so only the RVA blob lookup
    // remains.
    static bool TryFillI4ArrayFromFieldBlob(int[] arr, ParsedAssembly asm, int fieldTok)
    {
        if (!TryReadFieldBlob(asm, fieldTok, arr.Length * 4, out var bytes)) return false;
        for (int i = 0; i < arr.Length; i++) arr[i] = BitConverter.ToInt32(bytes, i * 4);
        return true;
    }

    static bool TryFillR4ArrayFromFieldBlob(float[] arr, ParsedAssembly asm, int fieldTok)
    {
        if (!TryReadFieldBlob(asm, fieldTok, arr.Length * 4, out var bytes)) return false;
        for (int i = 0; i < arr.Length; i++) arr[i] = BitConverter.ToSingle(bytes, i * 4);
        return true;
    }

    // Fill a typed primitive array (bool[]/char[]/byte[]/…) from a RuntimeHelpers.InitializeArray
    // field blob. The blob layout (little-endian, element-sized) matches the primitive array's
    // memory, so a raw block copy is correct.
    static bool TryFillTypedArrayFromFieldBlob(Array arr, int elemSize, ParsedAssembly asm, int fieldTok)
    {
        if (!TryReadFieldBlob(asm, fieldTok, arr.Length * elemSize, out var bytes)) return false;
        Buffer.BlockCopy(bytes, 0, arr, 0, arr.Length * elemSize);
        return true;
    }

    static bool TryReadFieldBlob(ParsedAssembly asm, int fieldTok, int byteLen, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var fieldHandle = MetadataTokens.EntityHandle(fieldTok);
        if (fieldHandle.Kind != HandleKind.FieldDefinition) return false;
        var fd = asm.Reader.GetFieldDefinition((FieldDefinitionHandle)fieldHandle);
        int rva = fd.GetRelativeVirtualAddress();
        if (rva == 0) return false;
        var section = asm.Pe.GetSectionData(rva);
        var blobReader = section.GetReader();
        bytes = blobReader.ReadBytes(byteLen);
        return true;
    }

    static bool TryFillArrayFromFieldBlob(object?[] arr, ParsedAssembly asm, int elemTok, int fieldTok)
    {
        var elemHandle = MetadataTokens.EntityHandle(elemTok);
        if (elemHandle.Kind != HandleKind.TypeReference) return false;
        var elemRef  = asm.Reader.GetTypeReference((TypeReferenceHandle)elemHandle);
        var elemName = asm.Reader.GetString(elemRef.Name);

        int elemSize = elemName switch
        {
            "Boolean" or "Byte" or "SByte"  => 1,
            "Int16" or "UInt16" or "Char"   => 2,
            "Int32" or "UInt32" or "Single" => 4,
            "Int64" or "UInt64" or "Double" => 8,
            _ => 0,
        };
        if (elemSize == 0) return false;

        var fieldHandle = MetadataTokens.EntityHandle(fieldTok);
        if (fieldHandle.Kind != HandleKind.FieldDefinition) return false;
        var fd = asm.Reader.GetFieldDefinition((FieldDefinitionHandle)fieldHandle);
        int rva = fd.GetRelativeVirtualAddress();
        if (rva == 0) return false;

        var section = asm.Pe.GetSectionData(rva);
        var blobReader = section.GetReader();
        var bytes = blobReader.ReadBytes(arr.Length * elemSize);

        for (int i = 0; i < arr.Length; i++)
        {
            int o = i * elemSize;
            arr[i] = elemName switch
            {
                "Boolean" => bytes[o] != 0 ? 1 : 0,            // bools live as int (1/0) on the stack
                "Byte"    => (int)bytes[o],
                "SByte"   => (int)(sbyte)bytes[o],
                "Int16"   => (int)BitConverter.ToInt16(bytes, o),
                "UInt16"  => (int)BitConverter.ToUInt16(bytes, o),
                "Char"    => (int)BitConverter.ToChar(bytes, o),
                "Int32"   => BitConverter.ToInt32(bytes, o),
                "UInt32"  => (int)BitConverter.ToUInt32(bytes, o),
                "Single"  => BitConverter.ToSingle(bytes, o),
                "Int64"   => (object)BitConverter.ToInt64(bytes, o),
                "UInt64"  => (object)(long)BitConverter.ToUInt64(bytes, o),
                "Double"  => (object)(float)BitConverter.ToDouble(bytes, o),  // float-only subset
                _ => null,
            };
        }
        return true;
    }
}

// Simple stack-type tag (what the lowerer tracks per eval-stack slot).
// Placed at namespace level so HostBinding.cs can use it in FastCallDelegate.
// Vt = a host value type stored as contiguous bytes in numFrame, occupying
// ceil(byteSize/4) consecutive logical slot indices. Only the first slot is
// referenced; the rest are filler. byteSize and Type live in parallel
// LoweredMethod tables. See PLAN_FLAT_STRUCTS.md.
// O is first so default(SType) == SType.O. Anything that holds an SType field
// without initializing it (e.g. HostBinding.Entry.FlatReturnSType) gets the
// "no specific numeric type, use refFrame slot" default, which is what every
// caller wants. Changing this order will break any code that depends on the
// underlying byte values — none does today (no serialization, no (SType)0 casts).
internal enum SType : byte { O, I4, R4, Vt, I8, R8 }


// Typed return value from the Vm engine — eliminates boxing of primitive return values
// on script-to-script call paths. I4 covers both int and bool (bool stored as 0/1).
// Void (no-return) is represented by default(CallReturn): Type == I4, I4 == 0, O == null.
// The Type field disambiguates: I4 means no value or an int/bool; callers that need to
// know whether the callee was void check the calling method's IsVoid flag separately.
readonly struct CallReturn
{
    public readonly SType   Type;
    public readonly int     I4;   // valid when Type == I4 (includes bool as 0/1, and Void)
    public readonly float   R4;   // valid when Type == R4
    public readonly long    I8;   // valid when Type == I8
    public readonly double  R8;   // valid when Type == R8
    public readonly object? O;    // valid when Type == O or Vt

    CallReturn(SType t, int i4, float r4, long i8, double r8, object? o)
    { Type = t; I4 = i4; R4 = r4; I8 = i8; R8 = r8; O = o; }

    public static CallReturn FromI4(int v)     => new CallReturn(SType.I4, v, 0f, 0L, 0d, null);
    public static CallReturn FromR4(float v)   => new CallReturn(SType.R4, 0, v, 0L, 0d, null);
    public static CallReturn FromI8(long v)    => new CallReturn(SType.I8, 0, 0f, v, 0d, null);
    public static CallReturn FromR8(double v)  => new CallReturn(SType.R8, 0, 0f, 0L, v, null);
    public static CallReturn FromO(object? v)  => new CallReturn(SType.O, 0, 0f, 0L, 0d, v);
}
}
