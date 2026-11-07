using System.Collections.Generic;
using System.Linq;
using System.Text;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

#if UNITY_EDITOR || (UNITY_STANDALONE && DEBUG)
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp.Syntax;
#endif

namespace Unity.Pipeline.Telemetry
{
    /// <summary>
    /// Structural classification of an eval body. Matches the epic's usage buckets: the majority of
    /// eval calls are one-liners (a single method invoke / value read / flag set), which we tag as
    /// <see cref="SingleExpression"/>; anything with more than one statement (polling loops, bulk
    /// setup) is <see cref="Statements"/>.
    /// </summary>
    static class EvalClassification
    {
        public const string SingleExpression = "single-expression";
        public const string Statements = "statements";
    }

    /// <summary>
    /// Result of analysing one eval body: its structural <see cref="Classification"/> and the ranked
    /// API <see cref="Fingerprints"/> (top-level member-access paths) extracted from the syntax tree.
    /// </summary>
    struct EvalUsageAnalysis
    {
        public string Classification;
        public IReadOnlyList<string> Fingerprints;
    }

    /// <summary>
    /// Extracts a privacy-preserving "fingerprint" of an eval body from its C# syntax tree, without
    /// storing the raw source (AUTHAPI-29). A fingerprint is the top-level member-access path of each
    /// access site — e.g. <c>AssetDatabase.Refresh</c>, <c>PlayerSettings.SetScriptingBackend</c>,
    /// <c>Object.FindFirstObjectByType&lt;T&gt;</c>, <c>MatchManager.Instance.State</c> — which is
    /// enough to rank which APIs agents actually reach for (steering the eval-displacement backlog,
    /// AUTHAPI-24).
    ///
    /// PRIVACY CONTRACT — strict whitelist rendering. The only source text that can ever appear in a
    /// fingerprint is an identifier, type name, or member name taken from the syntax tree. Literals,
    /// interpolated strings, and every expression form not on the whitelist are replaced by the
    /// neutral <see cref="Placeholder"/> (<c>"secret".Length</c> records as <c>&lt;expr&gt;.Length</c>);
    /// object creations render as <c>new TypeName</c> (constructor arguments never appear); casts
    /// unwrap to the inner expression (<c>((Foo)o).Bar</c> records as <c>o.Bar</c>); generic type
    /// arguments are normalised to arity placeholders (<c>&lt;T&gt;</c>). A path that would be nothing
    /// but the placeholder carries no API information and is dropped entirely.
    ///
    /// Conditional-access chains (<c>a?.B.C()</c>) are fingerprinted with <c>?.</c> normalised to
    /// <c>.</c>, so they rank together with their unconditional form.
    ///
    /// Known blind spots — the ranking UNDERCOUNTS these forms (deliberate, to keep the walker
    /// simple and re-entrancy-free); consumers of the report should not read absence as proof of
    /// non-use:
    /// <list type="bullet">
    /// <item><description>Arguments nested inside a chain's receiver call are not walked: in
    /// <c>Configure(Settings.Load()).Apply()</c> only <c>Configure().Apply</c> is recorded — the
    /// callee chain is not re-entered, so <c>Settings.Load</c> is missed.</description></item>
    /// <item><description>A bare object creation with no trailing member access (<c>new Foo(...);</c> as a whole
    /// statement) produces no fingerprint.</description></item>
    /// <item><description>Bodies nested deeper than the walker's depth guard are abandoned mid-walk.</description></item>
    /// </list>
    ///
    /// Threading: pure Roslyn + BCL — no Unity API calls — so <see cref="Analyze"/> is safe to run
    /// off the main thread. The telemetry writer relies on this and runs it on a background task
    /// from a snapshot captured at eval completion.
    ///
    /// This is a SEPARATE parse from the one <c>EvalCodeCompiler</c> does to actually compile and
    /// run the eval, by necessity rather than oversight: that parse is of a generated wrapper
    /// source (the raw body embedded inside a <c>class PipelineEval_&lt;id&gt; { ... Execute() {
    /// ... } }</c>) under <c>SourceCodeKind.Regular</c> with the project's <c>#if</c> defines, not
    /// of the raw body alone as a top-level <c>SourceCodeKind.Script</c> compilation unit the way
    /// <see cref="Analyze"/> expects it. Sharing that tree would mean threading it out of
    /// <c>RoslynCompilationService</c> (on both the success AND failure path — failures still get
    /// recorded) and locating the wrapped <c>Execute()</c> body inside it, coupling this class to
    /// the wrapper's exact shape. Not worth it today: the extra parse already runs off the eval
    /// response path (see Threading above), so it costs background CPU, not eval latency.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EvalUsageFingerprinter
    {
        /// <summary>
        /// Upper bound on fingerprints kept per record, so a pathological body can't bloat the log.
        /// Truncation keeps the FIRST N access sites in tree (source) order, not the most frequent —
        /// acceptable because bodies with more than 32 access sites are rare outliers.
        /// </summary>
        public const int MaxFingerprints = 32;

        /// <summary>
        /// Neutral stand-in for any expression not on the rendering whitelist (literals, interpolated
        /// strings, arithmetic, lambdas, …). Guarantees no raw source text leaks into a fingerprint.
        /// </summary>
        public const string Placeholder = "<expr>";

#if UNITY_EDITOR || (UNITY_STANDALONE && DEBUG)

        private static readonly CSharpParseOptions m_ScriptOptions =
            new CSharpParseOptions(kind: SourceCodeKind.Script);

        /// <summary>
        /// Analyse an eval body: classify it and extract its API fingerprints. Never throws — a body
        /// that fails to parse yields an empty fingerprint list and a best-effort classification.
        /// </summary>
        public static EvalUsageAnalysis Analyze(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new EvalUsageAnalysis { Classification = EvalClassification.Statements, Fingerprints = new List<string>() };

            CompilationUnitSyntax root;
            try
            {
                root = CSharpSyntaxTree.ParseText(code, m_ScriptOptions).GetCompilationUnitRoot();
            }
            catch
            {
                return new EvalUsageAnalysis { Classification = EvalClassification.Statements, Fingerprints = new List<string>() };
            }

            return new EvalUsageAnalysis
            {
                Classification = Classify(root),
                Fingerprints = ExtractFingerprints(root)
            };
        }

        /// <summary>Convenience overload used by tests: fingerprints only.</summary>
        public static IReadOnlyList<string> ExtractFingerprints(string code) => Analyze(code).Fingerprints;

        /// <summary>Convenience overload used by tests: classification only.</summary>
        public static string Classify(string code) => Analyze(code).Classification;

        /// <summary>
        /// One-liner iff the body is exactly one statement that is an expression or a value-returning
        /// return. A lone loop/if/block (e.g. a polling loop) is a statement body, not a one-liner.
        /// </summary>
        private static string Classify(CompilationUnitSyntax root)
        {
            var statements = root.Members
                .OfType<GlobalStatementSyntax>()
                .Select(m => m.Statement)
                .ToList();

            if (statements.Count == 1)
            {
                var only = statements[0];
                if (only is ExpressionStatementSyntax ||
                    (only is ReturnStatementSyntax ret && ret.Expression != null))
                {
                    return EvalClassification.SingleExpression;
                }
            }

            return EvalClassification.Statements;
        }

        private static IReadOnlyList<string> ExtractFingerprints(CompilationUnitSyntax root)
        {
            var walker = new FingerprintWalker();
            walker.Visit(root);
            // Take() keeps the first MaxFingerprints access sites in tree (source) order — see the
            // MaxFingerprints doc for why that truncation policy is acceptable.
            return walker.Fingerprints.Count > MaxFingerprints
                ? walker.Fingerprints.Take(MaxFingerprints).ToList()
                : walker.Fingerprints;
        }

        /// <summary>
        /// Walks the syntax tree collecting one fingerprint per top-level access site: an invocation's
        /// callee (<c>A.B.C(...)</c> → <c>A.B.C</c>) or a member-access chain that is not itself the
        /// head of an enclosing call/access (<c>A.Instance.State</c>). Nested access sites inside call
        /// arguments are still collected (they are their own sites); the callee chain is not re-counted.
        /// A depth counter abandons subtrees nested past <see cref="MaxDepth"/> so a pathological body
        /// (machine-generated nesting) can't blow the stack — fingerprinting is best-effort.
        /// </summary>
        private sealed class FingerprintWalker : CSharpSyntaxWalker
        {
            /// <summary>Maximum syntax depth walked; deeper subtrees are abandoned.</summary>
            private const int MaxDepth = 64;

            public readonly List<string> Fingerprints = new List<string>();
            private int m_Depth;

            public override void Visit(SyntaxNode node)
            {
                if (m_Depth >= MaxDepth)
                    return; // Abandon this subtree — best-effort telemetry, never a liability.

                m_Depth++;
                try
                {
                    base.Visit(node);
                }
                finally
                {
                    m_Depth--;
                }
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                AddChainHeadFingerprint(node, node.Expression, () => RenderPath(node.Expression));

                // Deliberately do NOT recurse into the callee chain (already captured above), but do
                // walk the arguments so nested calls/reads passed as arguments are captured too.
                VisitArgumentsIfPresent(node.ArgumentList);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                if (IsChainHead(node))
                    Add(RenderPath(node));

                // Continue into the expression side so nested access sites are still discovered;
                // inner chain members are guarded by IsChainHead and won't be double-counted.
                base.VisitMemberAccessExpression(node);
            }

            public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
            {
                // A bare element-access chain head (cache[key]) previously produced NO fingerprint
                // at all — no member access for the walker to see — while the conditional form
                // (cache?[key]) records "cache[]": the two must rank together, so bare element
                // access on a member/name receiver is fingerprinted the same way.
                AddChainHeadFingerprint(node, node.Expression, () => RenderPath(node));

                // Walk the bracket arguments so nested reads/calls inside the indexer are captured.
                VisitArgumentsIfPresent(node.ArgumentList);
            }

            public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
            {
                // The whole ?. chain is ONE fingerprint (RenderPath normalizes '?.' to '.'):
                // descending into its parts would record the receiver and the bound members as
                // separate (and misleading) entries.
                if (IsChainHead(node))
                    Add(RenderPath(node));

                // Parity with VisitInvocationExpression/VisitElementAccessExpression: only the head
                // access's OWN argument list is walked, scoped to WhenNotNull (not the receiver
                // before '?.') — the receiver's nested-call arguments are the same documented
                // blind spot the unconditional forms accept, not something '?.' should uncover.
                // Only the outermost argument lists are visited — the walker recurses within.
                // Covers both parenthesized (ArgumentListSyntax) and bracketed
                // (BracketedArgumentListSyntax) forms, so an indexer inside a conditional chain
                // (a?.b[SomeCall()]) is captured too — a bracketed list was previously invisible
                // to this sweep, silently dropping SomeCall.
                if (node.WhenNotNull != null)
                {
                    foreach (var argumentList in node.WhenNotNull
                                 .DescendantNodes(n => !(n is ArgumentListSyntax) && !(n is BracketedArgumentListSyntax))
                                 .Where(n => n is ArgumentListSyntax || n is BracketedArgumentListSyntax))
                        Visit(argumentList);
                }
            }

            // Shared shape between VisitInvocationExpression and VisitElementAccessExpression: add
            // the rendered path only for a chain head whose receiver is renderable, computing the
            // path lazily so non-chain-head nodes (the overwhelming majority visited) skip RenderPath
            // entirely instead of just discarding its result.
            private void AddChainHeadFingerprint(SyntaxNode node, ExpressionSyntax receiver, System.Func<string> render)
            {
                if (IsChainHead(node) && IsMemberOrName(receiver))
                    Add(render());
            }

            private void VisitArgumentsIfPresent(BaseArgumentListSyntax argumentList)
            {
                if (argumentList != null)
                    Visit(argumentList);
            }

            private void Add(string path)
            {
                // A path that is nothing but the neutral placeholder carries no API information —
                // drop the fingerprint rather than pollute the ranking with "<expr>" noise.
                if (string.IsNullOrEmpty(path) || path == Placeholder)
                    return;
                Fingerprints.Add(path);
            }

            // A node is the head of its access/call chain when its parent is not another link of the
            // same chain (member access, invocation, or element access).
            private static bool IsChainHead(SyntaxNode node)
            {
                var parent = node.Parent;
                // See through parentheses: in (a?.b).c the conditional is part of the enclosing
                // chain, not a head of its own.
                while (parent is ParenthesizedExpressionSyntax paren)
                    parent = paren.Parent;
                return !(parent is MemberAccessExpressionSyntax)
                    && !(parent is InvocationExpressionSyntax)
                    && !(parent is ElementAccessExpressionSyntax);
            }

            // Also accepts a call or parenthesized expression as the receiver: RenderPath already
            // knows how to render both (GetHandler()() -> "GetHandler()", (A.B)() -> "A.B"), so
            // gating them out here was dropping the whole access site instead of rendering it —
            // not even the placeholder, since Add() was never reached.
            private static bool IsMemberOrName(ExpressionSyntax expr) =>
                expr is MemberAccessExpressionSyntax || expr is IdentifierNameSyntax || expr is GenericNameSyntax
                || expr is InvocationExpressionSyntax || expr is ParenthesizedExpressionSyntax;
        }

        /// <summary>
        /// Render an expression to its fingerprint path: dotted identifiers, with generic method type
        /// arguments normalised to arity placeholders (<c>&lt;T&gt;</c>, <c>&lt;T1, T2&gt;</c>) so
        /// <c>FindFirstObjectByType&lt;Foo&gt;</c> and <c>&lt;Bar&gt;</c> rank as one API.
        ///
        /// STRICT WHITELIST (the privacy contract of the class doc): only the expression forms listed
        /// below render source text, and each renders identifiers/type/member names exclusively.
        /// Everything else — literals, interpolated strings, arithmetic, lambdas, conditionals, … —
        /// falls through to the neutral <see cref="Placeholder"/> so no raw source can ever leak into
        /// a fingerprint. Casts unwrap to the inner expression; object creations render as
        /// <c>new TypeName</c>. Rendering recursion is bounded by <paramref name="depth"/> as a second
        /// layer of defence against pathological nesting (chains hang below their head, so they are
        /// not covered by the walker's descent guard).
        /// </summary>
        private static string RenderPath(ExpressionSyntax expr) => RenderPath(expr, 0);

        private const int MaxRenderDepth = 64;

        private static string RenderPath(ExpressionSyntax expr, int depth)
        {
            if (depth >= MaxRenderDepth)
                return Placeholder;
            depth++;

            switch (expr)
            {
                case IdentifierNameSyntax id:
                    return id.Identifier.Text;
                case GenericNameSyntax gn:
                    return gn.Identifier.Text + RenderArity(gn.TypeArgumentList.Arguments.Count);
                case QualifiedNameSyntax qn: // Namespace-qualified type in type position, e.g. new UnityEngine.GameObject
                    return RenderPath(qn.Left, depth) + "." + RenderSimpleName(qn.Right);
                case AliasQualifiedNameSyntax aq: // global::UnityEngine → keep the name, drop the alias
                    return RenderSimpleName(aq.Name);
                case MemberAccessExpressionSyntax ma:
                    return RenderPath(ma.Expression, depth) + "." + RenderSimpleName(ma.Name);
                case InvocationExpressionSyntax inv:
                    return RenderPath(inv.Expression, depth) + "()";
                case ElementAccessExpressionSyntax ea:
                    return RenderPath(ea.Expression, depth) + "[]";
                case ParenthesizedExpressionSyntax pe:
                    return RenderPath(pe.Expression, depth);
                case CastExpressionSyntax ce: // ((Foo)o).Bar → o.Bar: unwrap to the casted expression
                    return RenderPath(ce.Expression, depth);
                case ObjectCreationExpressionSyntax oc: // new GameObject("x").AddComponent<T> → new GameObject.AddComponent<T>
                    return "new " + RenderPath(oc.Type, depth);
                case PredefinedTypeSyntax pt:
                    return pt.Keyword.Text;
                case ThisExpressionSyntax _:
                    return "this";
                case BaseExpressionSyntax _:
                    return "base";
                case ConditionalAccessExpressionSyntax cond:
                    // a?.B.C renders as a.B.C: '?.' is null-propagation noise for ranking
                    // purposes. Rendering only the bound member (the old fall-through via the bare
                    // MemberBinding) silently dropped the receiver, producing a colliding entry
                    // ("State.Value" for MatchManager.Instance?.State.Value). When the chain ends
                    // in a call, fingerprint the CALLEE (a?.B() -> a.B) so it ranks together with
                    // its unconditional form — the walker uses the same callee convention for
                    // plain invocation heads.
                    var conditionalTail = cond.WhenNotNull is InvocationExpressionSyntax topCall
                        ? topCall.Expression
                        : cond.WhenNotNull;
                    var tailRendered = RenderPath(conditionalTail, depth);
                    // An element-binding tail (cache?[key] -> "[]") concatenates WITHOUT a dot so
                    // the fingerprint matches the unconditional cache[key] form ("cache[]").
                    return tailRendered.Length > 0 && tailRendered[0] == '['
                        ? RenderPath(cond.Expression, depth) + tailRendered
                        : RenderPath(cond.Expression, depth) + "." + tailRendered;
                case MemberBindingExpressionSyntax mb:
                    return RenderSimpleName(mb.Name);
                case ElementBindingExpressionSyntax _: // the ?[...] binding of a conditional chain
                    return "[]";
                default:
                    // NOT a raw ToString(): anything off the whitelist (string/char/numeric literals,
                    // interpolated strings, lambdas, arithmetic, …) is reduced to the neutral
                    // placeholder so source text can never leak into the telemetry log (AUTHAPI-29).
                    return Placeholder;
            }
        }

        private static string RenderSimpleName(SimpleNameSyntax name)
        {
            if (name is GenericNameSyntax gn)
                return gn.Identifier.Text + RenderArity(gn.TypeArgumentList.Arguments.Count);
            return name.Identifier.Text;
        }

        private static string RenderArity(int count)
        {
            if (count <= 0)
                return string.Empty;
            if (count == 1)
                return "<T>";

            var sb = new StringBuilder("<");
            for (var i = 1; i <= count; i++)
            {
                if (i > 1)
                    sb.Append(", ");
                sb.Append("T").Append(i);
            }
            sb.Append(">");
            return sb.ToString();
        }

#else
        /// <summary>Runtime compilation (and therefore eval) is unavailable on this platform.</summary>
        public static EvalUsageAnalysis Analyze(string code) =>
            new EvalUsageAnalysis { Classification = EvalClassification.Statements, Fingerprints = new List<string>() };

        public static IReadOnlyList<string> ExtractFingerprints(string code) => new List<string>();

        public static string Classify(string code) => EvalClassification.Statements;
#endif
    }
}
