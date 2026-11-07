using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Pipeline.Models;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.Telemetry;
using UnityEngine;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline.Runtime.Commands
{
    /// <summary>
    /// Command for evaluating C# code dynamically using Roslyn compilation.
    /// Requires security token for authorization.
    /// </summary>
    static class CodeEvalCommand
    {
        const string Group = "script";

        const string k_EvalFileDeprecation = "script eval_file is deprecated: use script eval --file.";

        [CliGroupCommand(Group, "Evaluate C# code, given inline or read from a .cs file, using Roslyn's script mode", MainThreadRequired = true, Tags = new[] { "scripts/eval" })]
        public static async Task<EvalResponse> Eval(
            [CliArg("code", "C# code to evaluate. Give either code or file.")] string code = null,
            [CliArg("file", "Path to a .cs file to evaluate instead of code. Relative paths resolve against the project root.")] string file = null,
            [CliArg("timeout", "Timeout in seconds")] double timeoutInSeconds = 30,
            [CliArg("dry-run", "Compile only: return diagnostics; nothing is loaded or run.")] bool dryRun = false,
            [CliArg("auto-import", "On a type or name that is not found, add a using directive for the one namespace that declares it and compile again; the reply lists the usings added.")] bool autoImport = false,
            [CliArg("internals", "Editor only: compile under an assembly name that Unity's modules grant internal access, so their internal APIs are callable.")] bool internals = false)
        {
            var hasCode = !string.IsNullOrWhiteSpace(code);
            var hasFile = !string.IsNullOrWhiteSpace(file);

            if (hasCode && hasFile)
                return RejectedEval(CommandNames.ScriptEval, code, "Give either code or file, not both");

            if (RejectInternals(internals, Application.isEditor) is { } internalsError)
                return RejectedEval(CommandNames.ScriptEval, code, internalsError);

            if (hasFile)
                return await EvaluateFile(file, CommandNames.ScriptEval, dryRun, autoImport, internals);

            if (!hasCode)
                return RejectedEval(CommandNames.ScriptEval, code, "Give the code to evaluate, or --file with a path to a .cs file");

            return await EvaluateSource(code, CommandNames.ScriptEval, dryRun, autoImport, internals);
        }

        [CliGroupCommand(Group, "Deprecated: use script eval --file. Evaluate C# code read from a .cs file on disk", MainThreadRequired = true, Tags = new[] { "scripts/eval" })]
        public static async Task<EvalResponse> EvalFile(
            [CliArg("file", "Path to a .cs file to evaluate. Relative paths resolve against the project root.", Required = true)] string file,
            [CliArg("timeout", "Timeout in seconds")] double timeoutInSeconds = 30,
            [CliArg("dry-run", "Compile only: return diagnostics; nothing is loaded or run.")] bool dryRun = false,
            [CliArg("auto-import", "On a type or name that is not found, add a using directive for the one namespace that declares it and compile again; the reply lists the usings added.")] bool autoImport = false,
            [CliArg("internals", "Editor only: compile under an assembly name that Unity's modules grant internal access, so their internal APIs are callable.")] bool internals = false)
        {
            var response = RejectInternals(internals, Application.isEditor) is { } internalsError
                ? RejectedEval(CommandNames.ScriptEvalFile, null, internalsError)
                : await EvaluateFile(file, CommandNames.ScriptEvalFile, dryRun, autoImport, internals);
            (response.Warnings ??= new List<string>()).Add(k_EvalFileDeprecation);
            return response;
        }

        private static async Task<EvalResponse> EvaluateFile(string file, string commandName, bool dryRun, bool autoImport, bool internals)
        {
            if (string.IsNullOrWhiteSpace(file))
                return RejectedEval(commandName, null, "File parameter is required and cannot be empty");

            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return RejectedEval(commandName, null, "File must have a .cs extension");

            var path = ResolveFilePath(file);
            if (!File.Exists(path))
                return RejectedEval(commandName, null, $"File not found: {path}");

            string code;
            try
            {
                code = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return RejectedEval(commandName, null, $"Failed to read file: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(code))
                return RejectedEval(commandName, code, $"File is empty: {path}");

            return await EvaluateSource(code, commandName, dryRun, autoImport, internals);
        }

        /// <summary>
        /// Why <c>internals</c> can't be honored here, or null when it can. Only the Editor
        /// supports it.
        /// </summary>
        internal static string RejectInternals(bool internals, bool isEditor) =>
            internals && !isEditor ? "internals is only available in the Editor" : null;

        /// <summary>
        /// In the Editor a relative path resolves against the project root (the parent of
        /// Assets/); in a Player, against the working directory.
        /// </summary>
        private static string ResolveFilePath(string file)
        {
            if (!Application.isEditor || Path.IsPathRooted(file))
                return file;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return projectRoot == null ? file : Path.Combine(projectRoot, file);
        }

        /// <summary>
        /// Reject an invocation at validation time AND record it: a rejected eval is still an eval
        /// invocation, and skipping telemetry here would undercount report_evals' errorCount /
        /// errorRate for exactly the mistakes an agent makes most (empty code, wrong path).
        /// </summary>
        private static EvalResponse RejectedEval(string command, string code, string errorDetails)
        {
            // Guarded like the RecordInBackground call in EvaluateSource: telemetry must never
            // affect eval behavior, so a failure in its synchronous prelude (ResolveDirectory,
            // the write-chain lock) can't turn a clean Bad Request into an unhandled exception.
            try
            {
                EvalUsageTelemetry.RecordInBackground(command, code ?? string.Empty, success: false,
                    error: "Bad Request", executionTimeMs: 0);
            }
            catch
            {
                // Telemetry must never affect eval behavior.
            }

            return EvalResponse.EvalFailure(EvalResponse.InvalidArgsCode, "Bad Request", errorDetails);
        }

        /// <summary>
        /// Shared evaluation path: compiles and executes the given C# source and returns the
        /// response. Inline code and code read from a file both funnel their source string into
        /// here. Each invocation is recorded to local eval-usage telemetry
        /// (AUTHAPI-29) on a best-effort basis before returning.
        /// </summary>
        private static async Task<EvalResponse> EvaluateSource(string code, string commandName, bool dryRun, bool autoImport, bool internals)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var details = CommandContext.Current?.EvalDetails;
            EvalResponse response;

            try
            {
                // We're on the main thread (MainThreadRequired = true); the script's awaits resume here.
                var result = await EvalCodeCompiler.CompileAndExecuteAsync(code, null, dryRun, autoImport, internals, details);

                stopwatch.Stop();

                // Update execution time
                if (result != null)
                {
                    result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                }

                response = result ?? EvalResponse.EvalFailure(
                    EvalResponse.InternalErrorCode,
                    "Unknown Error",
                    "Compilation returned null result",
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Debug.LogError($"Pipeline: Eval command failed: {ex.Message}");
                Debug.LogError($"Pipeline: Stack trace: {ex.StackTrace}");

                response = EvalResponse.EvalFailure(
                    EvalResponse.InternalErrorCode,
                    "Execution Failed",
                    ex.ToString(),
                    stopwatch.ElapsedMilliseconds);
            }

            // Best-effort local telemetry (editor-only; a no-op in players): fingerprint + shape only
            // by default (no raw source unless the explicit opt-in is set). The eval primitives and
            // the current settings are snapshotted here on the main thread, then the Roslyn parse and
            // file append run on a background task — fire-and-forget, zero latency added to the
            // response. RecordInBackground swallows its own errors, but guard here too so an
            // unexpected failure can never turn a good eval into a failed command.
            try
            {
                EvalUsageTelemetry.RecordInBackground(
                    commandName,
                    code,
                    response.Success,
                    response.Error,
                    response.ExecutionTimeMs ?? stopwatch.ElapsedMilliseconds);
            }
            catch
            {
                // Telemetry must never affect eval behavior.
            }

            return response;
        }
    }
}