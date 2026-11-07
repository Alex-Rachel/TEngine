using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// An assembly name that Unity's own modules grant <see cref="InternalsVisibleToAttribute"/>. Code
    /// compiled under it may call their internal APIs: Roslyn honors the grant at compile time and
    /// Mono at load time.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class InternalsAccessName
    {
        static readonly string[] k_RequiredGrantors = { "UnityEditor.CoreModule", "UnityEngine.CoreModule" };

        static string[] s_Candidates;

        /// <summary>
        /// A name both core modules grant that no loaded assembly uses yet, so each compile under it
        /// loads as a new assembly: the Editor hands back the already-loaded assembly for a name it
        /// has seen. Names of Unity's own test assemblies come first, since the Editor never loads a
        /// real assembly of that name, then names granted by the most Unity modules. Null once every
        /// such name is loaded, until the next domain reload.
        /// </summary>
        internal static string Get()
        {
            s_Candidates ??= Candidates();
            var loaded = LoadedNames();
            return s_Candidates.FirstOrDefault(name => !loaded.Contains(name));
        }

        static HashSet<string> LoadedNames() =>
            new HashSet<string>(PipelineUtils.GetLoadedAssemblies().Where(a => !a.IsDynamic).Select(a => a.GetName().Name), StringComparer.Ordinal);

        static string[] Candidates()
        {
            var assemblies = PipelineUtils.GetLoadedAssemblies().Where(a => !a.IsDynamic).ToList();
            var loaded = new HashSet<string>(assemblies.Select(a => a.GetName().Name), StringComparer.Ordinal);

            var grantCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var module in assemblies)
            {
                var moduleName = module.GetName().Name;
                if (!moduleName.StartsWith("UnityEngine.", StringComparison.Ordinal) && !moduleName.StartsWith("UnityEditor.", StringComparison.Ordinal))
                    continue;
                foreach (var name in GrantedNames(module))
                {
                    grantCounts.TryGetValue(name, out var count);
                    grantCounts[name] = count + 1;
                }
            }

            var requiredGrantors = assemblies.Where(a => k_RequiredGrantors.Contains(a.GetName().Name)).ToList();
            if (requiredGrantors.Count != k_RequiredGrantors.Length)
                return Array.Empty<string>();

            var candidates = new HashSet<string>(GrantedNames(requiredGrantors[0]), StringComparer.Ordinal);
            foreach (var grantor in requiredGrantors.Skip(1))
                candidates.IntersectWith(GrantedNames(grantor));
            candidates.ExceptWith(loaded);

            return candidates
                .OrderByDescending(name => name.IndexOf("Test", StringComparison.Ordinal) >= 0)
                .ThenByDescending(name => grantCounts[name])
                .ThenBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>The names an assembly grants internal access to, without a public key.</summary>
        static IEnumerable<string> GrantedNames(System.Reflection.Assembly assembly)
        {
            object[] attributes;
            try
            {
                attributes = assembly.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false);
            }
            catch
            {
                return Array.Empty<string>();
            }

            return attributes.Cast<InternalsVisibleToAttribute>()
                .Where(a => a.AssemblyName.IndexOf("PublicKey", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(a => a.AssemblyName.Split(',')[0].Trim())
                .Distinct();
        }
    }
}
