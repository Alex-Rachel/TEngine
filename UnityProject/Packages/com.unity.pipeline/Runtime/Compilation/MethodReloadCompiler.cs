using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Pipeline.MethodReload;
using Unity.Pipeline.Models;
using Unity.Pipeline.Threading;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Compilation
{
    /// <summary>
    /// Compiles transformed method reload source using the shared RoslynCompilationService.
    /// Creates versioned DLLs and manages method reload assembly lifecycle.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class MethodReloadCompiler
    {
        /// <summary>
        /// Interpreter-targeted overrides (in-editor backend and push-to-player) carry an embedded
        /// PDB and #line mapping, so runtime errors report the user's source file and line instead
        /// of an IL offset. Costs a few KB per pushed override and compiles it unoptimized
        /// (irrelevant to interpreted execution). Always on; this knob exists only so tests can
        /// exercise the no-line-info error path — nothing in production writes it, and a domain
        /// reload resets it to true.
        /// </summary>
        internal static bool EmitSourceLineInfo = true;

#if UNITY_EDITOR || (UNITY_STANDALONE && DEBUG)

        private static readonly Dictionary<string, int> _versionTracker = new();
        private const string MethodReloadTempDir = "Temp/MethodReload";

        /// <summary>
        /// Compile source code on main thread synchronously to avoid deadlocks.
        /// </summary>
        /// <param name="sourceCode">Method reload source code to compile.</param>
        /// <param name="baseFileName">Base filename for versioned assembly naming.</param>
        /// <param name="assemblyDir">Optional directory to save compiled assembly to disk.</param>
        /// <param name="emitPdb">Emit a portable PDB and load symbols so breakpoints can bind.</param>
        /// <param name="documentPath">Source document path recorded in the PDB (the original .cs file).</param>
        /// <param name="interpreterMethods">When set, the compiled bytes register with the interpreter backend instead of Assembly.Load.</param>
        /// <returns>The compilation result.</returns>
        internal static MethodReloadCompileResult CompileSourceCodeOnMainThread(string sourceCode, string baseFileName, string assemblyDir = null, bool emitPdb = false, string documentPath = null, InterpreterOverrideSet interpreterMethods = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var useInterpreter = interpreterMethods != null;

            try
            {
                Debug.Log($"MethodReload: Starting synchronous source code compilation for {baseFileName}");

                if (string.IsNullOrWhiteSpace(sourceCode))
                {
                    return MethodReloadCompileResult.Failure(
                        "Empty Source Code",
                        "Source code cannot be empty",
                        stopwatch.ElapsedMilliseconds);
                }

                // Generate versioned assembly name
                var nextVersion = GetNextVersion(baseFileName);
                var assemblyName = $"{baseFileName}_{nextVersion:D3}";

                // Ensure temp directory exists
                Directory.CreateDirectory(MethodReloadTempDir);

                // The interpreter path needs only the bytes (no Assembly.Load), with the PDB embedded
                // in them: the interpreter reads sequence points only from an embedded PDB and uses
                // them for source lines in runtime error messages.
                var compilationResult = CompileMethodReloadAssembly(sourceCode, assemblyName,
                    emitPdb: emitPdb && !useInterpreter, documentPath, loadAssembly: !useInterpreter,
                    embedPdb: useInterpreter && EmitSourceLineInfo);

                if (!compilationResult.Success)
                {
                    var dumpPath = DumpFailedSource(assemblyName, sourceCode);
                    return MethodReloadCompileResult.Failure(
                        "Compilation Failed",
                        "Method reload source code compilation failed" +
                        (dumpPath != null ? $" (generated source: {dumpPath})" : ""),
                        stopwatch.ElapsedMilliseconds,
                        compilationResult.Diagnostics.Select(FormatDiagnostic).ToList());
                }

                List<string> registeredMethods;
                List<string> skippedOverrides;
                if (useInterpreter)
                {
                    registeredMethods = InterpreterMethodReloadExecutor.Register(
                        compilationResult.AssemblyBytes, interpreterMethods.TypeName, interpreterMethods.MethodNames,
                        out skippedOverrides, out var bindingWarnings);
                    // The reload response carries one flat Diagnostics list; append binding warnings
                    // (unbound host members that throw only if reached) so the client sees the gap
                    // in the same reply that reports the reload as applied.
                    skippedOverrides.AddRange(bindingWarnings);
                }
                else
                {
                    registeredMethods = RegisterMethodReloadMethods(compilationResult.Assembly, assemblyName, out skippedOverrides);
                }

                // Save assembly to disk if assemblyDir is specified
                string actualAssemblyPath = null;
                if (!string.IsNullOrWhiteSpace(assemblyDir))
                {
                    Directory.CreateDirectory(assemblyDir);
                    actualAssemblyPath = Path.Combine(assemblyDir, $"{assemblyName}.dll");
                    File.WriteAllBytes(actualAssemblyPath, compilationResult.AssemblyBytes);
                    Debug.Log($"MethodReload: Assembly saved to disk: {actualAssemblyPath}");

                    // Symbols are loaded in-memory; also write the .pdb next to the .dll for convenience.
                    if (compilationResult.PdbBytes != null)
                    {
                        var pdbPath = Path.Combine(assemblyDir, $"{assemblyName}.pdb");
                        File.WriteAllBytes(pdbPath, compilationResult.PdbBytes);
                        Debug.Log($"MethodReload: Symbols saved to disk: {pdbPath}");
                    }
                }
                else
                {
                    actualAssemblyPath = Path.Combine(MethodReloadTempDir, $"{assemblyName}.dll"); // For compatibility (in-memory)
                }

                stopwatch.Stop();

                Debug.Log($"MethodReload: Successfully compiled source code -> {assemblyName}.dll with {registeredMethods.Count} methods in {stopwatch.ElapsedMilliseconds}ms");

                var sourceCompileResult = MethodReloadCompileResult.Success(
                    assemblyName,
                    actualAssemblyPath,
                    registeredMethods,
                    stopwatch.ElapsedMilliseconds);
                sourceCompileResult.Diagnostics = skippedOverrides;
                return sourceCompileResult;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Debug.LogError($"MethodReload: Synchronous source code compilation failed for {baseFileName}: {ex.Message}");
                return MethodReloadCompileResult.Failure(
                    "Exception",
                    ex.ToString(),
                    stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Clean up old method reload DLL versions, keeping only the latest for each base filename.
        /// </summary>
        /// <param name="assemblyDir">Directory containing method-reload assemblies, or null for the default temp directory.</param>
        /// <returns>Which files were deleted, and whether cleanup succeeded.</returns>
        public static MethodReloadCleanupResult CleanupMethodReloadDlls(string assemblyDir = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var deletedFiles = new List<string>();

            try
            {
                // Use specified assemblyDir or default to MethodReloadTempDir
                var targetDir = !string.IsNullOrWhiteSpace(assemblyDir) ? assemblyDir : MethodReloadTempDir;

                if (!Directory.Exists(targetDir))
                {
                    return new MethodReloadCleanupResult
                    {
                        Success = true,
                        DeletedFiles = deletedFiles,
                        Message = $"No method reload directory found: {targetDir}",
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                var dllFiles = Directory.GetFiles(targetDir, "*.dll");
                var groupedFiles = dllFiles
                    .Select(f => new
                    {
                        FilePath = f,
                        FileName = Path.GetFileNameWithoutExtension(f),
                        BaseFilename = ExtractBaseFilename(Path.GetFileNameWithoutExtension(f)),
                        Version = ExtractVersion(Path.GetFileNameWithoutExtension(f))
                    })
                    .Where(f => f.Version > 0) // Only versioned method reload DLLs
                    .GroupBy(f => f.BaseFilename)
                    .ToList();

                foreach (var group in groupedFiles)
                {
                    var sortedFiles = group.OrderByDescending(f => f.Version).ToList();

                    // Keep the latest version, delete older ones
                    for (int i = 1; i < sortedFiles.Count; i++)
                    {
                        var fileToDelete = sortedFiles[i];
                        File.Delete(fileToDelete.FilePath);
                        deletedFiles.Add(fileToDelete.FileName + ".dll");
                        Debug.Log($"MethodReload: Deleted old version {fileToDelete.FileName}.dll");
                    }
                }

                // Clear registry and reset version tracker
                MethodReloadRegistry.ClearAllOverrides();
                _versionTracker.Clear();

                stopwatch.Stop();

                Debug.Log($"MethodReload: Cleanup completed - deleted {deletedFiles.Count} files in {stopwatch.ElapsedMilliseconds}ms");

                return new MethodReloadCleanupResult
                {
                    Success = true,
                    DeletedFiles = deletedFiles,
                    Message = $"Deleted {deletedFiles.Count} old DLL versions",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Debug.LogError($"MethodReload: Cleanup failed: {ex.Message}");

                return new MethodReloadCleanupResult
                {
                    Success = false,
                    DeletedFiles = deletedFiles,
                    Message = $"Cleanup failed: {ex.Message}",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        /// <summary>
        /// Get the next version number for a base filename.
        /// </summary>
        private static int GetNextVersion(string baseFilename)
        {
            if (!_versionTracker.ContainsKey(baseFilename))
            {
                _versionTracker[baseFilename] = 0;
            }

            return ++_versionTracker[baseFilename];
        }

        /// <summary>
        /// Compile transformed override source to raw assembly bytes without loading or registering
        /// anything, for the push-to-player path (the device runs the bytes through the interpreter).
        /// The PDB is embedded so the interpreter can report source lines instead of IL offsets;
        /// <paramref name="documentPath"/> is recorded for source outside the transform's #line
        /// directives.
        /// </summary>
        internal static bool TryCompileOverrideBytes(string transformedCode, string baseFileName, out byte[] ilBytes, out List<string> diagnostics, string documentPath = null, string[] preprocessorSymbols = null)
        {
            ilBytes = null;
            diagnostics = new List<string>();

            var assemblyName = $"{baseFileName}_{GetNextVersion(baseFileName):D3}";
            var result = CompileMethodReloadAssembly(transformedCode, assemblyName, emitPdb: false, documentPath: documentPath, loadAssembly: false, embedPdb: EmitSourceLineInfo, preprocessorSymbols: preprocessorSymbols);
            if (!result.Success)
            {
                diagnostics = result.Diagnostics.Select(FormatDiagnostic).ToList();
                var dumpPath = DumpFailedSource(assemblyName, transformedCode);
                if (dumpPath != null)
                    diagnostics.Add($"Generated source written to {dumpPath}");
                return false;
            }

            ilBytes = result.AssemblyBytes;
            return ilBytes != null && ilBytes.Length > 0;
        }

        /// <summary>
        /// One diagnostic as the user should read it: the line of THEIR file it maps to (via the
        /// transform's #line directives), the compiler message, and the generated line that
        /// actually failed. The generated source differs from what the user wrote (bodies are
        /// lifted into a static class, members re-qualified), so a bare compiler message such as
        /// "the type name 'var' does not exist in the type 'Foo'" is not debuggable on its own.
        /// </summary>
        internal static string FormatDiagnostic(DiagnosticInfo d)
        {
            var where = d.File != null
                ? $"{Path.GetFileName(d.File)}({d.FileLine})"
                : $"generated line {d.Line + 1}";
            var text = $"{where}: {d.Severity} {d.Id}: {d.Message}";
            if (!string.IsNullOrEmpty(d.Source))
                text += $"\n    generated line {d.Line + 1}: {d.Source}";
            return text;
        }

        /// <summary>
        /// Write the generated override source next to the versioned DLLs so a failed compile can
        /// be inspected whole. Returns the path, or null when the write failed (never fatal).
        /// </summary>
        private static string DumpFailedSource(string assemblyName, string sourceCode)
        {
            try
            {
                Directory.CreateDirectory(MethodReloadTempDir);
                var path = Path.Combine(MethodReloadTempDir, $"{assemblyName}.failed.cs");
                File.WriteAllText(path, sourceCode);
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Compile method reload source code using shared RoslynCompilationService.
        /// Synchronous compilation for main thread use.
        /// </summary>
        private static CompilationResult CompileMethodReloadAssembly(string sourceCode, string assemblyName, bool emitPdb = false, string documentPath = null, bool loadAssembly = true, bool embedPdb = false, string[] preprocessorSymbols = null)
        {
            var request = new CompilationRequest
            {
                // Null = the project's define set; the push path passes the player's defines.
                PreprocessorSymbols = preprocessorSymbols,
                SourceCode = sourceCode,
                AssemblyName = assemblyName,
                // Ensure Unity.Pipeline assemblies are included (contains MethodReloadOverrideMethodAttribute)
                // Also include test assemblies for testing scenarios
                AdditionalAssemblyPrefixes = new[] { "Unity.Pipeline", "Unity.Pipeline.Tests" },
                EmitDebugInformation = emitPdb,
                EmbedDebugInformation = embedPdb,
                DocumentPath = documentPath,
                // The interpreter path runs the bytes directly; skip Assembly.Load (IL2CPP-unsafe).
                SkipLoad = !loadAssembly,
                // Interpreter-bound bytes only: the interpreter reaches non-public members via
                // reflection. Loaded assemblies keep standard access checks — Mono JIT-enforces
                // accessibility on Assembly.Load'd IL, so relaxing them would only trade a compile
                // error for a throw at first dispatch (see AccessibilityValidator).
                AllowNonPublicMemberAccess = !loadAssembly
            };

            return RoslynCompilationService.Compile(request);
        }

        /// <summary>
        /// Register the [MethodReloadOverrideMethod] methods of a compiled assembly with the registry.
        /// Only overrides that actually bind are returned; overrides that were skipped (e.g. the
        /// target is not a registered [MethodReload] method, or a signature mismatch) are reported via
        /// <paramref name="skipped"/> with a user-facing reason.
        /// </summary>
        private static List<string> RegisterMethodReloadMethods(Assembly assembly, string assemblyId, out List<string> skipped)
        {
            var registeredMethods = new List<string>();
            skipped = new List<string>();

            // An override can only bind to a target that is registered, and outside Play Mode
            // nothing has registered them yet.
            RuntimePipelineDriver.EnsureDiscoveredMethodReloadMethods();

            try
            {
                // Register assembly types for discovery
                foreach (var type in assembly.GetTypes())
                {
                    MethodReloadRegistry.RegisterMethodReloadType(type, assemblyId);

                    // Find methods with [MethodReloadOverrideMethod] attribute for overrides
                    var staticMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

                    foreach (var method in staticMethods)
                    {
                        // Try multiple ways to find the attribute
                        var methodReloadAttr = method.GetCustomAttribute<MethodReloadOverrideMethodAttribute>();
                        if (methodReloadAttr == null)
                        {
                            // Try by name in case of type loading issues
                            var attrByName = method.GetCustomAttributes(true)
                                .FirstOrDefault(a => a.GetType().Name == "MethodReloadOverrideMethodAttribute");
                            if (attrByName != null)
                            {
                                // Cast to the attribute type
                                methodReloadAttr = attrByName as MethodReloadOverrideMethodAttribute;
                            }
                        }

                        if (methodReloadAttr != null)
                        {
                            if (MethodReloadRegistry.RegisterMethodOverride(method, methodReloadAttr, type, out var skipReason))
                            {
                                registeredMethods.Add(methodReloadAttr.TargetMethodId);
                            }
                            else
                            {
                                skipped.Add($"{method.Name} -> {methodReloadAttr.TargetMethodId}: {skipReason}");
                            }
                        }
                    }
                }

                Debug.Log($"MethodReload: Registered {registeredMethods.Count} override methods: {string.Join(", ", registeredMethods)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"MethodReload: Error registering methods from assembly {assemblyId}: {ex.Message}");
                Debug.LogError($"MethodReload: Stack trace: {ex.StackTrace}");
            }

            return registeredMethods;
        }

        /// <summary>
        /// Extract base filename from versioned filename (e.g., "PlayerMovement_001" -> "PlayerMovement").
        /// </summary>
        private static string ExtractBaseFilename(string versionedFilename)
        {
            var lastUnderscoreIndex = versionedFilename.LastIndexOf('_');
            if (lastUnderscoreIndex > 0)
            {
                var potentialVersion = versionedFilename.Substring(lastUnderscoreIndex + 1);
                if (int.TryParse(potentialVersion, out _))
                {
                    return versionedFilename.Substring(0, lastUnderscoreIndex);
                }
            }
            return versionedFilename;
        }

        /// <summary>
        /// Extract version number from versioned filename (e.g., "PlayerMovement_001" -> 1).
        /// </summary>
        private static int ExtractVersion(string versionedFilename)
        {
            var lastUnderscoreIndex = versionedFilename.LastIndexOf('_');
            if (lastUnderscoreIndex > 0)
            {
                var potentialVersion = versionedFilename.Substring(lastUnderscoreIndex + 1);
                if (int.TryParse(potentialVersion, out var version))
                {
                    return version;
                }
            }
            return 0;
        }

#else
        // Method reload requires Roslyn compilation, which is only available in the Editor and in
        // Desktop development builds. In all other builds the methods below are compiled instead,
        // matching the surface used by callers (MethodReloadCommands, InPlaceReloadProcessor)
        // so the project still builds, and returning a clear "not supported" failure at runtime.
        const string NotSupportedMessage =
            "Method reload compilation is only supported on Desktop development builds (Windows/Mac/Linux).";

        /// <summary>
        /// Method reload source compilation (in-place editing) not supported on this build.
        /// </summary>
        /// <param name="sourceCode">Unused on this build.</param>
        /// <param name="baseFileName">Unused on this build.</param>
        /// <param name="assemblyDir">Unused on this build.</param>
        /// <param name="emitPdb">Unused on this build.</param>
        /// <param name="documentPath">Unused on this build.</param>
        /// <param name="interpreterMethods">Unused on this build.</param>
        /// <returns>A "Platform Not Supported" failure.</returns>
        internal static MethodReloadCompileResult CompileSourceCodeOnMainThread(string sourceCode, string baseFileName, string assemblyDir = null, bool emitPdb = false, string documentPath = null, InterpreterOverrideSet interpreterMethods = null)
        {
            return MethodReloadCompileResult.Failure("Platform Not Supported", NotSupportedMessage);
        }

        /// <summary>
        /// Override compilation to bytes (push path) not supported on this build (no Roslyn).
        /// </summary>
        /// <param name="transformedCode">Unused on this build.</param>
        /// <param name="baseFileName">Unused on this build.</param>
        /// <param name="ilBytes">Always null on this build.</param>
        /// <param name="diagnostics">Always contains the "not supported" message on this build.</param>
        /// <param name="documentPath">Unused on this build.</param>
        /// <param name="preprocessorSymbols">Unused on this build.</param>
        /// <returns>Always false on this build.</returns>
        internal static bool TryCompileOverrideBytes(string transformedCode, string baseFileName, out byte[] ilBytes, out List<string> diagnostics, string documentPath = null, string[] preprocessorSymbols = null)
        {
            ilBytes = null;
            diagnostics = new List<string> { NotSupportedMessage };
            return false;
        }

        /// <summary>
        /// Method reload cleanup not supported on this build.
        /// </summary>
        /// <param name="assemblyDir">Unused on this build.</param>
        /// <returns>A failure result explaining method reload isn't supported on this build.</returns>
        public static MethodReloadCleanupResult CleanupMethodReloadDlls(string assemblyDir = null)
        {
            return new MethodReloadCleanupResult
            {
                Success = false,
                Message = NotSupportedMessage,
                ExecutionTimeMs = 0
            };
        }
#endif
    }

    /// <summary>
    /// The override set an interpreter-backed compile registers instead of Assembly.Load: the
    /// reloaded type plus its surviving method names. Passing one to
    /// <see cref="MethodReloadCompiler.CompileSourceCodeOnMainThread"/> selects the interpreter path.
    /// </summary>
    internal class InterpreterOverrideSet
    {
        public string TypeName { get; set; }
        public List<string> MethodNames { get; set; }
    }

    /// <summary>
    /// Result from method reload compilation operation.
    /// </summary>
    class MethodReloadCompileResult
    {
        /// <summary>Whether compilation succeeded.</summary>
        public bool IsSuccess { get; set; }
        /// <summary>Name of the compiled method-reload assembly.</summary>
        public string AssemblyName { get; set; }
        /// <summary>Path the assembly was written to, if saved to disk.</summary>
        public string OutputPath { get; set; }
        /// <summary>Method ids registered as overrides.</summary>
        public List<string> RegisteredMethods { get; set; } = new List<string>();
        /// <summary>How long compilation took, in milliseconds.</summary>
        public long ExecutionTimeMs { get; set; }
        /// <summary>Error message, if compilation failed.</summary>
        public string Error { get; set; }
        /// <summary>Additional error details for debugging.</summary>
        public string ErrorDetails { get; set; }
        /// <summary>Compiler diagnostics (errors/warnings), if any.</summary>
        public List<string> Diagnostics { get; set; } = new List<string>();

        /// <summary>Create a successful compile result.</summary>
        /// <param name="assemblyName">Name of the compiled assembly.</param>
        /// <param name="outputPath">Path the assembly was written to, if saved to disk.</param>
        /// <param name="registeredMethods">Method ids registered as overrides.</param>
        /// <param name="executionTimeMs">How long compilation took, in milliseconds.</param>
        /// <returns>A successful result.</returns>
        public static MethodReloadCompileResult Success(string assemblyName, string outputPath, List<string> registeredMethods, long executionTimeMs)
        {
            return new MethodReloadCompileResult
            {
                IsSuccess = true,
                AssemblyName = assemblyName,
                OutputPath = outputPath,
                RegisteredMethods = registeredMethods,
                ExecutionTimeMs = executionTimeMs
            };
        }

        /// <summary>Create a failed compile result.</summary>
        /// <param name="error">Error message.</param>
        /// <param name="errorDetails">Additional error details for debugging.</param>
        /// <param name="executionTimeMs">How long compilation took, in milliseconds.</param>
        /// <param name="diagnostics">Compiler diagnostics (errors/warnings), if any.</param>
        /// <returns>A failed result.</returns>
        public static MethodReloadCompileResult Failure(string error, string errorDetails, long executionTimeMs = 0, List<string> diagnostics = null)
        {
            return new MethodReloadCompileResult
            {
                IsSuccess = false,
                Error = error,
                ErrorDetails = errorDetails,
                ExecutionTimeMs = executionTimeMs,
                Diagnostics = diagnostics ?? new List<string>()
            };
        }
    }

    /// <summary>
    /// Result from method reload cleanup operation.
    /// </summary>
    class MethodReloadCleanupResult
    {
        /// <summary>Whether cleanup succeeded.</summary>
        public bool Success { get; set; }
        /// <summary>Paths of the deleted files.</summary>
        public List<string> DeletedFiles { get; set; } = new List<string>();
        /// <summary>Human-readable summary or error message.</summary>
        public string Message { get; set; }
        /// <summary>How long cleanup took, in milliseconds.</summary>
        public long ExecutionTimeMs { get; set; }
    }

}