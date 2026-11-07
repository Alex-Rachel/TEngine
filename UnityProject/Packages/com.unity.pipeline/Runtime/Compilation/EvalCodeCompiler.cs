using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
#endif
using Unity.Pipeline.Models;
using Unity.Pipeline.Telemetry;
using UnityEngine;

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// C# code evaluation service using shared Roslyn compilation infrastructure.
    /// Compiles a snippet as a C# script, runs it, awaits it, and serializes its result.
    /// Supports both Editor and Runtime compilation for desktop development builds.
    /// </summary>
    static class EvalCodeCompiler
    {
#if UNITY_EDITOR || (UNITY_STANDALONE && DEBUG)

        // The snippet follows a two-line preamble: the Object alias line, then `#line 1` so stack
        // traces count the snippet's own lines. Diagnostics report raw lines, so subtracting one
        // less than the preamble makes the snippet's first line `line: 1`.
        const string k_ObjectAlias = "using Object = UnityEngine.Object;";
        const int k_DiagnosticLineOffset = 1;
        const string k_DocumentPath = "eval.cs";

        // A newly imported namespace can bring new unresolved names to light; stop after this many.
        const int k_MaxAutoImportRounds = 3;

        static readonly CSharpParseOptions k_ScriptParseOptions = new CSharpParseOptions(kind: SourceCodeKind.Script);

        /// <summary>
        /// Compile and run <paramref name="code"/> on the calling thread, which must be Unity's main
        /// thread. Awaits in the snippet resume on Unity's synchronization context, so this awaits
        /// the script's task without blocking: the editor keeps pumping while it runs.
        /// </summary>
        /// <param name="code">The C# script to evaluate.</param>
        /// <param name="stopwatch">Running stopwatch used to report execution time in the response.</param>
        /// <param name="dryRun">Compile only: report diagnostics; nothing is loaded or run.</param>
        /// <param name="autoImport">On unresolved names, import the namespace declaring each one and compile again.</param>
        /// <param name="internals">Compile under <see cref="InternalsAccessName"/>, so Unity's internal APIs are callable. Editor only.</param>
        /// <param name="details">Receives what the eval records for analytics; null to record nothing.</param>
        /// <returns>The evaluation result.</returns>
        public static async Task<EvalResponse> CompileAndExecuteAsync(string code, Stopwatch stopwatch, bool dryRun = false, bool autoImport = false, bool internals = false,
            EvalAnalyticsDetails details = null)
        {
            stopwatch ??= Stopwatch.StartNew();

            var assemblyName = $"PipelineEval_{Guid.NewGuid():N}";
            if (internals)
            {
                assemblyName = InternalsAccessName.Get();
                if (assemblyName == null)
                {
                    return EvalResponse.EvalFailure(
                        EvalResponse.InternalErrorCode,
                        "Internals Unavailable",
                        "No assembly name that Unity's core modules grant internal access to is free to use",
                        stopwatch.ElapsedMilliseconds);
                }
            }

            var request = BuildRequest(code, assemblyName, Application.isEditor, dryRun);
            var compilation = RoslynCompilationService.Compile(request);

            List<string> addedUsings = null;
            for (var round = 0; autoImport && !compilation.Success && round < k_MaxAutoImportRounds; round++)
            {
                var resolution = EvalAutoImport.Resolve(compilation.Diagnostics, includeNonPublic: internals);
                if (resolution.Ambiguous || resolution.Namespaces.Count == 0)
                    break;

                (addedUsings ??= new List<string>()).AddRange(resolution.Namespaces);
                request.ScriptUsings = request.ScriptUsings.Concat(resolution.Namespaces).ToArray();
                compilation = RoslynCompilationService.Compile(request);
            }

            details?.RecordCompilation(compilation);

            var response = await Run(compilation, dryRun, stopwatch, details);
            response.AddedUsings = addedUsings;
            return response;
        }

        static async Task<EvalResponse> Run(CompilationResult compilation, bool dryRun, Stopwatch stopwatch, EvalAnalyticsDetails details)
        {
            if (!compilation.Success)
            {
                return EvalResponse.EvalFailure(
                    EvalResponse.CompilationErrorCode,
                    "Compilation Failed",
                    SummarizeErrors(compilation.Diagnostics),
                    stopwatch.ElapsedMilliseconds,
                    compilation.Diagnostics);
            }

            if (dryRun)
            {
                var compiled = EvalResponse.EvalSuccess(null, null, stopwatch.ElapsedMilliseconds, compilation.Diagnostics);
                compiled.Message = "Compiled successfully (dry run; nothing was loaded or executed).";
                return compiled;
            }

            if (compilation.ScriptEntryPoint == null)
            {
                return EvalResponse.EvalFailure(
                    EvalResponse.InternalErrorCode,
                    "Assembly Load Error",
                    "Could not find the compiled script's entry point",
                    stopwatch.ElapsedMilliseconds,
                    compilation.Diagnostics);
            }

            // Captured before the script's first await, after which CommandContext.Current is gone;
            // waits in the script observe it through the ambient token.
            PipelineCancellation.AmbientToken = Commands.CommandContext.Current?.CancellationToken ?? CancellationToken.None;

            object value;
            try
            {
                var task = (Task<object>)compilation.ScriptEntryPoint.Invoke(null, new object[] { new object[2] });
                value = await task;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                details?.RecordException(inner, compilation.Assembly);
                return EvalResponse.EvalFailure(
                    EvalResponse.RuntimeExceptionCode,
                    "Runtime Error",
                    $"{inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}",
                    stopwatch.ElapsedMilliseconds,
                    compilation.Diagnostics);
            }

            return EvalResponse.EvalSuccess(
                SerializeResult(value),
                null,
                stopwatch.ElapsedMilliseconds,
                compilation.Diagnostics);
        }

        /// <summary>
        /// One line per compile error, for clients that show only the reply's error details.
        /// </summary>
        internal static string SummarizeErrors(List<DiagnosticInfo> diagnostics)
        {
            var errors = diagnostics
                .Where(d => d.Severity == "error")
                .Select(d => $"\n  line {d.Line}, col {d.Column}: error {d.Id}: {d.Message}");
            return "Code compilation failed:" + string.Concat(errors);
        }

        static CompilationRequest BuildRequest(string userCode, string assemblyName, bool isEditorContext, bool dryRun)
        {
            var usings = new List<string>
            {
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Threading.Tasks",
                "UnityEngine",
                "Unity.Pipeline.Scripting"
            };
            if (isEditorContext)
                usings.Add("UnityEditor");

            // A snippet declaring its own Object alias keeps it; the preamble line stays so line
            // numbers do not shift.
            var aliasLine = DeclaresObjectAlias(userCode) ? string.Empty : k_ObjectAlias;

            return new CompilationRequest
            {
                SourceCode = $"{aliasLine}\n#line 1\n{userCode}",
                AssemblyName = assemblyName,
                LineNumberOffset = k_DiagnosticLineOffset,
                // In-memory assemblies can't be unloaded, so a dry run never loads one.
                SkipLoad = dryRun,
                EmitDebugInformation = !dryRun,
                DocumentPath = k_DocumentPath,
                ScriptUsings = usings.ToArray()
            };
        }

        static bool DeclaresObjectAlias(string userCode) =>
            CSharpSyntaxTree.ParseText(userCode, k_ScriptParseOptions).GetCompilationUnitRoot().Usings
                .Any(u => u.Alias?.Name.Identifier.ValueText == "Object");

        /// <summary>
        /// Serialize result for JSON response.
        /// </summary>
        private static object SerializeResult(object value)
        {
            if (value == null) return null;

            // Handle primitive types directly
            if (value is string || value is bool || value is int || value is long || value is float || value is double)
                return value;

            // Handle Unity objects
            if (value is UnityEngine.Object unityObj)
            {
                try
                {
                    return Newtonsoft.Json.JsonConvert.DeserializeObject(UnityEngine.JsonUtility.ToJson(unityObj));
                }
                catch
                {
                    return value.ToString();
                }
            }

            // Handle other objects via JSON serialization
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject(
                    Newtonsoft.Json.JsonConvert.SerializeObject(value));
            }
            catch
            {
                return value.ToString();
            }
        }

#else
        /// <summary>
        /// Runtime compilation not supported on this platform.
        /// Desktop development builds only (Windows/Mac/Linux).
        /// </summary>
        /// <param name="code">Unused on this build.</param>
        /// <param name="stopwatch">Unused on this build.</param>
        /// <param name="dryRun">Unused on this build.</param>
        /// <param name="autoImport">Unused on this build.</param>
        /// <param name="internals">Unused on this build.</param>
        /// <param name="details">Unused on this build.</param>
        /// <returns>A failure result explaining runtime compilation isn't supported on this build.</returns>
        public static Task<EvalResponse> CompileAndExecuteAsync(string code, System.Diagnostics.Stopwatch stopwatch, bool dryRun = false, bool autoImport = false, bool internals = false,
            EvalAnalyticsDetails details = null)
        {
            return Task.FromResult(EvalResponse.EvalFailure(
                EvalResponse.UnsupportedPlatformCode,
                "Platform Not Supported",
                "Runtime code compilation only supported on Desktop development builds"));
        }
#endif
    }
}
