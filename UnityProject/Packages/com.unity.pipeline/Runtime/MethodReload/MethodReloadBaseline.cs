// Roslyn — the bundled UnityPipeline.Microsoft.CodeAnalysis* plugins — ships only to the Editor and
// desktop players (Runtime/Plugins/CodeAnalysis/*.dll.meta), so mobile/console players compile this
// file out. The dotnet harnesses (IlInterpreter/BundledRoslyn.props) define PIPELINE_HAS_ROSLYN.
#if UNITY_EDITOR || UNITY_STANDALONE || PIPELINE_HAS_ROSLYN
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityPipeline.Microsoft.CodeAnalysis.Text;
using Unity.Pipeline.Compilation;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.MethodReload
{
    /// <summary>
    /// Per-method snapshot of a reloadable file's last-compiled source, so a reload can skip
    /// methods whose bodies still match the code the target already runs compiled. Without this,
    /// every save re-registers an interpreter override for every [MethodReload] method in the file,
    /// and methods the user never touched pay the interpreter's per-call cost.
    ///
    /// A baseline is captured per (file, preprocessor symbols) pair — the same file diffs
    /// differently under editor and player defines. For each [MethodReload] method it stores a hash
    /// of the declaration's token stream (trivia excluded, so whitespace and comment edits don't
    /// count as changes), plus one context hash over the rest of the file with all executable
    /// bodies removed. A context change (fields, consts, signatures, usings) conservatively marks
    /// every method changed: an override compiles against the current file, so same-file
    /// declaration edits can shift an untouched body's meaning.
    ///
    /// Owned by whoever knows when disk matches the compiled state (the editor watch); with no
    /// baseline captured, classification returns null and reloads behave as before. The store is
    /// static, so the editor persists/restores it across domain reloads itself.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class MethodReloadBaseline
    {
        private sealed class Entry
        {
            public ulong ContextHash;
            public readonly Dictionary<string, ulong> MethodHashes = new Dictionary<string, ulong>();
        }

        // Key: normalized full path + '\u001f' + define key.
        private static readonly Dictionary<string, Entry> s_Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Number of captured (file, defines) baselines.</summary>
        public static int Count => s_Entries.Count;

        /// <summary>
        /// Snapshot <paramref name="filePath"/> as the compiled reference state for the given
        /// preprocessor symbols (null = project defaults). Call only when the file on disk is known
        /// to match what the reload target runs compiled. Returns false (and drops any stale entry)
        /// when the file can't be read or has no [MethodReload] method.
        /// </summary>
        public static bool Capture(string filePath, string[] preprocessorSymbols = null)
        {
            string source;
            try { source = File.ReadAllText(filePath); }
            catch { source = null; }
            return CaptureFromSource(filePath, source, preprocessorSymbols);
        }

        /// <summary>See <see cref="Capture"/>; source text supplied by the caller (tests).</summary>
        public static bool CaptureFromSource(string filePath, string sourceCode, string[] preprocessorSymbols = null)
        {
            var key = KeyFor(filePath, preprocessorSymbols);
            var entry = sourceCode == null ? null : ComputeEntry(sourceCode, preprocessorSymbols);
            if (entry == null)
            {
                s_Entries.Remove(key);
                return false;
            }
            s_Entries[key] = entry;
            return true;
        }

        /// <summary>
        /// Diff <paramref name="sourceCode"/> against the captured baseline for
        /// (<paramref name="filePath"/>, <paramref name="preprocessorSymbols"/>). Returns the
        /// [MethodReload] method names whose declarations still match it — safe to leave running
        /// compiled. Null when no baseline was captured for that pair (treat everything as
        /// changed); empty when the file's context changed, so nothing is skippable.
        /// </summary>
        public static HashSet<string> GetUnchangedMethods(
            string filePath, string sourceCode, string[] preprocessorSymbols = null)
        {
            if (!s_Entries.TryGetValue(KeyFor(filePath, preprocessorSymbols), out var baseline))
                return null;

            var unchanged = new HashSet<string>(StringComparer.Ordinal);
            var current = sourceCode == null ? null : ComputeEntry(sourceCode, preprocessorSymbols);
            if (current == null || current.ContextHash != baseline.ContextHash)
                return unchanged;

            foreach (var kv in current.MethodHashes)
                if (baseline.MethodHashes.TryGetValue(kv.Key, out var h) && h == kv.Value)
                    unchanged.Add(kv.Key);
            return unchanged;
        }

        /// <summary>
        /// True when every [MethodReload] method in <paramref name="sourceCode"/> matches the baseline
        /// and none of them is in <paramref name="mustReload"/> (methods that must reload anyway —
        /// e.g. already overridden on a device that can't unregister). Callers use this to skip a
        /// whole compile-and-push. False when no baseline was captured.
        /// </summary>
        public static bool IsFileUpToDate(
            string filePath, string sourceCode, string[] preprocessorSymbols = null,
            ICollection<string> mustReload = null)
        {
            if (!s_Entries.TryGetValue(KeyFor(filePath, preprocessorSymbols), out var baseline))
                return false;
            var current = sourceCode == null ? null : ComputeEntry(sourceCode, preprocessorSymbols);
            if (current == null || current.ContextHash != baseline.ContextHash)
                return false;

            foreach (var kv in current.MethodHashes)
            {
                if (!baseline.MethodHashes.TryGetValue(kv.Key, out var h) || h != kv.Value)
                    return false;
                if (mustReload != null && mustReload.Contains(kv.Key))
                    return false;
            }
            return true;
        }

        /// <summary>Drop every captured baseline (watch stopped, or about to recapture).</summary>
        public static void Clear() => s_Entries.Clear();

        /// <summary>
        /// Flatten the store to one string so the editor can stash it in SessionState across the
        /// play-mode domain reload — the reload wipes these statics while disk keeps the user's
        /// edits, so the baseline can't be recaptured from disk at that point.
        /// </summary>
        public static string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var kv in s_Entries)
            {
                sb.Append(kv.Key).Append('\u001e').Append(kv.Value.ContextHash.ToString("x", CultureInfo.InvariantCulture));
                foreach (var m in kv.Value.MethodHashes)
                    sb.Append('\u001e').Append(m.Key).Append('=').Append(m.Value.ToString("x", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Replace the store with a <see cref="Serialize"/> snapshot. Unparseable lines are dropped.</summary>
        public static void Restore(string serialized)
        {
            s_Entries.Clear();
            if (string.IsNullOrEmpty(serialized)) return;

            foreach (var line in serialized.Split('\n'))
            {
                if (line.Length == 0) continue;
                var fields = line.Split('\u001e');
                if (fields.Length < 2) continue;
                if (!ulong.TryParse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var contextHash))
                    continue;

                var entry = new Entry { ContextHash = contextHash };
                bool ok = true;
                for (int i = 2; i < fields.Length; i++)
                {
                    int eq = fields[i].LastIndexOf('=');
                    if (eq <= 0 || !ulong.TryParse(fields[i].Substring(eq + 1),
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h))
                    {
                        ok = false;
                        break;
                    }
                    entry.MethodHashes[fields[i].Substring(0, eq)] = h;
                }
                if (ok)
                    s_Entries[fields[0]] = entry;
            }
        }

        private static string KeyFor(string filePath, string[] preprocessorSymbols)
        {
            string path;
            try { path = Path.GetFullPath(filePath); }
            catch { path = filePath ?? ""; }
            var defines = preprocessorSymbols == null ? "<project>" : string.Join(";", preprocessorSymbols);
            return path + '\u001f' + defines;
        }

        /// <summary>
        /// Hash the file: one hash per [MethodReload] method name (overload hashes fold together —
        /// method ids are name-keyed everywhere else too), and a context hash over every token
        /// outside an executable body. Null when no class carries a [MethodReload] method.
        /// </summary>
        private static Entry ComputeEntry(string sourceCode, string[] preprocessorSymbols)
        {
            SyntaxNode root;
            try
            {
                root = CSharpSyntaxTree.ParseText(sourceCode,
                    RoslynCompilationService.ProjectParseOptions(preprocessorSymbols)).GetRoot();
            }
            catch
            {
                return null;
            }

            var entry = new Entry();
            foreach (var node in root.DescendantNodes())
            {
                if (node is not MethodDeclarationSyntax method ||
                    !InPlaceReloadProcessor.HasMethodReloadAttribute(method))
                    continue;
                var name = method.Identifier.ValueText;
                var declHash = HashTokens(method, null);
                entry.MethodHashes[name] = entry.MethodHashes.TryGetValue(name, out var prior)
                    ? MixHash(prior, declHash)
                    : declHash;
            }
            if (entry.MethodHashes.Count == 0)
                return null;

            entry.ContextHash = HashTokens(root, CollectBodySpans(root));
            return entry;
        }

        /// <summary>
        /// Spans of every executable body in the file: method/ctor/operator bodies, accessor
        /// bodies, and expression bodies (incl. expression-bodied properties/indexers). Reloadable
        /// bodies are covered per-method; the others bind to compiled code from an override either
        /// way, so their edits can't change what an override does and must not poison the context.
        /// </summary>
        private static List<TextSpan> CollectBodySpans(SyntaxNode root)
        {
            var spans = new List<TextSpan>();
            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case BaseMethodDeclarationSyntax m:
                        if (m.Body != null) spans.Add(m.Body.Span);
                        if (m.ExpressionBody != null) spans.Add(m.ExpressionBody.Span);
                        break;
                    case AccessorDeclarationSyntax a:
                        if (a.Body != null) spans.Add(a.Body.Span);
                        if (a.ExpressionBody != null) spans.Add(a.ExpressionBody.Span);
                        break;
                    case PropertyDeclarationSyntax p when p.ExpressionBody != null:
                        spans.Add(p.ExpressionBody.Span);
                        break;
                    case IndexerDeclarationSyntax ix when ix.ExpressionBody != null:
                        spans.Add(ix.ExpressionBody.Span);
                        break;
                }
            }
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            return spans;
        }

        // FNV-1a 64 over the token stream. Trivia never enters the hash, so formatting and
        // comments can't mark a method changed. A separator byte between tokens keeps ("ab","c")
        // and ("a","bc") distinct even though C# tokenization already makes that unlikely.
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static ulong HashTokens(SyntaxNode node, List<TextSpan> excludedSpans)
        {
            ulong h = FnvOffset;
            int next = 0; // excludedSpans is sorted; tokens come in document order
            foreach (var token in node.DescendantTokens())
            {
                if (excludedSpans != null)
                {
                    while (next < excludedSpans.Count && excludedSpans[next].End <= token.Span.Start)
                        next++;
                    if (next < excludedSpans.Count && excludedSpans[next].Contains(token.Span))
                        continue;
                }

                h = (h ^ 0xFF) * FnvPrime;
                var text = token.Text;
                for (int i = 0; i < text.Length; i++)
                {
                    h = (h ^ (byte)text[i]) * FnvPrime;
                    h = (h ^ (byte)(text[i] >> 8)) * FnvPrime;
                }
            }
            return h;
        }

        private static ulong MixHash(ulong a, ulong b)
        {
            for (int i = 0; i < 8; i++)
                a = (a ^ (byte)(b >> (i * 8))) * FnvPrime;
            return a;
        }
    }
}
#endif
