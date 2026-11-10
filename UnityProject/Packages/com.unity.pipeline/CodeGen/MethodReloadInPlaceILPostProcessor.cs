using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Unity.Pipeline.CodeGen
{
    /// <summary>
    /// Weaves a method-reload dispatch prologue into every method tagged [MethodReload] at compile
    /// time, so a running method can route to a reloaded override with no domain reload. The
    /// injected prologue is:
    ///
    ///     if (MethodReloadRegistry.TryInvokeMethodReload("Type.Method", this, args)) return;
    ///     // ... original body ...
    ///
    /// Weaves instance AND static methods (a static method dispatches with a null instance),
    /// returning void, a value (via TryInvokeMethodReloadResult), or a System.Collections.IEnumerator
    /// coroutine. Parameters are supported (boxed into object[]). Only IEnumerable/generic
    /// iterator returns are skipped with a diagnostic.
    /// </summary>
    class MethodReloadInPlaceILPostProcessor : ILPostProcessor
    {
        private const string RuntimeAssemblyName = "Unity.Pipeline";
        // Full name: a user attribute merely named MethodReloadAttribute must not get a dispatch
        // prologue — runtime discovery checks the real type, so its overrides would never bind.
        private const string AttributeFullName = "Unity.Pipeline.MethodReload.MethodReloadAttribute";
        private const string RegistryTypeFullName = "Unity.Pipeline.MethodReload.MethodReloadRegistry";
        private const string DispatchMethodName = "TryInvokeMethodReload";
        private const string ResultDispatchMethodName = "TryInvokeMethodReloadResult";
        private const string HasOverrideMethodName = "HasOverride";
        private const string ThrowNullResultMethodName = "ThrowOverrideReturnedNull";

        /// <summary>Unity requires a fresh instance per compilation; this processor is stateless, so returns itself.</summary>
        /// <returns>This instance.</returns>
        public override ILPostProcessor GetInstance() => this;

        /// <summary>Whether this assembly should be woven: only those referencing the runtime assembly can use [MethodReload].</summary>
        /// <param name="compiledAssembly">The assembly about to be processed.</param>
        /// <returns>True if the assembly references the Unity.Pipeline runtime assembly.</returns>
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            // Only assemblies that reference the runtime assembly can use [MethodReload] or call
            // the registry. This naturally excludes the runtime assembly itself (it does not
            // reference itself) and the CodeGen assembly (no runtime reference).
            return compiledAssembly.References.Any(
                r => Path.GetFileNameWithoutExtension(r) == RuntimeAssemblyName);
        }

        /// <summary>Weave the method-reload dispatch prologue into every [MethodReload] method in the assembly.</summary>
        /// <param name="compiledAssembly">The assembly to process.</param>
        /// <returns>The woven assembly bytes plus any diagnostics.</returns>
        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();

            using (var resolver = new PostProcessorAssemblyResolver(compiledAssembly.References))
            using (var assembly = ReadAssembly(compiledAssembly, resolver))
            {
                var module = assembly.MainModule;

                var targets = module.GetTypes()
                    .SelectMany(t => t.Methods)
                    .Where(m => m.HasBody && m.CustomAttributes.Any(a => a.AttributeType.FullName == AttributeFullName))
                    .ToList();

                // A user attribute merely NAMED MethodReloadAttribute never weaves (full-name match
                // above) — say so, or the author of a [MethodReload] imported from the wrong
                // namespace debugs a silent no-op. Runs before the targets early-out: a module
                // whose only tagged methods carry the impostor still gets the warning.
                WarnForeignMethodReloadAttributes(module, diagnostics);

                if (targets.Count == 0)
                    return new ILPostProcessResult(null, diagnostics);

                // The asmdef may declare Unity.Pipeline while the compiler prunes the reference, because
                // the C# compiler omits an assembly reference no type is used from. An assembly that only
                // uses [MethodReload] (which lives in Unity.Pipeline.Attributes) hits this, and the woven
                // prologue below needs the reference to call into MethodReloadRegistry. WillProcess already
                // confirmed Unity.Pipeline is among the compilation references, so resolve it and add it.
                if (module.AssemblyReferences.All(a => a.Name != RuntimeAssemblyName))
                {
                    var probe = new AssemblyNameReference(RuntimeAssemblyName, new Version(0, 0, 0, 0));
                    var runtime = resolver.Resolve(probe);
                    if (runtime != null)
                        module.AssemblyReferences.Add(AssemblyNameReference.Parse(runtime.Name.FullName));
                }

                // Resolve failures below leave every [MethodReload] method unwoven — the per-cause
                // warning alone reads like a detail, so state the consequence explicitly.
                var dispatch = ResolveDispatchMethod(module, resolver, diagnostics);
                var hasOverride = dispatch == null ? null : ResolveHasOverrideMethod(module, resolver, diagnostics);
                if (dispatch == null || hasOverride == null)
                {
                    diagnostics.Add(Warn($"{targets.Count} [MethodReload] method(s) in {module.Name} were NOT woven — " +
                        "method reload will not work for this assembly (see the preceding warning for the cause)."));
                    return new ILPostProcessResult(null, diagnostics);
                }

                // Value-returning [MethodReload] methods dispatch through the out-result overload; may be
                // absent in an older runtime, in which case those methods are skipped (below).
                var resultDispatch = ResolveResultDispatchMethod(module, resolver, diagnostics);

                // Guard helper for value-type returns (see WeaveDispatchPrologue); absent in an
                // older runtime, in which case the unguarded (pre-existing) IL is woven.
                var throwNullResult = ResolveThrowNullResultMethod(module, resolver, diagnostics);

                int wovenCount = 0;
                foreach (var method in targets)
                {
                    bool isVoid = method.ReturnType.MetadataType == MetadataType.Void;

                    // Coroutines returning System.Collections.IEnumerator weave like any other
                    // value-returning method: the woven stub dispatches at iterator CREATION
                    // (per StartCoroutine call), and the override's state machine — compiled
                    // (Mono) or interpreted via the ScriptEnumerator bridge (IL2CPP) — is what
                    // the scheduler drives. IEnumerable/generic iterator returns stay excluded:
                    // nothing in the coroutine workflow produces them and the interpreter bridge
                    // only implements non-generic IEnumerator.
                    if (!isVoid && IsExcludedIteratorReturn(method.ReturnType))
                    {
                        diagnostics.Add(Warn($"[MethodReload] '{method.FullName}' returns an IEnumerable or generic iterator and was skipped (coroutines must return System.Collections.IEnumerator)."));
                        continue;
                    }

                    if (!isVoid && resultDispatch == null)
                    {
                        diagnostics.Add(Warn($"[MethodReload] '{method.FullName}' returns a value but the runtime has no {ResultDispatchMethodName}; skipped."));
                        continue;
                    }

                    WeaveDispatchPrologue(method, dispatch, resultDispatch, hasOverride, throwNullResult);
                    wovenCount++;
                }

                if (wovenCount == 0)
                    return new ILPostProcessResult(null, diagnostics);

                return new ILPostProcessResult(WriteAssembly(assembly), diagnostics);
            }
        }

        /// <summary>
        /// Warn for every method tagged with an attribute that shares the simple name
        /// MethodReloadAttribute but is not the Unity.Pipeline one: it gets no dispatch prologue by
        /// design (runtime discovery checks the real type), and without the warning that reads as
        /// method reload silently not working.
        /// </summary>
        private static void WarnForeignMethodReloadAttributes(ModuleDefinition module, List<DiagnosticMessage> diagnostics)
        {
            foreach (var method in module.GetTypes().SelectMany(t => t.Methods))
            {
                if (!method.HasCustomAttributes)
                    continue;
                var impostor = method.CustomAttributes.FirstOrDefault(a =>
                    a.AttributeType.Name == "MethodReloadAttribute" &&
                    a.AttributeType.FullName != AttributeFullName);
                if (impostor != null)
                    diagnostics.Add(Warn($"'{method.FullName}' is tagged [{impostor.AttributeType.FullName}], " +
                        $"which is not {AttributeFullName} — no dispatch prologue was woven, method reload will not apply to it."));
            }
        }

        /// <summary>True for System.Nullable`1, whose unbox.any accepts null (empty Nullable).</summary>
        private static bool IsNullable(TypeReference rt)
            => rt is GenericInstanceType git && git.ElementType.FullName == "System.Nullable`1";

        /// <summary>True for the iterator returns that stay excluded from weaving: IEnumerable
        /// (both) and generic IEnumerator`1. Plain System.Collections.IEnumerator — the Unity
        /// coroutine shape — weaves through the result dispatch (see the weave site).</summary>
        private static bool IsExcludedIteratorReturn(TypeReference rt)
        {
            var name = rt.GetElementType().FullName;
            return name == "System.Collections.IEnumerable"
                || name == "System.Collections.Generic.IEnumerator`1"
                || name == "System.Collections.Generic.IEnumerable`1";
        }

        /// <summary>
        /// Inject, at the very top of the method:
        ///     if (MethodReloadRegistry.HasOverride("Type.Method")
        ///         &amp;&amp; MethodReloadRegistry.TryInvokeMethodReload("Type.Method", this, args)) return;
        ///
        /// The HasOverride guard is a cheap dictionary lookup that runs first, so the (boxed) argument
        /// array is only built when an override is actually active — a woven method with no override
        /// allocates nothing per call.
        /// </summary>
        private static void WeaveDispatchPrologue(
            MethodDefinition method, MethodReference dispatch, MethodReference resultDispatch,
            MethodReference hasOverride, MethodReference throwNullResult)
        {
            var body = method.Body;
            body.SimplifyMacros();

            var il = body.GetILProcessor();
            var first = body.Instructions[0];
            var methodId = $"{method.DeclaringType.Name}.{method.Name}";
            bool isVoid = method.ReturnType.MetadataType == MetadataType.Void;

            // A value-returning method holds the dispatched result in a local, then unboxes it.
            VariableDefinition resultLocal = null;
            if (!isVoid)
            {
                resultLocal = new VariableDefinition(method.Module.TypeSystem.Object);
                body.Variables.Add(resultLocal);
            }

            var prologue = new List<Instruction>
            {
                // Guard: skip straight to the original body (no allocation) when no override is active.
                Instruction.Create(OpCodes.Ldstr, methodId),
                Instruction.Create(OpCodes.Call, hasOverride),
                Instruction.Create(OpCodes.Brfalse, first),
                // An override exists: build the dispatch call and its argument array.
                Instruction.Create(OpCodes.Ldstr, methodId),
                // The dispatch's instance argument: `this`, or null for a static method (the
                // override ABI keeps a leading instance parameter either way; it arrives null).
                method.IsStatic
                    ? Instruction.Create(OpCodes.Ldnull)
                    : Instruction.Create(OpCodes.Ldarg_0),
            };

            // Build the object[] of parameters (null when parameterless).
            var ps = method.Parameters;
            if (ps.Count == 0)
            {
                prologue.Add(Instruction.Create(OpCodes.Ldnull));
            }
            else
            {
                prologue.Add(Instruction.Create(OpCodes.Ldc_I4, ps.Count));
                prologue.Add(Instruction.Create(OpCodes.Newarr, method.Module.TypeSystem.Object));
                for (int i = 0; i < ps.Count; i++)
                {
                    prologue.Add(Instruction.Create(OpCodes.Dup));
                    prologue.Add(Instruction.Create(OpCodes.Ldc_I4, i));
                    prologue.Add(Instruction.Create(OpCodes.Ldarg, ps[i]));
                    if (ps[i].ParameterType.IsValueType || ps[i].ParameterType.IsGenericParameter)
                        prologue.Add(Instruction.Create(OpCodes.Box, ps[i].ParameterType));
                    prologue.Add(Instruction.Create(OpCodes.Stelem_Ref));
                }
            }

            if (isVoid)
            {
                //   if (TryInvokeMethodReload(id, this, args)) return;  else <body>
                prologue.Add(Instruction.Create(OpCodes.Call, dispatch));
                prologue.Add(Instruction.Create(OpCodes.Brfalse, first)); // no override -> run original body
                prologue.Add(Instruction.Create(OpCodes.Ret));            // override ran -> skip body
            }
            else
            {
                //   if (TryInvokeMethodReloadResult(id, this, args, out object r)) return (TRet)r;  else <body>
                prologue.Add(Instruction.Create(OpCodes.Ldloca, resultLocal)); // out object result
                prologue.Add(Instruction.Create(OpCodes.Call, resultDispatch));
                prologue.Add(Instruction.Create(OpCodes.Brfalse, first));      // no override -> run original body
                var loadResult = Instruction.Create(OpCodes.Ldloc, resultLocal);
                // A null result cannot unbox to a value type — without this guard the unbox.any
                // below throws an opaque NullReferenceException at the call site. Nullable<T>
                // stays unguarded: unbox.any legitimately converts null to an empty Nullable.
                if (throwNullResult != null && method.ReturnType.IsValueType && !IsNullable(method.ReturnType))
                {
                    prologue.Add(Instruction.Create(OpCodes.Ldloc, resultLocal));
                    prologue.Add(Instruction.Create(OpCodes.Brtrue, loadResult));
                    prologue.Add(Instruction.Create(OpCodes.Ldstr, methodId));
                    prologue.Add(Instruction.Create(OpCodes.Call, throwNullResult)); // never returns
                }
                prologue.Add(loadResult);
                // unbox.any handles both value types (unbox) and reference types (castclass).
                prologue.Add(Instruction.Create(OpCodes.Unbox_Any, method.ReturnType));
                prologue.Add(Instruction.Create(OpCodes.Ret));                 // override ran -> return its result
            }

            foreach (var instr in prologue)
                il.InsertBefore(first, instr);

            body.OptimizeMacros();
        }

        /// <summary>
        /// Resolve a MethodReloadRegistry method (static, non-generic; <paramref name="match"/> picks
        /// the overload) from the runtime assembly and import it into the target module.
        /// <paramref name="missingMessage"/> is the warning added when the registry or the method
        /// can't be found; null makes absence silent — for overloads an older runtime may not have.
        /// </summary>
        private static MethodReference ResolveRegistryMethod(
            ModuleDefinition module, PostProcessorAssemblyResolver resolver, List<DiagnosticMessage> diagnostics,
            Func<MethodDefinition, bool> match, string missingMessage)
        {
            var runtimeRef = module.AssemblyReferences.FirstOrDefault(a => a.Name == RuntimeAssemblyName);
            if (runtimeRef == null)
            {
                if (missingMessage != null)
                    diagnostics.Add(Warn($"Could not find a reference to {RuntimeAssemblyName} while weaving {module.Name}."));
                return null;
            }

            var runtime = resolver.Resolve(runtimeRef);
            var registry = runtime?.MainModule.GetType(RegistryTypeFullName);
            if (registry == null)
            {
                if (missingMessage != null)
                    diagnostics.Add(Warn($"Could not resolve {RegistryTypeFullName} in {RuntimeAssemblyName}."));
                return null;
            }

            var method = registry.Methods.FirstOrDefault(m => m.IsStatic && !m.HasGenericParameters && match(m));
            if (method == null)
            {
                if (missingMessage != null)
                    diagnostics.Add(Warn(missingMessage));
                return null;
            }

            return module.ImportReference(method);
        }

        /// <summary>
        /// Resolve MethodReloadRegistry.TryInvokeMethodReload(string, object, object[]) from the runtime
        /// assembly and import it into the target module.
        /// </summary>
        private static MethodReference ResolveDispatchMethod(
            ModuleDefinition module, PostProcessorAssemblyResolver resolver, List<DiagnosticMessage> diagnostics)
        {
            return ResolveRegistryMethod(module, resolver, diagnostics,
                m => m.Name == DispatchMethodName &&
                     m.Parameters.Count == 3 &&
                     m.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
                     m.Parameters[1].ParameterType.MetadataType == MetadataType.Object,
                $"Could not find non-generic {DispatchMethodName}(string, object, object[]) on {RegistryTypeFullName}.");
        }

        /// <summary>
        /// Resolve MethodReloadRegistry.TryInvokeMethodReloadResult(string, object, object[], out object)
        /// -> bool for value-returning methods. Returns null (not an error) when the runtime predates
        /// the overload — callers then skip value-returning methods with a diagnostic.
        /// </summary>
        private static MethodReference ResolveResultDispatchMethod(
            ModuleDefinition module, PostProcessorAssemblyResolver resolver, List<DiagnosticMessage> diagnostics)
        {
            return ResolveRegistryMethod(module, resolver, diagnostics,
                m => m.Name == ResultDispatchMethodName &&
                     m.Parameters.Count == 4 &&
                     m.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
                     m.Parameters[1].ParameterType.MetadataType == MetadataType.Object &&
                     m.Parameters[3].ParameterType.IsByReference,
                missingMessage: null);
        }

        /// <summary>
        /// Resolve MethodReloadRegistry.ThrowOverrideReturnedNull(string) — the woven null guard for
        /// value-type returns. Resolved from the runtime like the dispatch methods (importing
        /// exception types from the post-processor's own corlib would not resolve in a player).
        /// Returns null (not an error) when the runtime predates the helper — the unguarded IL is
        /// woven then, matching the old behavior.
        /// </summary>
        private static MethodReference ResolveThrowNullResultMethod(
            ModuleDefinition module, PostProcessorAssemblyResolver resolver, List<DiagnosticMessage> diagnostics)
        {
            return ResolveRegistryMethod(module, resolver, diagnostics,
                m => m.Name == ThrowNullResultMethodName &&
                     m.Parameters.Count == 1 &&
                     m.Parameters[0].ParameterType.MetadataType == MetadataType.String,
                missingMessage: null);
        }

        /// <summary>
        /// Resolve MethodReloadRegistry.HasOverride(string) -> bool from the runtime assembly and import
        /// it into the target module (used by the woven prologue's no-allocation guard).
        /// </summary>
        private static MethodReference ResolveHasOverrideMethod(
            ModuleDefinition module, PostProcessorAssemblyResolver resolver, List<DiagnosticMessage> diagnostics)
        {
            return ResolveRegistryMethod(module, resolver, diagnostics,
                m => m.Name == HasOverrideMethodName &&
                     m.Parameters.Count == 1 &&
                     m.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
                     m.ReturnType.MetadataType == MetadataType.Boolean,
                $"Could not find {HasOverrideMethodName}(string) on {RegistryTypeFullName}.");
        }

        private static AssemblyDefinition ReadAssembly(ICompiledAssembly compiledAssembly, IAssemblyResolver resolver)
        {
            var pdb = compiledAssembly.InMemoryAssembly.PdbData;
            var hasSymbols = pdb != null && pdb.Length > 0;

            var readerParameters = new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadingMode = ReadingMode.Immediate,
                ReadWrite = true,
                ReadSymbols = hasSymbols,
                SymbolReaderProvider = hasSymbols ? new PortablePdbReaderProvider() : null,
                SymbolStream = hasSymbols ? new MemoryStream(pdb) : null,
            };

            var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData);
            return AssemblyDefinition.ReadAssembly(peStream, readerParameters);
        }

        private static InMemoryAssembly WriteAssembly(AssemblyDefinition assembly)
        {
            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            var writerParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(),
                WriteSymbols = true,
                SymbolStream = pdb,
            };

            assembly.Write(pe, writerParameters);
            return new InMemoryAssembly(pe.ToArray(), pdb.ToArray());
        }

        private static DiagnosticMessage Warn(string message) => new DiagnosticMessage
        {
            DiagnosticType = DiagnosticType.Warning,
            MessageData = "MethodReloadInPlace: " + message,
        };
    }

    /// <summary>
    /// Minimal IAssemblyResolver that resolves referenced assemblies from the compiled assembly's
    /// reference paths (which are absolute file paths supplied by the compilation pipeline).
    /// </summary>
    internal sealed class PostProcessorAssemblyResolver : IAssemblyResolver
    {
        private readonly string[] _referencePaths;
        private readonly Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>();

        public PostProcessorAssemblyResolver(string[] referencePaths)
        {
            _referencePaths = referencePaths;
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
            => Resolve(name, new ReaderParameters(ReadingMode.Deferred));

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(name.Name, out var cached))
                    return cached;

                var path = _referencePaths.FirstOrDefault(
                    r => Path.GetFileNameWithoutExtension(r) == name.Name);
                if (path == null || !File.Exists(path))
                    return null;

                parameters.AssemblyResolver = this;
                var definition = AssemblyDefinition.ReadAssembly(path, parameters);
                _cache[name.Name] = definition;
                return definition;
            }
        }

        public void Dispose()
        {
            lock (_cache)
            {
                foreach (var def in _cache.Values)
                    def.Dispose();
                _cache.Clear();
            }
        }
    }
}
