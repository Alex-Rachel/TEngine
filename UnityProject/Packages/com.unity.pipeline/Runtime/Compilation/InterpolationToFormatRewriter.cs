// The package bundles Roslyn renamed with a "UnityPipeline." prefix (Runtime/Plugins/CodeAnalysis);
// the dotnet test projects reference the same DLLs (IlInterpreter/BundledRoslyn.props). Those plugins
// ship only to the Editor and desktop players, so mobile/console players compile this file out; the
// dotnet harnesses define PIPELINE_HAS_ROSLYN.
#if UNITY_EDITOR || UNITY_STANDALONE || PIPELINE_HAS_ROSLYN
using System.Collections.Generic;
using System.Text;
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// Rewrites C# string interpolation (<c>$"..."</c>) into <c>string.Format(fmt, args…)</c> at the
    /// source level: the interpreter can't run the compiler's normal interpolation lowering
    /// (<c>DefaultInterpolatedStringHandler</c>, a ref struct), but it can call <c>String.Format</c>
    /// (bound in <see cref="IlInterpreter.Interpreter.HostBinding"/>). Applied by
    /// <see cref="SourceCodeTransformer"/> before an in-place reload body is compiled.
    ///
    /// Purely syntactic — no semantic model. Format specifiers, alignment, escaped braces, verbatim
    /// strings and nested interpolation are handled. The one unserved case is an interpolation whose
    /// target type is <c>FormattableString</c>/<c>IFormattable</c>: rewriting yields a <c>string</c>,
    /// so that rare usage becomes a compile error rather than a silent wrong result.
    /// </summary>
    sealed class InterpolationToFormatRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            var fmt = new StringBuilder();
            var args = new List<ArgumentSyntax>();
            int index = 0;

            foreach (var content in node.Contents)
            {
                switch (content)
                {
                    case InterpolatedStringTextSyntax text:
                        // ValueText decodes escapes (\t etc.) but leaves literal braces doubled
                        // ({{ / }}) — exactly the escaping a Format string wants, so append as-is.
                        fmt.Append(text.TextToken.ValueText);
                        break;

                    case InterpolationSyntax interp:
                        fmt.Append('{').Append(index++);
                        if (interp.AlignmentClause != null)
                            fmt.Append(',').Append(interp.AlignmentClause.Value.ToString().Trim());
                        if (interp.FormatClause != null)
                            fmt.Append(':').Append(interp.FormatClause.FormatStringToken.ValueText);
                        fmt.Append('}');
                        // Visit the hole so nested interpolations rewrite bottom-up and later
                        // member-qualification passes see it.
                        args.Add(SyntaxFactory.Argument((ExpressionSyntax)Visit(interp.Expression)));
                        break;
                }
            }

            // No holes: emit a plain string literal (no Format, no brace doubling), matching how
            // Roslyn treats a hole-less interpolated string.
            if (index == 0)
            {
                var raw = new StringBuilder();
                foreach (var content in node.Contents)
                    if (content is InterpolatedStringTextSyntax t)
                        // A plain literal wants single braces, so undo the {{ / }} escaping ValueText keeps.
                        raw.Append(t.TextToken.ValueText.Replace("{{", "{").Replace("}}", "}"));
                return SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(raw.ToString()))
                    .WithTriviaFrom(node);
            }

            var allArgs = new List<ArgumentSyntax>(args.Count + 1)
            {
                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(fmt.ToString()))),
            };
            allArgs.AddRange(args);

            // Passing holes as loose args lets Roslyn pick the Format overload: fixed arity for ≤3,
            // params object[] for ≥4 — both are bound on the interpreter side.
            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.ParseExpression("string.Format"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(allArgs)))
                .WithTriviaFrom(node);
        }
    }
}
#endif
