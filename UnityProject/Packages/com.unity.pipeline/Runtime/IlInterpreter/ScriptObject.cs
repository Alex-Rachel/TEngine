#nullable enable
using System;
using System.Collections.Generic;
namespace IlInterpreter.Interpreter
{

// Describes a script-defined class: maps field tokens to typed offsets.
internal sealed class ScriptTypeDescriptor
{
    // Non-nullable fields are always set by the descriptor-building pass in Parse;
    // `= null!` marks that invariant.
    public string Name = null!;
    public int    FieldCount;
    // FieldDef metadata token → slot index in ScriptObject.Fields
    // (kept for write-back path in call_host_byref; slot index = field index)
    public Dictionary<int, int> FieldSlots = null!;
    // Per-field type tag (parallel to field declaration order 0..FieldCount-1)
    public SType[] FieldTypes = null!;
    // Per-field storage offset:
    //   I4/R4 fields: byte offset into ScriptObject.PrimBytes
    //   O fields:     index into ScriptObject.RefSlots
    //   Vt fields:    byte offset into ScriptObject.PrimBytes (inline storage)
    public int[] FieldOffsets = null!;
    public int PrimByteSize;   // total size of PrimBytes array
    public int RefSlotCount;   // total length of RefSlots array
    // Per-field struct layout, non-null only for SType.Vt fields.
    // Length = FieldCount. Parallel to FieldTypes/FieldOffsets.
    public HostBinding.StructLayout?[]? VtFieldLayouts;
    // Per-field flag: true when field i is a SCRIPT-defined struct stored in an O slot (a value type
    // masquerading as a ScriptObject reference). Non-null only when at least one such field exists.
    // Parallel to FieldTypes/FieldOffsets. Used by Clone() to deep-copy nested value-type fields so a
    // struct copy does not alias the source's nested structs (genuine reference fields stay shared).
    public bool[]? FieldIsScriptStruct;
    // Per-field descriptor of a script-struct field's own type (parallel to FieldTypes). Non-null entry
    // only for a resolved script-struct field; used by ScriptObject.Create to recursively allocate the
    // nested struct so a value-type field is never null (matching C#'s zero-init of value fields).
    public ScriptTypeDescriptor?[]? FieldStructDescriptors;
    // True when this type is a script-defined STRUCT (value type, not an enum, not a class). Used to
    // zero-init the elements of `new T[n]` — a value-type array holds usable structs, not nulls.
    public bool IsScriptStructValue;

    // Base script type (null = no script base). Set by Parse's post-pass; used with VirtualsBySig
    // to resolve callvirt targets from the RECEIVER's runtime type (script virtual dispatch).
    public ScriptTypeDescriptor? BaseDesc;
    // Runs a script ToString override (a VirtualsBySig value) on a receiver — installed by
    // ScriptInterpreter.Load so ScriptObject.ToString can dispatch without referencing the
    // interpreter's nested types. Null until the owning interpreter loads the assembly.
    public Func<ScriptObject, object, string>? ScriptToStringInvoker;
    // "name/argCount" → this type's own virtual (or override) instance method. Resolution walks
    // the BaseDesc chain from the receiver's descriptor; first hit wins (standard override rules).
    public System.Collections.Generic.Dictionary<string, object>? VirtualsBySig;

    // Key of VirtualsBySig: method name + the raw method signature blob, so overloads that share a
    // name and arity still dispatch separately. Same-assembly overrides have byte-identical blobs.
    public static string VirtualKey(string name, byte[] signature)
        => name + "/" + System.BitConverter.ToString(signature);

    // `string ToString()`: HASTHIS calling convention, 0 params, STRING return.
    public static readonly string ToStringVirtualKey = VirtualKey("ToString", new byte[] { 0x20, 0x00, 0x0E });

    // Per-field wide kind: 0 = not wide, 1 = long/ulong (I8), 2 = double (R8). Non-null only when
    // at least one wide field exists. Wide fields are stored BOXED in RefSlots; Create pre-boxes a
    // typed zero (0L / 0d) so a default instance's wide field reads 0 everywhere — a null slot
    // renders "" through ToString/format consumers where C# prints 0.
    public byte[]? FieldWideKinds;

    // Non-null when this script struct is BLITTABLE (no reference fields, transitively) and is
    // represented FLAT: locals/args/returns live as raw bytes in Vt frame slots (the flat image is
    // identical to the PrimBytes image), and only O-boundary crossings materialize a boxed
    // ScriptObject via the layout's marshallers. Synthesized by the parser's flat-resolution pass.
    public HostBinding.StructLayout? FlatLayout;
}

// An instance of a script-defined class.
// Primitives (I4, R4) live in PrimBytes at their FieldOffsets[i] byte offset.
// References (O, boxed Vt) live in RefSlots at their FieldOffsets[i] ref index.
internal sealed class ScriptObject
{
    // Create and Clone (the only construction sites) always set all three fields;
    // `= null!` marks that invariant.
    public ScriptTypeDescriptor Type = null!;
    public byte[]    PrimBytes = null!;   // typed primitive storage (I4/R4/bool)
    public object?[] RefSlots = null!;    // reference fields only (O slots)

    // Allocate an instance of a script class/struct, recursively initializing nested SCRIPT-struct
    // fields to fresh zeroed instances. A value-type field is never null in C# (`new Outer()` gives a
    // usable `inner`), so `a.inner.v = x` must not dereference null. Recursion terminates because
    // value-type containment is acyclic (a struct cannot contain itself by value).
    public static ScriptObject Create(ScriptTypeDescriptor desc) => Create(desc, 0);

    static ScriptObject Create(ScriptTypeDescriptor desc, int depth)
    {
        // C# forbids value-type containment cycles, but hostile METADATA doesn't — unbounded
        // recursion here is an uncatchable StackOverflowException (the PE-corruption fuzzing
        // class). 64 levels of by-value struct nesting is far beyond anything real.
        if (depth > 64)
            throw new InvalidOperationException($"struct nesting too deep (or cyclic) creating '{desc.Name}'");
        var o = new ScriptObject
        {
            Type      = desc,
            PrimBytes = new byte[desc.PrimByteSize],
            RefSlots  = new object?[desc.RefSlotCount],
        };
        var subs = desc.FieldStructDescriptors;
        if (subs != null)
        {
            var offs = desc.FieldOffsets;
            for (int i = 0; i < subs.Length; i++)
                if (subs[i] != null)
                    o.RefSlots[offs[i]] = Create(subs[i]!, depth + 1);
        }
        var wides = desc.FieldWideKinds;
        if (wides != null)
        {
            var offs = desc.FieldOffsets;
            for (int i = 0; i < wides.Length; i++)
                if (wides[i] != 0)
                    o.RefSlots[offs[i]] = wides[i] == 2 ? (object)0d : 0L; // typed zero (see FieldWideKinds)
        }
        return o;
    }

    // A script-defined ToString override must win EVERYWHERE .NET formats this object —
    // string.Concat/Format call it on the box directly, not through the interpreter — so
    // dispatch here, walking the descriptor chain like call_script_virt. Without an override,
    // the script-visible type name (what C#'s default ToString prints for a top-level script
    // type) beats leaking "IlInterpreter.Interpreter.ScriptObject".
    public override string ToString()
    {
        for (var d = Type; d != null; d = d.BaseDesc)
            if (d.VirtualsBySig != null && d.VirtualsBySig.TryGetValue(ScriptTypeDescriptor.ToStringVirtualKey, out var m))
                return Type.ScriptToStringInvoker?.Invoke(this, m) ?? Type.Name;
        return Type.Name;
    }

    // Value-copy for script-struct copy semantics. Genuine reference fields keep their reference
    // shared (a struct copy copies the reference, not its target) — but a NESTED SCRIPT STRUCT is
    // itself a value type stored in an O slot, so it must be deep-copied recursively, or
    // `b = a; b.inner.v = x` would alias a.inner. Recursion terminates because struct nesting
    // is acyclic.
    public ScriptObject Clone()
    {
        var copy = new ScriptObject
        {
            Type      = Type,
            PrimBytes = PrimBytes == null ? null! : (byte[])PrimBytes.Clone(),
            RefSlots  = RefSlots  == null ? null! : (object?[])RefSlots.Clone(),
        };
        var isSc = Type?.FieldIsScriptStruct;
        if (isSc != null && copy.RefSlots != null)
        {
            var offs = Type!.FieldOffsets;
            for (int i = 0; i < isSc.Length; i++)
            {
                if (!isSc[i]) continue;
                int ri = offs[i];
                if ((uint)ri < (uint)copy.RefSlots.Length && copy.RefSlots[ri] is ScriptObject so)
                    copy.RefSlots[ri] = so.Clone();
            }
        }
        return copy;
    }
}

// A script-created array of FLAT structs: one byte[] backing for all elements instead of one
// ScriptObject (3+ allocations) per element. Elements are memcpy'd in/out of Vt frame slots by
// ldelem_vt/stelem_vt. Host-ABI note: like the old object?[] representation, this cannot be
// passed to a host T[] parameter — unchanged limitation.
internal sealed class ScriptVtArray
{
    public HostBinding.StructLayout Layout = null!;
    public byte[] Bytes = null!;
    public int Length;
    public int Stride; // element size rounded up to 4
}
}
