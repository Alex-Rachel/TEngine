using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Pipeline.Models;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// Finds the namespace that declares a name a script could not resolve (CS0246, CS0103), so the
    /// script can be compiled again with that namespace imported. Extension methods (CS1061) and
    /// partially written namespaces are not resolved.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EvalAutoImport
    {
        const string k_TypeNotFound = "CS0246";
        const string k_NameNotFound = "CS0103";

        // Type name (with its `N arity suffix) to the namespaces declaring it, built on first use
        // from the loaded assemblies. A domain reload, which is when that set changes, clears it.
        static Dictionary<string, SortedSet<string>> s_PublicIndex;
        static Dictionary<string, SortedSet<string>> s_AllIndex;

        /// <summary>What one pass over a compilation's errors found.</summary>
        internal sealed class Resolution
        {
            /// <summary>The namespaces to import: one per unresolved name declared in exactly one.</summary>
            public readonly List<string> Namespaces = new List<string>();

            /// <summary>Whether some name is declared in several namespaces.</summary>
            public bool Ambiguous;
        }

        /// <summary>
        /// Looks up each unresolved name in <paramref name="diagnostics"/>. A name declared in
        /// several namespaces gets the candidates appended to its diagnostic's message.
        /// </summary>
        /// <param name="diagnostics">The failed compilation's diagnostics.</param>
        /// <param name="includeNonPublic">Whether non-public types are candidates too.</param>
        internal static Resolution Resolve(List<DiagnosticInfo> diagnostics, bool includeNonPublic)
        {
            var resolution = new Resolution();
            Dictionary<string, SortedSet<string>> index = null;

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity != "error" || (diagnostic.Id != k_TypeNotFound && diagnostic.Id != k_NameNotFound))
                    continue;
                if (!TryParseName(diagnostic.SpanText, out var name, out var arity))
                    continue;

                index ??= Index(includeNonPublic);
                var candidates = Lookup(index, name, arity, diagnostic.Id == k_TypeNotFound);
                if (candidates.Count == 1)
                {
                    var ns = candidates.Min;
                    if (!resolution.Namespaces.Contains(ns))
                        resolution.Namespaces.Add(ns);
                }
                else if (candidates.Count > 1)
                {
                    resolution.Ambiguous = true;
                    diagnostic.Message += $" auto-import found it in several namespaces: " +
                        $"{string.Join(", ", candidates.Select(ns => ns + "." + name))}. Add a using directive for the one you mean.";
                }
            }

            return resolution;
        }

        /// <summary>
        /// Splits a name as written, such as <c>Foo</c> or <c>Dictionary&lt;string, int&gt;</c>, into
        /// its identifier and generic arity.
        /// </summary>
        static bool TryParseName(string text, out string name, out int arity)
        {
            name = null;
            arity = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            var open = text.IndexOf('<');
            name = open < 0 ? text : text.Substring(0, open).Trim();
            if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return false;

            if (open >= 0)
            {
                arity = 1;
                var depth = 0;
                for (var i = open; i < text.Length; i++)
                {
                    if (text[i] == '<') depth++;
                    else if (text[i] == '>') depth--;
                    else if (text[i] == ',' && depth == 1) arity++;
                }
            }

            return true;
        }

        static SortedSet<string> Lookup(Dictionary<string, SortedSet<string>> index, string name, int arity, bool mayBeAttribute)
        {
            var candidates = new SortedSet<string>(StringComparer.Ordinal);
            if (index.TryGetValue(arity > 0 ? $"{name}`{arity}" : name, out var namespaces))
                candidates.UnionWith(namespaces);
            if (mayBeAttribute && arity == 0 && index.TryGetValue(name + "Attribute", out var attributeNamespaces))
                candidates.UnionWith(attributeNamespaces);
            return candidates;
        }

        static Dictionary<string, SortedSet<string>> Index(bool includeNonPublic)
        {
            if (includeNonPublic)
                return s_AllIndex ??= BuildIndex(includeNonPublic: true);
            return s_PublicIndex ??= BuildIndex(includeNonPublic: false);
        }

        static Dictionary<string, SortedSet<string>> BuildIndex(bool includeNonPublic)
        {
            var index = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                foreach (var type in LoadableTypes(assembly))
                {
                    if (type == null || type.IsNested || string.IsNullOrEmpty(type.Namespace) || type.Name.IndexOf('<') >= 0)
                        continue;
                    if (!type.IsPublic && !includeNonPublic)
                        continue;

                    if (!index.TryGetValue(type.Name, out var namespaces))
                        index[type.Name] = namespaces = new SortedSet<string>(StringComparer.Ordinal);
                    namespaces.Add(type.Namespace);
                }
            }

            return index;
        }

        static Type[] LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types;
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }
}
