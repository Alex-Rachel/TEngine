using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
// Roslyn — the bundled UnityPipeline.Microsoft.CodeAnalysis* plugins — ships only to the Editor and
// desktop players (Runtime/Plugins/CodeAnalysis/*.dll.meta); mobile/console players get the
// Roslyn-free stubs below.
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
using UnityPipeline.Microsoft.CodeAnalysis.Emit;
using UnityPipeline.Microsoft.CodeAnalysis.Text;
#endif
using Unity.Pipeline.Models;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// Shared Roslyn compilation service for both code evaluation and method reload systems.
    /// Eliminates code duplication by providing common compilation infrastructure.
    /// Compile on the main thread; CompileAsync is the off-thread entry point (see Compile).
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class RoslynCompilationService
    {
#if UNITY_EDITOR || (UNITY_STANDALONE && DEBUG)

        /// <summary>
        /// Compile source code to assembly with customizable options.
        ///
        /// Call on the main thread. Reference discovery (<see cref="GetMetadataReferences"/>) asks
        /// Unity for each loaded assembly's path, and under CoreCLR (Unity 7) that lookup is
        /// main-thread-only for any assembly loaded from bytes — which includes every assembly a
        /// previous compile loaded. Off the main thread the compile then fails with ROSLYN001
        /// "Internal_GetAssemblyLocation can only be called from the main thread". To compile off
        /// the main thread, use <see cref="CompileAsync"/>, which resolves references first.
        /// </summary>
        /// <param name="request">The source and compilation options.</param>
        /// <returns>The compilation result.</returns>
        public static CompilationResult Compile(CompilationRequest request) => Compile(request, null);

        /// <param name="request">The source and compilation options.</param>
        /// <param name="references">Pre-resolved references, or null to resolve them here.</param>
        static CompilationResult Compile(CompilationRequest request, List<MetadataReference> references)
        {
            try
            {
                // Parse syntax tree. When emitting debug info, the tree needs a document path and
                // UTF-8 encoding so the portable PDB can reference a source document. Explicit
                // symbols (e.g. run_script's `defines`) win; otherwise the project's define set
                // applies so the source's #if regions match the running assemblies.
                var emitDebug = request.EmitDebugInformation || request.EmbedDebugInformation;
                var isScript = request.ScriptUsings != null;
                var parseOptions = ProjectParseOptions(request.PreprocessorSymbols);
                if (isScript)
                    parseOptions = parseOptions.WithKind(SourceCodeKind.Script);
                var syntaxTree = emitDebug
                    ? CSharpSyntaxTree.ParseText(
                        SourceText.From(request.SourceCode, Encoding.UTF8),
                        parseOptions,
                        path: request.DocumentPath ?? request.AssemblyName + ".cs")
                    : CSharpSyntaxTree.ParseText(request.SourceCode, parseOptions);

                // Get metadata references with optional additional prefixes
                references = references ?? GetMetadataReferences(request.AdditionalAssemblyPrefixes);

                // Create compilation. Disable optimizations when emitting debug info so the JIT
                // keeps full sequence points (otherwise breakpoints can't bind).
                var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
                if (emitDebug)
                    options = options.WithOptimizationLevel(OptimizationLevel.Debug);
                // Non-public member access needs two pieces: MetadataImportOptions.All (the default,
                // Public, doesn't import non-public members of referenced assemblies at all — CS1061)
                // and BinderFlags.IgnoreAccessibility (the compiler does not honor
                // [assembly: IgnoresAccessChecksTo] — CS0122 otherwise). Interpreter-bound compiles
                // only (see the property doc): the interpreter executes the result by reflection,
                // which reaches non-public members. Unity's Mono JIT-enforces accessibility on
                // Assembly.Load'd IL and ignores IgnoresAccessChecksTo too (verified on Mono 6.13) —
                // see MethodReloadInPlaceTests.ReloadFileInPlace_PrivateFieldAccess_FailsValidationUpFront.
                if (request.AllowNonPublicMemberAccess)
                    options = WithIgnoreAccessibility(options.WithMetadataImportOptions(MetadataImportOptions.All));

                var compilation = isScript
                    ? CSharpCompilation.CreateScriptCompilation(
                        request.AssemblyName,
                        syntaxTree,
                        references,
                        options.WithUsings(request.ScriptUsings),
                        returnType: typeof(object))
                    : CSharpCompilation.Create(
                        request.AssemblyName,
                        new[] { syntaxTree },
                        references,
                        options);

                // Get diagnostics
                var diagnostics = compilation.GetDiagnostics();
                var diagnosticInfos = ConvertDiagnostics(diagnostics, request.LineNumberOffset);
                Func<IReadOnlyList<string>> listUnityApis = () => UnityApiCollector.Collect(compilation, syntaxTree);

                // Check for compilation errors
                var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (errors.Any())
                {
                    return new CompilationResult
                    {
                        Success = false,
                        Diagnostics = diagnosticInfos,
                        ListUnityApis = listUnityApis
                    };
                }

                // Compile to memory
                using (var peStream = new MemoryStream())
                {
                    byte[] pdbBytes = null;
                    EmitResult emitResult;

                    if (request.EmbedDebugInformation)
                    {
                        // Embed the portable PDB in the PE debug directory so the single byte[]
                        // pushed to a player carries its own sequence points; the IlInterpreter
                        // interpreter reads line info only from an embedded PDB.
                        emitResult = compilation.Emit(
                            peStream,
                            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));
                    }
                    else if (request.EmitDebugInformation)
                    {
                        using (var pdbStream = new MemoryStream())
                        {
                            emitResult = compilation.Emit(
                                peStream,
                                pdbStream,
                                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

                            if (emitResult.Success)
                                pdbBytes = pdbStream.ToArray();
                        }
                    }
                    else
                    {
                        emitResult = compilation.Emit(peStream);
                    }

                    if (!emitResult.Success)
                    {
                        var emitDiagnostics = ConvertDiagnostics(emitResult.Diagnostics, request.LineNumberOffset);
                        return new CompilationResult
                        {
                            Success = false,
                            Diagnostics = diagnosticInfos.Concat(emitDiagnostics).ToList(),
                            ListUnityApis = listUnityApis
                        };
                    }

                    // Load assembly from memory. When symbols are present, load them alongside the
                    // assembly so an attached debugger can bind breakpoints into the emitted code.
                    // SkipLoad = emit-only (run_script's dry-run, and the interpreter path — see
                    // the property doc).
                    var assemblyBytes = peStream.ToArray();
                    Assembly assembly = null;
                    if (!request.SkipLoad)
                    {
                        assembly = pdbBytes != null
                            ? PipelineUtils.LoadFromBytes(assemblyBytes, pdbBytes)
                            : PipelineUtils.LoadFromBytes(assemblyBytes);
                    }

                    MethodInfo scriptEntryPoint = null;
                    if (isScript && assembly != null)
                    {
                        var entryPoint = compilation.GetEntryPoint(default);
                        scriptEntryPoint = assembly.GetType(entryPoint.ContainingType.MetadataName)?.GetMethod(
                            entryPoint.MetadataName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    }

                    return new CompilationResult
                    {
                        Success = true,
                        Assembly = assembly,
                        AssemblyBytes = assemblyBytes,
                        PdbBytes = pdbBytes,
                        Diagnostics = diagnosticInfos,
                        ScriptEntryPoint = scriptEntryPoint,
                        ListUnityApis = listUnityApis
                    };
                }
            }
            catch (Exception ex)
            {
                return ExceptionResult(ex);
            }
        }

        static CompilationResult ExceptionResult(Exception ex) => new CompilationResult
        {
            Success = false,
            Diagnostics = new List<DiagnosticInfo>
            {
                new DiagnosticInfo
                {
                    Severity = "error",
                    Message = $"Compilation exception: {ex.Message}",
                    Line = 0,
                    Column = 0,
                    Id = "ROSLYN001"
                }
            }
        };

        /// <summary>
        /// Async wrapper for compilation that runs on background thread.
        /// Use when you need to avoid blocking the calling thread.
        /// </summary>
        /// <param name="request">The source and compilation options.</param>
        /// <returns>The compilation result.</returns>
        public static Task<CompilationResult> CompileAsync(CompilationRequest request)
        {
            // References are resolved here, on the calling thread, and only the Roslyn work moves to
            // the pool: resolving them asks Unity for assembly paths, which is main-thread-only under
            // CoreCLR for byte-loaded assemblies (see Compile). Call this from the main thread.
            List<MetadataReference> references;
            try
            {
                references = GetMetadataReferences(request.AdditionalAssemblyPrefixes);
            }
            catch (Exception ex)
            {
                // Same contract as Compile: a failure is a result, not a throw.
                return Task.FromResult(ExceptionResult(ex));
            }

            return Task.Run(() => Compile(request, references));
        }

        // MetadataReference cache keyed by assembly path. Reusing the same instances, both within one
        // reload and across successive reloads, lets Roslyn cache each assembly's parsed metadata
        // instead of re-reading all ~190 DLLs per compilation (~830 ms each). Static: cleared by
        // domain reload, which is when the loaded-assembly set can change.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MetadataReference> s_ReferenceCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, MetadataReference>();

        static MetadataReference GetCachedReference(string path) =>
            s_ReferenceCache.GetOrAdd(path, p => MetadataReference.CreateFromFile(p));

        // The internal BinderFlags.IgnoreAccessibility is the only way to disable Roslyn's
        // compile-time access checks (no public API; [IgnoresAccessChecksTo] is ignored, see Compile).
        // Resolve the internal CSharpCompilationOptions.WithTopLevelBinderFlags(BinderFlags) once.
        static bool s_BinderFlagResolved;
        static MethodInfo s_WithTopLevelBinderFlags;
        static object s_IgnoreAccessibilityFlag;

        static CSharpCompilationOptions WithIgnoreAccessibility(CSharpCompilationOptions options)
        {
            if (!s_BinderFlagResolved)
            {
                s_BinderFlagResolved = true;
                try
                {
                    var binderFlags = typeof(CSharpCompilationOptions).Assembly
                        .GetType(typeof(CSharpCompilationOptions).Namespace + ".BinderFlags");
                    if (binderFlags != null)
                    {
                        s_IgnoreAccessibilityFlag = Enum.Parse(binderFlags, "IgnoreAccessibility");
                        s_WithTopLevelBinderFlags = typeof(CSharpCompilationOptions).GetMethod(
                            "WithTopLevelBinderFlags", BindingFlags.NonPublic | BindingFlags.Instance);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Pipeline: could not resolve IgnoreAccessibility binder flag; method-reload " +
                        $"overrides touching non-public members may fail to compile. {ex.Message}");
                }
            }

            if (s_WithTopLevelBinderFlags != null && s_IgnoreAccessibilityFlag != null)
                return (CSharpCompilationOptions)s_WithTopLevelBinderFlags.Invoke(options, new[] { s_IgnoreAccessibilityFlag });
            return options;
        }

        /// <summary>
        /// Get metadata references for compilation with optional additional assembly prefixes.
        /// Handles both Editor and Runtime scenarios with appropriate filtering. Reference instances
        /// are cached by path (see <see cref="s_ReferenceCache"/>) so repeated compilations are cheap.
        /// </summary>
        /// <param name="additionalPrefixes">Extra assembly name prefixes to include as references.</param>
        /// <returns>The metadata references to compile against.</returns>
        public static List<MetadataReference> GetMetadataReferences(string[] additionalPrefixes = null)
        {
            var references = new List<MetadataReference>();
            var assemblies = PipelineUtils.GetLoadedAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(PipelineUtils.GetLoadedAssemblyPath(a)));

            if (Application.isEditor)
            {
                // Editor: Include all loaded assemblies for maximum compatibility
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        references.Add(GetCachedReference(PipelineUtils.GetLoadedAssemblyPath(assembly)));
                    }
                    catch
                    {
                        // Skip problematic assemblies
                    }
                }
            }
            else
            {
                // Runtime: Use curated filtering for Unity + user assemblies
                var allowedPrefixes = new List<string>
                {
                    "UnityEngine",
                    "Assembly-CSharp",
                    "netstandard",
                    "mscorlib",
                    "System.",
                    "Unity.Pipeline"
                };

                // Add any additional prefixes (e.g., for method reload: "UnityPipeline.Microsoft.CodeAnalysis")
                if (additionalPrefixes != null)
                {
                    allowedPrefixes.AddRange(additionalPrefixes);
                }

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var assemblyName = assembly.GetName().Name;
                        if (allowedPrefixes.Any(prefix => assemblyName.StartsWith(prefix)))
                        {
                            references.Add(GetCachedReference(PipelineUtils.GetLoadedAssemblyPath(assembly)));
                        }
                    }
                    catch
                    {
                        // Skip problematic assemblies
                    }
                }
            }

            return references;
        }

        /// <summary>
        /// Convert Roslyn diagnostics to DiagnosticInfo list with optional line number adjustment.
        /// Handles line number offset for wrapped code scenarios.
        /// </summary>
        private static List<DiagnosticInfo> ConvertDiagnostics(IEnumerable<Diagnostic> diagnostics, int lineOffset = 0)
        {
            var result = new List<DiagnosticInfo>();

            foreach (var diagnostic in diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning))
            {
                var location = diagnostic.Location.GetLineSpan();
                var line = Math.Max(0, location.StartLinePosition.Line - lineOffset);
                var column = location.StartLinePosition.Character;

                var info = new DiagnosticInfo
                {
                    Severity = diagnostic.Severity.ToString().ToLower(),
                    Message = diagnostic.GetMessage(),
                    Line = line,
                    Column = column,
                    Id = diagnostic.Id
                };

                // #line directives (method reload maps rewritten bodies back to the user's file) give
                // the user a location they can open; the raw line text shows what was compiled.
                var mapped = diagnostic.Location.GetMappedLineSpan();
                if (mapped.HasMappedPath)
                {
                    info.File = mapped.Path;
                    info.FileLine = mapped.StartLinePosition.Line + 1;
                }
                var text = diagnostic.Location.SourceTree?.GetText();
                var rawLine = location.StartLinePosition.Line;
                if (text != null && rawLine >= 0 && rawLine < text.Lines.Count)
                    info.Source = text.Lines[rawLine].ToString().Trim();
                if (text != null && diagnostic.Location.IsInSource && diagnostic.Location.SourceSpan.End <= text.Length)
                    info.SpanText = text.ToString(diagnostic.Location.SourceSpan);

                result.Add(info);
            }

            return result;
        }

#else
        /// <summary>
        /// Runtime compilation not supported on this platform.
        /// Desktop development builds only (Windows/Mac/Linux).
        /// </summary>
        /// <param name="request">Unused on this build.</param>
        /// <returns>A failure result explaining runtime compilation isn't supported on this build.</returns>
        public static CompilationResult Compile(CompilationRequest request)
        {
            return new CompilationResult
            {
                Success = false,
                Diagnostics = new List<DiagnosticInfo>
                {
                    new DiagnosticInfo
                    {
                        Severity = "error",
                        Message = "Runtime code compilation only supported on Desktop development builds",
                        Line = 0,
                        Column = 0,
                        Id = "PLATFORM001"
                    }
                }
            };
        }

        /// <summary>
        /// Runtime compilation not supported on this platform.
        /// </summary>
        /// <param name="request">Unused on this build.</param>
        /// <returns>A completed task carrying a failure result.</returns>
        public static Task<CompilationResult> CompileAsync(CompilationRequest request)
        {
            return Task.FromResult(Compile(request));
        }

#if UNITY_STANDALONE
        // Release desktop player: Roslyn is present (the parse sites in InPlaceReloadProcessor and
        // SourceCodeTransformer still compile) but compilation is stubbed out, so hand them no references.
        /// <summary>
        /// Runtime compilation not supported on this platform.
        /// </summary>
        /// <param name="additionalPrefixes">Unused on this build.</param>
        /// <returns>An empty list.</returns>
        public static List<MetadataReference> GetMetadataReferences(string[] additionalPrefixes = null)
        {
            return new List<MetadataReference>();
        }
#endif
#endif

        // Preprocessor symbols the surrounding project was compiled with. Reload/eval sources must
        // parse under the same defines as the assemblies they patch, or every #if UNITY_EDITOR /
        // ENABLE_INPUT_SYSTEM / UNITY_STANDALONE region resolves to the wrong branch. The editor
        // snapshots its live define set; a Mono dev player has no runtime API for it, so callers
        // that care (the editor-side push path) pass explicit symbols.
        //
        // Kept outside the desktop-dev #if region: the parse sites that consume this
        // (InPlaceReloadProcessor, SourceCodeTransformer) also compile on
        // release standalone, where the region above is compiled out.
        static string[] s_LastProjectDefines = Array.Empty<string>();

        static string[] ProjectDefines()
        {
#if UNITY_EDITOR
            // Editor API — main thread only. Off-thread callers reuse the last snapshot.
            try { s_LastProjectDefines = UnityEditor.EditorUserBuildSettings.activeScriptCompilationDefines; }
            catch { }
#endif
            return s_LastProjectDefines;
        }

        /// <summary>
        /// Seed the define snapshot from the main thread. Called at editor load
        /// (PipelineServerStartup's [InitializeOnLoad] ctor) so a background compile that runs
        /// before any main-thread parse doesn't see an EMPTY define set — its #if UNITY_EDITOR /
        /// UNITY_STANDALONE regions would resolve to the wrong branch.
        /// </summary>
        internal static void SnapshotProjectDefines() => ProjectDefines();

#if UNITY_EDITOR || UNITY_STANDALONE
        /// <summary>Parse options carrying <paramref name="preprocessorSymbols"/>, or the project's
        /// define set (see <see cref="ProjectDefines"/>) when null. Every parse of user source in the
        /// reload/eval pipeline should use this so #if regions match the running assemblies.
        /// Desktop only: the parse sites that call it compile out with Roslyn elsewhere.</summary>
        internal static CSharpParseOptions ProjectParseOptions(string[] preprocessorSymbols = null)
            => CSharpParseOptions.Default.WithPreprocessorSymbols(preprocessorSymbols ?? ProjectDefines());
#endif
    }

    /// <summary>
    /// Request for Roslyn compilation with customizable options.
    /// </summary>
    public class CompilationRequest
    {
        /// <summary>
        /// Source code to compile.
        /// </summary>
        public string SourceCode { get; set; }

        /// <summary>
        /// Name for the generated assembly (should be unique).
        /// </summary>
        public string AssemblyName { get; set; }

        /// <summary>
        /// Additional assembly prefixes to include in metadata references.
        /// Useful for method reload scenarios that need "UnityPipeline.Microsoft.CodeAnalysis" etc.
        /// </summary>
        public string[] AdditionalAssemblyPrefixes { get; set; }

        /// <summary>
        /// Preprocessor symbols to parse with. Null = the project's define set
        /// (<see cref="RoslynCompilationService.ProjectDefines"/>), so #if regions in the source
        /// resolve the same way they did when the running assemblies were compiled. The push-to-player
        /// path passes the target player's defines explicitly, and <c>run_script</c>'s
        /// <c>defines</c> parameter arrives here too.
        /// </summary>
        internal string[] PreprocessorSymbols { get; set; }

        /// <summary>
        /// Line number offset to subtract from diagnostic line numbers.
        /// Used when source code has wrapper code that should be hidden from diagnostics.
        /// </summary>
        public int LineNumberOffset { get; set; }

        /// <summary>
        /// When true, emit a portable PDB and load the assembly with its symbols so an attached
        /// managed debugger can bind breakpoints. Compiles unoptimized. Default false (no symbols).
        /// </summary>
        public bool EmitDebugInformation { get; set; }

        /// <summary>
        /// When true, emit the portable PDB inside the assembly itself (PE debug directory) instead
        /// of as separate <see cref="CompilationResult.PdbBytes"/>. Compiles unoptimized, like
        /// <see cref="EmitDebugInformation"/>. Use for the interpreter push path, which ships a single
        /// byte[] to the player — the IlInterpreter interpreter resolves error line numbers only from an
        /// embedded PDB.
        /// </summary>
        internal bool EmbedDebugInformation { get; set; }

        /// <summary>
        /// Document path recorded in the syntax tree when <see cref="EmitDebugInformation"/> or
        /// <see cref="EmbedDebugInformation"/> is set.
        /// Source that is not covered by explicit <c>#line</c> directives maps to this path.
        /// </summary>
        public string DocumentPath { get; set; }

        /// <summary>
        /// When true, compile and collect diagnostics but do NOT load the emitted assembly into
        /// the domain: <see cref="CompilationResult.Assembly"/> stays null;
        /// <see cref="CompilationResult.AssemblyBytes"/> is still returned. Two callers need this:
        /// compile-only paths such as <c>run_script</c>'s <c>dry-run</c> (in-memory assemblies are
        /// not unloadable under Mono, so loading would leak one per compile), and the IL2CPP-safe
        /// interpreter path, which runs the raw bytes directly — <c>Assembly.Load</c> is
        /// unavailable under IL2CPP. Default false — existing callers keep the load behavior.
        /// </summary>
        public bool SkipLoad { get; set; }

        /// <summary>
        /// When true, referenced assemblies' non-public members are imported and access checks against them
        /// are relaxed (<c>MetadataImportOptions.All</c>). Interpreter-backend compiles only: the
        /// interpreter executes the emitted IL via reflection, which reaches non-public members. Never set
        /// this for IL that will be <c>Assembly.Load</c>'d — Mono JIT-enforces accessibility on loaded IL,
        /// so the access would compile and then throw at first dispatch. Off by default.
        /// </summary>
        internal bool AllowNonPublicMemberAccess { get; set; }

        /// <summary>
        /// When non-null, the source compiles as a C# script (top-level statements, declarations,
        /// using directives and await) with these namespaces imported, and the loaded result exposes
        /// <see cref="CompilationResult.ScriptEntryPoint"/>. Null compiles a regular library.
        /// </summary>
        internal string[] ScriptUsings { get; set; }

    }

    /// <summary>
    /// Result from Roslyn compilation.
    /// </summary>
    public class CompilationResult
    {
        /// <summary>
        /// Whether compilation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Compiled assembly (only available if Success = true and
        /// <see cref="CompilationRequest.SkipLoad"/> was not set).
        /// </summary>
        public Assembly Assembly { get; set; }

        /// <summary>
        /// Raw assembly bytes (for potential disk saving or caching).
        /// </summary>
        public byte[] AssemblyBytes { get; set; }

        /// <summary>
        /// Portable PDB bytes when <see cref="CompilationRequest.EmitDebugInformation"/> was set;
        /// null otherwise.
        /// </summary>
        public byte[] PdbBytes { get; set; }

        /// <summary>
        /// Compilation diagnostics (errors, warnings, info).
        /// </summary>
        public List<DiagnosticInfo> Diagnostics { get; set; } = new List<DiagnosticInfo>();

        /// <summary>
        /// The loaded script's factory, for a <see cref="CompilationRequest.ScriptUsings"/> compile:
        /// invoke it with a two-slot <c>object[]</c> submission array to run the script and get its
        /// <c>Task&lt;object&gt;</c>. Null for library compiles and when nothing was loaded.
        /// </summary>
        internal MethodInfo ScriptEntryPoint { get; set; }

        /// <summary>
        /// Lists the Unity members the source resolves to. Binding every name is costly, so it runs
        /// only when called. Null when the compile failed before binding.
        /// </summary>
        internal Func<IReadOnlyList<string>> ListUnityApis { get; set; }
    }
}