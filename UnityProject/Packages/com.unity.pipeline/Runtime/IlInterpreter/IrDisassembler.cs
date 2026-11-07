#nullable enable
using System;
using System.Text;

namespace IlInterpreter.Interpreter
{
    // Debug tool: render a lowered method's IR (slot types, token/string tables, decoded ops) as text
    // — the missing "see what the lowerer produced" view for chasing slot-typing/marshalling bugs.
    // Internal, for tests and probe harnesses. Op widths come from OpWidthForCoalesce (never drifts).
    partial class ScriptInterpreter
    {
        /// <summary>Disassemble one loaded method by simple name (first match). Null if not loaded/found.</summary>
        internal string? DisassembleMethod(string name)
        {
            if (_parsed == null || !_parsed.ByName.TryGetValue(name, out var m)) return null;
            return DisassembleParsed(m);
        }

        /// <summary>Disassemble every loaded method (declaration order by token).</summary>
        internal string DisassembleAll()
        {
            var sb = new StringBuilder();
            if (_parsed == null) return "(no script loaded)";
            foreach (var m in _parsed.ByToken.Values)
                sb.Append(DisassembleParsed(m)).Append('\n');
            return sb.ToString();
        }

        string DisassembleParsed(ParsedMethod m)
        {
            var sb = new StringBuilder();
            sb.Append($"=== {m.Name}  (static={m.IsStatic}, args={m.ArgCount}, locals={m.LocalCount}, ret={m.ReturnSType}, argSTypes=[{string.Join(",", m.ArgSTypes)}]");
            var lo = m.Lowered;
            if (lo == null)
            {
                sb.Append($")  NOT LOWERED: {m.LoweringSkipReason ?? "(no reason)"}\n");
                return sb.ToString();
            }
            sb.Append($", frameSize={lo.FrameSize})\n");

            // Slot type table (slot -> SType), compact.
            sb.Append("  slots: ");
            for (int s = 0; s < lo.SlotTypes.Length; s++)
                sb.Append($"{s}:{lo.SlotTypes[s]} ");
            sb.Append('\n');

            if (lo.Strings.Length > 0)
            {
                sb.Append("  strings: ");
                for (int i = 0; i < lo.Strings.Length; i++)
                    sb.Append($"[{i}]=\"{lo.Strings[i]}\" ");
                sb.Append('\n');
            }

            // Instruction stream.
            var ir = lo.Ir;
            int ip = 0;
            while (ip < ir.Length)
            {
                var op = (Op)ir[ip];
                int w = IrLowerer.OpWidthForCoalesce(op, ir, ip);
                if (w <= 0) { sb.Append($"  {ip,4}: <bad width for {op}={(uint)ir[ip]}>\n"); break; }
                int il = ip < lo.IrToIlOffset.Length ? lo.IrToIlOffset[ip] : -1;
                sb.Append($"  {ip,4} (IL 0x{il:X4}): {op,-16}");
                for (int k = 1; k < w; k++) sb.Append($" {(int)ir[ip + k]}");
                sb.Append(Annotate(op, ir, ip, w, lo));
                sb.Append('\n');
                ip += w;
            }
            return sb.ToString();
        }

        // Human-readable suffix for the operands we can resolve: the ldstr literal, the target of a
        // host/script call, and the field behind a ld/stsfld/ld/stfld.
        string Annotate(Op op, uint[] ir, int ip, int w, LoweredMethod lo)
        {
            switch (op)
            {
                case Op.ldstr:
                    // [op, dst, strIdx]
                    int si = (int)ir[ip + 2];
                    return si < lo.Strings.Length ? $"   ; dst=s{(int)ir[ip + 1]} \"{lo.Strings[si]}\"" : "   ; <bad str idx>";
                case Op.call_host:
                    // [op, dst, recvSlot, tokIdx, argc, args...]
                    return $"   ; dst=s{(int)ir[ip + 1]} recv=s{(int)ir[ip + 2]} -> {TokName(lo, (int)ir[ip + 3])}";
                case Op.call_script:
                case Op.newobj_script:
                case Op.newobj_host:
                    // [op, dst, tokIdx, argc, args...]
                    return $"   ; dst=s{(int)ir[ip + 1]} -> {TokName(lo, (int)ir[ip + 2])}";
                case Op.ldsfld_i4: case Op.ldsfld_r4: case Op.ldsfld_o:
                case Op.stsfld_i4: case Op.stsfld_r4: case Op.stsfld_o:
                    return $"   ; {TokName(lo, (int)ir[ip + 2])}";
                default:
                    return "";
            }
        }

        string TokName(LoweredMethod lo, int tokIdx)
        {
            if (_parsed == null || lo.Tokens == null || (uint)tokIdx >= (uint)lo.Tokens.Length) return $"tok#{tokIdx}";
            int tok = lo.Tokens[tokIdx];
            return _parsed.TokenNames.TryGetValue(tok, out var n) ? n : $"tok 0x{tok:X8}";
        }
    }
}
