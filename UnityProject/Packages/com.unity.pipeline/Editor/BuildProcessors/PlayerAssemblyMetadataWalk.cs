using System;
using System.Collections.Generic;
using System.IO;
using Unity.Pipeline.Compilation;
// The package bundles System.Reflection.Metadata renamed with a "UnityPipeline." prefix
// (Runtime/Plugins/CodeAnalysis); the interpreter reads pushed IL with the same reader.
using UnityPipeline.System.Reflection.Metadata;
using UnityPipeline.System.Reflection.PortableExecutable;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.BuildProcessors
{
    /// <summary>
    /// Reads one player assembly's metadata tables and reports the types it defines and the
    /// engine/package/plugin types it references, in <c>link.xml</c> fullname form (nested types joined
    /// with <c>/</c>). No IL decoding, no assembly resolution: TypeDefinitions gives the assembly's
    /// own types, TypeReferences every type it uses from another assembly (IL, signatures, fields,
    /// attributes; generic instantiations point back at the open definition). Consumed by
    /// <see cref="MethodReloadLinkXml"/>.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class PlayerAssemblyMetadataWalk
    {
        public sealed class Scan
        {
            public string AssemblyName;
            /// <summary>Non-compiler-generated types this assembly defines.</summary>
            public readonly List<string> DefinedTypes = new();
            /// <summary>(owning assembly, type) pairs for referenced engine/package types.</summary>
            public readonly List<(string Assembly, string Type)> ReferencedTypes = new();
        }

        /// <summary>Null when the file carries no managed metadata.</summary>
        public static Scan Read(string dllPath)
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            var scan = new Scan { AssemblyName = md.GetString(md.GetAssemblyDefinition().Name) };

            // Own types, minus compiler-generated shapes (closures, iterator/async state machines) no
            // reloaded body can name.
            foreach (var handle in md.TypeDefinitions)
            {
                var name = DefinitionFullName(md, md.GetTypeDefinition(handle));
                if (name != null) scan.DefinedTypes.Add(name);
            }

            foreach (var handle in md.TypeReferences)
            {
                var scope = ReferenceScopeAndName(md, md.GetTypeReference(handle), out var name);
                if (scope == null || name == null) continue;
                if (!IsPreservableReferenceScope(scope)) continue;
                if (scope == "UnityEngine")
                {
                    // Facade with type forwarders; the linker wants the real module.
                    scope = ResolveForwardedModule(name);
                    if (scope == null) continue;
                }
                scan.ReferencedTypes.Add((scope, name));
            }
            return scan;
        }

        // Mirrors the runtime's demand-time auto-bind policy (IlInterpreterHostBindings): whatever a
        // reloaded body may bind to on device is what the file must keep. That excludes the BCL (the
        // interpreter's hand-rolled shims are authoritative there) and this package; additionally the
        // bundled Roslyn/BCL shims, which no game code should reference, and UnityEditor*, which the
        // editor-compiled assemblies reference from #if UNITY_EDITOR blocks but no player contains.
        // Everything else counts — engine modules, Unity packages, and third-party plugins such as
        // Newtonsoft.Json alike.
        internal static bool IsPreservableReferenceScope(string assemblyName) =>
            !IlInterpreterHostBindings.IsAutoBindSkippedAssembly(assemblyName) &&
            !assemblyName.StartsWith("UnityPipeline.", StringComparison.Ordinal) &&
            !assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal);

        static string DefinitionFullName(MetadataReader md, TypeDefinition td)
        {
            var name = md.GetString(td.Name);
            if (name.StartsWith("<", StringComparison.Ordinal)) return null; // <Module>, <>c, <Foo>d__1
            var declaring = td.GetDeclaringType();
            if (!declaring.IsNil)
            {
                var outer = DefinitionFullName(md, md.GetTypeDefinition(declaring));
                return outer == null ? null : outer + "/" + name;
            }
            var ns = md.GetString(td.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        // Walks the nesting chain of a TypeReference outward; returns the owning assembly's simple
        // name (null for module-scoped or unresolvable references) and the link.xml fullname.
        static string ReferenceScopeAndName(MetadataReader md, TypeReference tr, out string fullName)
        {
            var name = md.GetString(tr.Name);
            var scope = tr.ResolutionScope;
            switch (scope.Kind)
            {
                case HandleKind.AssemblyReference:
                    var ns = md.GetString(tr.Namespace);
                    fullName = ns.Length == 0 ? name : ns + "." + name;
                    return md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                case HandleKind.TypeReference:
                    var owner = ReferenceScopeAndName(md, md.GetTypeReference((TypeReferenceHandle)scope), out var outer);
                    fullName = outer == null ? null : outer + "/" + name;
                    return owner;
                default:
                    fullName = null;
                    return null;
            }
        }

        static readonly Dictionary<string, string> s_ForwardCache = new(StringComparer.Ordinal);

        // A reference through the UnityEngine facade: find the module that actually defines the type.
        static string ResolveForwardedModule(string linkName)
        {
            if (s_ForwardCache.TryGetValue(linkName, out var cached)) return cached;
            string found = null;
            var clrName = linkName.Replace('/', '+');
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                var n = asm.GetName().Name;
                if (n == null || !n.StartsWith("UnityEngine.", StringComparison.Ordinal)) continue;
                Type t = null;
                try { t = asm.GetType(clrName, throwOnError: false); } catch { }
                if (t != null) { found = n; break; }
            }
            s_ForwardCache[linkName] = found;
            return found;
        }
    }
}
