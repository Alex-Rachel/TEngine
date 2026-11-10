using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Response from code evaluation commands containing results, output, and diagnostics.
    /// </summary>
    [Serializable]
    class EvalResponse :CommandExecutionResponse
    {
        internal const string InvalidArgsCode = "INVALID_COMMAND_ARGS";
        internal const string CompilationErrorCode = "COMPILATION_ERROR";
        internal const string RuntimeExceptionCode = "RUNTIME_EXCEPTION";
        internal const string UnsupportedPlatformCode = "UNSUPPORTED_PLATFORM";
        internal const string InternalErrorCode = "EVAL_INTERNAL_ERROR";

        /// <summary>
        /// Console output captured during execution.
        /// </summary>
        [JsonProperty("output")]
        public string Output { get; set; }

        /// <summary>
        /// Compilation diagnostics (errors, warnings) from Roslyn.
        /// </summary>
        [JsonProperty("diagnostics")]
        public List<DiagnosticInfo> Diagnostics { get; set; } = new List<DiagnosticInfo>();

        /// <summary>
        /// The using directives auto-import added so the code compiled; omitted when it added none
        /// or was not asked for.
        /// </summary>
        [JsonProperty("addedUsings", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> AddedUsings { get; set; }

        /// <summary>
        /// Create a successful evaluation response.
        /// </summary>
        /// <param name="result">The evaluated expression's result.</param>
        /// <param name="output">Console output captured during execution.</param>
        /// <param name="executionTimeMs">How long execution took, in milliseconds.</param>
        /// <param name="diagnostics">Compilation diagnostics (errors, warnings) from Roslyn.</param>
        /// <returns>A successful response wrapping <paramref name="result"/>.</returns>
        public static EvalResponse EvalSuccess(object result, string output = null, long executionTimeMs = 0, List<DiagnosticInfo> diagnostics = null)
        {
            return new EvalResponse
            {
                Success = true,
                Command = CommandNames.ScriptEval,
                Result = result,
                Output = output,
                ExecutionTimeMs = executionTimeMs,
                Diagnostics = diagnostics ?? new List<DiagnosticInfo>(),
                ExecutedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Create a failed evaluation response. It fails the reply's envelope too: HTTP 500 for
        /// <see cref="InternalErrorCode"/>, 400 for every other code.
        /// </summary>
        /// <param name="errorCode">Machine-readable failure category, one of this class's codes.</param>
        /// <param name="error">Error message.</param>
        /// <param name="errorDetails">Additional error details for debugging.</param>
        /// <param name="executionTimeMs">How long execution took, in milliseconds.</param>
        /// <param name="diagnostics">Compilation diagnostics (errors, warnings) from Roslyn.</param>
        /// <returns>A failed response.</returns>
        public static EvalResponse EvalFailure(string errorCode, string error, string errorDetails = null, long executionTimeMs = 0, List<DiagnosticInfo> diagnostics = null)
        {
            return new EvalResponse
            {
                Success = false,
                Command = CommandNames.ScriptEval,
                Error = error,
                ErrorDetails = errorDetails,
                ErrorCode = errorCode,
                FailureStatusCode = errorCode == InternalErrorCode ? 500 : 400,
                ExecutionTimeMs = executionTimeMs,
                Diagnostics = diagnostics ?? new List<DiagnosticInfo>(),
                ExecutedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Compilation diagnostic information from Roslyn.
    /// </summary>
    [Serializable]
    public class DiagnosticInfo
    {
        /// <summary>
        /// Severity level: "error", "warning", "info".
        /// </summary>
        [JsonProperty("severity")]
        public string Severity { get; set; }

        /// <summary>
        /// Diagnostic message text.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Line number (0-based).
        /// </summary>
        [JsonProperty("line")]
        public int Line { get; set; }

        /// <summary>
        /// Column number (0-based).
        /// </summary>
        [JsonProperty("column")]
        public int Column { get; set; }

        /// <summary>
        /// Diagnostic identifier (e.g., "CS1002").
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// User source file the diagnostic maps to through a <c>#line</c> directive, or null when
        /// the compiled source carries no mapping (eval snippets, generated scaffolding).
        /// </summary>
        [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore)]
        public string File { get; set; }

        /// <summary>
        /// 1-based line in <see cref="File"/>; null when <see cref="File"/> is null.
        /// </summary>
        [JsonProperty("fileLine", NullValueHandling = NullValueHandling.Ignore)]
        public int? FileLine { get; set; }

        /// <summary>
        /// Trimmed text of the compiled source line the diagnostic points at. For method reload that
        /// is the generated override source, which is what actually failed to compile.
        /// </summary>
        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
        public string Source { get; set; }

        /// <summary>
        /// The exact source text the diagnostic spans, such as the name a CS0246 could not find.
        /// </summary>
        [JsonIgnore]
        internal string SpanText { get; set; }
    }
}