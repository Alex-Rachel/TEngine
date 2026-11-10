using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.GameObjects
{
    /// <summary>
    /// Resolves a <see cref="UnityEngine.Component"/>-derived <see cref="Type"/> from an
    /// agent-supplied name. WHY this is non-trivial: agents pass either a short name ("Rigidbody")
    /// or a fully-qualified one ("UnityEngine.Camera"), and the type may live in any loaded assembly
    /// (UnityEngine modules, user assemblies, packages). We therefore try, in order: an exact
    /// fully-qualified <see cref="Type.GetType(string)"/>, then a scan of all loaded assemblies for a
    /// full-name match, then a short-name match. Only <see cref="Component"/> subclasses are accepted
    /// so the result is always valid for AddComponent/GetComponent.
    /// </summary>
    static class TypeResolver
    {
        /// <summary>
        /// Resolve a component type by name, or return null if no unambiguous <see cref="Component"/>
        /// subclass matches. A short name that matches multiple types in different namespaces is
        /// treated as ambiguous and returns null (the caller should ask for a fully-qualified name).
        /// </summary>
        public static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            typeName = typeName.Trim();

            // 1. Exact, assembly-qualified or fully-qualified lookup.
            var direct = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (IsComponent(direct))
                return direct;

            var assemblies = PipelineUtils.GetLoadedAssemblies();

            // 2. Full-name match across loaded assemblies.
            var fullNameMatches = new List<Type>();
            // 3. Short-name match (collected in the same pass) as a fallback.
            var shortNameMatches = new List<Type>();

            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (!IsComponent(type))
                        continue;

                    if (string.Equals(type.FullName, typeName, StringComparison.Ordinal))
                        fullNameMatches.Add(type);
                    else if (string.Equals(type.Name, typeName, StringComparison.Ordinal))
                        shortNameMatches.Add(type);
                }
            }

            if (fullNameMatches.Count == 1)
                return fullNameMatches[0];
            if (fullNameMatches.Count > 1)
                return null; // genuinely ambiguous full names (rare) — caller must disambiguate.

            // Distinct in case the same type is reachable through multiple assembly references.
            var distinctShort = shortNameMatches.Distinct().ToList();
            return distinctShort.Count == 1 ? distinctShort[0] : null;
        }

        private static bool IsComponent(Type type)
        {
            return type != null && typeof(Component).IsAssignableFrom(type) && !type.IsAbstract;
        }
    }
}
