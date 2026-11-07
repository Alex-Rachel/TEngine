#if UNITY_EDITOR || (UNITY_STANDALONE && DEBUG)
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Telemetry;
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// Lists the Unity members a compilation's source resolves to: methods, constructors,
    /// properties, fields and events declared in Unity's assemblies, named
    /// <c>Namespace.Type.Member</c> without type arguments. Names that don't resolve are skipped,
    /// so a failed compile still lists the parts that bound.
    /// </summary>
    static class UnityApiCollector
    {
        const int k_MaxApis = 100;

        static readonly SymbolDisplayFormat k_TypeFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None);

        public static IReadOnlyList<string> Collect(UnityPipeline.Microsoft.CodeAnalysis.Compilation compilation, SyntaxTree tree)
        {
            var model = compilation.GetSemanticModel(tree);
            var apis = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (!(node is SimpleNameSyntax || node is BaseObjectCreationExpressionSyntax))
                    continue;

                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol is IMethodSymbol method)
                    symbol = method.ReducedFrom ?? method;
                if (!(symbol is IMethodSymbol || symbol is IPropertySymbol || symbol is IFieldSymbol || symbol is IEventSymbol))
                    continue;

                symbol = symbol.OriginalDefinition;
                // A script compiled under an internals-access name can itself carry a Unity-looking name.
                if (SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly)
                    || !EvalAnalyticsDetails.IsUnityAssembly(symbol.ContainingAssembly?.Name))
                    continue;

                apis.Add(symbol.ContainingType.ToDisplayString(k_TypeFormat) + "." + symbol.Name);
            }

            return apis.Take(k_MaxApis).ToArray();
        }
    }
}
#endif
