using System;
using System.IO;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.MethodReload;
using Unity.Pipeline.Models;
using UnityEngine;

namespace Unity.Pipeline.Runtime.Commands
{
    /// <summary>
    /// CLI commands for method reload operations: apply [MethodReload] edits from a source file, inspect
    /// the registry, and clean up saved reload assemblies.
    /// </summary>
    static class MethodReloadCommands
    {
        const string Group = "script.methodreload";

        [CliGroupCommand(Group, "Compile and apply in-place [MethodReload] edits from a source file", MainThreadRequired = true, Tags = new[] { "scripts/methodreload" })]
        public static MethodReloadResponse Reload(
            [CliArg("filename", "Source file containing [MethodReload] methods (e.g. Assets/Scripts/Player.cs)", Required = true)] string filename,
            [CliArg("timeout", "Compilation timeout in seconds")] double timeoutInSeconds = 60,
            [CliArg("assembly-dir", "Directory to save compiled assemblies to disk (optional, default is in-memory only)")] string assemblyDir = null,
            [CliArg("pdb", "Emit debug symbols (portable PDB) mapped to the original source so breakpoints bind in your editor. Compiles unoptimized.")] bool pdb = false)
        {
            return ReloadFileCore(CommandNames.ScriptMethodreloadReload, filename, PipelineTimeout.FromSeconds(timeoutInSeconds), assemblyDir, pdb, useInterpreter: false);
        }

        [CliGroupCommand(Group, "Compile in-place [MethodReload] edits and run them through the IlInterpreter VM in this process instead of Assembly.Load (IL2CPP-safe; only a minimal host API + the target type are available)", MainThreadRequired = true, Tags = new[] { "scripts/methodreload" })]
        public static MethodReloadResponse ReloadEditor(
            [CliArg("filename", "Source file containing [MethodReload] methods (e.g. Assets/Scripts/Player.cs)", Required = true)] string filename,
            [CliArg("timeout", "Compilation timeout in seconds")] double timeoutInSeconds = 60,
            [CliArg("assembly-dir", "Directory to save compiled assemblies to disk (optional, default is in-memory only)")] string assemblyDir = null,
            [CliArg("pdb", "Emit debug symbols (portable PDB) mapped to the original source so breakpoints bind in your editor. Compiles unoptimized.")] bool pdb = false)
        {
            return ReloadFileCore(CommandNames.ScriptMethodreloadReloadEditor, filename, PipelineTimeout.FromSeconds(timeoutInSeconds), assemblyDir, pdb, useInterpreter: true);
        }

        /// <summary>Shared body of <c>reload_file</c> and <c>reload_file_editor_interpreter</c>:
        /// the two commands differ only in the backend the reloaded methods run on.</summary>
        private static MethodReloadResponse ReloadFileCore(string commandName, string filename, TimeSpan timeout, string assemblyDir, bool pdb, bool useInterpreter)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Roslyn ships only to the Editor and desktop players, and a player has no source
                // files to resolve anyway: answer up front, before the file lookup below turns the
                // real reason into a misleading "File Not Found".
                if (!SourceReloadSupport.Available)
                {
                    stopwatch.Stop();
                    return MethodReloadResponse.CmdFailure(
                        "Platform Not Supported",
                        SourceReloadSupport.NotSupportedMessage,
                        stopwatch.ElapsedMilliseconds);
                }

                if (string.IsNullOrWhiteSpace(filename))
                {
                    stopwatch.Stop();
                    return MethodReloadResponse.CmdFailure(
                        "Bad Request",
                        "Filename parameter is required and cannot be empty",
                        stopwatch.ElapsedMilliseconds);
                }

                if (!filename.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    filename += ".cs";
                }

                var fullPath = ResolveSourceFilePath(filename);
                if (string.IsNullOrEmpty(fullPath))
                {
                    stopwatch.Stop();
                    return MethodReloadResponse.CmdFailure(
                        "File Not Found",
                        $"Could not locate source file: {filename}",
                        stopwatch.ElapsedMilliseconds);
                }

                // In a Player build, the file must be inside the project's build-baked roots. The
                // project layout cannot be resolved from a running build, so this is skipped in the
                // editor (which resolves files against the live project).
                if (!Application.isEditor)
                {
                    var scopeCheck = ValidateReloadPath(fullPath, MethodReloadRegistry.AllowedReloadRoots);
                    if (!scopeCheck.IsValid)
                    {
                        stopwatch.Stop();
                        return MethodReloadResponse.CmdFailure(
                            scopeCheck.Error,
                            scopeCheck.ErrorDetails,
                            stopwatch.ElapsedMilliseconds);
                    }
                }

                Debug.Log($"MethodReload: Executing {commandName} command: {fullPath}, timeout: {PipelineTimeout.Describe(timeout)}, assembly-dir: {assemblyDir ?? "in-memory"}, pdb: {pdb}, interpreter: {useInterpreter}");

                var task = InPlaceReloadProcessor.ProcessSourceFileAsync(fullPath, assemblyDir, pdb, useInterpreter);
                task.Wait();
                var result = task.Result;

                stopwatch.Stop();

                if (result.Success)
                {
                    // Everything matches the compiled baseline: an honest no-op success, not
                    // "reload successful with 0 methods" against a null assembly name. Reverting
                    // stale overrides (the edit was undone) is part of the outcome — say so
                    // instead of unregistering silently.
                    if (result.AllUpToDate)
                    {
                        var upToDate = MethodReloadResponse.CmdSuccess(
                            string.Empty,
                            $"Up to date: all {result.UpToDateMethods.Count} [MethodReload] method(s) match the compiled baseline — nothing reloaded" +
                            (result.RevertedMethods.Count > 0
                                ? $"; reverted {result.RevertedMethods.Count} stale override(s): {string.Join(", ", result.RevertedMethods)}"
                                : "") + ".",
                            result.RegisteredMethods,
                            stopwatch.ElapsedMilliseconds);
                        upToDate.Diagnostics = result.CompilationDiagnostics;
                        return upToDate;
                    }

                    // "Compiled OK but bound nothing" is a failed reload in practice: the compile
                    // succeeded but every method was skipped downstream (e.g. an interpreter-unsupported
                    // construct, or no RuntimePipelineDriver registered the target). Surface the
                    // reasons from CompilationDiagnostics as a failure instead of a silent success.
                    if (result.RegisteredMethods.Count == 0 && result.ExtractedMethods.Count > 0)
                    {
                        var details = result.CompilationDiagnostics != null && result.CompilationDiagnostics.Count > 0
                            ? "Compiled, but no methods were applied:\n- " + string.Join("\n- ", result.CompilationDiagnostics)
                            : "Compiled, but no methods were applied. Is the target component in the scene " +
                              "and in play mode (with the runtime Pipeline driver active)?";
                        return MethodReloadResponse.CmdFailure(
                            "No Methods Applied",
                            details,
                            stopwatch.ElapsedMilliseconds,
                            result.CompilationDiagnostics);
                    }

                    MethodReloadRegistry.InvokeReloadCallbacks(result.RegisteredMethods);

                    var response = MethodReloadResponse.CmdSuccess(
                        result.AssemblyName,
                        $"In-place method reload successful: {result.AssemblyName} with {result.RegisteredMethods.Count} methods" +
                        (result.RevertedMethods.Count > 0
                            ? $"; reverted {result.RevertedMethods.Count} stale override(s): {string.Join(", ", result.RevertedMethods)}"
                            : ""),
                        result.RegisteredMethods,
                        stopwatch.ElapsedMilliseconds);
                    // Surface partial-skip reasons (some methods bound, others skipped) on success too.
                    response.Diagnostics = result.CompilationDiagnostics;
                    return response;
                }

                return MethodReloadResponse.CmdFailure(
                    "In-Place Reload Failed",
                    result.ErrorMessage ?? "In-place method reload processing failed",
                    stopwatch.ElapsedMilliseconds,
                    result.CompilationDiagnostics);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Debug.LogError($"MethodReload: {commandName} command failed: {ex.Message}");
                Debug.LogError($"MethodReload: Stack trace: {ex.StackTrace}");

                return MethodReloadResponse.CmdFailure(
                    "Execution Failed",
                    ex.ToString(),
                    stopwatch.ElapsedMilliseconds);
            }
        }

        // Not RuntimeOnly: it prunes method-reload DLLs in a caller-supplied directory and clears
        // registry statics, neither of which needs a driver. Reload targets are discovered on
        // demand (see RuntimePipelineDriver.EnsureDiscoveredMethodReloadMethods), so the editor
        // reloads this cleans up after happen with no driver in the process at all.
        [CliGroupCommand(Group, "Remove old method reload DLL versions and clear registry", MainThreadRequired = true, Tags = new[] { "scripts/methodreload" })]
        public static MethodReloadResponse Cleanup(
            [CliArg("assembly-dir", "Directory containing assemblies to cleanup", Required = true)] string assemblyDir,
            [CliArg("force-domain-reload", "Force Unity domain reload after cleanup")] bool forceDomainReload = true)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                Debug.Log($"MethodReload: Executing {CommandNames.ScriptMethodreloadCleanup} command for directory: {assemblyDir}");

                // Execute cleanup
                var cleanupResult = MethodReloadCompiler.CleanupMethodReloadDlls(assemblyDir);

                stopwatch.Stop();

                if (cleanupResult.Success)
                {
                    var message = $"Cleanup successful: {cleanupResult.Message}";
                    if (cleanupResult.DeletedFiles.Count > 0)
                    {
                        message += $" Files removed: {string.Join(", ", cleanupResult.DeletedFiles)}";
                    }

                    // Force domain reload if requested (interrupts gameplay but ensures clean state)
                    if (forceDomainReload && Application.isEditor)
                    {
#if UNITY_EDITOR
                        Debug.Log("MethodReload: Requesting Unity domain reload to clean memory state");
                        UnityEditor.EditorUtility.RequestScriptReload();
                        message += " Unity domain reload requested.";
#endif
                    }

                    Debug.Log($"MethodReload: {CommandNames.ScriptMethodreloadCleanup} completed successfully in {stopwatch.ElapsedMilliseconds}ms");

                    return MethodReloadResponse.CmdSuccess(
                        "cleanup_completed",
                        message,
                        cleanupResult.DeletedFiles,
                        stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    return MethodReloadResponse.CmdFailure(
                        "Cleanup Failed",
                        cleanupResult.Message,
                        stopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Debug.LogError($"MethodReload: {CommandNames.ScriptMethodreloadCleanup} command failed: {ex.Message}");

                return MethodReloadResponse.CmdFailure(
                    "Execution Failed",
                    ex.ToString(),
                    stopwatch.ElapsedMilliseconds);
            }
        }

        // Not RuntimeOnly: this only reads registry statics, and it is the command that answers
        // "did my reload bind?" — the question the missing-target diagnostics raise. Gating it on a
        // driver made it unreachable in exactly the editor session that needed it.
        [CliGroupCommand(Group, "Show current method reload registry status and statistics", MainThreadRequired = true, Tags = new[] { "scripts/methodreload" })]
        public static MethodReloadResponse Status()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Get current registry stats
                var stats = MethodReloadRegistry.GetStats();

                stopwatch.Stop();

                var statusMessage = $"Method Reload Status - " +
                    $"Reloadable Methods: {stats.ReloadableMethodCount}, " +
                    $"Active Overrides: {stats.ActiveOverrideCount}, " +
                    $"Loaded Types: {stats.LoadedTypeCount}";

                Debug.Log($"MethodReload: {statusMessage}");

                return MethodReloadResponse.CmdSuccess(
                    "status_retrieved",
                    statusMessage,
                    stats.ActiveOverrideIds,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Debug.LogError($"MethodReload: {CommandNames.ScriptMethodreloadStatus} command failed: {ex.Message}");

                return MethodReloadResponse.CmdFailure(
                    "Execution Failed",
                    ex.ToString(),
                    stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Validate that a resolved file path lives inside one of the allowed project roots and
        /// exists on disk. "In scope" means under the Assets folder or a loaded package's location.
        /// The path is normalized (collapsing any <c>..</c> segments) before the scope check, so a
        /// path that escapes the allowed roots via traversal is rejected. A null or empty root set
        /// matches nothing, so the path is rejected as out of scope. This is a security boundary —
        /// it prevents files outside the project from being compiled and injected into the assembly.
        /// </summary>
        public static PathValidationResult ValidateReloadPath(string resolvedPath, System.Collections.Generic.IReadOnlyCollection<string> allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return PathValidationResult.Invalid("Bad Request", "Resolved file path is empty.");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(resolvedPath);
            }
            catch (Exception ex)
            {
                return PathValidationResult.Invalid("Bad Request", $"Invalid path: {ex.Message}");
            }

            var underAnyRoot = false;
            if (allowedRoots != null)
            {
                foreach (var root in allowedRoots)
                {
                    if (string.IsNullOrWhiteSpace(root))
                    {
                        continue;
                    }

                    string fullRoot;
                    try
                    {
                        fullRoot = Path.GetFullPath(root);
                    }
                    catch
                    {
                        continue;
                    }

                    if (IsUnderDirectory(fullPath, fullRoot))
                    {
                        underAnyRoot = true;
                        break;
                    }
                }
            }

            if (!underAnyRoot)
            {
                return PathValidationResult.Invalid(
                    "Out Of Project Scope",
                    $"Method reload only accepts files inside the project's baked roots (Assets/ or a loaded " +
                    $"package). '{fullPath}' is outside the project scope and will not be compiled.");
            }

            if (!File.Exists(fullPath))
            {
                return PathValidationResult.Invalid("File Not Found", $"Source file not found: {fullPath}");
            }

            return PathValidationResult.Valid();
        }

        /// <summary>
        /// True when <paramref name="path"/> is contained within <paramref name="directory"/>.
        /// Both are expected to be normalized absolute paths. A trailing separator is appended to
        /// the directory so that a sibling such as "AssetsExtra" is not treated as being under "Assets".
        /// </summary>
        private static bool IsUnderDirectory(string path, string directory)
        {
            var prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            // Windows file systems are case-insensitive; Unix file systems are case-sensitive.
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return path.StartsWith(prefix, comparison);
        }

        /// <summary>
        /// Resolve source file path from various possible locations.
        /// </summary>
        private static string ResolveSourceFilePath(string filename)
        {
            if (Path.IsPathRooted(filename) && File.Exists(filename))
            {
                return filename;
            }

            var potentialPaths = new[]
            {
                Path.Combine("Assets", filename),
                Path.Combine("Assets", "Scripts", filename),
                filename // Project root
            };

            foreach (var path in potentialPaths)
            {
                if (File.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }

            return null;
        }

    }

    /// <summary>
    /// Response model for method reload CLI commands.
    /// Provides consistent response format for all method reload operations.
    /// </summary>
    class MethodReloadResponse : CommandExecutionResponse
    {
        /// <summary>
        /// Assembly name or operation identifier for successful operations.
        /// </summary>
        public string AssemblyName { get; set; }

        /// <summary>
        /// List of registered method IDs or files processed.
        /// </summary>
        public System.Collections.Generic.List<string> Items { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Compilation or processing diagnostics.
        /// </summary>
        public System.Collections.Generic.List<string> Diagnostics { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Create a successful method reload response.
        /// </summary>
        public static MethodReloadResponse CmdSuccess(string assemblyName, string message, System.Collections.Generic.List<string> items, long executionTimeMs)
        {
            return new MethodReloadResponse
            {
                Success = true,
                AssemblyName = assemblyName,
                Message = message,
                Items = items ?? new System.Collections.Generic.List<string>(),
                ExecutionTimeMs = executionTimeMs,
                ExecutedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Create a failed method reload response.
        /// </summary>
        public static MethodReloadResponse CmdFailure(string error, string errorDetails, long executionTimeMs, System.Collections.Generic.List<string> diagnostics = null)
        {
            return new MethodReloadResponse
            {
                Success = false,
                Error = error,
                ErrorDetails = errorDetails,
                ExecutionTimeMs = executionTimeMs,
                Diagnostics = diagnostics ?? new System.Collections.Generic.List<string>(),
                ExecutedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Result of validating a method reload source path. <see cref="Error"/> is a short category and
    /// <see cref="ErrorDetails"/> the human-readable explanation, matching the shape consumed by
    /// <see cref="MethodReloadResponse.CmdFailure"/>.
    /// </summary>
    class PathValidationResult
    {
        public bool IsValid { get; private set; }
        public string Error { get; private set; }
        public string ErrorDetails { get; private set; }

        public static PathValidationResult Valid()
        {
            return new PathValidationResult { IsValid = true };
        }

        public static PathValidationResult Invalid(string error, string errorDetails)
        {
            return new PathValidationResult { IsValid = false, Error = error, ErrorDetails = errorDetails };
        }
    }
}