// The package bundles Roslyn renamed with a "UnityPipeline." prefix (Runtime/Plugins/CodeAnalysis);
// the dotnet test projects reference the same DLLs (IlInterpreter/BundledRoslyn.props). Those plugins
// ship only to the Editor and desktop players, so mobile/console players compile this file out; the
// dotnet harnesses define PIPELINE_HAS_ROSLYN.
#if UNITY_EDITOR || UNITY_STANDALONE || PIPELINE_HAS_ROSLYN
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Pipeline.Compilation;
using UnityEngine;

namespace Unity.Pipeline.MethodReload
{
    /// <summary>
    /// Transforms [MethodReload] method bodies (edited in place on a MonoBehaviour) into static
    /// method reload override methods that the registry can dispatch to.
    ///
    /// The transform is driven by a Roslyn <see cref="SemanticModel"/>. Because the body is moved into
    /// a standalone static class (no <c>this</c>, no base type, not nested in the original type), every
    /// reference that relied on the declaring type's scope is re-qualified so it still binds:
    /// <list type="bullet">
    /// <item>instance members of the type hierarchy (e.g. <c>transform</c> on Component) -> <c>instance.&lt;member&gt;</c></item>
    /// <item>static members inherited or declared on the type hierarchy (e.g. <c>FindObjectsByType</c> on
    ///   Object) -> <c>&lt;ContainingType&gt;.&lt;member&gt;</c></item>
    /// <item>nested types (enum/class) of the type hierarchy (e.g. a private <c>enum State</c>) -> <c>&lt;ContainingType&gt;.&lt;NestedType&gt;</c></item>
    /// </list>
    /// Locals, parameters, and types/members reachable via the file's <c>using</c> directives are left
    /// untouched. The output is a static override class the registry can bind:
    ///
    ///     [MethodReloadOverrideMethod("Type.Method")]
    ///     public static &lt;ret&gt; Method(Type instance, &lt;params&gt;) { ...rewritten body... }
    /// </summary>
    static class SourceCodeTransformer
    {
        /// <summary>
        /// Transform the [MethodReload] methods named in <paramref name="methodBodies"/> into a
        /// static override class. <paramref name="originalTypeDefinition"/> (the full source of the
        /// file) is required so the semantic model can resolve member bindings in context.
        /// </summary>
        /// <param name="emitLineDirectives">
        /// When true, each rewritten body is bracketed by <c>#line</c> directives that map it back to
        /// the original source file so an attached debugger binds breakpoints in the file the user
        /// edited. Requires <paramref name="originalFilePath"/>. The body is emitted with its original
        /// whitespace (no <c>NormalizeWhitespace</c>) so line positions survive the transform.
        /// </param>
        /// <param name="originalFilePath">Absolute path of the source file, recorded in the #line directives.</param>
        /// <param name="methodBodies">Map of method name to edited body source.</param>
        /// <param name="originalTypeName">Name of the declaring type.</param>
        /// <param name="originalMethodSignatures">Map of method name to its original signature.</param>
        /// <param name="originalTypeDefinition">The full original source file, needed to build the semantic model.</param>
        /// <param name="rewriteInterpolations">
        /// Rewrite string interpolation to <c>string.Format(...)</c>. Required by the interpreter
        /// backend, which cannot run the compiler's <c>DefaultInterpolatedStringHandler</c>
        /// lowering; pass false on the Assembly.Load backend so interpolations compile natively
        /// (e.g. a <c>FormattableString</c>-targeted interpolation survives).
        /// </param>
        /// <returns>Source for a static override class matching the shape the registry dispatches to.</returns>
        internal static string TransformMethodBodies(
            Dictionary<string, string> methodBodies,
            string originalTypeName,
            Dictionary<string, MethodSignatureInfo> originalMethodSignatures,
            string originalTypeDefinition = null,
            bool emitLineDirectives = false,
            string originalFilePath = null,
            bool rewriteInterpolations = true)
        {
            if (string.IsNullOrEmpty(originalTypeDefinition))
            {
                throw new InvalidOperationException(
                    "In-place transformation requires the full original source to build a semantic model.");
            }

            var model = BuildSemanticModel(originalTypeDefinition, originalTypeName, out var classDecl);
            if (classDecl == null)
                throw new InvalidOperationException($"Could not find class '{originalTypeName}' in source.");

            return TransformMethodBodies(model, classDecl, methodBodies, originalTypeName,
                originalMethodSignatures, emitLineDirectives, originalFilePath, rewriteInterpolations);
        }

        /// <summary>
        /// Parse <paramref name="source"/> and build the one compilation + semantic model the
        /// transform (and its callers' validation) runs against, plus the declaration of
        /// <paramref name="typeName"/> — null when the source has no such class. Imports ALL
        /// metadata members, not the Public default: the transform's new-method check diffs the
        /// source against the compiled type, and with public-only import every PRIVATE compiled
        /// method would look new and get wrongly co-emitted. <paramref name="preprocessorSymbols"/>
        /// (the project's, or a push target's) makes #if regions match the assembly being patched.
        /// </summary>
        internal static SemanticModel BuildSemanticModel(
            string source, string typeName, out ClassDeclarationSyntax classDecl,
            string[] preprocessorSymbols = null)
        {
            var tree = CSharpSyntaxTree.ParseText(source,
                RoslynCompilationService.ProjectParseOptions(preprocessorSymbols));
            var compilation = CSharpCompilation.Create(
                "MethodReloadInPlaceTransform",
                new[] { tree },
                RoslynCompilationService.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithMetadataImportOptions(MetadataImportOptions.All));
            var model = compilation.GetSemanticModel(tree);
            classDecl = tree.GetRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.ValueText == typeName);
            return model;
        }

        /// <summary>
        /// Core transform over an already-built semantic model + class declaration, so the in-place
        /// reload path can share one compilation between validation and transformation instead of
        /// building two.
        /// </summary>
        internal static string TransformMethodBodies(
            SemanticModel model,
            ClassDeclarationSyntax classDecl,
            Dictionary<string, string> methodBodies,
            string originalTypeName,
            Dictionary<string, MethodSignatureInfo> originalMethodSignatures,
            bool emitLineDirectives = false,
            string originalFilePath = null,
            bool rewriteInterpolations = true)
        {
            try
            {
                var root = classDecl.SyntaxTree.GetRoot();
                var classSymbol = model.GetDeclaredSymbol(classDecl);
                var compiledTwin = FindCompiledType(model.Compilation, classSymbol);
                // A top-level type with no compiled twin is entirely NEW (the file was added since
                // the build): every method is co-emittable, and the receiver parameter can't name
                // the type — it doesn't exist in any reference — so it degrades to object (the
                // dispatch passes null for statics anyway). Instance-member access still can't
                // compile in that state; new-type reloads are for self-contained static methods.
                var wholeTypeIsNew = compiledTwin == null && classSymbol?.ContainingType == null;
                var newMethods = ComputeNewMethodNames(classDecl, compiledTwin, wholeTypeIsNew);
                // A static type can't appear as a parameter (CS0721), so its receiver slot — always
                // dispatched null anyway — degrades to object. Check the compiled twin too: the
                // source may have dropped the static keyword while the running build still has it.
                var typeIsStatic = classDecl.Modifiers.Any(SyntaxKind.StaticKeyword)
                    || (compiledTwin?.IsStatic ?? false);
                var instanceTypeName = wholeTypeIsNew || typeIsStatic ? "object" : originalTypeName;
                var rewriter = new ImplicitScopeQualifier(model, classSymbol, newMethods,
                    new HashSet<string>(methodBodies.Keys));

                var sb = new StringBuilder();
                var emittedUsings = new HashSet<string>();
                foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                {
                    if (emittedUsings.Add(u.Name?.ToString() ?? u.ToString()))
                        sb.AppendLine(u.ToString());
                }
                if (emittedUsings.Add("Unity.Pipeline.MethodReload"))
                    sb.AppendLine("using Unity.Pipeline.MethodReload;");

                var namespaceName = GetNamespace(classDecl);
                if (!string.IsNullOrEmpty(namespaceName) && emittedUsings.Add(namespaceName))
                    sb.AppendLine($"using {namespaceName};");
                sb.AppendLine();

                sb.AppendLine($"public static class {originalTypeName}MethodReloadOverrides");
                sb.AppendLine("{");

                foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
                {
                    var methodName = method.Identifier.ValueText;
                    var isOverride = methodBodies.ContainsKey(methodName) && method.Body != null;
                    // A method the compiled type doesn't declare is NEW — nothing compiled can
                    // dispatch to it, but a reloaded body can: co-emit it into the override class
                    // so it becomes an interpreted method the rewritten call sites reach directly.
                    var isNewMethod = !isOverride && newMethods.Contains(methodName)
                        && (method.Body != null || method.ExpressionBody != null);
                    if (!isOverride && !isNewMethod)
                        continue;

                    // Qualify instance members first: the semantic model is bound to the original
                    // syntax tree, so this must run before any rewrite that produces new nodes.
                    // Interpolation-to-string.Format is purely syntactic — no semantic model — so
                    // it can safely run on the qualified (synthesized) tree. It runs only for the
                    // interpreter backend (see the rewriteInterpolations doc).
                    // New methods may be expression-bodied; emitted text is `=> expr;` then.
                    SyntaxNode rewrittenBody = method.Body != null
                        ? rewriter.Visit(method.Body)
                        : rewriter.Visit(method.ExpressionBody);
                    if (rewriteInterpolations)
                        rewrittenBody = new InterpolationToFormatRewriter().Visit(rewrittenBody);
                    var bodySuffix = method.Body != null ? "" : ";";
                    var returnType = method.ReturnType.ToString();

                    // Overrides always take the receiver first (the dispatch passes null for
                    // statics); a new STATIC method has no receiver at all — call sites stay bare.
                    var parameters = new List<string>();
                    if (isOverride || !method.Modifiers.Any(SyntaxKind.StaticKeyword))
                        parameters.Add($"{instanceTypeName} instance");
                    parameters.AddRange(isOverride
                        ? method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.ValueText}")
                        // New methods keep their parameter list verbatim (modifiers, defaults).
                        : method.ParameterList.Parameters.Select(p => p.ToString()));

                    if (isOverride)
                        sb.AppendLine($"    [MethodReloadOverrideMethod(\"{originalTypeName}.{methodName}\")]");
                    sb.AppendLine($"    public static {returnType} {methodName}({string.Join(", ", parameters)})");

                    if (emitLineDirectives && !string.IsNullOrEmpty(originalFilePath))
                    {
                        // Map the body back to the original file. The rewriter only makes intra-line
                        // edits, so a single #line at the body's opening brace maps every line of the
                        // block; emit the body with original trivia (not NormalizeWhitespace) to keep
                        // line counts intact. #line hidden masks the generated scaffolding that follows.
                        var bodyNode = (SyntaxNode)method.Body ?? method.ExpressionBody;
                        var bodyLine = bodyNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var escapedPath = originalFilePath.Replace("\\", "\\\\");
                        sb.AppendLine($"#line {bodyLine} \"{escapedPath}\"");
                        sb.AppendLine(rewrittenBody.ToString() + bodySuffix);
                        sb.AppendLine("#line hidden");
                    }
                    else
                    {
                        sb.AppendLine("    " + rewrittenBody.NormalizeWhitespace().ToString() + bodySuffix);
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("}");

                var result = sb.ToString();
                Debug.Log($"MethodReload: Transformed {methodBodies.Count} in-place method(s) for {originalTypeName}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"MethodReload: Error transforming in-place methods for {originalTypeName}: {ex.Message}");
                throw new InvalidOperationException(
                    $"Failed to transform in-place methods for {originalTypeName}: {ex.Message}", ex);
            }
        }

        private static string GetNamespace(SyntaxNode node)
        {
            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (current is NamespaceDeclarationSyntax ns)
                    return ns.Name.ToString();
            }
            return null;
        }

        /// <summary>
        /// Names of methods the edited source declares but the COMPILED type doesn't — methods the
        /// user added since the last compile. Their call sites can't bind to the host object, so
        /// they are co-emitted into the override class and called directly. Generic methods are
        /// excluded (outside the interpreter subset). When the whole type is new
        /// (<paramref name="wholeTypeIsNew"/>) every method qualifies; when only the compiled twin
        /// lookup failed (a nested type) the set is empty — every call site then binds to the
        /// host, the pre-existing behavior.
        /// </summary>
        private static HashSet<string> ComputeNewMethodNames(
            ClassDeclarationSyntax classDecl, INamedTypeSymbol compiledType, bool wholeTypeIsNew)
        {
            var result = new HashSet<string>();
            if (compiledType == null && !wholeTypeIsNew)
                return result;

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (method.Body == null && method.ExpressionBody == null)
                    continue;
                if (method.TypeParameterList != null)
                    continue;
                var name = method.Identifier.ValueText;
                // Declared-members check only: a name that exists compiled (any overload, or on a
                // base type but redeclared here) stays host-bound. A source method hiding a base
                // member counts as new — the co-emitted copy preserves the source's binding.
                if (wholeTypeIsNew || compiledType.GetMembers(name).Length == 0)
                    result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// The compiled (metadata) symbol for <paramref name="classSymbol"/>, resolved from the
        /// compilation's references — the source declaration shadows it inside the compilation, so
        /// the reference assemblies are probed directly. Null when no compiled twin exists.
        /// </summary>
        private static INamedTypeSymbol FindCompiledType(
            UnityPipeline.Microsoft.CodeAnalysis.Compilation compilation, INamedTypeSymbol classSymbol)
        {
            if (classSymbol == null || classSymbol.ContainingType != null)
                return null;
            var metadataName = classSymbol.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? $"{ns.ToDisplayString()}.{classSymbol.MetadataName}"
                : classSymbol.MetadataName;
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm
                    && asm.GetTypeByMetadataName(metadataName) is { } compiled)
                    return compiled;
            }
            return null;
        }

        /// <summary>
        /// Rewrites bare references that depended on the original type's scope so they still bind after
        /// the body is moved into a standalone static class, using the semantic model:
        /// instance members -> <c>instance.x</c>, static members of the type hierarchy ->
        /// <c>ContainingType.x</c>, and nested types of the type hierarchy -> <c>ContainingType.Nested</c>.
        /// Locals, parameters, and anything reachable via <c>using</c> are left untouched.
        /// </summary>
        private class ImplicitScopeQualifier : CSharpSyntaxRewriter
        {
            private readonly SemanticModel m_Model;
            private readonly INamedTypeSymbol m_Type;
            private readonly HashSet<string> m_NewMethods;
            private readonly HashSet<string> m_ReloadedMethods;

            public ImplicitScopeQualifier(SemanticModel model, INamedTypeSymbol type,
                HashSet<string> newMethods = null, HashSet<string> reloadedMethods = null)
            {
                m_Model = model;
                m_Type = type;
                m_NewMethods = newMethods ?? new HashSet<string>();
                m_ReloadedMethods = reloadedMethods ?? new HashSet<string>();
            }

            /// <summary>
            /// Calls to NEW methods (declared in the edited source, absent from the compiled type)
            /// can't bind to the host object — the callee only exists as a co-emitted static in the
            /// override class. Rebuild the call as a direct static invocation, threading the
            /// receiver (the implicit <c>instance</c>, or an explicit one like <c>other.Helper()</c>)
            /// as the first argument to match the co-emitted signature.
            /// </summary>
            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                // Resolve on the original node — the model is bound to the original tree.
                var target = m_NewMethods.Count > 0
                    ? m_Model.GetSymbolInfo(node.Expression).Symbol as IMethodSymbol
                    : null;
                var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);
                if (target == null || !m_NewMethods.Contains(target.Name)
                    || !SymbolEqualityComparer.Default.Equals(target.ContainingType, m_Type))
                    return visited;
                // Conditional calls (`x?.NewHelper()`) have no receiver expression to thread as an
                // argument; leave them — the compile fails with a clear CS1061 instead of silently
                // running the helper on the wrong instance.
                if (visited.Expression is MemberBindingExpressionSyntax)
                    return visited;

                var arguments = visited.ArgumentList;
                // A new method that is itself reloaded is emitted in the override shape — leading
                // instance parameter even when static (the dispatch it gets after the next compile
                // passes null there) — so its call sites must pass a receiver either way.
                if (!target.IsStatic || m_ReloadedMethods.Contains(target.Name))
                {
                    // The visited expression is either `receiver.Helper` (this -> instance already
                    // applied by the base visit) or a bare/qualified name; a member access carries
                    // the receiver to thread through, anything else means the implicit `instance`.
                    // A STATIC call has no receiver — its qualifier is a type name — so the
                    // caller's own `instance` is what the override-shaped callee gets.
                    var receiver = !target.IsStatic && visited.Expression is MemberAccessExpressionSyntax ma
                        ? ma.Expression
                        : (ExpressionSyntax)SyntaxFactory.IdentifierName("instance");
                    arguments = arguments.WithArguments(
                        arguments.Arguments.Insert(0, SyntaxFactory.Argument(receiver)));
                }
                return SyntaxFactory.InvocationExpression(
                        SyntaxFactory.IdentifierName(target.Name), arguments)
                    .WithTriviaFrom(visited);
            }

            public override SyntaxNode VisitThisExpression(ThisExpressionSyntax node)
            {
                return SyntaxFactory.IdentifierName("instance").WithTriviaFrom(node);
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (ShouldSkip(node))
                    return base.VisitIdentifierName(node);

                // Symbol is resolved from the original node (the semantic model is bound to the original
                // tree); the same node is what gets wrapped since an identifier has no children to rewrite.
                return TryQualify(node, node) ?? base.VisitIdentifierName(node);
            }

            public override SyntaxNode VisitGenericName(GenericNameSyntax node)
            {
                // e.g. GetComponent<Renderer>() or FindObjectsByType<T>() — visit type arguments first
                // (a type arg may itself be a nested type needing qualification), then wrap the result.
                var visited = (GenericNameSyntax)base.VisitGenericName(node);

                if (ShouldSkip(node))
                    return visited;

                return TryQualify(node, visited) ?? visited;
            }

            private static bool ShouldSkip(SimpleNameSyntax node)
            {
                // Right-hand side of a member access (foo.Bar -> Bar), or part of a qualified name.
                if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                    return true;
                if (node.Parent is QualifiedNameSyntax)
                    return true;
                if (node.Parent is MemberBindingExpressionSyntax)
                    return true;
                return false;
            }

            /// <summary>
            /// Returns the qualified replacement for <paramref name="toWrap"/> if <paramref name="original"/>
            /// binds to something that needs the original type's scope, otherwise null (leave as-is).
            /// </summary>
            private SyntaxNode TryQualify(SimpleNameSyntax original, SimpleNameSyntax toWrap)
            {
                var symbol = m_Model.GetSymbolInfo(original).Symbol;
                if (symbol == null)
                    return null;

                // `var` (and a `using Alias = Outer.Nested;` alias) binds to a type spelled
                // differently from the identifier. Qualifying the identifier would emit
                // `Outer.var` / `Outer.Alias`, which does not exist. Roslyn re-infers `var` on
                // the rewritten initializer, so leave the spelling alone.
                if (symbol is ITypeSymbol && symbol.Name != original.Identifier.ValueText)
                    return null;

                // Nested type of the type hierarchy (e.g. a private `enum State`) -> ContainingType.Nested.
                if (symbol is INamedTypeSymbol nested && nested.ContainingType != null
                    && InTypeHierarchy(nested.ContainingType))
                    return QualifyWithType(toWrap, nested.ContainingType, original);

                if (!IsMemberSymbol(symbol))
                    return null;

                if (!InTypeHierarchy(symbol.ContainingType))
                    return null;

                // A method GROUP over a new method (delegate argument, event `+=`): the member
                // doesn't exist on the compiled type, so `instance.M` can't bind. Wrap it as a
                // lambda over the co-emitted static instead.
                if (symbol is IMethodSymbol group && m_NewMethods.Contains(group.Name)
                    && !(original.Parent is InvocationExpressionSyntax inv && inv.Expression == original)
                    && MethodGroupLambda(group, original, receiver: null) is { } lambda)
                    return lambda.WithTriviaFrom(toWrap);

                return symbol.IsStatic
                    ? QualifyWithType(toWrap, symbol.ContainingType, original)
                    : QualifyWith(toWrap, SyntaxFactory.IdentifierName("instance"));
            }

            /// <summary>
            /// `x.Click` where Click is NEW and not invoked — the member-access form of a method
            /// group over a new method. Same lambda rewrite as the bare-identifier form, but the
            /// (visited) receiver expression is threaded as the co-emitted static's first argument.
            /// </summary>
            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                if (m_NewMethods.Count > 0
                    && !(node.Parent is InvocationExpressionSyntax inv && inv.Expression == node)
                    && m_Model.GetSymbolInfo(node).Symbol is IMethodSymbol group
                    && m_NewMethods.Contains(group.Name)
                    && SymbolEqualityComparer.Default.Equals(group.ContainingType, m_Type))
                {
                    var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node);
                    if (MethodGroupLambda(group, node, visited.Expression) is { } lambda)
                        return lambda.WithTriviaFrom(node);
                    return visited;
                }
                return base.VisitMemberAccessExpression(node);
            }

            /// <summary>
            /// Builds <c>((a0, a1) => M(receiver, a0, a1))</c> for a method group over a new
            /// method, shaped by the delegate type the group converts to (the semantic model's
            /// ConvertedType). Null when the conversion target isn't a delegate — the caller
            /// falls through, and the compile reports the site. One semantic difference from a
            /// real method-group conversion: the receiver expression is evaluated per invocation,
            /// not captured at conversion time.
            /// </summary>
            private ExpressionSyntax MethodGroupLambda(
                IMethodSymbol group, ExpressionSyntax original, ExpressionSyntax receiver)
            {
                if (m_Model.GetTypeInfo(original).ConvertedType is not INamedTypeSymbol delegateType
                    || delegateType.DelegateInvokeMethod is not { } invoke)
                    return null;

                var argList = new List<ArgumentSyntax>();
                // Matches the co-emitted signature: instance methods and reloaded (override-shaped)
                // methods take the receiver first; new plain statics don't.
                if (!group.IsStatic || m_ReloadedMethods.Contains(group.Name))
                    argList.Add(SyntaxFactory.Argument(receiver ?? SyntaxFactory.IdentifierName("instance")));
                var parameters = new List<ParameterSyntax>();
                for (int i = 0; i < invoke.Parameters.Length; i++)
                {
                    var name = "__mg" + i;
                    parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(name)));
                    argList.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name)));
                }
                var call = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(group.Name),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(argList)));
                return SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.ParenthesizedLambdaExpression(
                        SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)),
                        call));
            }

            private static bool IsMemberSymbol(ISymbol symbol)
            {
                switch (symbol.Kind)
                {
                    case SymbolKind.Field:
                    case SymbolKind.Property:
                    case SymbolKind.Method:
                    case SymbolKind.Event:
                        return true;
                    default:
                        return false;
                }
            }

            private bool InTypeHierarchy(INamedTypeSymbol containingType)
            {
                if (containingType == null)
                    return false;
                for (var t = m_Type; t != null; t = t.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(t, containingType))
                        return true;
                }
                return false;
            }

            /// <summary>Qualify with a fully-qualified type name (e.g. global::UnityEngine.Object.X).</summary>
            private static SyntaxNode QualifyWithType(SimpleNameSyntax node, INamedTypeSymbol type, SimpleNameSyntax original)
            {
                var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // In a type position (local/foreach declaration, cast, array type, generic argument,
                // typeof) the parent's slot holds a TypeSyntax, so the replacement must be a
                // QualifiedName — a MemberAccessExpression there makes the rewriter's typed rebuild
                // throw InvalidCastException. Expression positions keep MemberAccess; both print the
                // same text.
                if (SyntaxFacts.IsInNamespaceOrTypeContext(original))
                    return SyntaxFactory.QualifiedName(
                            SyntaxFactory.ParseName(typeName),
                            node.WithoutTrivia())
                        .WithTriviaFrom(node);

                return QualifyWith(node, SyntaxFactory.ParseExpression(typeName));
            }

            private static SyntaxNode QualifyWith(SimpleNameSyntax node, ExpressionSyntax qualifier)
            {
                return SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        qualifier,
                        node.WithoutTrivia())
                    .WithTriviaFrom(node);
            }
        }
    }

    /// <summary>
    /// Information about a method's signature for transformation purposes.
    /// </summary>
    class MethodSignatureInfo
    {
        /// <summary>The method's return type name, or "void".</summary>
        public string ReturnType { get; set; }
        /// <summary>The method's parameters.</summary>
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        /// <summary>True unless <see cref="ReturnType"/> is "void".</summary>
        public bool ReturnsValue => !string.IsNullOrEmpty(ReturnType) && ReturnType != "void";
    }

    /// <summary>
    /// Information about a method parameter.
    /// </summary>
    class ParameterInfo
    {
        /// <summary>The parameter's type name.</summary>
        public string Type { get; set; }
        /// <summary>The parameter's name.</summary>
        public string Name { get; set; }
        /// <summary>Whether the parameter has a default value.</summary>
        public bool HasDefaultValue { get; set; }
        /// <summary>The parameter's default value expression, if any.</summary>
        public string DefaultValue { get; set; }
    }
}
#endif
