#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
// The package bundles System.Runtime.CompilerServices.Unsafe renamed with a "UnityPipeline." prefix (Runtime/Plugins/CodeAnalysis);
// the dotnet test projects reference the same DLLs (IlInterpreter/BundledRoslyn.props).
using Unsafe = UnityPipeline.System.Runtime.CompilerServices.Unsafe;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace IlInterpreter.Interpreter
{

// Pre-built delegate invoked when a script calls a host method.
// receiver: the `this` object for instance methods, null for static.
// args:     explicit parameters (not including receiver).
// return:   return value, or null for void methods.
internal delegate object? HostCallDelegate(object? receiver, object?[] args);

// Fast-path delegate: reads args directly from the IR dual frame, bypassing
// ArgBuf allocation and Entry.Invoke coercion.  Populated by BuildTypedDelegates<T>.
// recv:    host receiver (null for static).
// num:     the Vm's unmanaged numeric stack base; slot S is at (bp+S)*4.
// ref_:    refStack (object?[]) — the shared call stack; slot S is at bp+S.
// ir:      the uint[] instruction stream.
// argBase: index in ir where arg slot indices start.
// dstSlot: frame-relative slot to write result into (-1 for void returns).
// bp:      base pointer — offset into num/ref_ for the current frame.
internal unsafe delegate void FastCallDelegate(object? recv, byte* num, object?[] ref_, SType[] slotT, uint[] ir, int argBase, int dstSlot, int bp);

// Open-instance delegate shapes for VALUE-TYPE receivers: the CLR requires the first
// parameter of an open-instance delegate over a struct method to be `ref T`. These power
// the FastVtRecv closures (computed struct members like Vector3.magnitude/normalized).
internal delegate TR RefRecvFn<TRecv, TR>(ref TRecv self);
internal delegate TR RefRecvFn1F<TRecv, TR>(ref TRecv self, float a);
internal delegate TR RefRecvFn1I<TRecv, TR>(ref TRecv self, int a);
internal delegate void RefRecvAct<TRecv>(ref TRecv self);
internal delegate void RefRecvAct1F<TRecv>(ref TRecv self, float a);
internal delegate void RefRecvAct1I<TRecv>(ref TRecv self, int a);
internal delegate void RefRecvAct2F<TRecv>(ref TRecv self, float a, float b);
internal delegate void RefRecvAct3F<TRecv>(ref TRecv self, float a, float b, float c);

// Holds all registered host method, constructor, and field delegates.
// Use the fluent Allow*() API to register; acts as both binding and builder.
#if UNITY_6000_5_OR_NEWER
[NoAutoStaticsCleanup]
#endif
internal sealed unsafe class HostBinding
{
    // Diagnostic warning sink. IlInterpreter has no UnityEngine dependency (these sources also compile in
    // the standalone dotnet test/fuzz builds), so warnings default to stderr; IlInterpreterHostBindings
    // redirects this to Debug.LogWarning on the Unity side.
    internal static Action<string> Warn = msg => Console.Error.WriteLine(msg);

    internal sealed class Entry
    {
        // Always set by the Allow*/Register* construction sites; `= null!` marks that invariant.
        public HostCallDelegate Delegate = null!;
        public int  ParamCount;   // number of explicit params (not this)
        public bool HasThis;
        public ParameterInfo[]? Params; // set by AllowType; enables int→bool coercion for Mono
        // Optional fast path built by BuildTypedDelegates<T>; null = use Delegate path.
        // When non-null, FastIsFlat tells the executor whether the closure writes its result
        // to numFrame (true) or to refFrame (false).
        public FastCallDelegate? Fast;
        public bool             FastIsFlat;
        // When true, Fast handles a FLAT (Vt) receiver itself: it reads the receiver slot
        // index from ir[argBase - 3] and takes the struct bytes in place from the numeric
        // frame (mutations land directly — no box, no write-back). Without it the executor
        // must drop to the boxing slow path for Vt receivers.
        public bool             FastVtRecv;
        // Optional return-type override consulted by the lowerer when allocating the call_host dst slot.
        // Default O = "unset, use Method.ReturnType (or O if Method is null too)". Set to R4 or I4 when
        // the C# signature doesn't reflect the script-visible return type — e.g. AllowBcl's Math.Sqrt
        // is C# (double)→double but the closure narrows to float, so AttachFlatFloat sets this to R4
        // so the lowerer picks an R4 slot and the closure can write flat bytes into it.
        public SType FlatReturnSType;
        // The Fast closure reads/writes WIDE (I8/R8) slots itself — the executor's wide-slot
        // fast-path guard lets it through. Set by AttachFlatDouble.
        public bool FastWideOk;
        // Optional method/ctor info — used by the IR loader to resolve struct param/return types
        // for the flat-struct (Vt) lowering path. Set by RegisterMethods / AllowConstructor when
        // the underlying MethodBase is available; null for hand-rolled Allow* delegates.
        public MethodBase? Method;
        public Type?       DeclaringType;
        // S4 operator inlining: set by InspectAndMarkOperators when the operator's IL
        // is verified as pure arithmetic. The lowerer synthesizes IR instead of call_host.
        public bool IsInlineableOp;
        // Verified trivial struct accessor: the getter/setter IL is exactly a single backing-field
        // load/store (`ldarg.0; ldfld f; ret` / `ldarg.0; ldarg.1; stfld f; ret`). On a FLAT (Vt)
        // receiver the lowerer emits ldfld_vt_*/stfld_vt_* at this byte offset instead of a host call.
        // -1 = not a trivial accessor. Set by MarkTrivialAccessors for flat-registered structs.
        public int   AccessorOffset = -1;
        public SType AccessorSt;
        // Layout of the backing field's type when the accessor is STRUCT-typed (Bounds.center →
        // Vector3): AccessorSt is Vt and the lowerer sizes the copy from this layout.
        public StructLayout? AccessorVtLayout;

        public object? Invoke(object? receiver, object?[] args)
        {
            if (Params != null) CoerceArgs(args, Params);
            var result = Delegate(receiver, args);
            // Enum and sub-int32/unsigned returns box as their real type, which fails the `is int`
            // checks in WrObj/RdI4 and reads 0 even though the lowerer types those slots I4 —
            // normalize the whole integer family to a boxed int. These returns always take this
            // slow path (Fast delegates are only built for exact int/float/bool shapes).
            return result is Enum ? unchecked((int)Convert.ToInt64(result)) : NormalizeIntegralReturn(result);
        }
    }

    // Coerce VM-boxed args to the parameter types reflection requires: every I4-slot value
    // (int, bool, byte, enum, …) arrives as a boxed int, but MethodBase.Invoke needs the real
    // type. Shared by Entry.Invoke (single-overload entries, driven by Entry.Params) and the
    // same-arity ctor dispatcher (which must coerce AFTER picking its overload).
    internal static void CoerceArgs(object?[] args, ParameterInfo[] ps)
    {
        for (int i = 0; i < args.Length && i < ps.Length; i++)
        {
            var pt = ps[i].ParameterType;
            if (pt == typeof(bool)   && args[i] is int ib) args[i] = ib != 0;
            else if (pt == typeof(char)   && args[i] is int ic)  args[i] = (char)ic;
            else if (pt == typeof(byte)   && args[i] is int i8)  args[i] = (byte)i8;
            else if (pt == typeof(sbyte)  && args[i] is int si8) args[i] = (sbyte)si8;
            else if (pt == typeof(short)  && args[i] is int i16) args[i] = (short)i16;
            else if (pt == typeof(ushort) && args[i] is int u16) args[i] = (ushort)u16;
            else if (pt == typeof(uint)   && args[i] is int u32) args[i] = unchecked((uint)u32);
            // Native int (ELEMENT_TYPE_I) rides a numeric slot and arrives boxed as
            // int/long; reflection Invoke has no implicit widening to IntPtr.
            else if (pt == typeof(IntPtr))
            {
                if (args[i] is long pl) args[i] = new IntPtr(pl);
                else if (args[i] is int pi) args[i] = new IntPtr(pi);
            }
            else if (pt == typeof(UIntPtr))
            {
                if (args[i] is long ul) args[i] = new UIntPtr(unchecked((ulong)ul));
                else if (args[i] is int ui) args[i] = new UIntPtr(unchecked((uint)ui));
            }
            else if (pt.IsEnum && args[i] is int ie) args[i] = Enum.ToObject(pt, ie);
            // Script-created reference-element arrays are object?[]-backed regardless of
            // their IL element type, but reflection Invoke needs the runtime array type
            // assignable to the declared parameter type (arrays only widen, string[] →
            // object[]). Rebuild as the declared array type; SetValue casts/unboxes per
            // element, so a genuinely wrong element still throws like C# would.
            else if (pt.IsArray && pt != typeof(object[]) && args[i] is object?[] oa
                     && args[i]!.GetType() == typeof(object[]))
            {
                var typed = Array.CreateInstance(pt.GetElementType()!, oa.Length);
                for (int j = 0; j < oa.Length; j++) typed.SetValue(oa[j], j);
                args[i] = typed;
            }
        }
    }

    // Box a sub-int32/unsigned integer value as a plain int (the lowerer types those slots I4), so
    // RdI4/WrObj/ldfld_i4's `is int` checks see the value instead of reading 0. Leaves int, long,
    // float, char, and reference types untouched. uint wraps like C# `(int)uintValue`.
    internal static object? NormalizeIntegralReturn(object? v) => v switch
    {
        byte b    => (int)b,
        sbyte sb  => (int)sb,
        short s   => (int)s,
        ushort us => (int)us,
        uint u    => unchecked((int)u),
        _         => v,
    };

    // Convert a VM I4-slot value (boxed int) back to a host field's real integer type for reflective
    // SetValue — a raw boxed int throws when assigned to a byte/short/uint field. Unchecked, matching C#.
    internal static object ToFieldIntegral(object? val, Type ft)
    {
        int iv = val is int i ? i : Convert.ToInt32(val);
        if (ft == typeof(byte))   return (byte)iv;
        if (ft == typeof(sbyte))  return (sbyte)iv;
        if (ft == typeof(short))  return (short)iv;
        if (ft == typeof(ushort)) return (ushort)iv;
        if (ft == typeof(uint))   return unchecked((uint)iv);
        return iv;
    }

    internal sealed class FieldEntry
    {
        // Both delegates are always set at registration; `= null!` marks that invariant.
        public Func<object?, object?>   Get = null!;
        public Action<object?, object?> Set = null!;
        // "Type.field", for runtime error messages (null-target reads/writes).
        public string? Name;
        // When the declaring type is a flat-layout struct (registered via AllowTypeStruct),
        // these record the field's byte offset and primitive SType (I4/R4/O) so the IR lowerer
        // can emit direct ldfld_vt_* / stfld_vt_* ops bypassing the boxed Get/Set delegates.
        // DeclaringStruct == null for non-flat-struct fields.
        public StructLayout? DeclaringStruct;
        public int           ByteOffset;
        public SType         PrimitiveSt;
        // Storage kind for I4-classified flat fields: 0 = full 4-byte cell, 1 = u1, 2 = i1,
        // 3 = u2, 4 = i2. Sub-4 fields need widening loads / truncating stores in the VM.
        public byte          PrimitiveKind;
        // Script-visible SType of the field, from its declared CLR type (enum → I4). The interpreter's
        // field-token classifier prefers this over its signature-byte guess (which can't tell an enum
        // 0x11 VALUETYPE from a struct, and so mis-typed enum fields as O — boxed — breaking `switch`
        // and comparisons on them). Default O ("unclassified") falls back to the signature guess.
        public SType         FieldSt;
        // Simple name of the field's declared CLR type. Lets the lowerer resolve the layout of a
        // NESTED flat struct field (Pose.P where P is a Vec2) at lowering time — registration
        // order-independent, unlike capturing the layout here.
        public string?       FieldTypeName;
    }

    // Layout descriptor for a host value type registered via AllowTypeStruct<T>().
    // Built lazily from reflection at registration time.
    internal sealed class StructLayout
    {
        // All fields are set by AllowTypeStruct's registration path; `= null!` marks that invariant.
        public Type   Type = null!;
        public int    Size;          // total byte size (Marshal.SizeOf<T>)
        public string TypeName = null!;      // type's simple name, used by parser
        // Per-field offsets, indexed by field name. Built from Marshal.OffsetOf.
        public Dictionary<string, (int Offset, SType St)> Fields = null!;
        // Storage kind for I4-classified sub-4-byte fields (absent = full 4-byte cell):
        // 1 = u1, 2 = i1, 3 = u2, 4 = i2. Loads must widen and stores must truncate.
        public Dictionary<string, byte>? FieldKinds;
        // Boundary marshalling straight off the unmanaged frame — no managed staging buffer.
        // Generic registrations implement these with Unsafe.ReadUnaligned<T>/WriteUnaligned;
        // the non-generic (Type-only) path uses a pinned-box memcpy (AOT-safe, no
        // MakeGenericMethod). BoxFromPtr's box is the ONLY allocation on the boundary.
        public BoxFromPtrDelegate BoxFromPtr = null!;
        public CopyToPtrDelegate  CopyToPtr = null!;
    }

    // Allocates a boxed T and copies Size bytes from src into its payload.
    internal unsafe delegate object BoxFromPtrDelegate(byte* src);
    // Reads the payload bytes of a boxed T (must be of layout's type) into dst.
    internal unsafe delegate void   CopyToPtrDelegate(byte* dst, object boxed);

    readonly Dictionary<string, StructLayout> _structLayouts = new();
    readonly Dictionary<Type, StructLayout>   _structLayoutsByType = new();

    readonly Dictionary<string, Entry>      _entries = new();
    readonly Dictionary<string, FieldEntry> _fields  = new();
    readonly HashSet<Type>                  _registeredTypes = new();
    readonly HashSet<string>               _collisions = new();
    readonly Dictionary<IntPtr, Entry>           _byHandle     = new();
    readonly Dictionary<(string, string), Type>  _genericTypes = new();

    // Demand-time auto-bind policy. Consulted by ScriptInterpreter.Load for every TypeRef the
    // script carries: maps a CLR full name ("UnityEngine.AI.NavMeshPath"; nested types use '+',
    // matching Assembly.GetType) to the Type to register, or null to decline — unknown name, or
    // policy says skip (e.g. the BCL, whose hand-rolled AllowBcl shims collapse doubles into the
    // script's float number space and must stay authoritative). Null resolver = strict allowlist:
    // only explicitly Allow*'d members are callable. With a resolver installed the curated surface
    // becomes the fast-path core rather than a capability boundary.
    internal Func<string, Type?>? AutoBindResolver { get; set; }
    // Resolver results per full name, INCLUDING negative ones, so repeated Loads against the same
    // binding don't rescan the AppDomain for every TypeRef of every reloaded script.
    readonly Dictionary<string, Type?> _autoBindCache = new();

    // Open generic method defs (e.g. GameObject.AddComponent<T>) captured at AllowType time.
    // The loader instantiates these lazily when it sees a MethodSpec token.
    // A (type, name, arity) key can hold several overloads (Object.Instantiate<T>(T,Transform,bool)
    // vs (T,Vector3,Quaternion)); the loader picks by matching the MemberRef signature.
    readonly Dictionary<(string, string, int), List<MethodInfo>> _openGenericMethods = new();
    // Cache of closed-method instantiations so repeated reloads don't re-MakeGenericMethod.
    readonly Dictionary<(IntPtr, string), MethodInfo> _closedCache = new();

    // Frame base (bp) of the call currently invoking a Fast delegate. The interpreter sets this
    // immediately before each Fast call so the arg readers below can translate an absolute stack
    // slot (bp + relativeSlot, used to index numStack/refStack) back to the method-relative index
    // that slotT (= LoweredMethod.SlotTypes, length FrameSize) uses. A Fast call reads all its args
    // before invoking the host method (and doesn't re-enter the interpreter before doing so), so a
    // thread-static is safe. Defaults to 0, which is correct for top-level (bp == 0) calls.
    [ThreadStatic] internal static int FastFrameBase;

    // Register an instance method: "TypeName.MethodName/N"
    public HostBinding Allow(string typeName, string methodName,
                             HostCallDelegate del, int paramCount,
                             ParameterInfo[]? ps = null, MethodBase? mi = null)
    {
        var entry = new Entry
        {
            Delegate      = del,
            ParamCount    = paramCount,
            HasThis       = true,
            Params        = ps,
            Method        = mi,
            DeclaringType = mi?.DeclaringType,
        };
        var key = $"{typeName}.{methodName}/{paramCount}";
        if (_entries.ContainsKey(key)) _collisions.Add(key);
        _entries[key] = entry;
        if (mi != null) _byHandle[mi.MethodHandle.Value] = entry;
        return this;
    }

    public HostBinding AllowStatic(string typeName, string methodName,
                                   HostCallDelegate del, int paramCount,
                                   ParameterInfo[]? ps = null, MethodBase? mi = null)
    {
        var entry = new Entry
        {
            Delegate      = del,
            ParamCount    = paramCount,
            HasThis       = false,
            Params        = ps,
            Method        = mi,
            DeclaringType = mi?.DeclaringType,
        };
        var key = $"{typeName}.{methodName}/{paramCount}";
        if (_entries.ContainsKey(key)) _collisions.Add(key);
        _entries[key] = entry;
        if (mi != null) _byHandle[mi.MethodHandle.Value] = entry;
        return this;
    }

    // Register a constructor (called via newobj). The delegate receives null as
    // receiver and the constructor arguments; it must return the new instance.
    public HostBinding AllowConstructor(string typeName,
                                        HostCallDelegate del, int paramCount,
                                        ParameterInfo[]? ps = null,
                                        ConstructorInfo? ci = null)
    {
        _entries[$"{typeName}..ctor/{paramCount}"] = new Entry
        {
            Delegate      = del,
            ParamCount    = paramCount,
            HasThis       = false,
            Params        = ps,
            Method        = ci,
            DeclaringType = ci?.DeclaringType,
        };
        return this;
    }

    // Register a host FIELD from reflection, classifying its script SType from the CLR type and
    // marshalling at the boundary so the interpreter sees a primitive, not a box: an enum reads/writes
    // as its underlying int (a boxed enum in an O slot makes `switch`/`==` on it silently never match);
    // a bool reads boxed (the VM's ldfld_i4 coerces bool→1/0) and writes from int→bool.
    // `obj` is the instance (null for static).
    void RegisterField(string typeName, FieldInfo fi)
        => _fields[$"{typeName}.{fi.Name}"] = BuildFieldEntry(fi);

    // The marshalling core of RegisterField, reusable for fields the parser resolves lazily
    // through a TypeSpec parent (e.g. ValueTuple`2.Item1) — those are token-keyed per closed
    // instantiation and never enter _fields.
    internal static FieldEntry BuildFieldEntry(FieldInfo fi)
    {
        var ft = fi.FieldType;
        SType st = ft.IsEnum ? SType.I4
            : ft == typeof(float) ? SType.R4
            : (ft == typeof(int) || ft == typeof(uint) || ft == typeof(bool)
               || ft == typeof(byte) || ft == typeof(sbyte) || ft == typeof(short) || ft == typeof(ushort))
                ? SType.I4
            : SType.O;

        Func<object?, object?> get;
        Action<object?, object?> set;
        if (ft.IsEnum)
        {
            // Read through the 64-bit underlying value and truncate (unchecked) to the I4 slot — matching
            // C#'s `(int)someEnum`. `Convert.ToInt32` throws OverflowException for an enum whose value
            // exceeds Int32 range (a `: long`/`: uint` enum), aborting the whole reload; truncation does not.
            get = obj => unchecked((int)Convert.ToInt64(fi.GetValue(obj)));
            set = (obj, val) => fi.SetValue(obj, Enum.ToObject(ft, val is int iv ? iv : Convert.ToInt32(val)));
        }
        else if (ft == typeof(bool))
        {
            get = obj => fi.GetValue(obj);
            set = (obj, val) => fi.SetValue(obj, val is int iv ? iv != 0 : val);
        }
        else if (st == SType.I4 && ft != typeof(int))
        {
            // Sub-int32/unsigned field (byte/sbyte/short/ushort/uint): present it to the VM as a boxed int
            // (reflection boxes it as its real type, which reads 0), and convert back on write (a boxed int
            // can't be SetValue'd directly into a byte/short/uint field).
            get = obj => NormalizeIntegralReturn(fi.GetValue(obj));
            set = (obj, val) => fi.SetValue(obj, ToFieldIntegral(val, ft));
        }
        else
        {
            get = obj => fi.GetValue(obj);
            set = (obj, val) => fi.SetValue(obj, val);
        }
        return new FieldEntry
        { Get = get, Set = set, FieldSt = st, FieldTypeName = ft.Name, Name = $"{fi.DeclaringType?.Name}.{fi.Name}" };
    }

    // Register a minimal set of BCL methods that Roslyn emits calls to.
    public HostBinding AllowBcl()
    {
        // Type — GetTypeFromHandle is an identity: ldtoken already pushed the Type directly
        AllowStatic("Type", "GetTypeFromHandle", (_, args) => args[0], 1);
        Allow("Type", "get_Name", (recv, _) => ((Type)recv!).Name, 0);
        // `t1 == t2` on System.Type operands compiles to Type.op_Equality (Roslyn resolves the
        // user-defined operator, not ceq), e.g. `a.GetType() == b.GetType()` in gameplay code.
        AllowStatic("Type", "op_Equality", (_, a) => (a[0] as Type) == (a[1] as Type) ? 1 : 0, 2);
        AllowStatic("Type", "op_Inequality", (_, a) => (a[0] as Type) != (a[1] as Type) ? 1 : 0, 2);
        // `.Name` on a Type receiver usually resolves to the declaring property MemberInfo.Name,
        // so Roslyn emits the MemberRef against MemberInfo, not Type — both keys are needed.
        Allow("MemberInfo", "get_Name", (recv, _) => ((System.Reflection.MemberInfo)recv!).Name, 0);

        Allow("Object", "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        Allow("Object", "GetHashCode", (recv, _) => recv?.GetHashCode() ?? 0, 0);

        // IDisposable — Roslyn emits `constrained.; callvirt IDisposable.Dispose` in the finally of
        // every foreach over a struct-enumerator collection (List<T>, Dictionary<K,V>, …).
        Allow("IDisposable", "Dispose", (recv, _) => { (recv as IDisposable)?.Dispose(); return null; }, 0);

        // List<T> — the workhorse gameplay collection. Its members arrive as MemberRefs on a
        // TypeSpec parent, keyed by the open type's short name ("List`1"), and dispatch through
        // the NON-generic collection interfaces so one shim serves every instantiation with no
        // MakeGenericType (AOT/IL2CPP-safe). Element values stay boxed, matching the O-slot
        // representation. `new List<T>()` inside a reloaded body is NOT covered — a ctor shim
        // can't know T; only members on existing instances are.
        // Element values COERCE to the list's T: an enum (or char/byte/short/float-from-int)
        // element arrives boxed as int off its I4 slot, and the boxed-int probe would silently
        // miss every Contains/IndexOf/Remove on a List<SomeEnum> — same identity erasure as
        // enum dictionary keys.
        static object? ListElem(object recv, object? val)
        {
            var t = recv.GetType();
            if (t.IsGenericType && val is int iv)
            {
                var e = t.GetGenericArguments()[0];
                if (e.IsEnum) return Enum.ToObject(e, iv);
                if (e == typeof(float)) return (float)iv;
                if (e == typeof(char)) return (char)iv;
                if (e == typeof(byte)) return (byte)iv;
                if (e == typeof(short)) return (short)iv;
                if (e == typeof(long)) return (long)iv;
            }
            return val;
        }
        Allow("List`1", "get_Item",   (recv, a) => ((System.Collections.IList)recv!)[Convert.ToInt32(a[0])], 1);
        Allow("List`1", "set_Item",   (recv, a) => { ((System.Collections.IList)recv!)[Convert.ToInt32(a[0])] = ListElem(recv!, a[1]); return null; }, 2);
        Allow("List`1", "get_Count",  (recv, _) => ((System.Collections.ICollection)recv!).Count, 0);
        Allow("List`1", "Add",        (recv, a) => { ((System.Collections.IList)recv!).Add(ListElem(recv!, a[0])); return null; }, 1);
        Allow("List`1", "Insert",     (recv, a) => { ((System.Collections.IList)recv!).Insert(Convert.ToInt32(a[0]), ListElem(recv!, a[1])); return null; }, 2);
        Allow("List`1", "RemoveAt",   (recv, a) => { ((System.Collections.IList)recv!).RemoveAt(Convert.ToInt32(a[0])); return null; }, 1);
        Allow("List`1", "Clear",      (recv, _) => { ((System.Collections.IList)recv!).Clear(); return null; }, 0);
        Allow("List`1", "Contains",   (recv, a) => ((System.Collections.IList)recv!).Contains(ListElem(recv!, a[0])) ? 1 : 0, 1);
        Allow("List`1", "IndexOf",    (recv, a) => ((System.Collections.IList)recv!).IndexOf(ListElem(recv!, a[0])), 1);
        Allow("List`1", "Remove",     (recv, a) =>
        {
            var l = (System.Collections.IList)recv!;
            var v = ListElem(recv!, a[0]);
            if (!l.Contains(v)) return 0;
            l.Remove(v);
            return 1;
        }, 1);
        // HashSet<T> — the visited-set staple. No non-generic interface carries Contains/Add,
        // so reflection dispatch on the closed type, with the same element coercion.
        Allow("HashSet`1", "Contains", (recv, a) => (bool)recv!.GetType().GetMethod("Contains")!.Invoke(recv, new[] { ListElem(recv!, a[0]) })! ? 1 : 0, 1);
        Allow("HashSet`1", "Add",      (recv, a) => (bool)recv!.GetType().GetMethod("Add")!.Invoke(recv, new[] { ListElem(recv!, a[0]) })! ? 1 : 0, 1);
        Allow("HashSet`1", "Remove",   (recv, a) => (bool)recv!.GetType().GetMethod("Remove", new[] { recv.GetType().GetGenericArguments()[0] })!.Invoke(recv, new[] { ListElem(recv!, a[0]) })! ? 1 : 0, 1);
        Allow("HashSet`1", "Clear",    (recv, _) => { recv!.GetType().GetMethod("Clear")!.Invoke(recv, null); return null; }, 0);
        Allow("HashSet`1", "UnionWith",     (recv, a) => { recv!.GetType().GetMethod("UnionWith")!.Invoke(recv, new[] { a[0] }); return null; }, 1);
        Allow("HashSet`1", "ExceptWith",    (recv, a) => { recv!.GetType().GetMethod("ExceptWith")!.Invoke(recv, new[] { a[0] }); return null; }, 1);
        Allow("HashSet`1", "IntersectWith", (recv, a) => { recv!.GetType().GetMethod("IntersectWith")!.Invoke(recv, new[] { a[0] }); return null; }, 1);
        Allow("HashSet`1", "get_Count", (recv, _) => recv!.GetType().GetProperty("Count")!.GetValue(recv), 0);
        Allow("HashSet`1", "GetEnumerator", (recv, _) => ((System.Collections.IEnumerable)recv!).GetEnumerator(), 0);
        // foreach support: IEnumerable.GetEnumerator boxes List<T>.Enumerator (a struct), and the
        // loop's MoveNext/get_Current calls — MemberRefs on the nested "Enumerator" TypeSpec —
        // dispatch on that box via IEnumerator, mutating it in place. Dispose is normally reached
        // through the constrained IDisposable shim above; registered here too for direct calls.
        // The "Enumerator" key is shared by every BCL struct enumerator (Dictionary, HashSet, …),
        // and the IEnumerator dispatch is correct for all of them.
        Allow("List`1", "GetEnumerator", (recv, _) => ((System.Collections.IEnumerable)recv!).GetEnumerator(), 0);
        Allow("Enumerator", "MoveNext",    (recv, _) => ((System.Collections.IEnumerator)recv!).MoveNext() ? 1 : 0, 0);
        Allow("Enumerator", "get_Current", (recv, _) => ((System.Collections.IEnumerator)recv!).Current, 0);
        Allow("Enumerator", "Dispose",     (recv, _) => { (recv as IDisposable)?.Dispose(); return null; }, 0);
        // foreach over a value typed as the INTERFACE IEnumerable<T> (not the concrete List<T> etc.)
        // — or explicit IEnumerator<T> use in a reloaded coroutine — dispatches through the generic
        // interface members, which are distinct TypeSpec MemberRefs from the "Enumerator" struct
        // above and from the non-generic IEnumerator. get_Current returns T via the non-generic
        // IEnumerator.Current; the same boxed-object dispatch is correct for every instantiation.
        Allow("IEnumerable`1", "GetEnumerator", (recv, _) => ((System.Collections.IEnumerable)recv!).GetEnumerator(), 0);
        Allow("IEnumerator`1", "MoveNext",      (recv, _) => ((System.Collections.IEnumerator)recv!).MoveNext() ? 1 : 0, 0);
        Allow("IEnumerator`1", "get_Current",   (recv, _) => ((System.Collections.IEnumerator)recv!).Current, 0);
        Allow("IEnumerator`1", "Dispose",       (recv, _) => { (recv as IDisposable)?.Dispose(); return null; }, 0);

        // Dictionary<K,V> — the other workhorse (inventories, lookups, pools). Same non-generic
        // dispatch strategy as List`1: one shim per member serves every instantiation, AOT-safe,
        // values stay boxed. `new Dictionary<K,V>()` in a reloaded body is NOT covered (no T at
        // shim time) — instances come from the host. Keys are COERCED to the dictionary's TKey:
        // an enum key arrives boxed as int (the I4 slot erased its identity), and the boxed-int
        // probe would silently miss every entry of a Dictionary<SomeEnum,V> (found by probing —
        // reads returned zero while writes coerced through the IDictionary cast).
        static object DictKey(object recv, object? key)
        {
            var t = recv.GetType();
            if (t.IsGenericType && key is int ik)
            {
                var k = t.GetGenericArguments()[0];
                if (k.IsEnum) return Enum.ToObject(k, ik);
                if (k == typeof(char)) return (char)ik;
                if (k == typeof(byte)) return (byte)ik;
                if (k == typeof(short)) return (short)ik;
                if (k == typeof(long)) return (long)ik;
            }
            return key!;
        }
        Allow("Dictionary`2", "get_Item", (recv, a) =>
        {
            var d = (System.Collections.IDictionary)recv!;
            var key = DictKey(recv!, a[0]);
            // The non-generic indexer returns null on a miss; C# throws. Fault parity matters —
            // a silent null reads as 0 through an I4 slot.
            if (!d.Contains(key)) throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
            return d[key];
        }, 1);
        Allow("Dictionary`2", "set_Item",    (recv, a) => { ((System.Collections.IDictionary)recv!)[DictKey(recv!, a[0])] = a[1]; return null; }, 2);
        Allow("Dictionary`2", "get_Count",   (recv, _) => ((System.Collections.ICollection)recv!).Count, 0);
        Allow("Dictionary`2", "Add",         (recv, a) => { ((System.Collections.IDictionary)recv!).Add(DictKey(recv!, a[0]), a[1]); return null; }, 2);
        Allow("Dictionary`2", "ContainsKey", (recv, a) => ((System.Collections.IDictionary)recv!).Contains(DictKey(recv!, a[0])) ? 1 : 0, 1);
        Allow("Dictionary`2", "Remove",      (recv, a) =>
        {
            var d = (System.Collections.IDictionary)recv!;
            var key = DictKey(recv!, a[0]);
            if (!d.Contains(key)) return 0;
            d.Remove(key);
            return 1;
        }, 1);
        // TryGetValue writes the out slot through the byref write-back path (the shim mutates
        // a[1] in place, exactly like reflection does for a MethodInfo out param). The lowerer
        // detects byref from Entry.Params, which hand shims don't have — borrow the parameter
        // shape from a marker method so the call site takes the write-back path.
        Allow("Dictionary`2", "TryGetValue", (recv, a) =>
        {
            var d = (System.Collections.IDictionary)recv!;
            var key = DictKey(recv!, a[0]);
            if (d.Contains(key)) { a[1] = d[key]; return 1; }
            a[1] = null; // value-typed outs read the null as their zero (CoerceBoxed*)
            return 0;
        }, 2, typeof(HostBinding).GetMethod(nameof(TryGetValueShape),
            BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters());
        // foreach: boxed struct enumerator via IEnumerable (yields boxed KeyValuePair<K,V>);
        // MoveNext/Current ride the shared "Enumerator" shims above. Key/Value dispatch by
        // reflection over the closed instantiation — cheap relative to a loop body.
        Allow("Dictionary`2", "GetEnumerator", (recv, _) => ((System.Collections.IEnumerable)recv!).GetEnumerator(), 0);
        Allow("KeyValuePair`2", "get_Key",   (recv, _) => recv!.GetType().GetProperty("Key")!.GetValue(recv), 0);
        Allow("KeyValuePair`2", "get_Value", (recv, _) => recv!.GetType().GetProperty("Value")!.GetValue(recv), 0);
        // foreach (var (k, v) in dict) — Roslyn calls KeyValuePair.Deconstruct(out, out).
        Allow("KeyValuePair`2", "Deconstruct", (recv, a) =>
        {
            var t = recv!.GetType();
            a[0] = t.GetProperty("Key")!.GetValue(recv);
            a[1] = t.GetProperty("Value")!.GetValue(recv);
            return null;
        }, 2, typeof(HostBinding).GetMethod(nameof(DeconstructShape),
            BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters());
        // foreach over dict.Keys / dict.Values: the collections come back by reflection (real
        // KeyCollection/ValueCollection instances) and their struct enumerators ride the shared
        // "Enumerator" shims; GetEnumerator dispatches via IEnumerable like every other one.
        Allow("Dictionary`2", "get_Keys",   (recv, _) => recv!.GetType().GetProperty("Keys")!.GetValue(recv), 0);
        Allow("Dictionary`2", "get_Values", (recv, _) => recv!.GetType().GetProperty("Values")!.GetValue(recv), 0);
        Allow("KeyCollection",   "GetEnumerator", (recv, _) => ((System.Collections.IEnumerable)recv!).GetEnumerator(), 0);
        Allow("ValueCollection", "GetEnumerator", (recv, _) => ((System.Collections.IEnumerable)recv!).GetEnumerator(), 0);
        Allow("KeyCollection",   "get_Count", (recv, _) => ((System.Collections.ICollection)recv!).Count, 0);
        Allow("ValueCollection", "get_Count", (recv, _) => ((System.Collections.ICollection)recv!).Count, 0);

        // Delegate invocation — System.Action/Func fields fire from gameplay code (event-style
        // callbacks like `CurrentSegementChanged.Invoke(seg)`). They're BCL TypeSpec MemberRefs the
        // auto-bind policy skips, so shim per arity via DynamicInvoke; bool results follow the
        // 1/0-int convention. User-declared delegate types resolve via their own auto-bound type.
        static object? CoerceDelegateResult(object? r) => r is bool b ? (b ? 1 : 0) : r;
        // Coerce against the delegate's OWN Invoke signature before DynamicInvoke: a char/bool
        // argument arrives boxed as int (it rides an I4 slot), and DynamicInvoke's binder refuses
        // Int32 → Char. Direct host calls get the same treatment from Entry.Params; these
        // string-keyed shims have no static ParameterInfo, so resolve it from the instance.
        static object? InvokeDelegate(object? recv, object?[] a)
        {
            var d = (Delegate)recv!;
            if (a.Length > 0)
                CoerceArgs(a, d.GetType().GetMethod("Invoke")!.GetParameters());
            return d.DynamicInvoke(a);
        }
        Allow("Action",   "Invoke", (recv, _) => ((Delegate)recv!).DynamicInvoke(), 0);
        Allow("Action`1", "Invoke", (recv, a) => InvokeDelegate(recv, a), 1);
        Allow("Action`2", "Invoke", (recv, a) => InvokeDelegate(recv, a), 2);
        Allow("Action`3", "Invoke", (recv, a) => InvokeDelegate(recv, a), 3);
        Allow("Action`4", "Invoke", (recv, a) => InvokeDelegate(recv, a), 4);
        Allow("Func`1",   "Invoke", (recv, _) => CoerceDelegateResult(((Delegate)recv!).DynamicInvoke()), 0);
        Allow("Func`2",   "Invoke", (recv, a) => CoerceDelegateResult(InvokeDelegate(recv, a)), 1);
        Allow("Func`3",   "Invoke", (recv, a) => CoerceDelegateResult(InvokeDelegate(recv, a)), 2);
        Allow("Func`4",   "Invoke", (recv, a) => CoerceDelegateResult(InvokeDelegate(recv, a)), 3);
        // 4 args — the ScriptDelegateAdapter's arity ceiling; the invoke surface matches it.
        Allow("Func`5",   "Invoke", (recv, a) => CoerceDelegateResult(InvokeDelegate(recv, a)), 4);
        // Combining delegate VALUES (`a += b` on an Action local/field) compiles to
        // Delegate.Combine/Remove + castclass. Null-tolerant like the BCL originals.
        AllowStatic("Delegate", "Combine", (_, a) => Delegate.Combine((Delegate?)a[0], (Delegate?)a[1]), 2);
        AllowStatic("Delegate", "Remove",  (_, a) => Delegate.Remove((Delegate?)a[0], (Delegate?)a[1]), 2);
        // `a == b` on delegates binds op_Equality — on MulticastDelegate for Action/Func, on
        // Delegate for the base type. Value equality (same target + method), like the BCL.
        AllowStatic("Delegate", "op_Equality",   (_, a) => Equals(a[0], a[1]) ? 1 : 0, 2);
        AllowStatic("Delegate", "op_Inequality", (_, a) => Equals(a[0], a[1]) ? 0 : 1, 2);
        AllowStatic("MulticastDelegate", "op_Equality",   (_, a) => Equals(a[0], a[1]) ? 1 : 0, 2);
        AllowStatic("MulticastDelegate", "op_Inequality", (_, a) => Equals(a[0], a[1]) ? 0 : 1, 2);
        AllowStatic("Object", "Equals", (_, a) => Equals(a[0], a[1]) ? 1 : 0, 2);

        // 5+ string concatenations: Roslyn lowers `a + b + c + d + e` to String.Concat(new object[] {...}).
        AllowStatic("String", "Concat", (_, a) =>
        {
            if (a[0] is object?[] objs) return string.Concat(objs);
            if (a[0] is string[] strs) return string.Concat(strs);
            return a[0]?.ToString() ?? "";
        }, 1);
        AllowStatic("String", "Concat", (_, a) => string.Concat(a[0]?.ToString(), a[1]?.ToString()), 2);
        AllowStatic("String", "Concat", (_, a) => string.Concat(a[0]?.ToString(), a[1]?.ToString(), a[2]?.ToString()), 3);
        AllowStatic("String", "Concat", (_, a) => string.Concat(a[0]?.ToString(), a[1]?.ToString(), a[2]?.ToString(), a[3]?.ToString()), 4);
        // String.Format — the target of the interpolation rewrite (InterpolationToFormatRewriter).
        // Arity 2 serves BOTH Format(string, object) [1 hole] and Format(string, params object[]) [4+
        // holes, Roslyn passes a constructed object[]] — they share the same 2-arg dispatch key, so the
        // delegate branches on whether the second arg is an object[]. Culture is default (CurrentCulture),
        // matching C# interpolation.
        AllowStatic("String", "Format", (_, a) =>
            a[1] is object?[] fargs ? string.Format((string)a[0]!, fargs) : string.Format((string)a[0]!, a[1]), 2);
        AllowStatic("String", "Format", (_, a) => string.Format((string)a[0]!, a[1], a[2]), 3);
        AllowStatic("String", "Format", (_, a) => string.Format((string)a[0]!, a[1], a[2], a[3]), 4);
        AllowStatic("String", "op_Equality", (_, a) => string.Equals(a[0] as string, a[1] as string) ? 1 : 0, 2);
        AllowStatic("String", "op_Inequality", (_, a) => !string.Equals(a[0] as string, a[1] as string) ? 1 : 0, 2);
        Allow("String", "get_Length", (recv, _) => ((string)recv!).Length, 0);
        // String indexing `s[i]` lowers to get_Chars. The analyzer permits it (it bans constructs,
        // not member calls) and get_Length's sibling was missing, so `s[i]` compiled + passed the
        // analyzer but threw "not registered" at runtime. Returns char -> I4; a bad index throws the
        // real IndexOutOfRangeException (fault parity). (found by fuzzing char/string indexing.)
        Allow("String", "get_Chars", (recv, a) => ((string)recv!)[(int)a[0]!], 1);
        // `new string(char[])` — building a string from a char buffer (e.g. NUL-padded name fields).
        AllowConstructor("String", (_, a) => new string((char[])a[0]!), 1);

        // Primitives — ToString (Roslyn emits Int32.ToString(), not Object.ToString())
        Allow("Int32",   "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        Allow("Int64",   "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        // (Single.ToString/0 registers below with the Convert.ToSingle receiver coercion —
        // registering it here too would be shadowed: entries key by name+arity, last wins.)
        Allow("Double",  "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        Allow("Boolean", "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        Allow("Char",    "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        // Sub-int integrals: reachable e.g. via Math.Max(char,char) -> ushort. Their decimal
        // rendering is identical to the (int-boxed) receiver value, so no ReceiverBox is needed.
        Allow("Byte",    "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        Allow("SByte",   "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        Allow("Int16",   "ToString", (recv, _) => recv?.ToString() ?? "", 0);
        Allow("UInt16",  "ToString", (recv, _) => recv?.ToString() ?? "", 0);

        // Int32.Parse — let FormatException propagate as ScriptRuntimeException
        AllowStatic("Int32", "Parse", (_, a) => int.Parse((string)a[0]!), 1);

        AllowStatic("String", "IsNullOrEmpty", (_, a) => string.IsNullOrEmpty(a[0] as string) ? 1 : 0, 1);
        AllowStatic("String", "IsNullOrWhiteSpace", (_, a) => string.IsNullOrWhiteSpace(a[0] as string) ? 1 : 0, 1);
        AllowStatic("String", "Join", (_, a) =>
        {
            if (a[1] is string[] sj) return string.Join((string?)a[0], sj);
            var src = (object?[])a[1]!;
            var parts = new string?[src.Length];
            for (int i = 0; i < src.Length; i++) parts[i] = src[i]?.ToString();
            return string.Join((string?)a[0], parts);
        }, 2);
        // The everyday instance surface HUD/parsing code leans on. Auto-bind deliberately skips
        // the BCL, so anything not shimmed here throws at runtime in a reloaded body. char args
        // arrive boxed as int (I4 slots) — coerce like CoerceArgs would.
        static char AsChar(object? v) => v is char c ? c : (char)Convert.ToInt32(v);
        Allow("String", "ToUpper",    (recv, _) => ((string)recv!).ToUpper(), 0);
        Allow("String", "ToLower",    (recv, _) => ((string)recv!).ToLower(), 0);
        Allow("String", "ToUpperInvariant", (recv, _) => ((string)recv!).ToUpperInvariant(), 0);
        Allow("String", "ToLowerInvariant", (recv, _) => ((string)recv!).ToLowerInvariant(), 0);
        Allow("String", "Trim",       (recv, _) => ((string)recv!).Trim(), 0);
        Allow("String", "TrimStart",  (recv, _) => ((string)recv!).TrimStart(), 0);
        Allow("String", "TrimEnd",    (recv, _) => ((string)recv!).TrimEnd(), 0);
        Allow("String", "Substring",  (recv, a) => ((string)recv!).Substring(Convert.ToInt32(a[0])), 1);
        Allow("String", "Substring",  (recv, a) => ((string)recv!).Substring(Convert.ToInt32(a[0]), Convert.ToInt32(a[1])), 2);
        Allow("String", "IndexOf",    (recv, a) => a[0] is string ixs ? ((string)recv!).IndexOf(ixs) : ((string)recv!).IndexOf(AsChar(a[0])), 1);
        Allow("String", "LastIndexOf",(recv, a) => a[0] is string lxs ? ((string)recv!).LastIndexOf(lxs) : ((string)recv!).LastIndexOf(AsChar(a[0])), 1);
        Allow("String", "Contains",   (recv, a) => (a[0] is string cs ? ((string)recv!).Contains(cs) : ((string)recv!).Contains(AsChar(a[0]))) ? 1 : 0, 1);
        Allow("String", "Replace",    (recv, a) => a[0] is string rs ? ((string)recv!).Replace(rs, (string?)a[1]) : ((string)recv!).Replace(AsChar(a[0]), AsChar(a[1])), 2);
        Allow("String", "StartsWith", (recv, a) => ((string)recv!).StartsWith((string)a[0]!) ? 1 : 0, 1);
        Allow("String", "EndsWith",   (recv, a) => ((string)recv!).EndsWith((string)a[0]!) ? 1 : 0, 1);
        Allow("String", "Trim",       (recv, a) => a[0] is char[] tcs ? ((string)recv!).Trim(tcs) : ((string)recv!).Trim(AsChar(a[0])), 1);
        // Comparison-mode overloads — the StringComparison enum arrives boxed from an I4 slot.
        // Instance comparison-mode only: the entry key is name+arity, and the lowering counts
        // stack args from HasThis, so STATIC string.Equals(a, b) can't share this slot — it
        // stays loud-unbound (write `a == b`); the 3-arg static form below has its own arity.
        Allow("String", "Equals",     (recv, a) => ((string)recv!).Equals(a[0] as string, (StringComparison)Convert.ToInt32(a[1])) ? 1 : 0, 2);
        Allow("String", "StartsWith", (recv, a) => ((string)recv!).StartsWith((string)a[0]!, (StringComparison)Convert.ToInt32(a[1])) ? 1 : 0, 2);
        Allow("String", "EndsWith",   (recv, a) => ((string)recv!).EndsWith((string)a[0]!, (StringComparison)Convert.ToInt32(a[1])) ? 1 : 0, 2);
        Allow("String", "IndexOf",    (recv, a) => a[1] is string || a[1] == null
            ? ((string)recv!).IndexOf((string)a[0]!, StringComparison.Ordinal)
            : ((string)recv!).IndexOf((string)a[0]!, (StringComparison)Convert.ToInt32(a[1])), 2);
        Allow("String", "Contains",   (recv, a) => ((string)recv!).Contains((string)a[0]!, (StringComparison)Convert.ToInt32(a[1])) ? 1 : 0, 2);
        AllowStatic("String", "Equals", (_, a) => string.Equals(a[0] as string, a[1] as string, (StringComparison)Convert.ToInt32(a[2])) ? 1 : 0, 3);
        Allow("String", "Remove",     (recv, a) => ((string)recv!).Remove(Convert.ToInt32(a[0])), 1);
        Allow("String", "Remove",     (recv, a) => ((string)recv!).Remove(Convert.ToInt32(a[0]), Convert.ToInt32(a[1])), 2);
        Allow("String", "Insert",     (recv, a) => ((string)recv!).Insert(Convert.ToInt32(a[0]), (string)a[1]!), 2);
        Allow("String", "ToCharArray",(recv, _) => ((string)recv!).ToCharArray(), 0);
        Allow("String", "CompareTo",  (recv, a) => ((string)recv!).CompareTo(a[0] as string), 1);
        AllowStatic("String", "Compare", (_, a) => string.Compare(a[0] as string, a[1] as string, StringComparison.CurrentCulture), 2);
        Allow("String", "PadLeft",    (recv, a) => ((string)recv!).PadLeft(Convert.ToInt32(a[0])), 1);
        Allow("String", "PadLeft",    (recv, a) => ((string)recv!).PadLeft(Convert.ToInt32(a[0]), AsChar(a[1])), 2);
        Allow("String", "PadRight",   (recv, a) => ((string)recv!).PadRight(Convert.ToInt32(a[0])), 1);
        Allow("String", "PadRight",   (recv, a) => ((string)recv!).PadRight(Convert.ToInt32(a[0]), AsChar(a[1])), 2);
        Allow("String", "Split",      (recv, a) => ((string)recv!).Split(AsChar(a[0])), 1);
        Allow("String", "ToString",   (recv, _) => recv, 0);

        // RuntimeHelpers.InitializeArray — fallback no-op. The interpreter pattern-matches
        // the Roslyn-emitted "newarr; dup; ldtoken <fld>; call InitializeArray" sequence in
        // ScriptInterpreter.Newarr and does the fill eagerly, so this delegate is normally
        // unreachable. Registered so BclCoverageTests sees the MemberRef as bound.
        AllowStatic("RuntimeHelpers", "InitializeArray", (_, _) => null, 2);

        // Array.Empty<T>() — Roslyn emits this when a params T[] parameter receives no arguments.
        var arrayEmptyDef = typeof(System.Array).GetMethod(nameof(System.Array.Empty))!;
        _openGenericMethods[("Array", "Empty", 0)] = new List<MethodInfo> { arrayEmptyDef };

        // string.Join<T>(string, IEnumerable<T>) — the HUD staple `string.Join(",", list)`
        // binds the generic overload through a MethodSpec.
        foreach (var sjm in typeof(string).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (sjm.Name == "Join" && sjm.IsGenericMethodDefinition && sjm.GetParameters().Length == 2
                && sjm.GetParameters()[0].ParameterType == typeof(string))
            { _openGenericMethods[("String", "Join", 2)] = new List<MethodInfo> { sjm }; break; }
        }

        // Array.IndexOf<T>(T[], T) / Sort<T>(T[]) / Reverse<T>(T[]) — C# binds the GENERIC
        // overloads for typed arrays, reached through MethodSpecs; register the open definitions
        // for lazy instantiation.
        foreach (var aim in typeof(System.Array).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!aim.IsGenericMethodDefinition) continue;
            int argc = aim.GetParameters().Length;
            if ((aim.Name == "IndexOf" && argc == 2) ||
                ((aim.Name == "Sort" || aim.Name == "Reverse") && argc == 1))
            {
                var key = ("Array", aim.Name, argc);
                if (!_openGenericMethods.ContainsKey(key))
                    _openGenericMethods[key] = new List<MethodInfo> { aim };
            }
        }

        // Math: the call site widens through conv.r8 and the result rides an R8 slot, so these
        // are bit-exact with compiled C#. Overloaded members switch on the runtime type of the
        // first argument. Each switch arm casts to (object) so C# does not unify the arm types
        // (which would widen int→float and box every result as float). The `double` case must
        // precede the `_` fallback, which converts to float and would lose range and precision.
        AllowStatic("Math", "Abs", (_, a) => a[0] switch
        {
            int    i => (object)Math.Abs(i),
            long   l => (object)Math.Abs(l),
            float  f => (object)Math.Abs(f),
            double d => (object)Math.Abs(d),
            _        => (object)(float)Math.Abs(Convert.ToSingle(a[0])),
        }, 1);
        AllowStatic("Math", "Min", (_, a) => (a[0], a[1]) switch
        {
            (int    x, int    y) => (object)Math.Min(x, y),
            (long   x, long   y) => (object)Math.Min(x, y),
            (uint   x, uint   y) => (object)Math.Min(x, y),   // unsigned arms: the call site re-boxes by the declared type
            (ulong  x, ulong  y) => (object)Math.Min(x, y),
            (float  x, float  y) => (object)Math.Min(x, y),
            (double x, double y) => (object)Math.Min(x, y),
            _                    => (object)(float)Math.Min(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])),
        }, 2);
        AllowStatic("Math", "Max", (_, a) => (a[0], a[1]) switch
        {
            (int    x, int    y) => (object)Math.Max(x, y),
            (long   x, long   y) => (object)Math.Max(x, y),
            (uint   x, uint   y) => (object)Math.Max(x, y),   // unsigned arms: the call site re-boxes by the declared type
            (ulong  x, ulong  y) => (object)Math.Max(x, y),
            (float  x, float  y) => (object)Math.Max(x, y),
            (double x, double y) => (object)Math.Max(x, y),
            _                    => (object)(float)Math.Max(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])),
        }, 2);
        AllowStatic("Math", "Clamp", (_, a) => (a[0], a[1], a[2]) switch
        {
            (int    x, int    lo, int    hi) => (object)Math.Clamp(x, lo, hi),
            (long   x, long   lo, long   hi) => (object)Math.Clamp(x, lo, hi),
            (uint   x, uint   lo, uint   hi) => (object)Math.Clamp(x, lo, hi),
            (ulong  x, ulong  lo, ulong  hi) => (object)Math.Clamp(x, lo, hi),
            (float  x, float  lo, float  hi) => (object)Math.Clamp(x, lo, hi),
            (double x, double lo, double hi) => (object)Math.Clamp(x, lo, hi),
            _                                => (object)(float)Math.Clamp(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]), Convert.ToSingle(a[2])),
        }, 3);
        AllowStatic("Math", "Sign", (_, a) => a[0] switch
        {
            int    i => (object)Math.Sign(i),
            long   l => (object)Math.Sign(l),
            float  f => (object)Math.Sign(f),
            double d => (object)Math.Sign(d),
            _        => (object)Math.Sign(Convert.ToSingle(a[0])),
        }, 1);
        AllowStatic("Math", "Sqrt",     (_, a) => Math.Sqrt(AsR8(a[0])), 1);
        AllowStatic("Math", "Pow",      (_, a) => Math.Pow(AsR8(a[0]), AsR8(a[1])), 2);
        AllowStatic("Math", "Sin",      (_, a) => Math.Sin(AsR8(a[0])), 1);
        AllowStatic("Math", "Cos",      (_, a) => Math.Cos(AsR8(a[0])), 1);
        AllowStatic("Math", "Tan",      (_, a) => Math.Tan(AsR8(a[0])), 1);
        AllowStatic("Math", "Asin",     (_, a) => Math.Asin(AsR8(a[0])), 1);
        AllowStatic("Math", "Acos",     (_, a) => Math.Acos(AsR8(a[0])), 1);
        AllowStatic("Math", "Atan",     (_, a) => Math.Atan(AsR8(a[0])), 1);
        AllowStatic("Math", "Atan2",    (_, a) => Math.Atan2(AsR8(a[0]), AsR8(a[1])), 2);
        AllowStatic("Math", "Floor",    (_, a) => Math.Floor(AsR8(a[0])), 1);
        AllowStatic("Math", "Ceiling",  (_, a) => Math.Ceiling(AsR8(a[0])), 1);
        AllowStatic("Math", "Round",    (_, a) => Math.Round(AsR8(a[0])), 1);
        AllowStatic("Math", "Truncate", (_, a) => Math.Truncate(AsR8(a[0])), 1);
        AllowStatic("Math", "Exp",      (_, a) => Math.Exp(AsR8(a[0])), 1);
        AllowStatic("Math", "Log",      (_, a) => Math.Log(AsR8(a[0])), 1);
        AllowStatic("Math", "Log10",    (_, a) => Math.Log10(AsR8(a[0])), 1);
        // Math.PI and Math.E are `const double` — Roslyn inlines as ldc.r8 literals at use
        // sites, so no MemberRef is emitted and no binding is needed.

        // MathF — the float-native twin gameplay code actually writes. Same surface as the Math
        // shims above, no double round-trip.
        AllowStatic("MathF", "Abs",      (_, a) => MathF.Abs(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Min",      (_, a) => MathF.Min(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])), 2);
        AllowStatic("MathF", "Max",      (_, a) => MathF.Max(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])), 2);
        AllowStatic("MathF", "Sign",     (_, a) => MathF.Sign(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Sqrt",     (_, a) => MathF.Sqrt(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Pow",      (_, a) => MathF.Pow(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])), 2);
        AllowStatic("MathF", "Sin",      (_, a) => MathF.Sin(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Cos",      (_, a) => MathF.Cos(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Tan",      (_, a) => MathF.Tan(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Atan2",    (_, a) => MathF.Atan2(Convert.ToSingle(a[0]), Convert.ToSingle(a[1])), 2);
        AllowStatic("MathF", "Floor",    (_, a) => MathF.Floor(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Ceiling",  (_, a) => MathF.Ceiling(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Round",    (_, a) => MathF.Round(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Round",    (_, a) => MathF.Round(Convert.ToSingle(a[0]), Convert.ToInt32(a[1])), 2);
        AllowStatic("MathF", "Truncate", (_, a) => MathF.Truncate(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Exp",      (_, a) => MathF.Exp(Convert.ToSingle(a[0])), 1);
        AllowStatic("MathF", "Log",      (_, a) => MathF.Log(Convert.ToSingle(a[0])), 1);

        // BitConverter bit-reinterprets — the safe int<->float bit cast, staple of binary file parsing
        // (reading floats out of a byte buffer). Pure/deterministic, no allocation.
        AllowStatic("BitConverter", "Int32BitsToSingle", (_, a) => BitConverter.Int32BitsToSingle(Convert.ToInt32(a[0])), 1);
        AllowStatic("BitConverter", "SingleToInt32Bits", (_, a) => BitConverter.SingleToInt32Bits(Convert.ToSingle(a[0])), 1);

        // Numeric formatting/parsing — the HUD staples. Formatted ToString on the I4/R4 slot
        // families needs the receiver's real type; the slot has erased char/bool identity but a
        // FORMATTED ToString is only meaningful on int/float anyway.
        Allow("Single", "ToString", (recv, _) => Convert.ToSingle(recv).ToString(), 0);
        Allow("Single", "ToString", (recv, a) => Convert.ToSingle(recv).ToString((string?)a[0]), 1);
        Allow("Int32",  "ToString", (recv, a) => Convert.ToInt32(recv).ToString((string?)a[0]), 1);
        // uint is analyzer-legal but rides I4 slots: the receiver may arrive boxed as a wrapped
        // NEGATIVE int (3000000000u -> -1294967296), so reinterpret the bits, never Convert.ToUInt32.
        Allow("UInt32", "ToString", (recv, _) => unchecked((uint)Convert.ToInt64(recv)).ToString(), 0);
        Allow("UInt32", "ToString", (recv, a) => unchecked((uint)Convert.ToInt64(recv)).ToString((string?)a[0]), 1);
        // 64-bit receivers arrive boxed as their true type (box_prim), but a value that crossed
        // through an O slot may be boxed long — reinterpret bits, never checked-Convert.
        Allow("Int64",  "ToString", (recv, a) => AsI8(recv).ToString((string?)a[0]), 1);
        Allow("UInt64", "ToString", (recv, _) => unchecked((ulong)AsI8(recv)).ToString(), 0);
        Allow("UInt64", "ToString", (recv, a) => unchecked((ulong)AsI8(recv)).ToString((string?)a[0]), 1);
        Allow("Double", "ToString", (recv, _) => AsR8(recv).ToString(), 0);
        Allow("Double", "ToString", (recv, a) => AsR8(recv).ToString((string?)a[0]), 1);
        // NaN/Infinity guards — the classic defensive checks around physics and division.
        AllowStatic("Single", "IsNaN",      (_, a) => float.IsNaN(Convert.ToSingle(a[0])) ? 1 : 0, 1);
        AllowStatic("Single", "IsInfinity", (_, a) => float.IsInfinity(Convert.ToSingle(a[0])) ? 1 : 0, 1);
        AllowStatic("Int32", "TryParse", (_, a) =>
        {
            if (int.TryParse(a[0] as string, out var v)) { a[1] = v; return 1; }
            a[1] = 0; return 0;
        }, 2, typeof(HostBinding).GetMethod(nameof(TryGetValueShape),
            BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters());
        AllowStatic("Int64", "TryParse", (_, a) =>
        {
            if (long.TryParse(a[0] as string, out var v)) { a[1] = v; return 1; }
            a[1] = 0L; return 0;
        }, 2, typeof(HostBinding).GetMethod(nameof(TryGetValueShape),
            BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters());
        AllowStatic("Double", "TryParse", (_, a) =>
        {
            if (double.TryParse(a[0] as string, out var v)) { a[1] = v; return 1; }
            a[1] = 0d; return 0;
        }, 2, typeof(HostBinding).GetMethod(nameof(TryGetValueShape),
            BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters());
        AllowStatic("Single", "TryParse", (_, a) =>
        {
            if (float.TryParse(a[0] as string, out var v)) { a[1] = v; return 1; }
            a[1] = 0f; return 0;
        }, 2, typeof(HostBinding).GetMethod(nameof(TryGetValueShape),
            BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters());

        // Bitmask checks on [Flags] enums. Receiver and argument both arrive as properly-typed
        // boxed enums (box_enum).
        Allow("Enum", "HasFlag", (recv, a) => ((Enum)recv!).HasFlag((Enum)a[0]!) ? 1 : 0, 1);

        // Primitive/enum receiver basics — GetHashCode/CompareTo/Equals on I4/R4 slot values.
        Allow("Int32",  "GetHashCode", (recv, _) => Convert.ToInt32(recv).GetHashCode(), 0);
        Allow("Single", "GetHashCode", (recv, _) => Convert.ToSingle(recv).GetHashCode(), 0);
        Allow("Int32",  "CompareTo", (recv, a) => Convert.ToInt32(recv).CompareTo(Convert.ToInt32(a[0])), 1);
        Allow("Single", "CompareTo", (recv, a) => Convert.ToSingle(recv).CompareTo(Convert.ToSingle(a[0])), 1);
        // Enum receivers arrive as typed boxed enums (box_enum / the constrained-enum path).
        Allow("Enum", "GetHashCode", (recv, _) => recv!.GetHashCode(), 0);
        Allow("Enum", "CompareTo",   (recv, a) => ((IComparable)recv!).CompareTo(a[0]), 1);

        // char classification statics — input/text parsing. Args ride I4 slots boxed as int.
        AllowStatic("Char", "IsDigit",      (_, a) => char.IsDigit(AsChar(a[0])) ? 1 : 0, 1);
        AllowStatic("Char", "IsLetter",     (_, a) => char.IsLetter(AsChar(a[0])) ? 1 : 0, 1);
        AllowStatic("Char", "IsLetterOrDigit", (_, a) => char.IsLetterOrDigit(AsChar(a[0])) ? 1 : 0, 1);
        AllowStatic("Char", "IsWhiteSpace", (_, a) => char.IsWhiteSpace(AsChar(a[0])) ? 1 : 0, 1);
        AllowStatic("Char", "IsUpper",      (_, a) => char.IsUpper(AsChar(a[0])) ? 1 : 0, 1);
        AllowStatic("Char", "IsLower",      (_, a) => char.IsLower(AsChar(a[0])) ? 1 : 0, 1);
        AllowStatic("Char", "ToUpper",      (_, a) => char.ToUpper(AsChar(a[0])), 1);
        AllowStatic("Char", "ToLower",      (_, a) => char.ToLower(AsChar(a[0])), 1);

        // Array.IndexOf — the generic static binds as IndexOf<T>; the non-generic overload
        // serves every element type.
        AllowStatic("Array", "IndexOf", (_, a) => Array.IndexOf((Array)a[0]!, a[1]), 2);
        AllowStatic("Array", "Copy",  (_, a) => { Array.Copy((Array)a[0]!, (Array)a[1]!, Convert.ToInt32(a[2])); return null; }, 3);
        AllowStatic("Array", "Copy",  (_, a) => { Array.Copy((Array)a[0]!, Convert.ToInt32(a[1]), (Array)a[2]!, Convert.ToInt32(a[3]), Convert.ToInt32(a[4])); return null; }, 5);
        AllowStatic("Array", "Clear", (_, a) => { Array.Clear((Array)a[0]!, Convert.ToInt32(a[1]), Convert.ToInt32(a[2])); return null; }, 3);

        // StringBuilder — HUD string building without per-frame concat garbage.
        AllowConstructor("StringBuilder", (_, _2) => new StringBuilder(), 0);
        AllowConstructor("StringBuilder", (_, a) => a[0] is int cap ? new StringBuilder(cap) : new StringBuilder((string?)a[0]), 1);
        Allow("StringBuilder", "Append",     (recv, a) => ((StringBuilder)recv!).Append(a[0]), 1);
        Allow("StringBuilder", "AppendLine", (recv, _) => ((StringBuilder)recv!).AppendLine(), 0);
        Allow("StringBuilder", "AppendLine", (recv, a) => ((StringBuilder)recv!).AppendLine(a[0]?.ToString()), 1);
        Allow("StringBuilder", "Clear",      (recv, _) => ((StringBuilder)recv!).Clear(), 0);
        Allow("StringBuilder", "ToString",   (recv, _) => ((StringBuilder)recv!).ToString(), 0);
        Allow("StringBuilder", "get_Length", (recv, _) => ((StringBuilder)recv!).Length, 0);

        // System.Random — seeded, deterministic across both sides of the oracle.
        // Defensive throws — `throw new InvalidOperationException("...")` on bad state.
        AllowConstructor("Exception",                  (_, a) => new Exception((string?)a[0]), 1);
        AllowConstructor("InvalidOperationException",  (_, _2) => new InvalidOperationException(), 0);
        AllowConstructor("InvalidOperationException",  (_, a) => new InvalidOperationException((string?)a[0]), 1);
        AllowConstructor("ArgumentException",          (_, a) => new ArgumentException((string?)a[0]), 1);
        AllowConstructor("ArgumentOutOfRangeException",(_, a) => new ArgumentOutOfRangeException((string?)a[0]), 1);
        AllowConstructor("ArgumentNullException",      (_, a) => new ArgumentNullException((string?)a[0]), 1);
        AllowConstructor("NotSupportedException",      (_, a) => new NotSupportedException((string?)a[0]), 1);
        AllowConstructor("IndexOutOfRangeException",   (_, a) => new IndexOutOfRangeException((string?)a[0]), 1);
        AllowConstructor("KeyNotFoundException",       (_, a) => new System.Collections.Generic.KeyNotFoundException((string?)a[0]), 1);

        // Range slices — `arr[1..4]` lowers to Index conversions + `new Range` +
        // RuntimeHelpers.GetSubArray<T>(array, range).
        AllowStatic("Index", "op_Implicit", (_, a) => (Index)Convert.ToInt32(a[0]), 1);
        AllowConstructor("Index", (_, a) => new Index(Convert.ToInt32(a[0]),
            a[1] is bool fromEnd ? fromEnd : Convert.ToInt32(a[1]) != 0), 2);
        AllowConstructor("Range", (_, a) => new Range((Index)a[0]!, (Index)a[1]!), 2);
        var gsa = typeof(System.Runtime.CompilerServices.RuntimeHelpers)
            .GetMethod("GetSubArray", BindingFlags.Public | BindingFlags.Static);
        if (gsa != null)
            _openGenericMethods[("RuntimeHelpers", "GetSubArray", 2)] = new List<MethodInfo> { gsa };

        foreach (var afm in typeof(System.Array).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (afm.Name == "Fill" && afm.IsGenericMethodDefinition && afm.GetParameters().Length == 2)
            { _openGenericMethods[("Array", "Fill", 2)] = new List<MethodInfo> { afm }; break; }
        }

        AllowConstructor("Random", (_, _2) => new Random(), 0);
        AllowConstructor("Random", (_, a) => new Random(Convert.ToInt32(a[0])), 1);
        Allow("Random", "Next", (recv, _) => ((Random)recv!).Next(), 0);
        Allow("Random", "Next", (recv, a) => ((Random)recv!).Next(Convert.ToInt32(a[0])), 1);
        Allow("Random", "Next", (recv, a) => ((Random)recv!).Next(Convert.ToInt32(a[0]), Convert.ToInt32(a[1])), 2);
        // netstandard2.1 (the editor profile) has no Random.NextSingle — same distribution via NextDouble.
        Allow("Random", "NextSingle", (recv, _) => (float)((Random)recv!).NextDouble(), 0);

        // Wall-clock cooldowns and save stamps. DateTime/TimeSpan are host structs the script
        // only ever holds boxed; float-view accessors keep results in the script's number space.
        AllowStatic("DateTime", "get_Now",    (_, _2) => DateTime.Now, 0);
        AllowStatic("DateTime", "get_UtcNow", (_, _2) => DateTime.UtcNow, 0);
        AllowStatic("DateTime", "op_Subtraction", (_, a) => (DateTime)a[0]! - (DateTime)a[1]!, 2);
        Allow("DateTime", "get_Hour",   (recv, _) => ((DateTime)recv!).Hour, 0);
        Allow("DateTime", "get_Minute", (recv, _) => ((DateTime)recv!).Minute, 0);
        Allow("DateTime", "get_Second", (recv, _) => ((DateTime)recv!).Second, 0);
        Allow("DateTime", "ToString",   (recv, a) => ((DateTime)recv!).ToString((string?)a[0]), 1);
        Allow("TimeSpan", "get_TotalSeconds",      (recv, _) => (float)((TimeSpan)recv!).TotalSeconds, 0);
        Allow("TimeSpan", "get_TotalMilliseconds", (recv, _) => (float)((TimeSpan)recv!).TotalMilliseconds, 0);
        AllowStatic("Environment", "get_TickCount", (_, _2) => Environment.TickCount, 0);
        // Roslyn's IEnumerable<T> iterator boilerplate checks thread affinity in GetEnumerator.
        AllowStatic("Environment", "get_CurrentManagedThreadId", (_, _2) => Environment.CurrentManagedThreadId, 0);

        // Enum.TryParse<T>(string, out T) — config/save parsing; the MethodSpec instantiation
        // carries real ParameterInfo, so the byref out rides the standard write-back path.
        foreach (var etp in typeof(Enum).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (etp.Name == "TryParse" && etp.IsGenericMethodDefinition && etp.GetParameters().Length == 2
                && etp.GetParameters()[0].ParameterType == typeof(string))
            { _openGenericMethods[("Enum", "TryParse", 2)] = new List<MethodInfo> { etp }; break; }
        }
        AllowStatic("Guid", "NewGuid", (_, _2) => Guid.NewGuid(), 0);
        Allow("Guid", "ToString", (recv, _) => ((Guid)recv!).ToString(), 0);

        // List<T> extras beyond the core set above; Sort/ToArray need the closed generic, so
        // dispatch by reflection on the instance (AOT-safe — the instantiation already exists).
        Allow("List`1", "Sort",    (recv, _) => { recv!.GetType().GetMethod("Sort", Type.EmptyTypes)!.Invoke(recv, null); return null; }, 0);
        Allow("List`1", "ToArray", (recv, _) => recv!.GetType().GetMethod("ToArray")!.Invoke(recv, null), 0);
        // Predicate members — the arg is a real closed delegate (Predicate<T>) built by the
        // delegate machinery, so reflection Invoke dispatches it directly.
        Allow("List`1", "AddRange", (recv, a) => { recv!.GetType().GetMethod("AddRange")!.Invoke(recv, new[] { a[0] }); return null; }, 1);
        Allow("List`1", "Reverse",  (recv, _) => { recv!.GetType().GetMethod("Reverse", Type.EmptyTypes)!.Invoke(recv, null); return null; }, 0);
        Allow("List`1", "GetRange", (recv, a) => recv!.GetType().GetMethod("GetRange")!.Invoke(recv, new object?[] { Convert.ToInt32(a[0]), Convert.ToInt32(a[1]) }), 2);
        Allow("List`1", "Find",      (recv, a) => NormalizeIntegralReturn(recv!.GetType().GetMethod("Find")!.Invoke(recv, new[] { a[0] })), 1);
        Allow("List`1", "FindIndex", (recv, a) => recv!.GetType().GetMethod("FindIndex", new[] { typeof(Predicate<>).MakeGenericType(recv.GetType().GetGenericArguments()) })!.Invoke(recv, new[] { a[0] }), 1);
        Allow("List`1", "Exists",    (recv, a) => (bool)recv!.GetType().GetMethod("Exists")!.Invoke(recv, new[] { a[0] })! ? 1 : 0, 1);
        Allow("List`1", "RemoveAll", (recv, a) => recv!.GetType().GetMethod("RemoveAll")!.Invoke(recv, new[] { a[0] }), 1);

        // Queue<T>/Stack<T> — pooling and undo stacks. No non-generic interface carries their
        // members, so reflection-dispatch per member.
        Allow("Queue`1", "Enqueue",  (recv, a) => { recv!.GetType().GetMethod("Enqueue")!.Invoke(recv, new[] { a[0] }); return null; }, 1);
        Allow("Queue`1", "Dequeue",  (recv, _) => recv!.GetType().GetMethod("Dequeue")!.Invoke(recv, null), 0);
        Allow("Queue`1", "Peek",     (recv, _) => recv!.GetType().GetMethod("Peek")!.Invoke(recv, null), 0);
        Allow("Queue`1", "Clear",    (recv, _) => { recv!.GetType().GetMethod("Clear")!.Invoke(recv, null); return null; }, 0);
        Allow("Queue`1", "get_Count", (recv, _) => ((System.Collections.ICollection)recv!).Count, 0);
        Allow("Stack`1", "Push",     (recv, a) => { recv!.GetType().GetMethod("Push")!.Invoke(recv, new[] { a[0] }); return null; }, 1);
        Allow("Stack`1", "Pop",      (recv, _) => recv!.GetType().GetMethod("Pop")!.Invoke(recv, null), 0);
        Allow("Stack`1", "Peek",     (recv, _) => recv!.GetType().GetMethod("Peek")!.Invoke(recv, null), 0);
        Allow("Stack`1", "Clear",    (recv, _) => { recv!.GetType().GetMethod("Clear")!.Invoke(recv, null); return null; }, 0);
        Allow("Stack`1", "get_Count", (recv, _) => ((System.Collections.ICollection)recv!).Count, 0);

        // Attach FastIsFlat paths to the always-float Math methods above — AllowBcl doesn't run
        // BuildTypedDelegates for Math, so Fasts are attached by hand. Multi-arm dispatch methods
        // (Abs, Sign, Min, Max, Clamp) stay on the slow delegate path: their result type depends
        // on the argument type, and mixing int and float results in the same R4 dst slot would
        // corrupt the int arm's value.

        // (double) → double — the closures compute at full width and write R8 dsts
        AttachFlatDouble("Math", "Sqrt",     1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Sqrt(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Sin",      1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Sin(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Cos",      1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Cos(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Tan",      1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Tan(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Asin",     1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Asin(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Acos",     1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Acos(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Atan",     1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Atan(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Floor",    1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Floor(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Ceiling",  1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Ceiling(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Round",    1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Round(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Truncate", 1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Truncate(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Exp",      1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Exp(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Log",      1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Log(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));
        AttachFlatDouble("Math", "Log10",    1, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Log10(NumReadDouble(num, ref_, slotT, bp + (int)ir[argBase]))));

        // (double, double) → double
        AttachFlatDouble("Math", "Atan2", 2, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
        {
            int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Atan2(NumReadDouble(num, ref_, slotT, s0), NumReadDouble(num, ref_, slotT, s1)));
        });
        AttachFlatDouble("Math", "Pow", 2, (_, num, ref_, slotT, ir, argBase, dst, bp) =>
        {
            int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                Math.Pow(NumReadDouble(num, ref_, slotT, s0), NumReadDouble(num, ref_, slotT, s1)));
        });

        return this;
    }

    // Parameter shape borrowed by the Dictionary`2.TryGetValue shim: the lowerer classifies
    // byref call sites from Entry.Params, which hand-written shims otherwise don't carry.
    internal static long AsI8(object? v) => v switch
    {
        long l => l, ulong u => unchecked((long)u), int i => i, uint u4 => u4,
        double d => (long)d, float f => (long)f, _ => Convert.ToInt64(v),
    };

    internal static double AsR8(object? v) => v switch
    {
        double d => d, float f => f, long l => l, ulong u => u, int i => i,
        _ => Convert.ToDouble(v),
    };

    static bool TryGetValueShape(object key, out object? value) { value = null; return false; }
    static void DeconstructShape(out object? key, out object? value) { key = null; value = null; }

    // Auto-register all public members of T and its base types via reflection.
    // Walks the inheritance chain so base-class members (e.g. ButtonControl.isPressed
    // inherited by KeyControl) are registered under their declaring type name.
    // Skips open generic type definitions; closed generics (e.g. InputControl<float>)
    // are registered so their members resolve correctly. Stops at Object / ValueType.
    public HostBinding AllowType(Type type)
    {
        AllowTypeWithBases(type);
        // Auto-flatten 4-byte-field structs even without a generic type argument: the pin-based
        // marshaller needs no MakeGenericMethod, so auto-bound structs (Rect, LayerMask, game
        // structs, ...) stop boxing at every host-call boundary. Blittable structs that don't
        // qualify (sub-4-byte fields, e.g. Color32) keep the boxed path and the perf warning.
        if (type.IsValueType && !type.IsEnum && IsSlot4FlatCandidate(type))
        {
            BuildStructLayoutNonGeneric(type);
            if (_structLayoutsByType.ContainsKey(type))
            {
                // Without generic Fast closures (value-type MakeGenericMethod is off-limits
                // here), flat args would be pin-boxed per OPERATOR call — costlier than the old
                // stay-boxed flow. Pure-arithmetic operators sidestep the boundary entirely:
                // the IL inspection below marks them and the lowerer synthesizes per-field IR.
                InspectAndMarkOperators(type, warnOnReject: false);
            }
        }
        else
            WarnIfFlatStructCandidate(type);
        // Build the allocation-free Fast delegates the generic AllowType<T>() would — otherwise
        // every method on a type registered this way (e.g. UnityHostBinding's Mathf/Time/Physics)
        // takes the boxing slow path. Reference types only: a reference T uses IL2CPP shared
        // generics (AOT-safe); value types must use AllowTypeStruct<T>() and a value-type
        // MakeGenericMethod is an AOT hazard, so they're skipped here.
        if (!type.IsValueType && !type.IsEnum)
        {
            try
            {
                typeof(HostBinding)
                    .GetMethod(nameof(BuildTypedDelegates), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(type)
                    .Invoke(this, null);
            }
            catch { /* best-effort fast-path; falls back to the (correct) slow path on failure */ }
        }
        // AOT-safe static-method fast paths (no MakeGenericMethod). Fills static-class members
        // (Mathf/Time/Physics) that the generic path above can't, and any static methods it missed.
        BuildStaticPrimitiveFastDelegates(type);
        return this;
    }

    /// <summary>
    /// Register the NON-PUBLIC members (fields, properties, methods — instance AND static) of
    /// <paramref name="type"/> and its user-defined base types, so an interpreter-run [MethodReload] body —
    /// compiled with access checks disabled (BinderFlags.IgnoreAccessibility) — can read/write private
    /// state (including private statics like the `s_Instance = this;` singleton pattern) and call private
    /// helpers on its target type. Public members are already covered by <see cref="AllowType(Type)"/>.
    ///
    /// The base-chain walk stops at the first UnityEngine / System / Unity.* type, so we never register
    /// the private internals of MonoBehaviour, Component or the BCL — only the user's own component
    /// hierarchy. Non-public members go through reflection Get/Set/Invoke,
    /// which is AOT-safe on IL2CPP as long as the members survive stripping.
    /// </summary>
    public HostBinding AllowNonPublicInstanceMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        for (var t = type; t != null; t = t.BaseType)
        {
            // Always process the target type itself; only stop CLIMBING once we reach an engine/BCL base
            // (MonoBehaviour, Component, object, …) — we never want to register their private internals.
            if (t != type)
            {
                var ns = t.Namespace ?? string.Empty;
                if (ns.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                    ns.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                    ns.StartsWith("System", StringComparison.Ordinal) ||
                    ns.StartsWith("Unity.", StringComparison.Ordinal))
                    break;
            }

            var name = t.Name;

            foreach (var f in t.GetFields(flags))
            {
                if (SkipType(f.FieldType)) continue;
                RegisterField(name, f);
            }

            // Non-public STATIC fields and methods too: a reloaded body may touch its own
            // type's private statics (the ubiquitous `s_Instance = this;` singleton pattern).
            // Public statics are already covered by AllowType's static walk.
            const BindingFlags statFlags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var f in t.GetFields(statFlags))
            {
                if (SkipType(f.FieldType)) continue;
                RegisterField(name, f);
            }
            RegisterMethods(t.GetMethods(statFlags), name, isStatic: true);

            // Non-public instance properties → get_/set_ method calls. Accessor arity from the
            // accessor itself — indexers take the index (see the public property pass).
            foreach (var p in t.GetProperties(flags))
            {
                if (SkipType(p.PropertyType)) continue;
                if (p.GetMethod != null && !SkipMethod(p.GetMethod))
                {
                    var g = p.GetMethod;
                    var gp = g.GetParameters();
                    if (gp.Length == 0)
                        Allow(name, g.Name, (recv, _) => g.Invoke(recv, null), 0, mi: g);
                    else
                        Allow(name, g.Name, (recv, args) => g.Invoke(recv, args), gp.Length, gp, mi: g);
                }
                if (p.SetMethod != null && !SkipMethod(p.SetMethod))
                {
                    var s = p.SetMethod;
                    var spms = s.GetParameters();
                    Allow(name, s.Name, (recv, args) => { s.Invoke(recv, args); return null; }, spms.Length, spms, mi: s);
                }
            }

            // Non-public instance methods (RegisterMethods skips get_/set_ via IsSpecialName)
            RegisterMethods(t.GetMethods(flags), name, isStatic: false);
        }
        return this;
    }

    public HostBinding AllowType<T>()
    {
        AllowTypeWithBases(typeof(T));
        // Auto-promote a blittable struct to the non-boxing flat path (equivalent to
        // AllowTypeStruct<T>). Compile-time generic over the concrete T → AOT-safe, no
        // reflection. Must run before BuildTypedDelegates so its flat ctor/operator fast paths
        // see the layout. Component-wise operators/ctors additionally inline (structurally
        // verified, quiet on rejection).
        if (IsFlatStructCandidate(typeof(T)))
        {
            BuildStructLayoutCore<T>();
            InspectAndMarkOperators(typeof(T), warnOnReject: false);
        }
        BuildTypedDelegates<T>();
        return this;
    }

    // A blittable (reference-free) struct registered via AllowType is left boxed — every value
    // crossing the host-call boundary allocates. AllowTypeStruct<T>() flows it through flat numeric
    // frame slots with zero boxing. Warn so the perf footgun is visible at registration time.
    static void WarnIfFlatStructCandidate(Type type)
    {
        if (IsFlatStructCandidate(type))
            Warn(
                $"[IlInterpreter] {type.Name} is a blittable struct registered via AllowType — values box on " +
                $"every host-call boundary. Use AllowTypeStruct<{type.Name}>() for the non-boxing flat path.");
    }

    static bool IsFlatStructCandidate(Type t)
    {
        if (!t.IsValueType || t.IsEnum || t.IsPrimitive || t.IsPointer || t.IsByRef) return false;
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var ft = f.FieldType;
            if (ft.IsPrimitive || ft.IsEnum) continue;
            if (ft.IsValueType && IsFlatStructCandidate(ft)) continue;
            return false; // reference field (or non-blittable) → AllowTypeStruct doesn't apply
        }
        return true;
    }

    // Flat-eligibility used by the AUTOMATIC (non-generic) flattening path: every transitive
    // field must be a fixed-width int/float — 4-byte, or a sub-4 integer handled by the
    // ldfld_vt_{u1,i1,u2,i2} widening / stfld_vt_{b1,b2} truncating ops — an int-backed enum,
    // or a nested struct that qualifies. bool and char stay excluded: their Marshal layout
    // diverges from the managed layout (bool marshals 4-byte, char ANSI 1-byte), so the flat
    // byte image would not match the managed struct. Explicit AllowTypeStruct<T>() keeps the
    // wider IsFlatStructCandidate behavior the caller opted into.
    static bool IsSlot4FlatCandidate(Type t)
    {
        if (!t.IsValueType || t.IsEnum || t.IsPrimitive || t.IsPointer || t.IsByRef) return false;
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var ft = f.FieldType;
            if (ft == typeof(int) || ft == typeof(uint) || ft == typeof(float)) continue;
            if (ft == typeof(byte) || ft == typeof(sbyte) || ft == typeof(short) || ft == typeof(ushort)) continue;
            if (ft.IsEnum && Enum.GetUnderlyingType(ft) == typeof(int)) continue;
            if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum && IsSlot4FlatCandidate(ft)) continue;
            return false;
        }
        return true;
    }

    // Non-generic fast-delegate builder for STATIC methods + static property getters with primitive
    // (or ref) signatures. Static members have no `this`, so Func<float,float>/Func<float>/... bind
    // without the declaring type — which is the only way to fast-path STATIC CLASSES (Mathf, Time,
    // Physics): they can't be generic type args, so BuildTypedDelegates<T> can't run for them, and
    // the non-generic AllowType(Type) MakeGenericMethod path throws on them. Uses no MakeGenericMethod
    // → AOT-safe on IL2CPP. Sets FlatReturnSType so the lowerer allocates a flat (R4/I4) dst slot.
    void BuildStaticPrimitiveFastDelegates(Type type)
    {
        var statBind = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        string tn = type.Name;
        foreach (var m in type.GetMethods(statBind))
        {
            if (m.IsGenericMethodDefinition) continue;
            // Skip operators/set_ accessors; allow get_ property getters (e.g. Time.deltaTime).
            if (m.IsSpecialName && !m.Name.StartsWith("get_", StringComparison.Ordinal)) continue;
            var ps = m.GetParameters();
            // Plain static methods live in _byHandle; property getters (IsSpecialName) live in _entries.
            if (!_byHandle.TryGetValue(m.MethodHandle.Value, out var entry)
                && !_entries.TryGetValue($"{tn}.{m.Name}/{ps.Length}", out entry)) continue;
            if (entry.Fast != null) continue; // already fast (e.g. ref type via BuildTypedDelegates)
            var rt = m.ReturnType;

            if (rt == typeof(float))
            {
                entry.FlatReturnSType = SType.R4; entry.FastIsFlat = true;
                if (ps.Length == 0)
                {
                    var del = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), m);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del());
                }
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(float))
                {
                    var del = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), m);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(NumReadFloat(num, ref_, slotT, bp + (int)ir[argBase])));
                }
                else if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float))
                {
                    var del = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), m);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    { int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                      Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(NumReadFloat(num, ref_, slotT, s0), NumReadFloat(num, ref_, slotT, s1))); };
                }
                else if (ps.Length == 3 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float) && ps[2].ParameterType == typeof(float))
                {
                    var del = (Func<float, float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float, float>), m);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    { int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1], s2 = bp + (int)ir[argBase + 2];
                      Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(NumReadFloat(num, ref_, slotT, s0), NumReadFloat(num, ref_, slotT, s1), NumReadFloat(num, ref_, slotT, s2))); };
                }
                else { entry.FastIsFlat = false; entry.FlatReturnSType = SType.O; } // unhandled arity — leave slow
            }
            else if (rt == typeof(int))
            {
                entry.FlatReturnSType = SType.I4; entry.FastIsFlat = true;
                if (ps.Length == 0)
                {
                    var del = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), m);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del());
                }
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                {
                    var del = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), m);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(NumReadInt(num, ref_, slotT, bp + (int)ir[argBase])));
                }
                else if (ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int))
                {
                    var del = (Func<int, int, int>)Delegate.CreateDelegate(typeof(Func<int, int, int>), m);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    { int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                      Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(NumReadInt(num, ref_, slotT, s0), NumReadInt(num, ref_, slotT, s1))); };
                }
                else { entry.FastIsFlat = false; entry.FlatReturnSType = SType.O; }
            }
            else if (ps.Length == 0 && !rt.IsValueType && rt != typeof(void))
            {
                // static () → reference type (factory/getter): store the ref straight into refFrame.
                var del = (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), m);
                entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) => ref_[bp + dst] = del();
            }
        }
    }

    // Like AllowType<T>() but also installs Unsafe.Unbox-based field accessors for struct types.
    // Replaces FieldInfo.GetValue (~23 ns) with direct byte-frame reads (~2 ns) for float/int fields.
    // IL2CPP-safe: the where T : struct constraint ensures Unsafe.Unbox<T> is AOT-compiled for T.
    // Call this instead of AllowType<T>() for performance-critical struct host types (V3, Color, etc.).
    // operatorsArePure: opt-in S4 operator inlining. When true, the IL of each operator is
    // inspected at registration time; pure arithmetic operators are marked inlineable and the
    // lowerer will synthesize field-by-field IR instead of emitting call_host.
    public HostBinding AllowTypeStruct<T>(bool operatorsArePure = false) where T : struct
    {
        AllowTypeWithBases(typeof(T));
        // Struct field/layout delegates first so BuildTypedDelegates can consult
        // _structLayoutsByType when building flat-write ctor FastFlat paths.
        BuildStructLayoutCore<T>();
        InstallUnsafeStructFieldAccessors<T>();
        BuildTypedDelegates<T>();
        // Operator/ctor inlining is verified STRUCTURALLY (component-wise IL match), so it is
        // always safe to attempt; operatorsArePure now only controls whether rejections warn.
        InspectAndMarkOperators(typeof(T), warnOnReject: operatorsArePure);
        return this;
    }

    // Builds the StructLayout the IR lowerer consumes for flat (Vt-slot) struct support, plus per-field
    // flat metadata (byte offset + SType). Constraint-free so the unconstrained AllowType<T>() can call
    // it to auto-promote a blittable struct to the non-boxing flat path. It's a compile-time generic
    // (each concrete T at a call site), so no reflection MakeGenericMethod — AOT-safe.
    void BuildStructLayoutCore<T>()
    {
        int size = Marshal.SizeOf<T>();
        // Boundary marshalling straight off the unmanaged frame. `(T)boxed` unboxes a copy (no
        // `where T : struct` needed, unlike Unsafe.Unbox<T>); the bytes written are identical,
        // and it's a read-from-box → write-to-frame so a copy is fine.
        BoxFromPtrDelegate box = src => { T val = Unsafe.ReadUnaligned<T>(src); return val!; };
        CopyToPtrDelegate copy = (dst, boxed) => Unsafe.WriteUnaligned(dst, (T)boxed!);
        FinishStructLayout(typeof(T), size, box, copy);
    }

    // Type-only flavor for structs that reach us without a generic type argument — the
    // non-generic AllowType(Type) path that auto-bind uses. The layout DATA is plain reflection;
    // the marshalling delegates use a pinned-box memcpy instead of Unsafe.ReadUnaligned<T>,
    // avoiding the value-type MakeGenericMethod that is unsafe under IL2CPP/AOT. Registration
    // runs a byte round-trip self-check and refuses the flat path (boxed fallback) on any
    // Marshal-vs-managed layout surprise.
    void BuildStructLayoutNonGeneric(Type type)
    {
        int size = Marshal.SizeOf(type);
        BoxFromPtrDelegate box = src =>
        {
            object boxed = Activator.CreateInstance(type)!;
            var h = GCHandle.Alloc(boxed, GCHandleType.Pinned);
            try { Buffer.MemoryCopy(src, (void*)h.AddrOfPinnedObject(), size, size); }
            finally { h.Free(); }
            return boxed;
        };
        CopyToPtrDelegate copy = (dst, boxed) =>
        {
            var h = GCHandle.Alloc(boxed, GCHandleType.Pinned);
            try { Buffer.MemoryCopy((void*)h.AddrOfPinnedObject(), dst, size, size); }
            finally { h.Free(); }
        };

        // Round-trip self-check: a recognizable byte pattern must survive box → copy-back.
        // Catches pinned-layout drift (e.g. a runtime where the box payload isn't the raw
        // struct bytes) at registration time instead of corrupting frames at run time.
        if (size <= 1024)
        {
            byte* probe = stackalloc byte[size];
            byte* echo  = stackalloc byte[size];
            for (int i = 0; i < size; i++) probe[i] = (byte)(0xA5 ^ i);
            try
            {
                copy(echo, box(probe));
                for (int i = 0; i < size; i++)
                    if (echo[i] != probe[i])
                    {
                        Warn($"[IlInterpreter] {type.Name}: pinned-box round-trip mismatch at byte {i} — keeping the boxed path.");
                        return;
                    }
            }
            catch (Exception ex)
            {
                Warn($"[IlInterpreter] {type.Name}: pinned-box marshalling unavailable ({ex.GetType().Name}) — keeping the boxed path.");
                return;
            }
        }

        FinishStructLayout(type, size, box, copy);
    }

    void FinishStructLayout(Type type, int size, BoxFromPtrDelegate box, CopyToPtrDelegate copy)
    {
        var typeName = type.Name;
        var layout = new StructLayout
        {
            Type     = type,
            Size     = size,
            TypeName = typeName,
            Fields   = new Dictionary<string, (int, SType)>(),
            BoxFromPtr = box,
            CopyToPtr  = copy,
        };
        RuntimeHelpers.PrepareDelegate(layout.BoxFromPtr);
        RuntimeHelpers.PrepareDelegate(layout.CopyToPtr);
        var instBind = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var fi in type.GetFields(instBind))
        {
            var key = $"{typeName}.{fi.Name}";
            int byteOffset;
            try { byteOffset = (int)Marshal.OffsetOf(type, fi.Name); }
            catch { continue; } // non-blittable field — skip

            SType pst;
            byte kind = 0; // full 4-byte cell unless a sub-4 primitive
            if (fi.FieldType == typeof(float)) pst = SType.R4;
            else if (fi.FieldType == typeof(int) || fi.FieldType == typeof(uint)
                     || fi.FieldType == typeof(bool)) pst = SType.I4;
            else if (fi.FieldType == typeof(byte))   { pst = SType.I4; kind = 1; }
            else if (fi.FieldType == typeof(sbyte))  { pst = SType.I4; kind = 2; }
            else if (fi.FieldType == typeof(ushort)) { pst = SType.I4; kind = 3; }
            else if (fi.FieldType == typeof(short))  { pst = SType.I4; kind = 4; }
            else if (fi.FieldType.IsEnum && Enum.GetUnderlyingType(fi.FieldType) == typeof(int)) pst = SType.I4;
            else pst = SType.O;

            layout.Fields[fi.Name] = (byteOffset, pst);
            if (kind != 0) (layout.FieldKinds ??= new Dictionary<string, byte>())[fi.Name] = kind;

            if (!_fields.TryGetValue(key, out var fe)) continue;
            fe.DeclaringStruct = layout;
            fe.ByteOffset      = byteOffset;
            fe.PrimitiveSt     = pst;
            fe.PrimitiveKind   = kind;
        }
        _structLayouts[typeName]   = layout;
        _structLayoutsByType[type] = layout;
        MarkTrivialAccessors(type, layout);
    }

    // IL-verify each public property accessor of a flat struct as a single backing-field
    // access; matches get {ldarg.0; ldfld f; ret} and set {ldarg.0; ldarg.1; stfld f; ret}
    // (the exact Release shape of Rect.width-style wrappers). Verified accessors get their
    // backing field's byte offset stamped on the Entry so the lowerer can inline them.
    void MarkTrivialAccessors(Type type, StructLayout layout)
    {
        var tn = type.Name;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            // Offsets come straight from the backing FieldInfo — layout.Fields only carries
            // PUBLIC fields, and Rect-style backing fields (m_Width) are private.
            var g = prop.GetMethod;
            if (g != null && TryReadIl(g, out var gil)
                && TryMatchAccessorIl(gil, g, isSetter: false, out var gfld)
                && TryAccessorField(type, layout, gfld, out int gOff, out SType gSt, out var gVtLay)
                && _entries.TryGetValue($"{tn}.get_{prop.Name}/0", out var gentry))
            {
                gentry.AccessorOffset   = gOff;
                gentry.AccessorSt       = gSt;
                gentry.AccessorVtLayout = gVtLay;
            }
            var st = prop.SetMethod;
            if (st != null && TryReadIl(st, out var sil)
                && TryMatchAccessorIl(sil, st, isSetter: true, out var sfld)
                && TryAccessorField(type, layout, sfld, out int sOff, out SType sSt, out var sVtLay)
                && _entries.TryGetValue($"{tn}.set_{prop.Name}/1", out var sentry))
            {
                sentry.AccessorOffset   = sOff;
                sentry.AccessorSt       = sSt;
                sentry.AccessorVtLayout = sVtLay;
            }
        }
    }

    bool TryAccessorField(Type type, StructLayout layout, FieldInfo fld, out int off, out SType st,
        out StructLayout? vtLay)
    {
        off = 0; st = SType.O; vtLay = null;
        if (fld.DeclaringType != type) return false;
        var ft = fld.FieldType;
        int fieldSize = 4;
        if (ft == typeof(float)) st = SType.R4;
        else if (ft == typeof(int) || ft == typeof(uint)
                 || (ft.IsEnum && Enum.GetUnderlyingType(ft) == typeof(int))) st = SType.I4;
        // Struct-typed backing field (Bounds.m_Center : Vector3) with a flat layout of its own:
        // the accessor becomes a byte-range copy. The field type must already be registered flat —
        // curated types are (Vector3 precedes any auto-bound consumer in CreateStandard).
        else if (ft.IsValueType && !ft.IsEnum && _structLayoutsByType.TryGetValue(ft, out vtLay))
        { st = SType.Vt; fieldSize = vtLay.Size; }
        else return false;
        try { off = (int)Marshal.OffsetOf(type, fld.Name); }
        catch (Exception) { return false; }
        return off >= 0 && off + fieldSize <= layout.Size;
    }

    static bool TryMatchAccessorIl(byte[] il, MethodBase scope, bool isSetter, out FieldInfo field)
    {
        field = null!;
        int idx = 0;
        if (!ExpectLdarg(il, ref idx, 0)) return false;
        if (isSetter && !ExpectLdarg(il, ref idx, 1)) return false;
        byte opByte = isSetter ? (byte)0x7D /* stfld */ : (byte)0x7B /* ldfld */;
        if (idx + 5 > il.Length || il[idx] != opByte) return false;
        int tok = BitConverter.ToInt32(il, idx + 1);
        idx += 5;
        if (idx + 1 != il.Length || il[idx] != 0x2A /* ret */) return false;
        try { field = (FieldInfo)scope.Module.ResolveField(tok)!; }
        catch (Exception) { return false; }
        return field != null;
    }

    // Optimization: replace the reflective float/int field accessors with Unsafe.Unbox-based ones
    // (~2 ns vs ~23 ns) for boxed-struct field access. Requires `where T : struct` (Unsafe.Unbox<T>),
    // so it's only installed via AllowTypeStruct<T>(); AllowType<T> auto-promotion keeps the (correct,
    // slower) reflective accessors for the rare boxed-field case. Run after BuildStructLayoutCore<T>.
    void InstallUnsafeStructFieldAccessors<T>() where T : struct
    {
#if ILINTERPRETER_UNSAFE_UNBOX
        var type = typeof(T);
        var typeName = type.Name;
        var instBind = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var fi in type.GetFields(instBind))
        {
            if (!_fields.TryGetValue($"{typeName}.{fi.Name}", out var fe)) continue;
            int off = fe.ByteOffset;
            if (fi.FieldType == typeof(float))
            {
                fe.Get = obj => (object)Unsafe.ReadUnaligned<float>(ref Unsafe.Add(ref Unsafe.As<T, byte>(ref Unsafe.Unbox<T>(obj!)), off));
                fe.Set = (obj, val) => Unsafe.WriteUnaligned(ref Unsafe.Add(ref Unsafe.As<T, byte>(ref Unsafe.Unbox<T>(obj!)), off), val is float f ? f : val is int i ? (float)i : 0f);
                RuntimeHelpers.PrepareDelegate(fe.Get);
                RuntimeHelpers.PrepareDelegate(fe.Set);
            }
            else if (fi.FieldType == typeof(int))
            {
                fe.Get = obj => (object)Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref Unsafe.As<T, byte>(ref Unsafe.Unbox<T>(obj!)), off));
                fe.Set = (obj, val) => Unsafe.WriteUnaligned(ref Unsafe.Add(ref Unsafe.As<T, byte>(ref Unsafe.Unbox<T>(obj!)), off), val is int i ? i : 0);
                RuntimeHelpers.PrepareDelegate(fe.Get);
                RuntimeHelpers.PrepareDelegate(fe.Set);
            }
        }
#else
        // Unity's bundled System.Runtime.CompilerServices.Unsafe predates Unsafe.Unbox<T> (added in
        // 6.0), so the boxed-struct fast path won't compile here. The reflective float/int accessors
        // installed by AllowTypeWithBases stay in place — correct, just slower for boxed-struct field
        // access (the flat VM path uses byte offsets directly and is unaffected). Define
        // ILINTERPRETER_UNSAFE_UNBOX (and supply a 6.0+ Unsafe) to restore the optimization.
#endif
    }

    internal bool TryGetStructLayout(string typeName, out StructLayout? layout) =>
        _structLayouts.TryGetValue(typeName, out layout);

    internal bool TryGetStructLayout(Type type, out StructLayout? layout) =>
        _structLayoutsByType.TryGetValue(type, out layout);

    // Verify and mark operators/ctors the lowerer may INLINE as per-field arithmetic
    // (EmitInlinedOp). Marking is only safe when the IL is STRUCTURALLY component-wise —
    // "uses only pure opcodes" is not enough (a Quaternion-style op_Multiply is pure IL but
    // not per-field, and name-driven synthesis would silently mis-evaluate it). The verifier
    // accepts exactly:
    //   operator: for each field f_i in layout order: <lhs_i> <rhs_i> ARITH  — where a
    //             struct param contributes `ldarg.k; ldfld f_i` and a scalar param `ldarg.k`
    //             (unary: `ldarg.0; ldfld f_i; neg`) — then `newobj <field-order ctor>; ret`.
    //   ctor:     for each field f_i: `ldarg.0; ldarg.(i+1); stfld f_i` — then `ret`.
    // Anything else (locals, branches, calls, reordered fields, debug-shaped IL) degrades to
    // the normal call path. warnOnReject keeps the explicit AllowTypeStruct(operatorsArePure)
    // opt-in loud while the automatic flatten path stays quiet.
    void InspectAndMarkOperators(Type type, bool warnOnReject = true)
    {
        if (!_structLayoutsByType.TryGetValue(type, out var lay) || lay == null) return;
        var fieldOrder = FieldOrderByOffset(type, lay);
        if (fieldOrder == null) return; // non-numeric or unresolvable fields — nothing to inline

        var statBind = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        (string Name, byte Arith)[] ops =
        {
            ("op_Addition", (byte)0x58), ("op_Subtraction", (byte)0x59),
            ("op_Multiply", (byte)0x5A), ("op_Division", (byte)0x5B),
            ("op_UnaryNegation", (byte)0x65),
        };
        foreach (var (opName, arith) in ops)
        {
            // Overloads of one operator (e.g. op_Multiply(V3,float) and (float,V3)) share ONE
            // Entry, and the entry-level flag can't distinguish them — so mark only when EVERY
            // overload verifies; a single unverified sibling would otherwise inline with the
            // verified overload's semantics.
            bool any = false, all = true;
            foreach (var m in type.GetMethods(statBind))
            {
                if (m.Name != opName) continue;
                any = true;
                if (m.ReturnType == type
                    && VerifyComponentwiseOperator(m, type, fieldOrder, arith, out var ctorUsed)
                    && ctorUsed != null && VerifyFieldOrderCtor(ctorUsed, fieldOrder))
                    continue;
                all = false;
                if (warnOnReject)
                    Warn($"[IlInterpreter] operator {m.Name} on {type.Name} is not component-wise field arithmetic — not inlined (falls back to the call path)");
            }
            if (any && all)
            {
                foreach (var m in type.GetMethods(statBind))
                    if (m.Name == opName && _byHandle.TryGetValue(m.MethodHandle.Value, out var entry))
                        entry.IsInlineableOp = true;
            }
        }
        // The all-fields, field-order ctor: `new V3(x, y, z)` inlines to per-field stores.
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            var ps = ctor.GetParameters();
            if (ps.Length != fieldOrder.Length) continue;
            if (!VerifyFieldOrderCtor(ctor, fieldOrder)) continue;
            var key = $"{type.Name}..ctor/{ps.Length}";
            if (_entries.TryGetValue(key, out var ctorEntry))
                ctorEntry.IsInlineableOp = true;
        }
    }

    // The struct's instance fields sorted by flat byte offset. Null when any field is missing
    // from the layout or is not a 4-byte numeric (the inliner emits I4/R4 arithmetic only).
    FieldInfo[]? FieldOrderByOffset(Type type, StructLayout lay)
    {
        var fis = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (fis.Length == 0 || fis.Length != lay.Fields.Count) return null;
        var order = new FieldInfo[fis.Length];
        var offs  = new int[fis.Length];
        for (int i = 0; i < fis.Length; i++)
        {
            if (!lay.Fields.TryGetValue(fis[i].Name, out var e)) return null;
            if (e.St != SType.I4 && e.St != SType.R4) return null;
            // Sub-4 field: the inliner's per-field 4-byte ops would read/clobber neighbors.
            if (lay.FieldKinds != null && lay.FieldKinds.ContainsKey(fis[i].Name)) return null;
            order[i] = fis[i]; offs[i] = e.Offset;
        }
        for (int a = 0; a < order.Length - 1; a++)
            for (int b = a + 1; b < order.Length; b++)
                if (offs[b] < offs[a])
                {
                    (offs[a], offs[b]) = (offs[b], offs[a]);
                    (order[a], order[b]) = (order[b], order[a]);
                }
        return order;
    }

    static bool TryReadIl(MethodBase m, out byte[] il)
    {
        il = Array.Empty<byte>();
        try
        {
            var body = m.GetMethodBody();
            if (body == null) return false;
            var bytes = body.GetILAsByteArray();
            if (bytes == null || bytes.Length == 0) return false;
            il = bytes;
            return true;
        }
        catch (Exception) { return false; } // AOT runtimes may throw instead of returning null
    }

    static bool ExpectLdarg(byte[] il, ref int idx, int argIndex)
    {
        if (idx >= il.Length) return false;
        byte b = il[idx];
        if (argIndex <= 3 && b == (byte)(0x02 + argIndex)) { idx++; return true; }
        if (b == 0x0E && idx + 1 < il.Length && il[idx + 1] == argIndex) { idx += 2; return true; } // ldarg.s
        return false;
    }

    static bool ExpectFieldToken(byte[] il, ref int idx, byte opByte, MethodBase scope, FieldInfo want)
    {
        if (idx + 5 > il.Length || il[idx] != opByte) return false;
        int tok = BitConverter.ToInt32(il, idx + 1);
        idx += 5;
        if (tok == want.MetadataToken) return true; // FieldDef in the same module — the common case
        try { return scope.Module.ResolveField(tok) == want; }
        catch (Exception) { return false; }
    }

    bool VerifyComponentwiseOperator(MethodInfo m, Type type, FieldInfo[] fieldOrder, byte arith,
        out ConstructorInfo? ctor)
    {
        ctor = null;
        if (!TryReadIl(m, out var il)) return false;
        var ps = m.GetParameters();
        if (ps.Length < 1 || ps.Length > 2) return false;
        bool anyStruct = false;
        var isStructArg = new bool[ps.Length];
        for (int a = 0; a < ps.Length; a++)
        {
            var pt = ps[a].ParameterType;
            if (pt == type) { isStructArg[a] = true; anyStruct = true; }
            else if (pt != typeof(float) && pt != typeof(int)) return false;
        }
        if (!anyStruct) return false;

        int idx = 0;
        for (int f = 0; f < fieldOrder.Length; f++)
        {
            for (int a = 0; a < ps.Length; a++)
            {
                if (!ExpectLdarg(il, ref idx, a)) return false;
                if (isStructArg[a] && !ExpectFieldToken(il, ref idx, 0x7B /* ldfld */, m, fieldOrder[f])) return false;
            }
            if (idx >= il.Length || il[idx++] != arith) return false;
        }
        if (idx + 5 > il.Length || il[idx] != 0x73 /* newobj */) return false;
        int ctorTok = BitConverter.ToInt32(il, idx + 1);
        idx += 5;
        ConstructorInfo? cb;
        try { cb = m.Module.ResolveMethod(ctorTok) as ConstructorInfo; }
        catch (Exception) { return false; }
        if (cb == null || cb.DeclaringType != type) return false;
        if (idx + 1 != il.Length || il[idx] != 0x2A /* ret */) return false;
        ctor = cb;
        return true;
    }

    bool VerifyFieldOrderCtor(ConstructorInfo ctor, FieldInfo[] fieldOrder)
    {
        if (ctor.GetParameters().Length != fieldOrder.Length) return false;
        if (!TryReadIl(ctor, out var il)) return false;
        int idx = 0;
        for (int f = 0; f < fieldOrder.Length; f++)
        {
            if (!ExpectLdarg(il, ref idx, 0)) return false;      // this
            if (!ExpectLdarg(il, ref idx, f + 1)) return false;  // param f
            if (!ExpectFieldToken(il, ref idx, 0x7D /* stfld */, ctor, fieldOrder[f])) return false;
        }
        return idx + 1 == il.Length && il[idx] == 0x2A /* ret */;
    }

    HostBinding AllowTypeWithBases(Type type)
    {
        var t = type;
        while (t != null && t != typeof(object) && t != typeof(ValueType))
        {
            if (!t.IsGenericTypeDefinition && _registeredTypes.Add(t))
            {
                if (t.IsGenericType)
                {
                    var args = t.GetGenericArguments();
                    var key  = (t.Name, args[0].Name);
                    if (!_genericTypes.ContainsKey(key))
                        _genericTypes[key] = t;
                }
                AllowTypeImpl(t);
            }
            t = t.BaseType;
        }
        return this;
    }

    HostBinding AllowTypeImpl(Type type)
    {
        var name     = type.Name;
        var instBind = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var statBind = BindingFlags.Public | BindingFlags.Static  | BindingFlags.DeclaredOnly;

        foreach (var f in type.GetFields(instBind))
        {
            if (SkipType(f.FieldType)) continue;
            RegisterField(name, f);
        }

        // Static fields (ldsfld/stsfld both dispatch through FieldEntry). fi.GetValue(null)/SetValue(null, v)
        // work whether obj is null or not for a static, so the same RegisterField marshalling applies.
        foreach (var f in type.GetFields(statBind))
        {
            if (SkipType(f.FieldType)) continue;
            RegisterField(name, f);
        }

        // Instance properties  →  get_Prop / set_Prop method calls. Accessor arity comes from the
        // accessor itself, NOT a plain-property assumption: an INDEXER's get_Item takes the index
        // (set_Item takes index + value). Hardcoding 0/1 registered `get_Item/0` whose entry
        // popped no argument — the call site then read the index as the RECEIVER
        // (`root[root.childCount-1]` failed with "cannot run on a receiver of type Int32").
        foreach (var p in type.GetProperties(instBind))
        {
            if (SkipType(p.PropertyType)) continue;
            if (p.GetMethod != null && !SkipMethod(p.GetMethod))
            {
                var g = p.GetMethod;
                var gp = g.GetParameters();
                if (gp.Length == 0)
                    Allow(name, g.Name, (recv, _) => g.Invoke(recv, null), 0, mi: g);
                else
                    Allow(name, g.Name, (recv, args) => g.Invoke(recv, args), gp.Length, gp, mi: g);
            }
            if (p.SetMethod != null && !SkipMethod(p.SetMethod))
            {
                var s = p.SetMethod;
                var spms = s.GetParameters();
                Allow(name, s.Name, (recv, args) => { s.Invoke(recv, args); return null; }, spms.Length, spms, mi: s);
            }
        }

        foreach (var p in type.GetProperties(statBind))
        {
            if (SkipType(p.PropertyType)) continue;
            if (p.GetMethod != null && !SkipMethod(p.GetMethod))
            {
                var g = p.GetMethod;
                AllowStatic(name, g.Name, (_, _2) => g.Invoke(null, null), 0, mi: g);
            }
            if (p.SetMethod != null && !SkipMethod(p.SetMethod))
            {
                var s = p.SetMethod;
                AllowStatic(name, s.Name, (_, args) => { s.Invoke(null, args); return null; }, 1, s.GetParameters(), mi: s);
            }
        }

        RegisterMethods(type.GetMethods(instBind), name, isStatic: false);
        RegisterMethods(type.GetMethods(statBind), name, isStatic: true);

        // Operator methods (op_Addition, op_Multiply, op_UnaryNegation, etc.)
        // Group by name so overloads (e.g. Vector3*float and float*Vector3) can be dispatched
        // at call-time by checking the runtime type of the first argument.
        var opByName = new Dictionary<string, List<MethodInfo>>();
        foreach (var m in type.GetMethods(statBind))
        {
            if (!m.IsSpecialName || !m.Name.StartsWith("op_", StringComparison.Ordinal)) continue;
            if (SkipMethod(m)) continue;
            if (!opByName.TryGetValue(m.Name, out var lst)) opByName[m.Name] = lst = new List<MethodInfo>();
            lst.Add(m);
        }
        foreach (var (opName, overloads) in opByName)
        {
            if (overloads.Count == 1)
            {
                var method = overloads[0];
                var mp = method.GetParameters();
                AllowStatic(name, opName, (_, args) => method.Invoke(null, args), mp.Length, mp, mi: method);
            }
            else if (overloads.Count == 2)
            {
                // Two overloads: find first param index where signatures diverge, dispatch on that.
                // Using args[0] alone breaks when both overloads share the same first-arg type
                // (e.g. op_Multiply(Quaternion,Quaternion) vs op_Multiply(Quaternion,Vector3)).
                var m0 = overloads[0];
                var m1 = overloads[1];
                var p0 = m0.GetParameters();
                var p1 = m1.GetParameters();
                int dispatchIdx = 0;
                for (int i = 0; i < p0.Length; i++)
                    if (p0[i].ParameterType != p1[i].ParameterType) { dispatchIdx = i; break; }
                var tDispatch = p0[dispatchIdx].ParameterType;
                AllowStatic(name, opName, (_, args) =>
                {
                    var pick = args[dispatchIdx]?.GetType() == tDispatch ? m0 : m1;
                    return pick.Invoke(null, args);
                }, p0.Length, mi: m0);
                _byHandle[m1.MethodHandle.Value] = _byHandle[m0.MethodHandle.Value];
            }
            else
            {
                // 3+ overloads (StyleLength.op_Implicit(float | Length | StyleKeyword)): the
                // aliased-pair Fast machinery above doesn't scale past two, so these were
                // skipped entirely — leaving the operator unbound whenever the call site's
                // signature didn't decode. Register a name-keyed dispatcher like the same-arity
                // ctor one: pick by the args' runtime types, coerce boxed ints after the pick.
                // Deliberately NOT put in _byHandle (no mi): a call site that resolves its exact
                // MethodInfo synthesizes a per-overload fallback entry instead, and the
                // Fast/inline builders (which attach per-handle) never see this reflective entry.
                var byArity = new Dictionary<int, List<MethodInfo>>();
                foreach (var m in overloads)
                {
                    int n = m.GetParameters().Length;
                    if (!byArity.TryGetValue(n, out var lst)) byArity[n] = lst = new List<MethodInfo>();
                    lst.Add(m);
                }
                foreach (var (arity, group) in byArity)
                {
                    if (group.Count == 1)
                    {
                        var m = group[0];
                        var mp = m.GetParameters();
                        AllowStatic(name, opName, (_, args) => m.Invoke(null, args), arity, mp, mi: m);
                        continue;
                    }
                    var all = group.ToArray();
                    var pss = new ParameterInfo[all.Length][];
                    for (int i = 0; i < all.Length; i++) pss[i] = all[i].GetParameters();
                    AllowStatic(name, opName, (_, args) =>
                    {
                        int pick = PickOverloadByArgs(pss, args);
                        if (pick < 0) pick = 0;
                        CoerceArgs(args, pss[pick]);
                        return all[pick].Invoke(null, args);
                    }, arity);
                }
            }
        }

        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var ctorGroups = new Dictionary<int, List<ConstructorInfo>>();
        foreach (var ctor in ctors)
        {
            if (SkipMethod(ctor)) continue;
            int n = ctor.GetParameters().Length;
            if (!ctorGroups.TryGetValue(n, out var lst)) ctorGroups[n] = lst = new List<ConstructorInfo>();
            lst.Add(ctor);
        }
        foreach (var (arity, overloads) in ctorGroups)
        {
            if (overloads.Count == 1)
            {
                var ctor = overloads[0];
                var cp = ctor.GetParameters();
                AllowConstructor(name, (_, args) => ctor.Invoke(args), arity, cp, ctor);
                continue;
            }
            // Same-arity overloads (e.g. StyleLength(float | Length | StyleKeyword)): one entry
            // that picks the overload by the args' runtime types at call time. Entry.Params stays
            // null — the boxed-int coercions depend on WHICH overload wins, so the delegate
            // coerces after picking instead of letting Entry.Invoke coerce up front.
            var all = overloads.ToArray();
            var pss = new ParameterInfo[all.Length][];
            for (int i = 0; i < all.Length; i++) pss[i] = all[i].GetParameters();
            AllowConstructor(name, (_, args) =>
            {
                int pick = PickOverloadByArgs(pss, args);
                if (pick < 0) pick = 0;
                CoerceArgs(args, pss[pick]);
                return all[pick].Invoke(args);
            }, arity, ci: all[0]);
        }

        return this;
    }

    // Index of the same-arity overload whose parameters best accept the runtime args, -1 when
    // none fits. Exact type matches outscore coercible ones, so `(float)` vs `(MyEnum)` resolves
    // to float for a boxed float and to MyEnum for a boxed int — every I4-slot value (int, bool,
    // enum, …) crosses the boundary as a boxed int, so an int arg matches any CoerceArgs target.
    // Null args match any reference-type parameter.
    static int PickOverloadByArgs(ParameterInfo[][] pss, object?[] args)
    {
        int best = -1, bestScore = -1;
        for (int c = 0; c < pss.Length; c++)
        {
            var ps = pss[c];
            int score = 0; bool ok = true;
            for (int i = 0; i < ps.Length && ok; i++)
            {
                var pt = ps[i].ParameterType;
                var a  = i < args.Length ? args[i] : null;
                if (a == null) { ok = !pt.IsValueType; continue; }
                var at = a.GetType();
                if (at == pt) { score += 2; continue; }
                if (a is int && (pt.IsEnum || pt == typeof(bool) || pt == typeof(char)
                                 || pt == typeof(byte) || pt == typeof(sbyte)
                                 || pt == typeof(short) || pt == typeof(ushort)
                                 || pt == typeof(uint)))
                { score += 1; continue; }
                if (pt.IsAssignableFrom(at)) { score += 1; continue; }
                ok = false;
            }
            if (ok && score > bestScore) { best = c; bestScore = score; }
        }
        return best;
    }

    // Build typed-delegate fast paths for all operators and methods on T that match a known
    // signature shape: replaces the MethodInfo.Invoke-based Delegate with a typed closure and
    // populates Entry.Fast so the interpreter can bypass ArgBuf + Entry.Invoke entirely.
    //
    // Called only from AllowType<T>() where T is a concrete type known at compile time — that's
    // the AOT-safety guarantee: every generic instantiation here is written explicitly; no
    // MakeGenericMethod calls happen at call time.
    void BuildTypedDelegates<T>()
    {
        var type   = typeof(T);
        bool isRef = !type.IsValueType; // open-instance delegates require reference receiver
        // FastIsFlat can only be set when T has a StructLayout — without one the lowerer
        // can't allocate a Vt dst slot, and the flat-write closure would scribble into a
        // boxed-result O slot. AllowType<T> alone (no AllowTypeStruct<T>) leaves T flat-incapable.
        bool flatOk = type.IsValueType && _structLayoutsByType.ContainsKey(type);
        var statBind = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var instBind = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // Safe slot readers used by Fast delegates — a slot's actual frame depends on slotT.
        static float RF(byte* num, object?[] ref_, SType[] slotT, int s)
        {
            var t = slotT[s - FastFrameBase];
            if (t == SType.R4) return Unsafe.ReadUnaligned<float>(num + (s * 4));
            if (t == SType.I4) return (float)Unsafe.ReadUnaligned<int>(num + (s * 4));
            if (t == SType.Vt) return 0f; // Vt refFrame entries are not cleared; never read them
            var v = ref_[s]; return v is float f ? f : v is int i ? (float)i : 0f;
        }
        static int RI(byte* num, object?[] ref_, SType[] slotT, int s)
        {
            var t = slotT[s - FastFrameBase];
            if (t == SType.I4) return Unsafe.ReadUnaligned<int>(num + (s * 4));
            if (t == SType.R4) return (int)Unsafe.ReadUnaligned<float>(num + (s * 4));
            if (t == SType.Vt) return 0;  // Vt refFrame entries are not cleared; never read them
            var v = ref_[s]; return v is int i ? i : v is bool b ? (b?1:0) : 0;
        }
        // Generic Vt-or-boxed reader for value-type T. Picks flat bytes when the slot is Vt
        // (Stage 2 fast path) or unboxes the refFrame entry otherwise (compatibility with
        // boxed args that flow in from ldfld_o on a class field).
        static TVal RT<TVal>(byte* num, object?[] ref_, SType[] slotT, int s)
        {
            if (slotT[s - FastFrameBase] == SType.Vt) return Unsafe.ReadUnaligned<TVal>(num + (s * 4));
            return (TVal)ref_[s]!;
        }

        // --- Static operators and methods ---
        // T args (including struct types) always live in refFrame O-slots: host call returns are
        // allocated as SType.O (AllocSlot(..., SType.O) in the lowerer), and there is no slot
        // reuse — every AllocSlot call creates a fresh slot with a fixed type for the method's
        // lifetime. So ref_[s] is the correct way to read a T arg regardless of IsValueType.
        //
        // Overloaded operators aliased in AllowTypeImpl (e.g. op_Multiply(V3,float) and
        // op_Multiply(float,V3)) share a single Entry. We handle them separately below:
        // collect the two overloads, build typed delegates for each, then attach a Fast that
        // dispatches by whether ref_[s0] is T (true → T-first overload, false → float-first).
        var seenMethods = new Dictionary<Entry, List<MethodInfo>>(EntryReferenceComparer.Instance);
        foreach (var m in type.GetMethods(statBind))
        {
            if (m.IsGenericMethodDefinition) continue;
            if (!_byHandle.TryGetValue(m.MethodHandle.Value, out var e2)) continue;
            if (!seenMethods.TryGetValue(e2, out var lst)) seenMethods[e2] = lst = new List<MethodInfo>();
            lst.Add(m);
        }
        foreach (var (entry, methods) in seenMethods)
        {
            if (methods.Count == 2)
            {
                // Aliased pair: two overloads sharing one Entry. Build per-overload typed delegates
                // and a Fast that dispatches by whether the first arg is T.
                var mA = methods[0]; var mB = methods[1];
                var psA = mA.GetParameters(); var psB = mB.GetParameters();
                var rtA = mA.ReturnType;
                bool aIsTV = psA[0].ParameterType == type;
                bool bIsTV = psB[0].ParameterType == type;

                // (T,float)→T + (float,T)→T  — the common operator*(V3,float) + operator*(float,V3) case.
                // Guard aIsTV != bIsTV: pairs like (Color,Color)+(Color,float) both have T first and must
                // not enter this path — the wrong method would be passed to CreateDelegate.
                if (psA.Length == 2 && rtA == type && rtA == mB.ReturnType && aIsTV != bIsTV)
                {
                    var mTF = aIsTV ? mA : mB; var mFT = aIsTV ? mB : mA;
                    var delTF = (Func<T, float, T>)Delegate.CreateDelegate(typeof(Func<T, float, T>), mTF);
                    var delFT = (Func<float, T, T>)Delegate.CreateDelegate(typeof(Func<float, T, T>), mFT);
                    entry.Delegate = (_, a) => a[0] is T tv
                        ? (object)delTF(tv, (float)a[1]!)!
                        : (object)delFT((float)a[0]!, (T)a[1]!)!;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                        if (ref_[s0] is T t0) ref_[bp + dst] = delTF(t0, RF(num, ref_, slotT, s1));
                        else                  ref_[bp + dst] = delFT(RF(num, ref_, slotT, s0), (T)ref_[s1]!);
                    };
                    if (flatOk)
                    {
                        entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        {
                            int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                            // Dispatch by which arg looks like T (Vt slot or boxed T).
                            // Either arg may be a boxed T arriving from an ldfld_o on a class field,
                            // so RT<T> handles both Vt and refFrame paths.
                            bool s0IsT = slotT[s0 - FastFrameBase] == SType.Vt
                                ? true
                                : ref_[s0] is T;
                            if (s0IsT)
                                Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                                    delTF(RT<T>(num, ref_, slotT, s0), RF(num, ref_, slotT, s1)));
                            else
                                Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                                    delFT(RF(num, ref_, slotT, s0), RT<T>(num, ref_, slotT, s1)));
                        };
                    }
                }

                // (T,T)→T + (T,float)→T  — Color-like: same first arg, dispatch on second arg type.
                // Both overloads must return T and have T as first arg. Second arg is T for one, float for other.
                // Scoped to same-return-type pairs only (differing-return Quaternion case stays on slow path).
                if (psA.Length == 2 && rtA == type && mB.ReturnType == type && aIsTV && bIsTV)
                {
                    bool aTT = psA[1].ParameterType == type;
                    bool bTT = psB[1].ParameterType == type;
                    bool aTF = psA[1].ParameterType == typeof(float);
                    bool bTF = psB[1].ParameterType == typeof(float);
                    if ((aTT && bTF) || (aTF && bTT))
                    {
                        var mTT = aTT ? mA : mB; var mTF = aTT ? mB : mA;
                        var delTT = (Func<T, T, T>)Delegate.CreateDelegate(typeof(Func<T, T, T>), mTT);
                        var delTF = (Func<T, float, T>)Delegate.CreateDelegate(typeof(Func<T, float, T>), mTF);
                        entry.Delegate = (_, a) => a[1] is T t1
                            ? (object)delTT((T)a[0]!, t1)!
                            : (object)delTF((T)a[0]!, (float)a[1]!)!;
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        {
                            int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                            if (ref_[s1] is T t1v) ref_[bp + dst] = delTT((T)ref_[s0]!, t1v);
                            else                   ref_[bp + dst] = delTF((T)ref_[s0]!, RF(num, ref_, slotT, s1));
                        };
                        if (flatOk)
                        {
                            entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            {
                                int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                                if (slotT[s1 - FastFrameBase] == SType.Vt)
                                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                                        delTT(RT<T>(num, ref_, slotT, s0), RT<T>(num, ref_, slotT, s1)));
                                else
                                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                                        delTF(RT<T>(num, ref_, slotT, s0), RF(num, ref_, slotT, s1)));
                            };
                        }
                    }
                }
                continue;
            }

            // Single overload — build typed Delegate + Fast.
            var method = methods[0];
            var ps = method.GetParameters();
            var rt = method.ReturnType;

            // (T, T) → T  e.g. op_Addition(V3, V3) → V3
            if (ps.Length == 2 && ps[0].ParameterType == type && ps[1].ParameterType == type && rt == type)
            {
                var del = (Func<T, T, T>)Delegate.CreateDelegate(typeof(Func<T, T, T>), method);
                entry.Delegate = (_, a) => (object)del((T)a[0]!, (T)a[1]!)!;
                entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                    ref_[bp + dst] = del((T)ref_[s0]!, (T)ref_[s1]!);
                };
                if (flatOk)
                {
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del(RT<T>(num, ref_, slotT, s0),
                                RT<T>(num, ref_, slotT, s1)));
                    };
                }
                continue;
            }

            // (T, float) → T  e.g. op_Multiply(V3, float) → V3
            if (ps.Length == 2 && ps[0].ParameterType == type && ps[1].ParameterType == typeof(float) && rt == type)
            {
                var del = (Func<T, float, T>)Delegate.CreateDelegate(typeof(Func<T, float, T>), method);
                entry.Delegate = (_, a) => (object)del((T)a[0]!, (float)a[1]!)!;
                entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                    ref_[bp + dst] = del((T)ref_[s0]!, RF(num, ref_, slotT, s1));
                };
                if (flatOk)
                {
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del(RT<T>(num, ref_, slotT, s0),
                                RF(num, ref_, slotT, s1)));
                    };
                }
                continue;
            }

            // (float, T) → T  e.g. op_Multiply(float, V3) → V3
            if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == type && rt == type)
            {
                var del = (Func<float, T, T>)Delegate.CreateDelegate(typeof(Func<float, T, T>), method);
                entry.Delegate = (_, a) => (object)del((float)a[0]!, (T)a[1]!)!;
                entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                    ref_[bp + dst] = del(RF(num, ref_, slotT, s0), (T)ref_[s1]!);
                };
                if (flatOk)
                {
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del(RF(num, ref_, slotT, s0),
                                RT<T>(num, ref_, slotT, s1)));
                    };
                }
                continue;
            }

            // (T, T) → int  e.g. op_Equality, op_LessThan returning bool (int in script)
            if (ps.Length == 2 && ps[0].ParameterType == type && ps[1].ParameterType == type
                && (rt == typeof(int) || rt == typeof(bool)))
            {
                if (rt == typeof(bool))
                {
                    var del = (Func<T, T, bool>)Delegate.CreateDelegate(typeof(Func<T, T, bool>), method);
                    entry.Delegate = (_, a) => del((T)a[0]!, (T)a[1]!) ? (object)1 : 0;
                    // FastIsFlat: write 1/0 int directly into numFrame, no boxing. An int result is
                    // always flat-safe, so no non-flat fallback is needed (RT<T> reads either frame).
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del(RT<T>(num, ref_, slotT, s0), RT<T>(num, ref_, slotT, s1)) ? 1 : 0);
                    };
                }
                else
                {
                    var del = (Func<T, T, int>)Delegate.CreateDelegate(typeof(Func<T, T, int>), method);
                    entry.Delegate = (_, a) => (object)del((T)a[0]!, (T)a[1]!);
                    // FastIsFlat: write int directly into numFrame (always flat-safe).
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del(RT<T>(num, ref_, slotT, s0), RT<T>(num, ref_, slotT, s1)));
                    };
                }
                continue;
            }

            // (T) → float  e.g. Vector3.Magnitude
            if (ps.Length == 1 && ps[0].ParameterType == type && rt == typeof(float))
            {
                var del = (Func<T, float>)Delegate.CreateDelegate(typeof(Func<T, float>), method);
                entry.Delegate = (_, a) => (object)del((T)a[0]!);
                entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase];
                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(RT<T>(num, ref_, slotT, s0)));
                };
                continue;
            }

            // (T, T) → float  e.g. Vector3.Dot, Vector3.Distance
            if (ps.Length == 2 && ps[0].ParameterType == type && ps[1].ParameterType == type && rt == typeof(float))
            {
                var del = (Func<T, T, float>)Delegate.CreateDelegate(typeof(Func<T, T, float>), method);
                entry.Delegate = (_, a) => (object)del((T)a[0]!, (T)a[1]!);
                entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                        del(RT<T>(num, ref_, slotT, s0), RT<T>(num, ref_, slotT, s1)));
                };
                continue;
            }

            // (T) → T  e.g. op_UnaryNegation(V3) → V3
            if (ps.Length == 1 && ps[0].ParameterType == type && rt == type)
            {
                var del = (Func<T, T>)Delegate.CreateDelegate(typeof(Func<T, T>), method);
                entry.Delegate = (_, a) => (object)del((T)a[0]!)!;
                entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase];
                    ref_[bp + dst] = del((T)ref_[s0]!);
                };
                if (flatOk)
                {
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int s0 = bp + (int)ir[argBase];
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del(RT<T>(num, ref_, slotT, s0)));
                    };
                }
                continue;
            }

            // () → T   e.g. static factory like V3.Zero (rare; no args)
            if (ps.Length == 0 && rt == type)
            {
                var del = (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), method);
                entry.Delegate = (_, _) => (object)del()!;
                entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) => ref_[bp + dst] = del();
                if (flatOk)
                {
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del());
                }
                continue;
            }

            // (float, float) → float  e.g. Vector2.Dot, Distance etc.
            if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float) && rt == typeof(float))
            {
                var del = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), method);
                entry.Delegate = (_, a) => (object)del((float)a[0]!, (float)a[1]!);
                entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                        del(RF(num, ref_, slotT, s0), RF(num, ref_, slotT, s1)));
                };
                continue;
            }

            // (float) → float  e.g. Mathf.Sqrt, Abs, Cos, Sin etc.
            if (ps.Length == 1 && ps[0].ParameterType == typeof(float) && rt == typeof(float))
            {
                var del = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), method);
                entry.Delegate = (_, a) => (object)del((float)a[0]!);
                entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                        del(RF(num, ref_, slotT, bp + (int)ir[argBase])));
                continue;
            }

            // (int) → int  e.g. Math.Abs(int), Math.Sign(int)
            if (ps.Length == 1 && ps[0].ParameterType == typeof(int) && rt == typeof(int))
            {
                var del = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), method);
                entry.Delegate = (_, a) => (object)del((int)a[0]!);
                entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                        del(RI(num, ref_, slotT, bp + (int)ir[argBase])));
                continue;
            }

            // (int, int) → int  e.g. Math.Min(int,int), Math.Max(int,int)
            if (ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int) && rt == typeof(int))
            {
                var del = (Func<int, int, int>)Delegate.CreateDelegate(typeof(Func<int, int, int>), method);
                entry.Delegate = (_, a) => (object)del((int)a[0]!, (int)a[1]!);
                entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    int s0 = bp + (int)ir[argBase], s1 = bp + (int)ir[argBase + 1];
                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                        del(RI(num, ref_, slotT, s0), RI(num, ref_, slotT, s1)));
                };
                continue;
            }
        }

        // --- Constructors for value-type T: build flat-write delegates for the common shapes.
        // Optimisation: when every param matches a same-named primitive field on T, we bypass
        // ConstructorInfo.Invoke entirely and write the fields' bytes directly into numFrame at
        // their layout offsets. This is what V3(float,float,float) — the bench's hot path — looks
        // like in practice. The fallback uses ctor.Invoke on a temp args array (slow but correct
        // for ctors that do non-trivial work).
        if (type.IsValueType
            && _structLayoutsByType.TryGetValue(type, out var ctorLayout))
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            // Entries key ctors by NAME/ARITY, and Entry.Invoke dispatches same-arity overloads
            // by runtime arg type. A Fast closure binds ONE ConstructorInfo, so installing it on
            // an ambiguous arity would clobber that dispatch (TriCtor(float) running the int
            // overload) — skip those arities and leave them on the reflective path.
            var ctorArityCounts = new Dictionary<int, int>();
            for (int ci = 0; ci < ctors.Length; ci++)
            {
                int a = ctors[ci].GetParameters().Length;
                ctorArityCounts[a] = ctorArityCounts.TryGetValue(a, out var c) ? c + 1 : 1;
            }
            for (int ci = 0; ci < ctors.Length; ci++)
            {
                var ctor = ctors[ci];
                var cps = ctor.GetParameters();
                if (ctorArityCounts[cps.Length] > 1) continue;
                if (!_entries.TryGetValue($"{type.Name}..ctor/{cps.Length}", out var ctorEntry)) continue;

                // 0-arg ctor: just zero the destination bytes.
                if (cps.Length == 0)
                {
                    int sz = ctorLayout.Size;
                    ctorEntry.FastIsFlat = true; ctorEntry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int dstOff = (bp + dst) * 4;
                        for (int b = 0; b < sz; b++) num[dstOff + b] = 0;
                    };
                    continue;
                }

                // Field-init shortcut: every param has same name and matching primitive type as a
                // declared field on T. Build a per-arg (offset, kind) plan once.
                int[]?  argOffsets = new int[cps.Length];
                int[]?  argKinds   = new int[cps.Length]; // 0 = float, 1 = int
                bool    allFieldInit = true;
                for (int p = 0; p < cps.Length; p++)
                {
                    var pn = cps[p].Name;
                    if (pn == null || !ctorLayout.Fields.TryGetValue(pn, out var fld))
                    { allFieldInit = false; break; }
                    if (cps[p].ParameterType == typeof(float) && fld.St == SType.R4)
                        { argOffsets[p] = fld.Offset; argKinds[p] = 0; }
                    else if (cps[p].ParameterType == typeof(int) && fld.St == SType.I4)
                        { argOffsets[p] = fld.Offset; argKinds[p] = 1; }
                    else
                        { allFieldInit = false; break; }
                }

                if (allFieldInit)
                {
                    int sz = ctorLayout.Size;
                    var offs = argOffsets!;
                    var kinds = argKinds!;
                    ctorEntry.FastIsFlat = true; ctorEntry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int dstOff = (bp + dst) * 4;
                        // Zero any holes so padding bytes match GetUninitializedObject's bytes.
                        for (int b = 0; b < sz; b++) num[dstOff + b] = 0;
                        for (int p = 0; p < offs.Length; p++)
                        {
                            int srcSlot = bp + (int)ir[argBase + p];
                            if (kinds[p] == 0)
                                Unsafe.WriteUnaligned(num + (dstOff + offs[p]),
                                    RF(num, ref_, slotT, srcSlot));
                            else
                                Unsafe.WriteUnaligned(num + (dstOff + offs[p]),
                                    RI(num, ref_, slotT, srcSlot));
                        }
                    };
                    continue;
                }

                // Generic fallback for ctors that do real work: pay the per-call args array
                // and Invoke + Unsafe.As<T,byte> copy. Better than the boxed path because we
                // still skip the heap box for the result and read the args we have flat.
                var captured = ctor;
                int paramCount = cps.Length;
                ctorEntry.FastIsFlat = true; ctorEntry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                {
                    var args = new object?[paramCount];
                    for (int p = 0; p < paramCount; p++)
                    {
                        int s = bp + (int)ir[argBase + p];
                        var pt = cps[p].ParameterType;
                        // Every I4-classified primitive lives in the NUMERIC frame — ref_[s] is
                        // null for them, and ConstructorInfo.Invoke turns a null arg into 0
                        // (silent zeroed fields; caught by the host-surface sweep on ColorLike).
                        if (pt == typeof(float))       args[p] = RF(num, ref_, slotT, s);
                        else if (pt == typeof(int))    args[p] = RI(num, ref_, slotT, s);
                        else if (pt == typeof(bool))   args[p] = RI(num, ref_, slotT, s) != 0;
                        else if (pt == typeof(char))   args[p] = (char)RI(num, ref_, slotT, s);
                        else if (pt == typeof(byte))   args[p] = (byte)RI(num, ref_, slotT, s);
                        else if (pt == typeof(sbyte))  args[p] = (sbyte)RI(num, ref_, slotT, s);
                        else if (pt == typeof(short))  args[p] = (short)RI(num, ref_, slotT, s);
                        else if (pt == typeof(ushort)) args[p] = (ushort)RI(num, ref_, slotT, s);
                        else if (pt == typeof(uint))   args[p] = unchecked((uint)RI(num, ref_, slotT, s));
                        else if (pt.IsEnum)            args[p] = Enum.ToObject(pt, RI(num, ref_, slotT, s));
                        // A flat-struct arg (Vector3 into Bounds..ctor) lives in the numeric
                        // frame — ref_[s] is null there, and Invoke would zero the param.
                        else if (slotT[s] == SType.Vt && pt.IsValueType
                                 && _structLayoutsByType.TryGetValue(pt, out var pLay))
                            args[p] = pLay.BoxFromPtr(num + s * 4);
                        else                           args[p] = ref_[s];
                    }
                    var boxed = captured.Invoke(args);
                    T val = (T)boxed!;
                    Unsafe.WriteUnaligned(num + ((bp + dst) * 4), val);
                };
            }
        }

        // --- Instance methods & property accessors for VALUE-TYPE T with a flat layout ---
        // Open-instance delegates over a struct method take `ref T`, so these closures read
        // the receiver IN PLACE: from its Vt frame slot (no box; mutations land directly in
        // the frame bytes — the lowerer's nested-offset/write-back machinery composes on top),
        // or, for boxed receivers (O slot from a class field), by deferring to Entry.Invoke,
        // whose reflective call mutates the box in place as recvWriteBack expects.
        // FastVtRecv tells the executor the closure handles Vt receivers itself; it reads the
        // receiver slot index from ir[argBase - 3] (the call_host recv operand).
        if (!isRef && flatOk)
        {
            foreach (var m in type.GetMethods(instBind)) // includes get_/set_ accessor methods
            {
                if (m.IsGenericMethodDefinition) continue;
                if (!_byHandle.TryGetValue(m.MethodHandle.Value, out var entry)) continue;
                if (entry.Fast != null) continue;
                var e = entry; // captured by the closures below
                var ps = m.GetParameters();
                var rt = m.ReturnType;

                // () → float  (get_magnitude, get_sqrMagnitude)
                if (ps.Length == 0 && rt == typeof(float))
                {
                    var del = (RefRecvFn<T, float>)Delegate.CreateDelegate(typeof(RefRecvFn<T, float>), m);
                    entry.FastVtRecv = true; entry.FastIsFlat = true;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int rs = (int)ir[argBase - 3];
                        if (slotT[rs] == SType.Vt)
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4))));
                        else
                        {
                            var r = e.Invoke(recv, Array.Empty<object?>());
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), r is float f ? f : r is int ri ? (float)ri : 0f);
                        }
                    };
                    continue;
                }

                // () → int / bool  (get_isNormalized-style)
                if (ps.Length == 0 && (rt == typeof(int) || rt == typeof(bool)))
                {
                    if (rt == typeof(bool))
                    {
                        var del = (RefRecvFn<T, bool>)Delegate.CreateDelegate(typeof(RefRecvFn<T, bool>), m);
                        entry.FastVtRecv = true; entry.FastIsFlat = true;
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        {
                            int rs = (int)ir[argBase - 3];
                            if (slotT[rs] == SType.Vt)
                                Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4))) ? 1 : 0);
                            else
                            {
                                var r = e.Invoke(recv, Array.Empty<object?>());
                                Unsafe.WriteUnaligned(num + ((bp + dst) * 4), r is int ri ? ri : r is bool rb ? (rb ? 1 : 0) : 0);
                            }
                        };
                    }
                    else
                    {
                        var del = (RefRecvFn<T, int>)Delegate.CreateDelegate(typeof(RefRecvFn<T, int>), m);
                        entry.FastVtRecv = true; entry.FastIsFlat = true;
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        {
                            int rs = (int)ir[argBase - 3];
                            if (slotT[rs] == SType.Vt)
                                Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4))));
                            else
                            {
                                var r = e.Invoke(recv, Array.Empty<object?>());
                                Unsafe.WriteUnaligned(num + ((bp + dst) * 4), r is int ri ? ri : 0);
                            }
                        };
                    }
                    continue;
                }

                // () → T  (get_normalized) — dst is a Vt slot (ReturnStruct is set by the parser).
                if (ps.Length == 0 && rt == type)
                {
                    var del = (RefRecvFn<T, T>)Delegate.CreateDelegate(typeof(RefRecvFn<T, T>), m);
                    entry.FastVtRecv = true; entry.FastIsFlat = true;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int rs = (int)ir[argBase - 3];
                        if (slotT[rs] == SType.Vt)
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4))));
                        else
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), (T)e.Invoke(recv, Array.Empty<object?>())!);
                    };
                    continue;
                }

                // () → void  (Normalize())
                if (ps.Length == 0 && rt == typeof(void))
                {
                    var del = (RefRecvAct<T>)Delegate.CreateDelegate(typeof(RefRecvAct<T>), m);
                    entry.FastVtRecv = true;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int rs = (int)ir[argBase - 3];
                        if (slotT[rs] == SType.Vt) del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4)));
                        else e.Invoke(recv, Array.Empty<object?>());
                    };
                    continue;
                }

                // (float) → void  (Scale(f), computed set_ accessors)
                if (ps.Length == 1 && ps[0].ParameterType == typeof(float) && rt == typeof(void))
                {
                    var del = (RefRecvAct1F<T>)Delegate.CreateDelegate(typeof(RefRecvAct1F<T>), m);
                    entry.FastVtRecv = true;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int rs = (int)ir[argBase - 3];
                        float a0 = RF(num, ref_, slotT, bp + (int)ir[argBase]);
                        if (slotT[rs] == SType.Vt) del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4)), a0);
                        else e.Invoke(recv, new object?[] { a0 });
                    };
                    continue;
                }

                // (int) → void
                if (ps.Length == 1 && ps[0].ParameterType == typeof(int) && rt == typeof(void))
                {
                    var del = (RefRecvAct1I<T>)Delegate.CreateDelegate(typeof(RefRecvAct1I<T>), m);
                    entry.FastVtRecv = true;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int rs = (int)ir[argBase - 3];
                        int a0 = RI(num, ref_, slotT, bp + (int)ir[argBase]);
                        if (slotT[rs] == SType.Vt) del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4)), a0);
                        else e.Invoke(recv, new object?[] { a0 });
                    };
                    continue;
                }

                // (float, float) → void and (float, float, float) → void  (Set(x, y[, z]))
                if (rt == typeof(void) && (ps.Length == 2 || ps.Length == 3)
                    && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float)
                    && (ps.Length == 2 || ps[2].ParameterType == typeof(float)))
                {
                    if (ps.Length == 2)
                    {
                        var del = (RefRecvAct2F<T>)Delegate.CreateDelegate(typeof(RefRecvAct2F<T>), m);
                        entry.FastVtRecv = true;
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        {
                            int rs = (int)ir[argBase - 3];
                            float a0 = RF(num, ref_, slotT, bp + (int)ir[argBase]);
                            float a1 = RF(num, ref_, slotT, bp + (int)ir[argBase + 1]);
                            if (slotT[rs] == SType.Vt) del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4)), a0, a1);
                            else e.Invoke(recv, new object?[] { a0, a1 });
                        };
                    }
                    else
                    {
                        var del = (RefRecvAct3F<T>)Delegate.CreateDelegate(typeof(RefRecvAct3F<T>), m);
                        entry.FastVtRecv = true;
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        {
                            int rs = (int)ir[argBase - 3];
                            float a0 = RF(num, ref_, slotT, bp + (int)ir[argBase]);
                            float a1 = RF(num, ref_, slotT, bp + (int)ir[argBase + 1]);
                            float a2 = RF(num, ref_, slotT, bp + (int)ir[argBase + 2]);
                            if (slotT[rs] == SType.Vt) del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4)), a0, a1, a2);
                            else e.Invoke(recv, new object?[] { a0, a1, a2 });
                        };
                    }
                    continue;
                }

                // (float) → float
                if (ps.Length == 1 && ps[0].ParameterType == typeof(float) && rt == typeof(float))
                {
                    var del = (RefRecvFn1F<T, float>)Delegate.CreateDelegate(typeof(RefRecvFn1F<T, float>), m);
                    entry.FastVtRecv = true; entry.FastIsFlat = true;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int rs = (int)ir[argBase - 3];
                        float a0 = RF(num, ref_, slotT, bp + (int)ir[argBase]);
                        if (slotT[rs] == SType.Vt)
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4)), a0));
                        else
                        {
                            var r = e.Invoke(recv, new object?[] { a0 });
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), r is float f ? f : r is int ri ? (float)ri : 0f);
                        }
                    };
                    continue;
                }

                // (int) → int
                if (ps.Length == 1 && ps[0].ParameterType == typeof(int) && rt == typeof(int))
                {
                    var del = (RefRecvFn1I<T, int>)Delegate.CreateDelegate(typeof(RefRecvFn1I<T, int>), m);
                    entry.FastVtRecv = true; entry.FastIsFlat = true;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                    {
                        int rs = (int)ir[argBase - 3];
                        int a0 = RI(num, ref_, slotT, bp + (int)ir[argBase]);
                        if (slotT[rs] == SType.Vt)
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del(ref Unsafe.AsRef<T>(num + ((bp + rs) * 4)), a0));
                        else
                        {
                            var r = e.Invoke(recv, new object?[] { a0 });
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), r is int ri ? ri : 0);
                        }
                    };
                    continue;
                }
            }
        }

        // --- Instance methods: only for reference types (open-instance delegates require ref receiver) ---
        if (isRef)
        {
            foreach (var m in type.GetMethods(instBind))
            {
                if (m.IsSpecialName || m.IsGenericMethodDefinition) continue;
                if (!_byHandle.TryGetValue(m.MethodHandle.Value, out var entry)) continue;
                var ps = m.GetParameters();
                var rt = m.ReturnType;

                // () → float
                if (ps.Length == 0 && rt == typeof(float))
                {
                    var del = (Func<T, float>)Delegate.CreateDelegate(typeof(Func<T, float>), null, m);
                    entry.Delegate = (recv, _) => (object)del((T)recv!);
                    // FastFlat shape for primitive returns: writes float bytes directly into
                    // numFrame, dropping the per-call float box. Lowerer allocates an R4 dst
                    // when this is set on a primitive-returning entry.
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del((T)recv!));
                    continue;
                }

                // () → T
                if (ps.Length == 0 && rt == type)
                {
                    var del = (Func<T, T>)Delegate.CreateDelegate(typeof(Func<T, T>), null, m);
                    entry.Delegate = (recv, _) => (object)del((T)recv!)!;
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        ref_[bp + dst] = del((T)recv!);
                    continue;
                }

                // (T) → void
                if (ps.Length == 1 && ps[0].ParameterType == type && rt == typeof(void))
                {
                    var del = (Action<T, T>)Delegate.CreateDelegate(typeof(Action<T, T>), null, m);
                    entry.Delegate = (recv, a) => { del((T)recv!, (T)a[0]!); return null; };
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        del((T)recv!, (T)ref_[bp + (int)ir[argBase]]!);
                    continue;
                }

                // (float) → void
                if (ps.Length == 1 && ps[0].ParameterType == typeof(float) && rt == typeof(void))
                {
                    var del = (Action<T, float>)Delegate.CreateDelegate(typeof(Action<T, float>), null, m);
                    entry.Delegate = (recv, a) => { del((T)recv!, (float)a[0]!); return null; };
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        del((T)recv!, RF(num, ref_, slotT, bp + (int)ir[argBase]));
                    continue;
                }

                // (int) → void
                if (ps.Length == 1 && ps[0].ParameterType == typeof(int) && rt == typeof(void))
                {
                    var del = (Action<T, int>)Delegate.CreateDelegate(typeof(Action<T, int>), null, m);
                    entry.Delegate = (recv, a) => { del((T)recv!, (int)a[0]!); return null; };
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        del((T)recv!, RI(num, ref_, slotT, bp + (int)ir[argBase]));
                    continue;
                }

                // () → void  (e.g. Jump(), Reset())
                if (ps.Length == 0 && rt == typeof(void))
                {
                    var del = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), null, m);
                    entry.Delegate = (recv, _) => { del((T)recv!); return null; };
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) => del((T)recv!);
                    continue;
                }

                // (float, float) → void and (float, float, float) → void — the Translate/Rotate/
                // Set(x, y[, z]) family. Profiling showed these falling to the reflection slow path
                // (MethodBaseInvoker + per-call arg boxing), ~25% of a host-call-heavy loop.
                if (rt == typeof(void) && (ps.Length == 2 || ps.Length == 3)
                    && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(float)
                    && (ps.Length == 2 || ps[2].ParameterType == typeof(float)))
                {
                    if (ps.Length == 2)
                    {
                        var del = (Action<T, float, float>)Delegate.CreateDelegate(typeof(Action<T, float, float>), null, m);
                        entry.Delegate = (recv, a) => { del((T)recv!, (float)a[0]!, (float)a[1]!); return null; };
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            del((T)recv!,
                                RF(num, ref_, slotT, bp + (int)ir[argBase]),
                                RF(num, ref_, slotT, bp + (int)ir[argBase + 1]));
                    }
                    else
                    {
                        var del = (Action<T, float, float, float>)Delegate.CreateDelegate(typeof(Action<T, float, float, float>), null, m);
                        entry.Delegate = (recv, a) => { del((T)recv!, (float)a[0]!, (float)a[1]!, (float)a[2]!); return null; };
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            del((T)recv!,
                                RF(num, ref_, slotT, bp + (int)ir[argBase]),
                                RF(num, ref_, slotT, bp + (int)ir[argBase + 1]),
                                RF(num, ref_, slotT, bp + (int)ir[argBase + 2]));
                    }
                    continue;
                }

                // (float) → float  (e.g. GetAxis-style scaling helpers)
                if (ps.Length == 1 && ps[0].ParameterType == typeof(float) && rt == typeof(float))
                {
                    var del = (Func<T, float, float>)Delegate.CreateDelegate(typeof(Func<T, float, float>), null, m);
                    entry.Delegate = (recv, a) => (object)del((T)recv!, (float)a[0]!);
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del((T)recv!, RF(num, ref_, slotT, bp + (int)ir[argBase])));
                    continue;
                }

                // (int) → int
                if (ps.Length == 1 && ps[0].ParameterType == typeof(int) && rt == typeof(int))
                {
                    var del = (Func<T, int, int>)Delegate.CreateDelegate(typeof(Func<T, int, int>), null, m);
                    entry.Delegate = (recv, a) => (object)del((T)recv!, (int)a[0]!);
                    entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        Unsafe.WriteUnaligned(num + ((bp + dst) * 4),
                            del((T)recv!, RI(num, ref_, slotT, bp + (int)ir[argBase])));
                    continue;
                }

                // () → S where S is a registered struct (e.g. IWorld.GetPosition() → V3).
                // A Func<T,S> closed over a value-type S is the usual IL2CPP/AOT risk; the
                // instantiation exists in the build because the host type registers it statically.
                if (ps.Length == 0 && rt.IsValueType && _structLayoutsByType.TryGetValue(rt, out var retLay))
                {
                    var helper = typeof(HostBinding).GetMethod(nameof(BindInstReturnsStruct),
                        BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type, rt);
                    helper.Invoke(null, new object[] { entry, m });
                    continue;
                }

                // (S) → void where S is a registered struct (e.g. IWorld.SetPosition(V3)).
                if (ps.Length == 1 && ps[0].ParameterType.IsValueType && rt == typeof(void)
                    && _structLayoutsByType.TryGetValue(ps[0].ParameterType, out var argLay))
                {
                    var helper = typeof(HostBinding).GetMethod(nameof(BindInstTakesStructVoid),
                        BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type, ps[0].ParameterType);
                    helper.Invoke(null, new object[] { entry, m });
                    continue;
                }

                // () → int or () → bool (e.g. Count, IsValid). FastIsFlat is REQUIRED for
                // numeric returns: the lowerer allocates a numeric (I4) dst whenever the
                // method's return type is numeric, so the closure must write numFrame —
                // a boxed write to refFrame leaves the numeric cell uninitialized (zeros on
                // Mono, garbage on IL2CPP: the Transform.childCount player bug).
                if (ps.Length == 0 && (rt == typeof(int) || rt == typeof(bool)))
                {
                    if (rt == typeof(bool))
                    {
                        var del = (Func<T, bool>)Delegate.CreateDelegate(typeof(Func<T, bool>), null, m);
                        entry.Delegate = (recv, _) => del((T)recv!) ? (object)1 : 0;
                        entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del((T)recv!) ? 1 : 0);
                    }
                    else
                    {
                        var del = (Func<T, int>)Delegate.CreateDelegate(typeof(Func<T, int>), null, m);
                        entry.Delegate = (recv, _) => (object)del((T)recv!);
                        entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del((T)recv!));
                    }
                    continue;
                }

                // () → reference type (any object return not handled above). Func<T,object> via
                // reference covariance; store the ref straight into refFrame — no boxing/reflection.
                if (ps.Length == 0 && !rt.IsValueType && rt != typeof(void))
                {
                    var del = (Func<T, object>)Delegate.CreateDelegate(typeof(Func<T, object>), null, m);
                    entry.Delegate = (recv, _) => del((T)recv!);
                    entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                        ref_[bp + dst] = del((T)recv!);
                    continue;
                }
            }
        }

        // --- Instance property getters/setters: only for reference types ---
        if (!isRef) return;
        var typeName = type.Name;
        foreach (var p in type.GetProperties(instBind))
        {
            if (SkipType(p.PropertyType)) continue;
            // Indexers don't fit the zero-arg getter shapes below (their /1 keys wouldn't match
            // the /0 lookups anyway, but a same-name /0 twin must never bind a 2-arg accessor).
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.GetMethod != null && !SkipMethod(p.GetMethod))
            {
                var g = p.GetMethod;
                var key = $"{typeName}.{g.Name}/0";
                if (_entries.TryGetValue(key, out var entry))
                {
                    // Numeric getters must be FastIsFlat and write numFrame — the lowerer
                    // allocates a numeric dst for numeric return types (see the () → int
                    // method shape above for the failure mode).
                    if (g.ReturnType == typeof(float))
                    {
                        var del = (Func<T, float>)Delegate.CreateDelegate(typeof(Func<T, float>), null, g);
                        entry.Delegate = (recv, _) => (object)del((T)recv!);
                        entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del((T)recv!));
                    }
                    else if (g.ReturnType == type)
                    {
                        var del = (Func<T, T>)Delegate.CreateDelegate(typeof(Func<T, T>), null, g);
                        entry.Delegate = (recv, _) => (object)del((T)recv!)!;
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            ref_[bp + dst] = del((T)recv!);
                    }
                    else if (g.ReturnType == typeof(bool))
                    {
                        var del = (Func<T, bool>)Delegate.CreateDelegate(typeof(Func<T, bool>), null, g);
                        entry.Delegate = (recv, _) => del((T)recv!) ? (object)1 : 0;
                        entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del((T)recv!) ? 1 : 0);
                    }
                    else if (g.ReturnType == typeof(int))
                    {
                        var del = (Func<T, int>)Delegate.CreateDelegate(typeof(Func<T, int>), null, g);
                        entry.Delegate = (recv, _) => (object)del((T)recv!);
                        entry.FastIsFlat = true; entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del((T)recv!));
                    }
                    else if (!g.ReturnType.IsValueType)
                    {
                        // Returns a reference type (e.g. GameObject.transform → Transform). Func<T,object>
                        // binds via reference covariance; the result is already an object, so store it
                        // straight into refFrame — no boxing, no reflection. AOT-safe (closed over T).
                        var del = (Func<T, object>)Delegate.CreateDelegate(typeof(Func<T, object>), null, g);
                        entry.Delegate = (recv, _) => del((T)recv!);
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            ref_[bp + dst] = del((T)recv!);
                    }
                }
            }
            if (p.SetMethod != null && !SkipMethod(p.SetMethod))
            {
                var s = p.SetMethod;
                var pt = s.GetParameters()[0].ParameterType;
                var key = $"{typeName}.{s.Name}/1";
                if (_entries.TryGetValue(key, out var entry))
                {
                    if (pt == typeof(float))
                    {
                        var del = (Action<T, float>)Delegate.CreateDelegate(typeof(Action<T, float>), null, s);
                        entry.Delegate = (recv, a) => { del((T)recv!, (float)a[0]!); return null; };
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            del((T)recv!, RF(num, ref_, slotT, bp + (int)ir[argBase]));
                    }
                    else if (pt == type)
                    {
                        var del = (Action<T, T>)Delegate.CreateDelegate(typeof(Action<T, T>), null, s);
                        entry.Delegate = (recv, a) => { del((T)recv!, (T)a[0]!); return null; };
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            del((T)recv!, (T)ref_[bp + (int)ir[argBase]]!);
                    }
                    else if (pt == typeof(int))
                    {
                        var del = (Action<T, int>)Delegate.CreateDelegate(typeof(Action<T, int>), null, s);
                        entry.Delegate = (recv, a) => { del((T)recv!, (int)a[0]!); return null; };
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            del((T)recv!, RI(num, ref_, slotT, bp + (int)ir[argBase]));
                    }
                    else if (pt == typeof(bool))
                    {
                        var del = (Action<T, bool>)Delegate.CreateDelegate(typeof(Action<T, bool>), null, s);
                        entry.Delegate = (recv, a) => { del((T)recv!, a[0] is int iv ? iv != 0 : (bool)a[0]!); return null; };
                        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
                            del((T)recv!, RI(num, ref_, slotT, bp + (int)ir[argBase]) != 0);
                    }
                }
            }
        }

    }

    // Templates instantiated via reflection from BuildTypedDelegates<T> when an instance method
    // either returns or accepts a registered host struct. Cross-type generic shapes can't be
    // expressed inline because BuildTypedDelegates only knows about T, not the struct type S.
    // The reflection step (MakeGenericMethod with a value-type S) is the same IL2CPP risk that
    // any AllowTypeStruct + AllowType combination already takes, and is exercised at registration
    // time so the closed delegate is hot before the script runs.
    static void BindInstReturnsStruct<TRecv, TStruct>(Entry entry, MethodInfo m)
        where TStruct : struct
    {
        var del = (Func<TRecv, TStruct>)Delegate.CreateDelegate(typeof(Func<TRecv, TStruct>), null, m);
        entry.Delegate = (recv, _) => (object)del((TRecv)recv!)!;
        entry.FastIsFlat = true;
        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
            Unsafe.WriteUnaligned(num + ((bp + dst) * 4), del((TRecv)recv!));
    }

    static void BindInstTakesStructVoid<TRecv, TStruct>(Entry entry, MethodInfo m)
        where TStruct : struct
    {
        var del = (Action<TRecv, TStruct>)Delegate.CreateDelegate(typeof(Action<TRecv, TStruct>), null, m);
        entry.Delegate = (recv, a) => { del((TRecv)recv!, (TStruct)a[0]!); return null; };
        entry.FastIsFlat = true;
        entry.Fast = (recv, num, ref_, slotT, ir, argBase, dst, bp) =>
        {
            int s0 = bp + (int)ir[argBase];
            TStruct val = slotT[s0 - FastFrameBase] == SType.Vt
                ? Unsafe.ReadUnaligned<TStruct>(num + (s0 * 4))
                : (TStruct)ref_[s0]!;
            del((TRecv)recv!, val);
        };
    }

    // Flat-frame readers shared between BuildTypedDelegates and AllowBcl fast-path helpers.
    // These mirror the local RF/RI statics inside BuildTypedDelegates but are accessible
    // as private static members so AllowBcl can close over them without a generic instantiation.
    static float NumReadFloat(byte* num, object?[] ref_, SType[] slotT, int s)
    {
        var t = slotT[s - FastFrameBase];
        if (t == SType.R4) return Unsafe.ReadUnaligned<float>(num + (s * 4));
        if (t == SType.I4) return (float)Unsafe.ReadUnaligned<int>(num + (s * 4));
        if (t == SType.Vt) return 0f; // Vt refFrame entries are not cleared; never read them
        var v = ref_[s]; return v is float f ? f : v is int i ? (float)i : 0f;
    }

    static double NumReadDouble(byte* num, object?[] ref_, SType[] slotT, int s)
    {
        var t = slotT[s - FastFrameBase];
        if (t == SType.R8) return Unsafe.ReadUnaligned<double>(num + (s * 4));
        if (t == SType.R4) return Unsafe.ReadUnaligned<float>(num + (s * 4));
        if (t == SType.I4) return Unsafe.ReadUnaligned<int>(num + (s * 4));
        if (t == SType.I8) return Unsafe.ReadUnaligned<long>(num + (s * 4));
        if (t == SType.Vt) return 0d;
        var v = ref_[s]; return v is double d ? d : v is float f ? f : v is int i ? i : v is long l ? l : 0d;
    }

    static int NumReadInt(byte* num, object?[] ref_, SType[] slotT, int s)
    {
        var t = slotT[s - FastFrameBase];
        if (t == SType.I4) return Unsafe.ReadUnaligned<int>(num + (s * 4));
        if (t == SType.R4) return (int)Unsafe.ReadUnaligned<float>(num + (s * 4));
        if (t == SType.Vt) return 0;  // Vt refFrame entries are not cleared; never read them
        var v = ref_[s]; return v is int i ? i : v is bool b ? (b?1:0) : 0;
    }

    // Attach a FastIsFlat float-returning fast path to an already-registered entry.
    // Sets FlatReturnSType = R4 so the lowerer allocates an R4 dst slot (required for
    // flat writes when Method is null, e.g. AllowBcl hand-rolled entries).
    // No-ops if the entry is not found (e.g. if AllowStatic was never called for it).
    void AttachFlatDouble(string typeName, string methodName, int paramCount, FastCallDelegate fast)
    {
        var key = $"{typeName}.{methodName}/{paramCount}";
        if (!_entries.TryGetValue(key, out var entry)) return;
        entry.FastIsFlat = true;
        entry.FlatReturnSType = SType.R8;
        entry.FastWideOk = true;
        entry.Fast = fast;
    }

    void RegisterMethods(MethodInfo[] methods, string typeName, bool isStatic)
    {
        // All non-special methods go directly into _byHandle; signature-based MemberRef
        // resolution at parse time picks the exact overload via GetMethod + handle.
        foreach (var m in methods)
        {
            if (m.IsSpecialName || SkipMethod(m)) continue;
            if (m.IsGenericMethodDefinition)
            {
                var openKey = (typeName, m.Name, m.GetParameters().Length);
                if (!_openGenericMethods.TryGetValue(openKey, out var overloads))
                    _openGenericMethods[openKey] = overloads = new List<MethodInfo>();
                overloads.Add(m);
                continue;
            }
            var mp = m.GetParameters();
            var captured = m;
            _byHandle[m.MethodHandle.Value] = new Entry
            {
                Delegate      = isStatic ? (_, args) => captured.Invoke(null, args) : (recv, args) => captured.Invoke(recv, args),
                ParamCount    = mp.Length,
                HasThis       = !isStatic,
                Params        = mp,
                Method        = m,
                DeclaringType = m.DeclaringType,
            };
        }
    }

    static bool SkipType(Type t) => t.IsPointer || t.IsByRef || t.IsGenericTypeDefinition;

    // IsGenericMethodDefinition is NOT skipped — RegisterMethods diverts those into
    // _openGenericMethods for lazy instantiation at script load time. Byref (ref/out)
    // params are supported; only pointer params disqualify a method.
    static bool SkipMethod(MethodBase m)
    {
        foreach (var p in m.GetParameters())
            if (p.ParameterType.IsPointer) return true;
        return false;
    }

    // Test helper; constructors resolve via methodName ".ctor".
    internal bool HasMethod(string typeName, string methodName, int arity) =>
        _entries.ContainsKey($"{typeName}.{methodName}/{arity}");

    internal bool TryGet(string key, int arity, [NotNullWhen(true)] out Entry? entry) =>
        _entries.TryGetValue($"{key}/{arity}", out entry);

    internal bool TryGet(string key, [NotNullWhen(true)] out Entry? entry) =>
        _entries.TryGetValue(key, out entry);

    internal bool TryGetField(string key, [NotNullWhen(true)] out FieldEntry? entry) =>
        _fields.TryGetValue(key, out entry);

    internal IReadOnlyCollection<string> Collisions => _collisions;
    internal IReadOnlyCollection<Type> RegisteredTypes => _registeredTypes;

    internal bool TryGetByHandle(IntPtr handle, [NotNullWhen(true)] out Entry? entry) =>
        _byHandle.TryGetValue(handle, out entry);

    internal bool TryGetGenericType(string outerName, string arg0Name, out Type? type) =>
        _genericTypes.TryGetValue((outerName, arg0Name), out type);

    internal bool TryGetOpenGenerics(string typeName, string methodName, int arity, out List<MethodInfo>? overloads)
    {
        if (_openGenericMethods.TryGetValue((typeName, methodName, arity), out var lst) && lst.Count > 0)
        { overloads = lst; return true; }
        overloads = null; return false;
    }

    internal bool TryGetTypeByName(string name, out Type? type)
    {
        foreach (var t in _registeredTypes)
            if (t.Name == name) { type = t; return true; }
        type = null; return false;
    }

    // Namespace-qualified variant: simple names collide across namespaces (UnityEngine.Application
    // vs UnityEngine.WSA/Device.Application) and the first registered twin would win above.
    internal bool TryGetTypeByFullName(string fullName, out Type? type)
    {
        foreach (var t in _registeredTypes)
            if (t.FullName == fullName) { type = t; return true; }
        type = null; return false;
    }

    // Resolve a CLR full name through AutoBindResolver and AllowType the result on first use.
    // Returns true when the type is registered after the call — whether this call bound it or a
    // prior one did. Open generic definitions can't be AllowType'd directly, but they're
    // remembered by short name so TryMakeClosedGenericType can instantiate them on demand when a
    // TypeSpec member (e.g. AsyncOperationHandle`1<GameObject>.op_Implicit) needs the closed type.
    internal bool TryAutoBindType(string fullName)
    {
        var resolver = AutoBindResolver;
        if (resolver == null) return false;
        if (!_autoBindCache.TryGetValue(fullName, out var t))
        {
            try { t = resolver(fullName); } catch { t = null; }
            _autoBindCache[fullName] = t;
        }
        if (t == null || t.IsPointer || t.IsByRef) return false;
        if (t.IsGenericTypeDefinition)
        {
            if (!_openGenericTypeDefs.ContainsKey(t.Name)) _openGenericTypeDefs[t.Name] = t;
            return false;
        }
        if (!_registeredTypes.Contains(t)) AllowType(t);
        return true;
    }

    // Open generic type definitions seen by the auto-bind pass, keyed by metadata short name
    // ("AsyncOperationHandle`1"). Source for on-demand closed-type construction below.
    readonly Dictionary<string, Type> _openGenericTypeDefs = new();

    // Construct and register <paramref name="outerName"/>&lt;arg0&gt; from an auto-bind-seen open
    // definition and an already-registered type argument. Registering via AllowType also populates
    // _genericTypes, so subsequent members of the same instantiation hit TryGetGenericType directly.
    // Runtime MakeGenericType is safe on IL2CPP when the closed type exists in the build's metadata
    // (it does whenever the reloaded source's original assembly used it) — same caveat as
    // GetOrMakeClosedMethod for generic methods.
    internal bool TryMakeClosedGenericType(string outerName, string arg0Name, out Type? closed)
    {
        closed = null;
        if (!_openGenericTypeDefs.TryGetValue(outerName, out var def)) return false;
        if (def.GetGenericArguments().Length != 1) return false;
        if (!TryGetTypeByName(arg0Name, out var arg) || arg == null) return false;
        try { closed = def.MakeGenericType(arg); } catch { return false; }
        if (!_registeredTypes.Contains(closed)) AllowType(closed);
        return true;
    }

    internal MethodInfo GetOrMakeClosedMethod(MethodInfo openDef, Type[] typeArgs)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < typeArgs.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(typeArgs[i].FullName);
        }
        var key = (openDef.MethodHandle.Value, sb.ToString());
        if (_closedCache.TryGetValue(key, out var cached)) return cached;
        var closed = openDef.MakeGenericMethod(typeArgs);
        _closedCache[key] = closed;
        return closed;
    }
}

#if UNITY_6000_5_OR_NEWER
[NoAutoStaticsCleanup]
#endif
sealed class EntryReferenceComparer : IEqualityComparer<HostBinding.Entry>
{
    public static readonly EntryReferenceComparer Instance = new();
    public bool Equals(HostBinding.Entry? x, HostBinding.Entry? y) => ReferenceEquals(x, y);
    public int GetHashCode(HostBinding.Entry obj) => RuntimeHelpers.GetHashCode(obj);
}
}
