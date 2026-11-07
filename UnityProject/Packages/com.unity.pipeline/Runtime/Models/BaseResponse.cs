using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Base class for all responses. Serve also as the base class for all errors.
    /// </summary>
    [Serializable]
    public class BaseResponse
    {
        /// <summary>
        /// Response message if no error
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Machine-actionable warnings for the caller: things the server dropped, coerced, or
        /// wants the agent to correct (e.g. an unrecognized option value), without failing the
        /// request. Null when there is nothing to say, and omitted from the JSON in EVERY mode
        /// (an empty warnings array carries no information — attribute-level gating, so this
        /// holds on all endpoints and in verbose serialization too). (AUTHAPI-21)
        /// </summary>
        [JsonProperty("warnings", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Warnings { get; set; }

        /// <summary>
        /// Error message if the command failed.
        /// </summary>
        [JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>
        /// Additional error details for debugging.
        /// </summary>
        [JsonProperty("errorDetails")]
        public string ErrorDetails { get; set; }

        /// <summary>
        /// When was the response created.
        /// </summary>
        /// <remarks>
        /// On the /api/exec wire this is emitted only in verbose mode — the lean/verbose gating lives
        /// in <c>ExecResponseSerializer</c>, NOT here, so every other serialization of this type
        /// (status endpoints, logs, tests) keeps the field. (AUTHAPI-21)
        /// </remarks>
        [JsonProperty("executedAt")]
        public DateTime ExecutedAt { get; set; }

        /// <summary>
        /// Create a failed execution response.
        /// </summary>
        /// <param name="error">Error message.</param>
        /// <param name="errorDetails">Additional error details for debugging.</param>
        /// <returns>A response with <see cref="Error"/>/<see cref="ErrorDetails"/> set.</returns>
        public static BaseResponse Failure(string error, string errorDetails)
        {
            return new BaseResponse
            {
                ExecutedAt = DateTime.UtcNow,
                Error = error,
                ErrorDetails = errorDetails
            };
        }
    }
}