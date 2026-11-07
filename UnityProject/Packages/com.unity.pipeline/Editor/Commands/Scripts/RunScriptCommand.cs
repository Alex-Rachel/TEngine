using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using Unity.Pipeline.Runtime.Commands;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Commands.Scripts
{
    /// <summary>
    /// The <c>run_script</c> command (AUTHAPI-26): compile a single project <c>.cs</c> file in memory
    /// via the shared <see cref="RoslynCompilationService"/> and execute a NAMED static entry point —
    /// with NO domain reload and NO code carried through the protocol. This formalizes the
    /// "builder pattern": bulk construction lives in a versioned project script that is written to
    /// disk (ideally OUTSIDE <c>Assets/</c>, so writing it triggers no asset import / domain reload)
    /// and invoked here by name, instead of shipping escaped C# strings through <c>eval</c>.
    ///
    /// It composes the two halves the pipeline already had — <c>eval_file</c> (code from disk) and
    /// <c>reload_file</c> (single-file in-memory Roslyn compile) — into one first-class command:
    ///   - <c>ephemeral</c> (default): compile to an in-memory assembly, run the entry point, discard.
    ///   - <c>hotpatch</c>: delegate to <c>reload_file</c>'s in-place <c>[MethodReload]</c> semantics.
    ///
    /// This is still arbitrary code execution — only the carrier changes (a reviewable file on disk
    /// instead of a protocol string). It is grouped under the same <c>scripts/eval</c> tag as
    /// <c>eval</c> so a host that disables the eval family disables this too.
    /// </summary>
    static class RunScriptCommand
    {
        const string Group = "script";

        [CliGroupCommand(Group,
            "Compile a project C# file in memory (no domain reload) and execute a named static entry point. " +
            "The builder-pattern path for bulk construction. Still arbitrary code execution — shares " + CommandNames.ScriptEval + "'s capability gate.",
            MainThreadRequired = true,
            Tags = new[] { "scripts/eval" })]
        public static async Task<RunScriptResponse> Run(
            [CliArg("file", "Path to a .cs file to compile. Relative paths resolve against the PROJECT ROOT (the parent of Assets/), not the process working directory. May live OUTSIDE Assets/ (e.g. AgentScripts/) so writing it never triggers an asset import or domain reload.", Required = true)] string file,
            [CliArg("entry", "Entry point as Namespace.Type.Method (or Type.Method, or a bare Method name). Default: the single public static method if unambiguous, else a method named Main. Static methods only in v1. Ephemeral mode only (rejected with mode=hotpatch, which never invokes an entry point).")] string entry = null,
            [CliArg("args", "JSON array of arguments passed to the entry point, coerced to its parameter types: primitives, enum names, string[], and a string/object handle (ObjectRef) for UnityEngine.Object parameters. Ephemeral mode only (rejected with mode=hotpatch).")] JArray args = null,
            [CliArg("mode", "ephemeral (default): compile to an in-memory assembly, run the entry, discard. hotpatch: apply [MethodReload] method replacements to already-loaded types (" + CommandNames.ScriptMethodreloadReload + " semantics).")] string mode = "ephemeral",
            [CliArg("references", "Extra assembly name prefixes to reference beyond the auto-resolved set. In the Editor all loaded assemblies are already referenced, so this is effectively a no-op there. Ephemeral mode only; not applied by hotpatch (which delegates to " + CommandNames.ScriptMethodreloadReload + "'s own compile).")] string[] references = null,
            [CliArg("defines", "Extra scripting define symbols APPENDED to the project's active editor defines (UNITY_EDITOR etc. are already set) for the compilation's #if directives. Ephemeral mode only; not applied by hotpatch.")] string[] defines = null,
            [CliArg("pdb", "Emit a portable PDB mapped to the source file so breakpoints bind and exception stack traces map to file:line. Compiles unoptimized.")] bool pdb = false,
            [CliArg("timeout", "Timeout in seconds. Bounds the whole run, including how long an async entry point's Task is awaited (on expiry the task keeps running detached).")] double timeoutInSeconds = 60,
            [CliArg("dry-run", "Compile only: return diagnostics; the emitted assembly is NOT loaded into the domain and nothing executes. Ephemeral mode only (rejected with mode=hotpatch, which would apply live method replacements).")] bool dryRun = false)
        {
            var total = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(file))
                return RunScriptResponse.Fail("Bad Request", "The 'file' parameter is required and cannot be empty.", total.ElapsedMilliseconds);

            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return RunScriptResponse.Fail("Bad Request", "The 'file' must have a .cs extension.", total.ElapsedMilliseconds);

            var timeout = PipelineTimeout.FromSeconds(timeoutInSeconds);
            var normalizedMode = (mode ?? "ephemeral").Trim().ToLowerInvariant();

            try
            {
                switch (normalizedMode)
                {
                    case "ephemeral":
                        return await RunEphemeral(file, entry, args, references, defines, pdb, dryRun, timeout, total);
                    case "hotpatch":
                    {
                        // hotpatch delegates wholesale to reload_file's [MethodReload] semantics:
                        // nothing is invoked, so entry/args are meaningless, and there is no
                        // compile-only path — a "dry run" would still apply live method
                        // replacements. Reject rather than silently ignore or, worse, apply.
                        if (dryRun)
                        {
                            return RunScriptResponse.Fail("Bad Request",
                                "'dry-run' is not supported with mode=hotpatch; it would apply live method replacements. " +
                                "Use mode=ephemeral (the default) for a compile-only check.",
                                total.ElapsedMilliseconds);
                        }

                        var unsupported = new List<string>();
                        if (!string.IsNullOrWhiteSpace(entry)) unsupported.Add("'entry'");
                        if (args != null && args.Count > 0) unsupported.Add("'args'");
                        if (unsupported.Count > 0)
                        {
                            return RunScriptResponse.Fail("Bad Request",
                                $"{string.Join(" and ", unsupported)} {(unsupported.Count == 1 ? "is" : "are")} not supported with mode=hotpatch: " +
                                "it applies [MethodReload] method replacements in place and never invokes an entry point. " +
                                "Use mode=ephemeral to run an entry point.",
                                total.ElapsedMilliseconds);
                        }

                        return RunHotpatch(file, pdb, timeout, total);
                    }
                    default:
                        return RunScriptResponse.Fail("Bad Request", $"Unknown mode '{mode}'. Expected 'ephemeral' or 'hotpatch'.", total.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: {CommandNames.ScriptRun} failed: {ex}");
                return RunScriptResponse.Fail("Execution Failed", ex.ToString(), total.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// ephemeral mode: single-file in-memory compile → resolve the entry point → coerce args →
        /// invoke → serialize. No <c>AssetDatabase.Refresh</c> and no domain reload occur. Async so
        /// a Task-returning entry point can be awaited without blocking the main thread (see the
        /// comment at the await); <paramref name="timeout"/> bounds that wait.
        /// </summary>
        private static async Task<RunScriptResponse> RunEphemeral(
            string file, string entry, JArray args, string[] references, string[] defines, bool pdb, bool dryRun, TimeSpan timeout, Stopwatch total)
        {
            // Resolve the path: absolute as-is, else relative to the PROJECT ROOT (the parent of
            // Assets/) — NOT the process working directory, which is usually but not guaranteed to
            // be the project root. This deliberately accepts files outside Assets/. (hotpatch mode
            // intentionally keeps reload_file's own resolution, which probes under Assets/.)
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            var fullPath = ProjectPaths.ResolveFile(file, projectRoot);
            if (!File.Exists(fullPath))
                return RunScriptResponse.Fail("File Not Found", $"Source file not found: {fullPath}", total.ElapsedMilliseconds);

            string source;
            try
            {
                source = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                return RunScriptResponse.Fail("Bad Request", $"Failed to read file: {ex.Message}", total.ElapsedMilliseconds);
            }

            if (string.IsNullOrWhiteSpace(source))
                return RunScriptResponse.Fail("Bad Request", $"File is empty: {fullPath}", total.ElapsedMilliseconds);

            var assemblyName = $"PipelineRunScript_{Path.GetFileNameWithoutExtension(fullPath)}_{Guid.NewGuid():N}";

            // Emit symbols mapped to the source file whenever we are going to execute, so that
            // exception stack traces (and, with an attached debugger, breakpoints) map back to
            // file:line in the original .cs. dry-run only needs symbols when explicitly requested.
            var emitDebug = pdb || !dryRun;

            // Seed the compile with the project's ACTIVE editor defines (UNITY_EDITOR, the
            // UNITY_<version> symbols, platform defines, project scripting symbols, …) so the
            // source's #if directives behave like project code. The user's `defines` extend that
            // set; they do not replace it.
            var activeDefines = EditorUserBuildSettings.activeScriptCompilationDefines;
            var effectiveDefines = defines == null || defines.Length == 0
                ? activeDefines
                : activeDefines.Concat(defines).Distinct().ToArray();

            var details = CommandContext.Current?.EvalDetails;
            var compileSw = Stopwatch.StartNew();
            var compilation = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = source,
                AssemblyName = assemblyName,
                EmitDebugInformation = emitDebug,
                DocumentPath = fullPath,
                AdditionalAssemblyPrefixes = references,
                PreprocessorSymbols = effectiveDefines,
                // dry-run never executes, so don't load the emitted assembly into the domain —
                // in-memory assemblies are not unloadable under Mono and would leak per call.
                SkipLoad = dryRun
            });
            compileSw.Stop();
            details?.RecordCompilation(compilation);

            if (!compilation.Success)
            {
                return RunScriptResponse.Fail(
                    "Compilation Failed",
                    EvalCodeCompiler.SummarizeErrors(compilation.Diagnostics),
                    total.ElapsedMilliseconds,
                    compilation.Diagnostics,
                    compileSw.ElapsedMilliseconds,
                    0,
                    assemblyName);
            }

            if (dryRun)
            {
                // Emit-only: no assembly was loaded, so there is no assemblyName to report.
                var dr = RunScriptResponse.Ok(null, null, compileSw.ElapsedMilliseconds, 0, total.ElapsedMilliseconds, compilation.Diagnostics);
                dr.Message = "Compiled successfully (dry run; nothing was loaded or executed).";
                return dr;
            }

            // Resolve the entry point.
            var method = ResolveEntryPoint(compilation.Assembly, entry, Path.GetFileName(fullPath), out var entryError);
            if (method == null)
            {
                return RunScriptResponse.Fail("Entry Point Not Found", entryError, total.ElapsedMilliseconds,
                    compilation.Diagnostics, compileSw.ElapsedMilliseconds, 0, assemblyName);
            }

            // Coerce JSON args to the entry point's parameter types.
            if (!TryCoerceArguments(method, args, out var callArgs, out var argError))
            {
                return RunScriptResponse.Fail("Bad Request", argError, total.ElapsedMilliseconds,
                    compilation.Diagnostics, compileSw.ElapsedMilliseconds, 0, assemblyName);
            }

            // Execute.
            var execSw = Stopwatch.StartNew();
            object returnValue = null;
            try
            {
                returnValue = method.Invoke(null, callArgs);

                // A Task/Task<T>-returning entry point must be awaited — otherwise the call
                // reports success the moment the first await yields, and any exception in the
                // async work is never observed. Awaited ASYNCHRONOUSLY (never a blocking
                // GetResult()): this command runs on the main thread, and an entry point's awaits
                // resume on the Unity SynchronizationContext — i.e. this same thread — so a
                // blocking wait here deadlocks the whole Editor permanently. Awaiting instead
                // returns control to the editor loop at this point (the server unwraps the
                // command's own Task off the main thread), letting those continuations pump.
                // The wait is bounded by what remains of the timeout; on expiry the task keeps
                // running detached, like an abandoned eval. Task<T> exposes Result via reflection
                // (an `async Task` materializes as Task<VoidTaskResult>, whose Result carries no
                // value — treat it as null).
                if (returnValue is Task task)
                {
                    var stopAt = PipelineTimeout.StopBefore(timeout);
                    var remaining = PipelineTimeout.IsInfinite(stopAt)
                        ? stopAt
                        : (stopAt > total.Elapsed ? stopAt - total.Elapsed : TimeSpan.Zero);
                    var completed = await Task.WhenAny(task, Task.Delay(remaining));
                    if (completed != task)
                    {
                        execSw.Stop();
                        return RunScriptResponse.Fail(
                            "Timeout",
                            $"The entry point's Task did not complete within the {PipelineTimeout.Describe(timeout)} timeout " +
                            $"({PipelineTimeout.Describe(remaining)} remained after the synchronous part). The task keeps running detached and " +
                            "its effects may still land; raise --timeout if it legitimately needs longer.",
                            total.ElapsedMilliseconds,
                            compilation.Diagnostics,
                            compileSw.ElapsedMilliseconds,
                            execSw.ElapsedMilliseconds,
                            assemblyName);
                    }

                    await task; // a faulted task throws the entry point's own exception here
                    var resultProperty = task.GetType().GetProperty("Result");
                    returnValue = resultProperty != null && resultProperty.PropertyType.FullName != "System.Threading.Tasks.VoidTaskResult"
                        ? resultProperty.GetValue(task)
                        : null;
                }
            }
            catch (Exception ex) when (ex is TargetInvocationException || returnValue is Task)
            {
                execSw.Stop();
                // Reflection wraps a synchronous throw in a TargetInvocationException; a faulted
                // task rethrows the entry point's own exception from the await. Normalize both
                // to the entry point's exception. With the PDB emitted above and DocumentPath set
                // to the source file, the stack trace carries "... in <file>:line <n>", so the
                // failure maps back to the original source.
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                details?.RecordException(inner, compilation.Assembly);
                return RunScriptResponse.Fail(
                    "Runtime Error",
                    $"{inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}",
                    total.ElapsedMilliseconds,
                    compilation.Diagnostics,
                    compileSw.ElapsedMilliseconds,
                    execSw.ElapsedMilliseconds,
                    assemblyName);
            }
            execSw.Stop();

            return RunScriptResponse.Ok(
                SerializeResult(returnValue),
                assemblyName,
                compileSw.ElapsedMilliseconds,
                execSw.ElapsedMilliseconds,
                total.ElapsedMilliseconds,
                compilation.Diagnostics);
        }

        /// <summary>
        /// hotpatch mode: apply in-place <c>[MethodReload]</c> method replacements to already-loaded
        /// types, delegating to the existing <c>reload_file</c> command so its behavior/tests are
        /// preserved. The result is re-shaped into a <see cref="RunScriptResponse"/>.
        /// </summary>
        private static RunScriptResponse RunHotpatch(string file, bool pdb, TimeSpan timeout, Stopwatch total)
        {
            var hr = MethodReloadCommands.Reload(file, PipelineTimeout.IsInfinite(timeout) ? double.PositiveInfinity : timeout.TotalSeconds, null, pdb);

            // reload_file reports a single duration for the whole reload (compile + apply); surface
            // it as executeMs rather than mislabeling it compile time. compileMs stays 0 here.
            var reloadMs = hr.ExecutionTimeMs ?? 0L;

            if (hr.Success)
            {
                // reload_file can succeed with advisory diagnostics (e.g. partially-skipped
                // overrides) — keep them, as "warning" entries in the same shape the failure
                // path uses, instead of dropping them on the floor.
                var warnings = (hr.Diagnostics ?? new List<string>())
                    .Select(d => new DiagnosticInfo { Severity = "warning", Message = d })
                    .ToList();
                var ok = RunScriptResponse.Ok(hr.Items, hr.AssemblyName, 0, reloadMs, total.ElapsedMilliseconds, warnings);
                ok.Message = hr.Message;
                return ok;
            }

            var diagnostics = (hr.Diagnostics ?? new List<string>())
                .Select(d => new DiagnosticInfo { Severity = "error", Message = d })
                .ToList();
            return RunScriptResponse.Fail(hr.Error ?? "Method Reload Failed", hr.ErrorDetails, total.ElapsedMilliseconds,
                diagnostics, 0, reloadMs, hr.AssemblyName);
        }

        /// <summary>
        /// Resolve the static entry method from a compiled assembly.
        /// - "Namespace.Type.Method" / "Type.Method": split on the last dot; find the type, then the
        ///   static method by name (public or non-public). Nested types may be written with dots
        ///   ("Outer.Inner.Method") — the '+' reflection form is retried automatically.
        /// - bare "Method": the single static method with that name across all types.
        /// - null/empty: the single public static method if unambiguous, else a method named "Main".
        /// Generic methods are never candidates (no type arguments to close them with).
        /// Returns null and sets <paramref name="error"/> when it cannot resolve unambiguously.
        /// </summary>
        private static MethodInfo ResolveEntryPoint(Assembly assembly, string entry, string fileName, out string error)
        {
            error = null;

            // Skip compiler-generated types (e.g. <PrivateImplementationDetails>).
            var types = assembly.GetTypes().Where(t => !t.Name.Contains("<") && !t.Name.Contains(">")).ToArray();

            const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

            if (!string.IsNullOrWhiteSpace(entry))
            {
                entry = entry.Trim();
                var lastDot = entry.LastIndexOf('.');

                if (lastDot > 0)
                {
                    var typeName = entry.Substring(0, lastDot);
                    var methodName = entry.Substring(lastDot + 1);

                    var type = assembly.GetType(typeName)
                        ?? types.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
                    if (type == null && typeName.IndexOf('.') >= 0)
                    {
                        // Nested types use '+' in reflection names ("Outer+Inner"), while callers
                        // naturally write dots ("Outer.Inner.Method"). Retry with the last dot of
                        // the type part replaced.
                        var nestedDot = typeName.LastIndexOf('.');
                        var nestedName = typeName.Substring(0, nestedDot) + "+" + typeName.Substring(nestedDot + 1);
                        type = assembly.GetType(nestedName)
                            ?? types.FirstOrDefault(t => t.FullName == nestedName);
                    }
                    if (type == null)
                    {
                        error = $"No type '{typeName}' in compiled file '{fileName}'. Types: [{string.Join(", ", types.Select(t => t.FullName))}].";
                        return null;
                    }

                    var named = type
                        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .Where(m => m.Name == methodName && !m.IsSpecialName)
                        .ToList();
                    var candidates = named.Where(m => !m.ContainsGenericParameters).ToList();
                    if (candidates.Count == 1)
                        return candidates[0];
                    if (candidates.Count > 1)
                    {
                        error = $"Entry '{entry}' is overloaded ({candidates.Count} static overloads); overloaded entry points are not supported in v1.";
                        return null;
                    }
                    if (named.Count > 0)
                    {
                        error = $"Entry '{entry}' only resolves to generic method(s); generic entry points are not supported.";
                        return null;
                    }

                    var instance = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == methodName && !m.IsSpecialName);
                    error = instance != null
                        ? $"Entry '{entry}' is an instance method; only static methods are supported in v1."
                        : $"No static method '{methodName}' on type '{type.FullName}'.";
                    return null;
                }

                // Bare method name — search every type for a static method with that name.
                var byNameAll = types.SelectMany(t => t.GetMethods(AnyStatic))
                    .Where(m => m.Name == entry && !m.IsSpecialName)
                    .ToList();
                var byName = byNameAll.Where(m => !m.ContainsGenericParameters).ToList();
                if (byName.Count == 1)
                    return byName[0];
                if (byName.Count == 0)
                {
                    error = byNameAll.Count > 0
                        ? $"Static method '{entry}' in '{fileName}' is generic; generic entry points are not supported."
                        : $"No static method named '{entry}' found in '{fileName}'.";
                    return null;
                }

                error = $"Multiple static methods named '{entry}' found in '{fileName}'; qualify the entry as Namespace.Type.Method.";
                return null;
            }

            // No entry specified — auto-detect. Generic methods can't be invoked without type
            // arguments, so they are never entry-point candidates.
            var publicStatic = types.SelectMany(t => t.GetMethods(PublicStatic))
                .Where(m => !m.IsSpecialName && !m.ContainsGenericParameters)
                .ToList();

            if (publicStatic.Count == 1)
                return publicStatic[0];

            var mains = types.SelectMany(t => t.GetMethods(AnyStatic))
                .Where(m => m.Name == "Main" && !m.IsSpecialName && !m.ContainsGenericParameters)
                .ToList();
            if (mains.Count == 1)
                return mains[0];

            if (publicStatic.Count == 0)
            {
                error = $"No public static entry point found in '{fileName}'. Add a public static method or specify 'entry'.";
                return null;
            }

            error = $"Ambiguous entry point in '{fileName}': {publicStatic.Count} public static methods and no unique 'Main'. " +
                    $"Specify 'entry' as Namespace.Type.Method. Candidates: [{string.Join(", ", publicStatic.Select(m => (m.DeclaringType?.FullName ?? "?") + "." + m.Name))}].";
            return null;
        }

        /// <summary>
        /// Coerce the JSON <paramref name="args"/> array to the entry point's parameter types using
        /// the shared converters: primitives / enum names / string[] via Newtonsoft, and a
        /// string-or-object handle (<see cref="ObjectRef"/> + <see cref="ObjectResolver"/>) for
        /// UnityEngine.Object parameters. Omitted trailing args fall back to C# default values.
        /// An explicit JSON null is only valid for reference-type and Nullable&lt;T&gt; parameters —
        /// for a non-nullable value type it is a coercion error, never a silent default(T).
        /// </summary>
        private static bool TryCoerceArguments(MethodInfo method, JArray args, out object[] callArgs, out string error)
        {
            error = null;
            var parameters = method.GetParameters();
            callArgs = new object[parameters.Length];
            var provided = args?.Count ?? 0;

            if (provided > parameters.Length)
            {
                error = $"Entry point '{method.DeclaringType?.FullName}.{method.Name}' expects {parameters.Length} argument(s) but {provided} were provided.";
                return false;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var pt = p.ParameterType;

                if (i >= provided)
                {
                    if (p.HasDefaultValue)
                    {
                        callArgs[i] = p.DefaultValue;
                        continue;
                    }

                    error = $"Missing required argument #{i} for parameter '{p.Name}' ({pt.Name}).";
                    return false;
                }

                var token = args[i];

                if (typeof(Object).IsAssignableFrom(pt))
                {
                    if (token == null || token.Type == JTokenType.Null)
                    {
                        callArgs[i] = null;
                        continue;
                    }

                    ObjectRef handle;
                    try
                    {
                        handle = token.ToObject<ObjectRef>();
                    }
                    catch (Exception ex)
                    {
                        error = $"Argument #{i} ('{p.Name}'): not a valid object reference: {ex.Message}";
                        return false;
                    }

                    if (handle == null || handle.IsEmpty)
                    {
                        error = $"Argument #{i} ('{p.Name}'): empty object reference.";
                        return false;
                    }

                    if (!ObjectResolver.TryResolve(handle, out var resolved, out var resolveError))
                    {
                        error = $"Argument #{i} ('{p.Name}'): {resolveError}";
                        return false;
                    }

                    if (!pt.IsInstanceOfType(resolved))
                    {
                        // Convenience: a GameObject handle can satisfy a Component parameter (and vice versa).
                        Object coerced = null;
                        if (resolved is GameObject go && typeof(Component).IsAssignableFrom(pt))
                            coerced = go.GetComponent(pt);
                        else if (resolved is Component c && pt == typeof(GameObject))
                            coerced = c.gameObject;

                        if (coerced != null && pt.IsInstanceOfType(coerced))
                        {
                            resolved = coerced;
                        }
                        else
                        {
                            error = $"Argument #{i} ('{p.Name}') resolved to a {resolved.GetType().Name}, which is not assignable to {pt.Name}.";
                            return false;
                        }
                    }

                    callArgs[i] = resolved;
                    continue;
                }

                if (token == null || token.Type == JTokenType.Null)
                {
                    // null has no representation in a non-nullable value type — silently passing
                    // default(T) would hide the caller's mistake. Nullable<T> (and reference
                    // types) accept null as-is.
                    if (pt.IsValueType && Nullable.GetUnderlyingType(pt) == null)
                    {
                        error = $"Argument #{i} ('{p.Name}'): null cannot be converted to value type {pt.Name}; pass a value, or make the parameter nullable ({pt.Name}?).";
                        return false;
                    }

                    callArgs[i] = null;
                    continue;
                }

                try
                {
                    callArgs[i] = token.ToObject(pt);
                }
                catch (Exception ex)
                {
                    error = $"Argument #{i} ('{p.Name}'): cannot convert to {pt.Name}: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Serialize the entry point's return value for the JSON response. Mirrors
        /// <c>EvalCodeCompiler.SerializeResult</c> so run_script results look like eval results:
        /// primitives pass through, Unity objects go via JsonUtility, everything else via Newtonsoft,
        /// with a ToString() fallback.
        /// </summary>
        private static object SerializeResult(object value)
        {
            if (value == null) return null;

            if (value is string || value is bool || value is int || value is long || value is float || value is double)
                return value;

            if (value is Object unityObj)
            {
                try
                {
                    return JsonConvert.DeserializeObject(JsonUtility.ToJson(unityObj));
                }
                catch
                {
                    return value.ToString();
                }
            }

            try
            {
                return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value));
            }
            catch
            {
                return value.ToString();
            }
        }
    }

    /// <summary>
    /// Response for <c>run_script</c>. Result shape: <c>{ result, diagnostics, compileMs, executeMs,
    /// assemblyName }</c> on top of the standard command envelope. Deliberately carries no echo of the
    /// source (path + diagnostics only), so MCP results stay small and reviewable.
    /// </summary>
    [Serializable]
    class RunScriptResponse : CommandExecutionResponse
    {
        /// <summary>Compilation diagnostics (errors, warnings) from Roslyn, with line/column.</summary>
        [JsonProperty("diagnostics")]
        public List<DiagnosticInfo> Diagnostics { get; set; } = new List<DiagnosticInfo>();

        /// <summary>
        /// Milliseconds spent compiling the file to an in-memory assembly. 0 for hotpatch, whose
        /// compile is inside <c>reload_file</c> and not reported separately (see <see cref="ExecuteMs"/>).
        /// </summary>
        [JsonProperty("compileMs")]
        public long CompileMs { get; set; }

        /// <summary>
        /// Milliseconds spent executing the entry point (0 for dry-run). For hotpatch this is the
        /// whole reload duration — compile + apply — since <c>reload_file</c> does not split the two.
        /// </summary>
        [JsonProperty("executeMs")]
        public long ExecuteMs { get; set; }

        /// <summary>Name of the generated in-memory assembly.</summary>
        [JsonProperty("assemblyName")]
        public string AssemblyName { get; set; }

        public static RunScriptResponse Ok(object result, string assemblyName, long compileMs, long executeMs, long executionTimeMs, List<DiagnosticInfo> diagnostics)
        {
            return new RunScriptResponse
            {
                Success = true,
                Command = CommandNames.ScriptRun,
                Result = result,
                AssemblyName = assemblyName,
                CompileMs = compileMs,
                ExecuteMs = executeMs,
                ExecutionTimeMs = executionTimeMs,
                Diagnostics = diagnostics ?? new List<DiagnosticInfo>(),
                ExecutedAt = DateTime.UtcNow
            };
        }

        public static RunScriptResponse Fail(string error, string errorDetails, long executionTimeMs,
            List<DiagnosticInfo> diagnostics = null, long compileMs = 0, long executeMs = 0, string assemblyName = null)
        {
            return new RunScriptResponse
            {
                Success = false,
                Command = CommandNames.ScriptRun,
                Error = error,
                ErrorDetails = errorDetails,
                AssemblyName = assemblyName,
                CompileMs = compileMs,
                ExecuteMs = executeMs,
                ExecutionTimeMs = executionTimeMs,
                Diagnostics = diagnostics ?? new List<DiagnosticInfo>(),
                ExecutedAt = DateTime.UtcNow
            };
        }
    }
}
