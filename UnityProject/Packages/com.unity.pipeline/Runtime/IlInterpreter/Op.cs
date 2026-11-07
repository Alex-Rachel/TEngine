#nullable enable
using System;
using System.Collections.Generic;
namespace IlInterpreter.Interpreter
{

// Typed IR opcodes for the lowered-IR interpreter.
//
// Type suffixes:
//   _i4     32-bit int (includes bool as 0/1)
//   _r4     32-bit float
//   _o      object reference (string, ScriptObject, host ref, null)
//   _struct boxed value type (flat blittable structs use the _vt ops instead)
//
// Encoding (all in uint[]):
//   [op, dst, src1, src2]              arithmetic, compare
//   [op, dst, imm]                     ldc_i4, ldc_r4, ldnull, ldstr
//   [op, target_ip]                    br
//   [op, cond, target_ip]              brtrue_i4, brfalse_i4, brtrue_o, brfalse_o
//   [op, dst, recv, argc, arg0, ...]   call_static, call_virtual, call_host, newobj
//   [op, dst, src]                     mov, conv_*, ldfld_*, ldsfld_*, ldlen, newarr, ldelem_*, neg_*
//   [op, recv, fldIdx/tok, src]        stfld_*, stsfld_*
//   [op, dst, arr, idx]                ldelem_* (arr=slot, idx=slot)
//   [op, arr, idx, src]                stelem_*
//   [op]                               ret_void
//   [op, src]                          ret_i4, ret_r4, ret_o
enum Op : uint
{
    // --- No-op (usually elided during lowering) ---
    nop,

    // --- Move (type-agnostic; routes between frames by slot type) ---
    mov,            // [op, dst, src]

    // --- Constants ---
    ldc_i4,         // [op, dst, imm_value]      imm is the int value cast to uint
    ldc_r4,         // [op, dst, imm_bits]        imm is BitConverter.SingleToUInt32Bits(f)
    ldnull,         // [op, dst]
    ldstr,          // [op, dst, str_idx]         str_idx indexes into LoweredMethod.Strings

    // --- Integer arithmetic (i4) ---
    add_i4,         // [op, dst, src1, src2]
    add_i4_nn,      // both srcs in numFrame as I4
    sub_i4,
    sub_i4_nn,
    mul_i4,
    mul_i4_nn,
    div_i4,
    div_i4_nn,
    rem_i4,
    rem_i4_nn,
    neg_i4,         // [op, dst, src]
    neg_i4_n,       // src in numFrame as I4

    // --- Float arithmetic (r4) ---
    add_r4,         // [op, dst, src1, src2]
    add_r4_nn,      // both srcs in numFrame as R4
    sub_r4,
    sub_r4_nn,
    mul_r4,
    mul_r4_nn,
    div_r4,
    div_r4_nn,
    rem_r4,
    rem_r4_nn,
    neg_r4,         // [op, dst, src]
    neg_r4_n,       // src in numFrame as R4

    // --- Bitwise (i4 only; float has no bitwise) ---
    and_i4,         // [op, dst, src1, src2]
    and_i4_nn,
    or_i4,
    or_i4_nn,
    xor_i4,
    xor_i4_nn,
    not_i4,         // [op, dst, src]
    not_i4_n,
    shl_i4,         // [op, dst, src1, src2]
    shl_i4_nn,
    shr_i4,
    shr_i4_nn,
    shr_un_i4,
    shr_un_i4_nn,
    div_un_i4,      // unsigned div/rem: uint is analyzer-legal, so `u / 2u` must run
    div_un_i4_nn,
    rem_un_i4,
    rem_un_i4_nn,

    // --- Integer comparisons (produce 1/0 int) ---
    ceq_i4,         // [op, dst, src1, src2]
    ceq_i4_nn,
    cgt_i4,
    cgt_i4_nn,
    clt_i4,
    clt_i4_nn,
    cgt_un_i4,
    cgt_un_i4_nn,
    clt_un_i4,
    clt_un_i4_nn,

    // --- Float comparisons ---
    ceq_r4,         // [op, dst, src1, src2]
    ceq_r4_nn,
    cgt_r4,
    cgt_r4_nn,
    clt_r4,
    clt_r4_nn,
    // Unordered float comparisons — what C# emits for float `<=`/`>=` (via cgt.un/clt.un).
    // cgt_un_r4 = !(a <= b) (true when a > b OR either is NaN); clt_un_r4 = !(a >= b).
    cgt_un_r4,      // [op, dst, src1, src2]
    clt_un_r4,      // [op, dst, src1, src2]

    // --- Object/reference comparisons ---
    ceq_o,          // [op, dst, src1, src2]  — reference equality
    cgt_un_o,       // [op, dst, src1, src2]  — cgt.un on object (null check pattern)

    // --- Branches ---
    br,             // [op, target_ip]
    brtrue_i4,      // [op, cond, target_ip]
    brfalse_i4,     // [op, cond, target_ip]
    brtrue_o,       // [op, cond, target_ip]
    brfalse_o,      // [op, cond, target_ip]

    // --- Conversions ---
    conv_i4_r4,     // [op, dst, src]   int → float
    conv_r4_i4,     // [op, dst, src]   float → int
    conv_i4_i1,     // [op, dst, src]   int → sbyte (sign-extend)
    conv_i4_u1,     // [op, dst, src]   int → byte (zero-extend)
    conv_i4_i2,     // [op, dst, src]   int → short
    conv_i4_u2,     // [op, dst, src]   int → ushort

    // --- Host field access (via token + reflection-style lookup) ---
    // dst = frame slot; obj = frame slot holding receiver; tok = field token index
    ldfld_i4,       // [op, dst, obj, tok_idx]
    ldfld_r4,
    ldfld_o,
    ldfld_struct,
    ldflda,         // [op, dst, obj, tok_idx]  — load field value (same as ldfld_o)
    stfld_i4,       // [op, obj, tok_idx, src]
    stfld_r4,
    stfld_o,
    stfld_struct,

    // --- Script-class field access (flat typed storage) ---
    // For script-defined classes, fields are stored in ScriptObject.PrimBytes (primitives)
    // or ScriptObject.RefSlots (references). Byte offset is baked at lowering time.
    // ldfld_sc_i4/r4: [op, dst, obj, byteOffset]   — read from PrimBytes
    // ldfld_sc_o:     [op, dst, obj, refIdx]        — read from RefSlots
    // stfld_sc_i4/r4: [op, obj, byteOffset, src]   — write to PrimBytes
    // stfld_sc_o:     [op, obj, refIdx, src]        — write to RefSlots
    // ldfld_sc_vt:    [op, dst, obj, byteOffset]    — copy N bytes from PrimBytes into Vt slot
    // stfld_sc_vt:    [op, obj, byteOffset, src]    — copy N bytes from Vt slot into PrimBytes
    ldfld_sc_i4,
    ldfld_sc_r4,
    ldfld_sc_o,
    ldfld_sc_vt,
    stfld_sc_i4,
    stfld_sc_r4,
    stfld_sc_o,
    stfld_sc_vt,

    // --- Static field access ---
    ldsfld_i4,      // [op, dst, tok_idx]
    ldsfld_r4,
    ldsfld_o,
    ldsfld_struct,
    stsfld_i4,      // [op, tok_idx, src]
    stsfld_r4,
    stsfld_o,
    stsfld_struct,

    // --- Arrays ---
    newarr,         // [op, dst, len_src, elem_tok_idx]
    // Typed-backing variants (same 5-word encoding as newarr): when the element type is
    // statically System.Int32 / System.Single the executor allocates a real int[]/float[]
    // instead of object?[], and ldelem/stelem take non-boxing direct paths — an int store
    // into an object?[] element boxes (GC pressure in hot Update bodies) and Mono pays ~3x
    // CoreCLR on that path.
    newarr_i4,
    newarr_r4,
    // Flat-struct element arrays (ScriptVtArray backing): zero-init bytes = C# array semantics,
    // no per-element allocation. Same 5-word encoding as newarr; elem layout resolved at lowering
    // into LoweredMethod.LayoutByTokIdx.
    newarr_vt,
    ldlen,          // [op, dst, arr_src]
    arr_bounds_check, // [op, arr_src, idx_src] — throw IndexOutOfRange if (uint)idx >= len; no dst.
                      // Emitted eagerly at ldelema: .NET bounds-checks AT ldelema, but the
                      // interpreter's element-address phantom defers to the consumer, so an
                      // unconsumed address (unused ref local, release codegen) skipped the check.
    ldelem_i4,      // [op, dst, arr, idx]
    ldelem_r4,
    ldelem_o,
    ldelem_struct,
    stelem_i4,      // [op, arr, idx, src]
    stelem_r4,
    stelem_o,
    stelem_struct,
    // Flat-struct element access: memcpy between the array's byte backing and a Vt frame slot
    // (value semantics without clone_sc). Fall back to boxed forms for object?[]/host T[] arrays.
    ldelem_vt,      // [op, dst(Vt), arr, idx]
    stelem_vt,      // [op, arr, idx, src(Vt)]

    // --- Calls ---
    // [op, dst, recv, argc, arg0, arg1, ...]
    // dst = 0xFFFFFFFF for void calls (ret discarded)
    // recv = 0xFFFFFFFF for static calls
    call_script,    // callee is a script-defined method; tok_idx in recv slot (reuse)
    call_script_virt, // same shape as call_script; re-resolves the callee from the RECEIVER's
                      // runtime ScriptTypeDescriptor (virtual dispatch — callvirt on a virtual
                      // script method; plain `call`/base.M() stays call_script)
    call_host,      // callee is a host entry
    // call_host with byref args. After arg0..argN comes a writeback table:
    //   wbCount, [argIdx, frameSlot, kind] × wbCount
    // kind: 0 = frame slot, 1 = field (frameSlot=objSlot, plus tokIdx in next word),
    //       2 = array element (frameSlot=arrSlot, plus idxSlot in next word).
    // The slow Invoke path is always used (Fast skipped) so MethodInfo.Invoke writes
    // back into the boxed args[] array, which we then copy into the right destination.
    call_host_byref,

    // --- Object construction ---
    newobj_script,  // [op, dst, tok_idx, argc, arg0, ...]  script-defined type ctor
    newobj_host,    // [op, dst, tok_idx, argc, arg0, ...]  host type ctor
    box_enum,       // [op, dst, src, tok_idx]  box an I4 value as its HOST ENUM type (resolved
                    // via ParsedAssembly.TokenTypes) so ToString/Format render the member name
    new_delegate,   // [op, dst, tok_idx, recv]  delegate creation (ldftn/ldvirtftn + newobj);
                    // tok_idx indexes LoweredMethod.DelegateSiteByTokIdx (pre-resolved target +
                    // delegate type); recv is the receiver's O slot (holds null for statics).
                    // A delegate TYPE naming a SCRIPT type (Action<Node>) has no runtime
                    // System.Type: the executor (DelegateSite.DelegateType == null) builds an
                    // internal ScriptDelegate instead of a CLR Delegate.
    invoke_script_delegate, // [op, dst, tok_idx, recv, argc, arg0, ...]  invoke a ScriptDelegate
                    // held in O slot recv; dst = 0xFFFFFFFF for a void delegate. Re-enters the VM
                    // on the target (captured receiver prepended) through InvokeDelegateTarget,
                    // like the CLR ScriptDelegateAdapter.

    // --- Misc ---
    box,            // [op, dst, src]  — read slot as object into refFrame
    box_prim,       // [op, dst, src, typecode] — box a primitive as its TRUE type (bool/char/… not int)
    unbox_any,      // [op, dst, src, tok_idx]
    castclass,      // [op, dst, src, tok_idx]
    isinst,         // [op, dst, src, tok_idx]
    ldtoken,        // [op, dst, tok_idx]
    initobj,        // [op, dst]   — zero-initialize slot (for script struct locals)
    switch_i4,      // [op, val, n, ip0, ip1, ...]  variable length; ip_default follows

    // --- Return ---
    ret_void,       // [op]
    ret_i4,         // [op, src]
    ret_r4,         // [op, src]
    ret_o,          // [op, src]
    throw_o,        // [op, src] — `throw` of a script-constructed exception; ends the block like ret
    // Flat-struct return: reads the callee's Vt src bytes, memcpys them into the caller's
    // (Vt) RetDst slot on frame pop; boxes once via the layout at the top-level boundary.
    ret_vt,         // [op, src(Vt)]

    // Zero-initialize a script-defined struct (O slot) without invoking a ctor.
    // Used when the IL emits ldloca + initobj for a parameterless default ctor.
    initobj_script,  // [op, dst, typeDefTokIdx]

    // Ensure a script-defined struct O slot is non-null. Allocates a fresh zero-initialized
    // ScriptObject only if the slot is currently null; leaves it untouched otherwise.
    // Emitted by the stfld lowerer before field writes on struct locals that Roslyn considers
    // "definitely assigned by field stores" (so no initobj in the IL) — our O slots start null.
    ensure_script,   // [op, dst, typeDefTokIdx]

    // Value-copy a script-defined struct: dst = shallow clone of the ScriptObject in src
    // (copies PrimBytes + RefSlots). Emitted when a script struct is loaded as a VALUE
    // (ldloc/ldarg/ldfld/ldelem) so each named location owns its instance — script structs
    // are represented as ScriptObject references, so without this a copy would alias. A null
    // or non-ScriptObject src is passed through unchanged.
    clone_sc,        // [op, dst, src]

    // --- Flat-struct (Vt slots): read/write primitive fields by byte offset, copy/box at boundary ---
    // Vt slots reserve ceil(size/4) consecutive logical slots in numFrame.
    // ldfld_vt_*/stfld_vt_* take a byte offset (within the Vt slot) instead of a token,
    // since the field's offset is resolved at lowering time.
    // box_vt/unbox_vt/mov_vt look up the StructLayout via lm.StructLayouts[src or dst].
    ldfld_vt_i4,    // [op, dst, vtObj, byteOffset]
    ldfld_vt_r4,    // [op, dst, vtObj, byteOffset]
    stfld_vt_i4,    // [op, vtObj, byteOffset, src]
    stfld_vt_r4,    // [op, vtObj, byteOffset, src]
    mov_vt,         // [op, dst, src]   — copy struct bytes (size from layout)
    // Nested flat-struct field access: copy a SUB-RANGE of a Vt slot (a blittable struct field
    // inlined inside another flat struct). Offsets are byte offsets within the receiver slot;
    // the sub-struct's size comes from the dst/src slot's own layout.
    ldfld_vt_vt,    // [op, dst(Vt), vtObj, byteOffset] — memcpy size(dst) bytes out
    stfld_vt_vt,    // [op, vtObj, byteOffset, src(Vt)] — memcpy size(src) bytes in
    // Sub-4-byte fields of flat host structs (Color32.r-class): loads widen into an I4 slot
    // (zero- or sign-extended); stores truncate. Script structs never need these — the parser
    // gives every script field its own 4-byte cell.
    ldfld_vt_u1,    // [op, dst(I4), vtObj, byteOffset]
    ldfld_vt_i1,
    ldfld_vt_u2,
    ldfld_vt_i2,
    stfld_vt_b1,    // [op, vtObj, byteOffset, src] — writes low byte
    stfld_vt_b2,    // [op, vtObj, byteOffset, src] — writes low 2 bytes
    box_vt,         // [op, dst, src]   — read Vt bytes, box as T into refFrame[dst]
    unbox_vt,       // [op, dst, src]   — unbox refFrame[src] into Vt dst bytes
    // Reuse existing newobj_host / call_host opcodes; executor branches on dst's slot type.

    // --- Operand-immediate forms: RHS literal embedded in the IR word ---
    // Encoding: [op, dst, src, k_imm] (4 words). k_imm is the constant value cast to uint
    // (BitConverter.SingleToUInt32Bits for floats). Executor reads it directly; no separate
    // K-table lookup. Emitted by the lowerer when an `ldc_X tmp; binop_X_nn dst, src, tmp`
    // pattern has tmp single-use. Subset is corpus-driven — only ops that show up in bench
    // loops; expand on demand. Compare ops cover RHS-constant only (the canonical loop-bound
    // pattern); LHS-constant doesn't appear in the bench corpus.
    add_i4_nk,      // [op, dst, src, k_int]
    add_r4_nk,      // [op, dst, src, k_bits]
    sub_i4_nk,
    sub_r4_nk,
    mul_r4_nk,
    clt_i4_nk,
    clt_r4_nk,
    cgt_i4_nk,
    cgt_r4_nk,
    ceq_i4_nk,
    // Second _nk batch (profiling-driven): masks (`i & 255`), shifts (`acc >> 5`), scaling
    // (`i * 3`) and float division/equality against a literal all showed up as hot
    // `ldc; binop_nn` pairs in the bench op mix. Same [op, dst, src, k] encoding.
    mul_i4_nk,
    div_i4_nk,      // k is the divisor (RHS only); k == 0 still faults like the slot form
    rem_i4_nk,
    and_i4_nk,
    or_i4_nk,
    xor_i4_nk,
    shl_i4_nk,
    shr_i4_nk,
    shr_un_i4_nk,
    div_r4_nk,
    ceq_r4_nk,

    // --- Fused compare-and-branch ---
    // Encoding: [op, s1, s2_or_k, target_ip] (4 words). _nn forms read both
    // operands as slots; _nk forms read s1 as a slot and s2_or_k as an inline
    // immediate. Always test "branch if cmp true" — for compare-then-brfalse
    // patterns the lowerer fuses ceq → bne. Subset is corpus-driven (only the
    // compare→branch patterns observed in bench loop bodies; no bge/ble, no
    // LHS-constant `_kn` forms); add more on demand.
    blt_i4_nn,      // [op, s1, s2, target_ip]    if (slot[s1] < slot[s2]) goto target
    blt_i4_nk,      // [op, s1, k,  target_ip]    if (slot[s1] < k)        goto target
    bgt_r4_nn,
    bgt_r4_nk,
    beq_i4_nn,
    beq_i4_nk,
    bne_i4_nn,
    bne_i4_nk,

    // --- Numeric for-loop super-instruction ---
    // Encoding: [op, induction_slot, limit_imm, target_ip] (4 words).
    // Executor body: slot[N] += 1; if (slot[N] < limit) goto target. Three
    // semantic steps (increment + bound check + back-edge) in one dispatch —
    // analogous to Luau's LOP_FORNLOOP. Step is hardcoded to 1 (Roslyn-emitted
    // C# `for (int i = ...; i < L; i++)` pattern). Variants for step != 1,
    // reverse loops, and r4 induction are deferred until the bench corpus
    // demands them.
    //
    // Lowerer pass detects `add_i4_nk slot[N], slot[N], 1; blt_i4_nk slot[N],
    // K_limit, body_top` plus a preceding `ldc_i4 slot[N] = K_init; br ->
    // blt_ip` with K_init statically less than K_limit. Replaces add+blt with
    // for_i4_nk and redirects the entry br to body_top so the first body
    // iteration isn't skipped.
    for_i4_nk,      // [op, induction_slot, limit_int, target_ip]

    // --- try/finally (non-exceptional path only) ---
    // IL `leave T` lowers to push_cont(T), push_cont(outer handlers)…, br(innermost handler);
    // IL `endfinally` lowers to br_cont, which pops the next continuation and jumps to it —
    // chaining inner finally → outer finally → leave target. Exceptions thrown inside a try
    // still propagate without running the finally (the whole dispatch falls back to the
    // original compiled body via MethodReloadRegistry.TryInvokeMethodReload).
    push_cont,      // [op, target_ip]  — push an IR continuation address
    br_cont,        // [op]             — pop a continuation address and jump to it

    // --- 64-bit family (I8 = long/ulong, R8 = double) ---
    // Wide slots: two consecutive 4-byte frame cells, filler cell typed O/null like Vt
    // continuation cells. Methods whose frames contain a wide slot SKIP the optimizer
    // passes wholesale (see LowerMethod), so no _nn/_nk fast forms exist and none of
    // these ops appear in the pass tables.
    ldc_i8,         // [op, dst, lo, hi]
    ldc_r8,         // [op, dst, lo, hi] — IEEE754 double bits
    add_i8, sub_i8, mul_i8, div_i8, rem_i8,        // [op, dst, s1, s2]
    div_un_i8, rem_un_i8,
    and_i8, or_i8, xor_i8,
    shl_i8, shr_i8, shr_un_i8,                     // [op, dst, value(I8), count(I4)]
    neg_i8, not_i8,                                // [op, dst, src]
    add_r8, sub_r8, mul_r8, div_r8, rem_r8,        // [op, dst, s1, s2]
    neg_r8,                                        // [op, dst, src]
    ceq_i8, cgt_i8, clt_i8, cgt_un_i8, clt_un_i8,  // [op, dst(I4), s1, s2]
    ceq_r8, cgt_r8, clt_r8, cgt_un_r8, clt_un_r8,  // unordered variants for float semantics
    conv_i8_i4,     // [op, dst, src] — sign-extend i4 → i8
    conv_i8_u4,     //                  zero-extend u4 → i8
    conv_i4_i8,     //                  truncate i8 → i4 (also serves conv.u4)
    conv_i8_r4, conv_r4_i8,
    conv_i8_r8, conv_r8_i8,
    conv_r8_r4, conv_r4_r8,
    conv_r8_i4, conv_i4_r8,
    conv_r8_u8,     // ulong bits → double (conv.r.un)
    brtrue_i8, brfalse_i8,   // [op, src, target_ip]
    ret_i8, ret_r8,          // [op, src]
}
}
