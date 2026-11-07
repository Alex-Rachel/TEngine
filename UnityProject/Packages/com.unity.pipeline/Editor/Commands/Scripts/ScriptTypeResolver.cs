using System;
using System.Linq;
using System.Reflection;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.Scripts
{
    /// <summary>
    /// Resolves an agent-supplied type name (or a <see cref="MonoScript"/> reference) to a compiled
    /// <see cref="Type"/>, searching the currently-loaded assemblies.
    ///
    /// This is the heart of the compile-aware behaviour for CLI-195: a script that was just created
    /// has no compiled type until a domain reload runs, so resolution here will fail until the agent
    /// recompiles. Callers translate that failure into a clear, recoverable error rather than a crash.
    /// </summary>
    static class ScriptTypeResolver
    {
        /// <summary>
        /// Try to resolve a MonoBehaviour-derived type by its name. Accepts either a bare class name
        /// ("PlayerController") or a fully-qualified name ("Game.Player.PlayerController"). When a
        /// bare name is ambiguous across namespaces the first match wins; callers should prefer the
        /// fully-qualified name. Returns false with a human-readable <paramref name="error"/> when no
        /// loaded type matches (typically: not yet compiled).
        /// </summary>
        public static bool TryResolveComponentType(string typeName, out Type type, out string error)
        {
            type = null;
            error = null;

            if (string.IsNullOrWhiteSpace(typeName))
            {
                error = "Type name is required.";
                return false;
            }

            var name = typeName.Trim();
            var match = FindType(name);

            if (match == null)
            {
                error =
                    $"Type '{name}' was not found in any loaded assembly. " +
                    $"If you just created this script it is not compiled yet: run '{CommandNames.ScriptRecompile}', poll " +
                    $"'{CommandNames.ScriptRecompileStatus}' until it reports completed/up_to_date, then retry {CommandNames.ScriptAttach}. " +
                    "If it should already exist, check the class name (and namespace) and that the file compiled without errors.";
                return false;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(match))
            {
                error = $"Type '{match.FullName}' does not derive from MonoBehaviour and cannot be added as a component.";
                return false;
            }

            if (match.IsAbstract)
            {
                error = $"Type '{match.FullName}' is abstract and cannot be instantiated as a component.";
                return false;
            }

            type = match;
            return true;
        }

        /// <summary>
        /// Resolve the compiled <see cref="Type"/> backing a <see cref="MonoScript"/> asset reference
        /// (e.g. one returned by create_script). Returns false when the script has no class yet
        /// (uncompiled) or the asset is not a MonoScript.
        /// </summary>
        public static bool TryResolveFromMonoScript(MonoScript script, out Type type, out string error)
        {
            type = null;
            error = null;

            if (script == null)
            {
                error = "MonoScript reference is null.";
                return false;
            }

            var scriptClass = script.GetClass();
            if (scriptClass == null)
            {
                error =
                    $"Script '{script.name}' has no compiled class. It is likely not compiled yet: run '{CommandNames.ScriptRecompile}', " +
                    $"poll '{CommandNames.ScriptRecompileStatus}', then retry.";
                return false;
            }

            type = scriptClass;
            return true;
        }

        /// <summary>
        /// Search all loaded assemblies for a type by full name first, then by bare class name. Done
        /// over <see cref="AppDomain.CurrentDomain"/> rather than TypeCache so it also finds types in
        /// non-Unity-managed assemblies and matches what Activator/AddComponent can actually load.
        /// </summary>
        private static Type FindType(string name)
        {
            var assemblies = PipelineUtils.GetLoadedAssemblies();

            // 1. Fully-qualified name (exact).
            foreach (var asm in assemblies)
            {
                var t = asm.GetType(name, throwOnError: false, ignoreCase: false);
                if (t != null)
                    return t;
            }

            // 2. Bare class name — match on Name across loaded types. GetTypes() is expensive, so skip
            // dynamic and system/Unity assemblies (which never hold user MonoBehaviours). This mirrors
            // CommandRegistry.IsSystemAssembly's predicate (replicated locally rather than shared so we
            // don't widen that internal helper's surface).
            foreach (var asm in assemblies)
            {
                if (IsSkippableAssembly(asm))
                    continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                var hit = types.FirstOrDefault(t => t != null && t.Name == name);
                if (hit != null)
                    return hit;
            }

            return null;
        }

        /// <summary>
        /// Assemblies we never need to scan for a user MonoBehaviour: dynamic (in-memory, can't be
        /// reflected for types reliably) and core system/Unity assemblies. Mirrors the predicate in
        /// <c>CommandRegistry.IsSystemAssembly</c>.
        /// </summary>
        private static bool IsSkippableAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic)
                return true;

            var name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name))
                return false;

            return name.StartsWith("System.", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                   name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                   name.StartsWith("netstandard", StringComparison.Ordinal) ||
                   name.Equals("UnityEngine", StringComparison.Ordinal) ||
                   name.Equals("UnityEditor", StringComparison.Ordinal);
        }
    }
}
