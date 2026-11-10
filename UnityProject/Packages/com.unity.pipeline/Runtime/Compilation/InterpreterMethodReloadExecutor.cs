using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Pipeline.MethodReload;
using IlInterpreter.Interpreter;

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// IL2CPP-safe in-place method reload backend: instead of <c>Assembly.Load</c> + reflection, the
    /// transformed override assembly bytes run in the IlInterpreter interpreter, and each override is
    /// registered as an interpreter dispatch in <see cref="MethodReloadRegistry"/>.
    ///
    /// Override methods are <c>static {ret} {Method}({TargetType} instance, ...)</c>, dispatched as
    /// <c>interpreter.Invoke("{Method}", instance, ...args)</c>. The host binding is the shared
    /// standard surface plus the target type — public and non-public members (the override is
    /// compiled with access checks disabled). Other referenced types bind on demand during Load via
    /// the binding's <c>AutoBindResolver</c>.
    ///
    /// Inherits the interpreter's limits: constructs IlInterpreter doesn't lower yet throw at dispatch
    /// time, and <see cref="MethodReloadRegistry.TryInvokeMethodReload"/> falls back to the original
    /// compiled body for that call. Member refs unbound at Load are reported through the
    /// <c>warnings</c> out param so the reload response shows the gap before play-mode execution hits it.
    /// </summary>
    static class InterpreterMethodReloadExecutor
    {
// Editor and any development player (incl. IL2CPP on device): the interpreter runs pushed IL
// bytes without Assembly.Load or Roslyn.
#if UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE

        /// <summary>
        /// Load <paramref name="assemblyBytes"/> into an interpreter and register an interpreter-backed
        /// override for each <paramref name="methodNames"/> entry on <paramref name="typeName"/>.
        /// A name with no woven dispatch target (the method — or its whole type — was added since
        /// the running build was compiled) cannot be dispatched from compiled code and is reported
        /// through <paramref name="skipped"/>; it still loads into the interpreter, so other
        /// reloaded bodies in the same file can call it.
        /// Returns the method ids that bound; <paramref name="skipped"/> carries
        /// reasons for any that didn't; <paramref name="warnings"/> carries non-fatal load-time
        /// diagnostics (host members that resolved to throwing stubs — the reload still applies, and
        /// a stub only throws if its call site is reached).
        /// </summary>
        public static List<string> Register(
            byte[] assemblyBytes, string typeName, IEnumerable<string> methodNames,
            out List<string> skipped, out List<string> warnings)
        {
            var registered = new List<string>();
            skipped = new List<string>();
            warnings = new List<string>();

            var names = new List<string>(methodNames ?? Array.Empty<string>());
            if (assemblyBytes == null || names.Count == 0)
                return registered;

            // Make sure the project's reload targets are registered. In a Player, and on Play Mode
            // entry in the Editor, that already happened; applying a reload from Edit Mode reaches
            // neither of those paths, and would otherwise resolve nothing.
            RuntimePipelineDriver.EnsureDiscoveredMethodReloadMethods();

            // All methods in one in-place file share the target type, but not every NAME resolves:
            // a [MethodReload] method added since the last compile has no woven prologue to dispatch
            // from, so its id is not in the registry. Resolve the type from the first name that is;
            // the rest load but only intra-file calls reach them (reported below). A file whose type
            // was never compiled resolves nothing — targetType stays null and the binding runs
            // without the target-type overlay.
            Type targetType = null;
            var resolvable = new List<string>();
            var unwovenNames = new List<string>();
            foreach (var m in names)
            {
                if (MethodReloadRegistry.TryGetReloadableDeclaringType($"{typeName}.{m}", out var t))
                {
                    targetType = targetType ?? t;
                    resolvable.Add(m);
                }
                else
                {
                    unwovenNames.Add(m);
                }
            }

            IlInterpreter.Interpreter.ScriptInterpreter interpreter;
            try
            {
                // Standard shared surface plus the target type so the rewritten body can touch its
                // own members, public and non-public. Every other type the override references is
                // bound on demand by the Load pass via the binding's AutoBindResolver.
                var binding = IlInterpreterHostBindings.CreateStandard();
                if (targetType != null)
                    binding = binding.AllowType(targetType)
                        .AllowNonPublicInstanceMembers(targetType);

                interpreter = new IlInterpreter.Interpreter.ScriptInterpreter(
                    binding, msg => Debug.Log(msg));
                // Lenient: a method that can't be lowered becomes a skipped stub (reported per method
                // in the registration loop below) instead of failing the whole file's reload — so one
                // bad method (e.g. SetupUI referencing a member missing on the running build) doesn't
                // drop the other reloaded methods (UIUpdate) in the same file.
                interpreter.Load(new OverrideScript(assemblyBytes), lenient: true);
            }
            catch (Exception ex)
            {
                // Lenient load no longer throws for per-method lowering failures, so this is a
                // whole-assembly failure (parse error, cctor, a genuine crash). Anything but a
                // ScriptValidationException carries an unactionable message ("Index was outside the
                // bounds of the array") — name the type and put the full stack in the log.
                var detail = ex is IlInterpreter.ScriptValidationException
                    ? ex.Message
                    : $"{ex.GetType().Name}: {ex.Message} (full stack in the editor log)";
                Debug.LogError($"MethodReload: interpreter load failed for '{typeName}': {ex}");
                foreach (var m in names)
                    skipped.Add($"{m}: interpreter load failed: {detail}");
                return registered;
            }

            // These member refs resolved to throwing stubs. The reload still applies (a stub only
            // throws if reached), but report the gap in the reload response rather than as a mid-play
            // ScriptRuntimeException on whichever frame first hits it.
            if (interpreter.UnboundHostMembers.Count > 0)
                warnings.Add(
                    $"warning: {interpreter.UnboundHostMembers.Count} host member(s) referenced by the " +
                    $"reloaded code are not in the binding and will throw if reached: " +
                    $"{string.Join(", ", interpreter.UnboundHostMembers)}. Types outside the standard " +
                    "surface and demand-time auto-bind are not callable from interpreted code; " +
                    "BCL types beyond the shims are intentionally not auto-bound.");

            foreach (var methodName in resolvable)
            {
                var targetMethodId = $"{typeName}.{methodName}";
                var name = methodName; // capture per iteration

                // Skipped under lenient load — its body couldn't be lowered. Report the reason and
                // move on rather than register a dispatcher that throws on first call. The most
                // common cause across the editor→player boundary is a reference the running build
                // lacks, so make that actionable.
                if (interpreter.TryGetLoweringSkip(methodName, out var lowerSkip))
                {
                    if (lowerSkip.Contains("did not resolve to a host or script method"))
                        lowerSkip += " — the referenced member is neither an interpreted " +
                            "(reloaded/[MethodReload]) method nor a bound host member. If it was added " +
                            "since the running build was made, rebuild/redeploy the player to sync, " +
                            "or mark it [MethodReload].";
                    skipped.Add($"{methodName}: interpreter load skipped this method: {lowerSkip}");
                    continue;
                }

                object Dispatch(object instance, object[] parameters)
                {
                    // Override signature: static M(TargetType instance, ...originalArgs). Use typed
                    // Invoke overloads for the common arities to avoid a per-call object[] (args are
                    // already boxed by the woven prologue). Returns the interpreter's (boxed) result,
                    // which the woven prologue of a value-returning method unboxes; void methods
                    // return null, which that prologue ignores.
                    int n = parameters?.Length ?? 0;
                    switch (n)
                    {
                        case 0: return interpreter.Invoke<object>(name, instance);
                        case 1: return interpreter.Invoke<object, object>(name, instance, parameters[0]);
                        case 2: return interpreter.Invoke<object, object, object>(name, instance, parameters[0], parameters[1]);
                        default:
                            var args = new object[1 + n];
                            args[0] = instance;
                            Array.Copy(parameters, 0, args, 1, n);
                            return interpreter.Invoke(name, args);
                    }
                }

                if (MethodReloadRegistry.RegisterInterpreterMethodOverride(targetMethodId, Dispatch, out var skipReason))
                    registered.Add(targetMethodId);
                else
                    skipped.Add($"{methodName} -> {targetMethodId}: {skipReason}");
            }

            // A method with no woven target has no compiled caller to dispatch from. It is still
            // part of the loaded assembly, so other reloaded bodies in this file call it fine —
            // report it so the reload response says why it isn't independently dispatchable.
            //
            // Which cause applies matters, and it is per-reload rather than per-method: an empty
            // registry says nothing in the *running build* is reloadable, which needs different
            // wording from a single stale method. Resolve it once.
            var unwovenReason = DescribeMissingDispatchTarget(typeName, resolvable.Count > 0);
            foreach (var methodName in unwovenNames)
            {
                if (interpreter.TryGetLoweringSkip(methodName, out var lowerSkip))
                {
                    skipped.Add($"{methodName}: interpreter load skipped this method: {lowerSkip}");
                    continue;
                }
                skipped.Add($"{methodName}: {unwovenReason}");
            }

            return registered;
        }

        /// <summary>
        /// Explain why a <c>[MethodReload]</c> method has no dispatch target, distinguishing the causes
        /// — they need different emphasis, though a compile is the first thing to try in all three:
        /// <list type="bullet">
        /// <item>the registry is empty: a scan ran and matched nothing, so the running build carries
        /// no recognised <c>[MethodReload]</c> method at all — either it predates the attribute (the
        /// first target in a project reaches exactly this state) or the attribute is not the one
        /// this package recognises;</item>
        /// <item>the registry has other types but not this one: the type (or its <c>[MethodReload]</c>
        /// attribute) postdates the running build;</item>
        /// <item>the type is known but this method isn't: the method postdates the running build.</item>
        /// </list>
        /// Says "discovered", never "exists": the registry reflects what was scanned in the running
        /// build, and a method absent from it may be present in the source perfectly well.
        /// </summary>
        /// <param name="typeName">Short name of the reloaded type.</param>
        /// <param name="typeIsReloadable">Whether another method on this type did resolve.</param>
        /// <remarks>Internal rather than private so the branch selection is unit-testable without
        /// compiling and loading an override assembly for each case.</remarks>
        internal static string DescribeMissingDispatchTarget(string typeName, bool typeIsReloadable)
        {
            // Reached after the reload path has already run discovery, so this is not "nothing has
            // scanned yet" — the scan happened and found nothing to register. That is not by itself
            // evidence of a misapplied attribute: the methods that get here were picked out of the
            // source by their [MethodReload] attribute, so the project has one, and adding the very
            // first target in a project lands in this branch until the build catches up (a running
            // watch suppresses auto-refresh, so it stays there across saves). Lead with the compile
            // and keep the stickier causes behind it.
            if (!MethodReloadRegistry.HasReloadableMethods)
                return "no reload target was discovered in any loaded assembly — the running build " +
                    "carries no [MethodReload] method at all, though the source just reloaded declares " +
                    "one. Usually it simply predates the attribute: recompile (with a method reload " +
                    "watch running, auto-refresh is off, so saving alone will not), or rebuild and " +
                    "redeploy the player, then reload again. If a fresh build still discovers " +
                    "nothing, the running code is not merely stale — check that the attribute is " +
                    "Unity.Pipeline.MethodReload.MethodReloadAttribute (an unrelated attribute of the " +
                    "same short name is ignored by design), and — in a Player — that its assembly " +
                    "survived managed stripping.";

            if (!typeIsReloadable && !MethodReloadRegistry.HasReloadableMethodsForType(typeName))
                return $"no woven dispatch target — no [MethodReload] method was discovered on " +
                    $"'{typeName}', so the type (or its [MethodReload] attribute) was added after the " +
                    "running build was compiled. Recompile, or rebuild/redeploy the player, to sync.";

            return "no woven dispatch target — the method was added after the running build was " +
                "compiled. Other reloaded [MethodReload] bodies in this file can call it; dispatching " +
                "it on its own needs a recompile (or a player rebuild) first.";
        }

        /// <summary>
        /// Push-time host-surface check: load the compiled override bytes into a throwaway
        /// interpreter against the same binding a player builds on receipt, and return the member
        /// refs that resolved to throwing stubs. Running the device's load pass in the editor turns
        /// a mid-play ScriptRuntimeException into a synchronous diagnostic in the
        /// reload_file_player_interpreter response (the device ack is async and carries only a warning
        /// count).
        ///
        /// The editor's surface is a superset of a device player's (no IL2CPP stripping, and
        /// editor-only assemblies resolve), so a clean result here is no guarantee — the player's
        /// own load pass stays the backstop. The converse holds strictly: every member reported
        /// here is a throwing stub on the player too.
        /// </summary>
        /// <param name="note">Non-fatal context when the check was degraded: the target type
        /// could not be resolved (its non-public members may be reported as unbound), or the
        /// interpreter load itself threw (the player's load will likely fail the same way).</param>
        public static List<string> ValidateBindingSurface(
            byte[] assemblyBytes, string typeName, IReadOnlyList<string> methodNames, out string note)
        {
            note = null;
            var unbound = new List<string>();
            if (assemblyBytes == null || assemblyBytes.Length == 0)
                return unbound;

            try
            {
                var binding = IlInterpreterHostBindings.CreateStandard();
                var targetType = ResolveTargetType(typeName, methodNames);
                if (targetType != null)
                    binding.AllowType(targetType).AllowNonPublicInstanceMembers(targetType);
                else
                    note = $"target type '{typeName}' not resolved in the editor — its own " +
                        "non-public members may show up as unbound";

                using var interpreter = new IlInterpreter.Interpreter.ScriptInterpreter(binding, _ => { });
                // Lenient like the player's Register: a method that can't lower is skipped, not
                // thrown, so the pre-check still reports the unbound members of the methods that DID
                // lower instead of failing the whole validation on the first bad method.
                interpreter.Load(new OverrideScript(assemblyBytes), lenient: true);
                unbound.AddRange(interpreter.UnboundHostMembers);
            }
            catch (Exception ex)
            {
                note = $"interpreter load failed during validation: {ex.Message} — the player's " +
                    "load pass will likely fail the same way";
            }
            return unbound;
        }

        /// <summary>
        /// Resolve the override's target type the way <see cref="Register"/> does (the method-reload
        /// registry, only populated in play mode), falling back to a scan of the loaded user
        /// assemblies for a unique short-name match (override extraction records short names).
        /// Returns null when unknown or ambiguous; validation then runs without the target-type overlay.
        /// </summary>
        static Type ResolveTargetType(string typeName, IReadOnlyList<string> methodNames)
        {
            if (methodNames != null && methodNames.Count > 0 &&
                MethodReloadRegistry.TryGetReloadableDeclaringType($"{typeName}.{methodNames[0]}", out var fromRegistry))
                return fromRegistry;

            // Skip engine/BCL assemblies ([MethodReload] target types live in user code); mirrors
            // IlInterpreterHostBindings.IsAutoBindSkippedAssembly's classification intent.
            Type unique = null;
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                var name = asm.GetName().Name ?? string.Empty;
                if (name.StartsWith("Unity", StringComparison.Ordinal) ||
                    name.StartsWith("System", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                    name.StartsWith("Mono.", StringComparison.Ordinal) ||
                    name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                    name.StartsWith("netstandard", StringComparison.Ordinal) ||
                    name.StartsWith("nunit.", StringComparison.Ordinal))
                    continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.Name != typeName) continue;
                    if (unique != null && unique != t) return null; // ambiguous — skip the overlay
                    unique = t;
                }
            }
            return unique;
        }

        /// <summary>Minimal <see cref="IlInterpreter.IScript"/> over the Roslyn-emitted override PE bytes.</summary>
        private sealed class OverrideScript : IlInterpreter.IScript
        {
            public OverrideScript(byte[] bytes) { Il = bytes; }
            public string Name => "PipelineMethodReload";
            public ReadOnlyMemory<byte> Il { get; }
        }

#else
        public static List<string> Register(
            byte[] assemblyBytes, string typeName, IEnumerable<string> methodNames,
            out List<string> skipped, out List<string> warnings)
        {
            skipped = new List<string> { "Interpreter method reload only supported on Desktop development builds" };
            warnings = new List<string>();
            return new List<string>();
        }

        public static List<string> ValidateBindingSurface(
            byte[] assemblyBytes, string typeName, IReadOnlyList<string> methodNames, out string note)
        {
            note = "Binding validation only supported in the editor and Desktop development builds";
            return new List<string>();
        }
#endif
    }
}
