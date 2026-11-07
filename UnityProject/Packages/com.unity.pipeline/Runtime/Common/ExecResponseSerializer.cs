using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Unity.Pipeline.Models;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Single source of truth for how <c>/api/exec</c> replies are serialized (AUTHAPI-21). Centralized
    /// here so the server and the envelope byte-size regression test share one contract.
    ///
    /// Every mode serializes with <see cref="Formatting.None"/> (compact): agents consume the JSON, and
    /// an MCP client can pretty-print for a human if ever needed — the wire form never needs indentation.
    ///
    /// Two independent request flags select the mode:
    ///
    /// * <c>verbose</c> — envelope metadata. Off (default): the always-on metadata (executedAt,
    ///   command, executionTimeMs) is stripped from every <see cref="BaseResponse"/>-derived object
    ///   in the graph (the outer envelope AND responses nested as <c>result</c>, e.g. eval's) —
    ///   except that a reply to a raw <c>argv</c>/<c>commandLine</c> request keeps its <c>command</c>
    ///   (see <see cref="CommandExecutionResponse.EchoCommand"/>) — and
    ///   the envelope's own null keys (message/error/errorDetails when null, result on failure — all
    ///   redundant given <c>success</c>) are omitted, so a minimal success is just
    ///   <c>{"success":true,"result":...}</c>. On: full fidelity for debugging/correlation.
    /// * <c>omitNulls</c> — payload null fidelity. Off (default): the command's own <c>result</c>
    ///   payload keeps explicit nulls — an agent must be able to distinguish "this field is null"
    ///   from "this field doesn't exist / I got the name wrong". On: null keys are dropped
    ///   everywhere, for callers that already know the result schema and want the bytes back
    ///   (e.g. bulk list reads where a null column repeats hundreds of times).
    ///
    /// One invariant holds in EVERY mode: a successful reply always carries <c>result</c>, even
    /// when its value is null — a null result on success is the command's actual value (e.g. a
    /// detail="value" read of a genuinely-null field), never a droppable envelope null.
    ///
    /// The two axes are deliberately separate booleans: envelope metadata and payload null fidelity
    /// are orthogonal concerns, and each combination is meaningful.
    ///
    /// A reply embeds the command's output as it is: the HTTP transport asks for it as
    /// <see cref="SerializeResult(object, bool, bool)"/> gives it for the reply's flags, the same JSON
    /// this serializer gives the result inside the envelope, so the result is serialized once.
    ///
    /// The gating lives in a contract resolver scoped to THIS serializer, not in ShouldSerialize*
    /// hooks on the models: serializing these types anywhere else (other endpoints, logs, tests) is
    /// unaffected, and Serialize never mutates the response object.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class ExecResponseSerializer
    {
        /// <summary>
        /// Lean-envelope gating for every <see cref="BaseResponse"/>-derived object in the graph
        /// (nested results included): strips the always-on metadata and omits the envelope's own
        /// null keys — WITHOUT touching null handling on non-envelope payload objects, which keep
        /// the serializer-level default. One shared instance per settings object:
        /// DefaultContractResolver caches resolved contracts per resolver instance (thread-safe),
        /// so the reflection cost is paid once per type, not per request.
        /// </summary>
        private class LeanEnvelopeContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);

                // DeclaringType guards against unrelated same-named members (e.g.
                // CommandExecutionRequest.Command must keep serializing).
                if (member.DeclaringType != null && typeof(BaseResponse).IsAssignableFrom(member.DeclaringType))
                {
                    switch (member.Name)
                    {
                        case nameof(BaseResponse.ExecutedAt):
                        case nameof(CommandExecutionResponse.ExecutionTimeMs):
                            property.ShouldSerialize = _ => false;
                            break;

                        // Lean mode drops the command echo, except on a reply to a raw
                        // command-line request: there the caller named the command by position
                        // and the echo is how it learns what it resolved to (EchoCommand).
                        case nameof(CommandExecutionResponse.Command):
                            property.ShouldSerialize = instance =>
                                instance is CommandExecutionResponse response && response.EchoCommand;
                            break;

                        // The ONLY keys lean mode may null-drop: genuinely redundant given
                        // `success` (error/errorDetails are null exactly on success, message on
                        // both paths when there is nothing to say). Everything else declared on
                        // a BaseResponse-DERIVED type is payload (TestExecutionResponse's
                        // summary/results/filterApplied, EvalResponse.Output, …) and keeps
                        // explicit nulls — a dropped payload key is indistinguishable from a
                        // nonexistent or misspelled one; e.g. an async run_tests poller could
                        // not tell "not ready yet" from "not a field". Enumerated per key
                        // instead of type-wide for exactly that reason.
                        case nameof(BaseResponse.Message):
                        case nameof(BaseResponse.Error):
                        case nameof(BaseResponse.ErrorDetails):
                            property.NullValueHandling = NullValueHandling.Ignore;
                            break;

                        case nameof(CommandExecutionResponse.Result):
                            // A null result on SUCCESS is not a redundant envelope null — it is
                            // the command's actual value (e.g. get_serialized_fields
                            // detail="value" reading a genuinely-null field), so a successful
                            // reply always carries "result", explicitly null when null (Include
                            // also defeats the omitNulls serializer settings). On failure the
                            // result is structurally meaningless and stays omitted.
                            property.NullValueHandling = NullValueHandling.Include;
                            property.ShouldSerialize = instance =>
                                !(instance is CommandExecutionResponse response) || response.Success
                                || response.KeepsResultOnFailure;
                            break;
                    }
                }

                return property;
            }
        }

        private static readonly JsonSerializerSettings m_LeanSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
            ContractResolver = new LeanEnvelopeContractResolver(),
        };

        private static readonly JsonSerializerSettings m_LeanOmitNullsSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new LeanEnvelopeContractResolver(),
        };

        private static readonly JsonSerializerSettings m_VerboseSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
        };

        private static readonly JsonSerializerSettings m_VerboseOmitNullsSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>
        /// Serialize an exec response body with the contract selected by the request's flags (see
        /// the class doc for the two axes). Pure: the body is never mutated — the mode is carried
        /// entirely by the serializer settings, so serializing the same instance repeatedly (in
        /// any order) always yields the same JSON per mode.
        /// </summary>
        public static string Serialize(BaseResponse body, bool verbose, bool omitNulls = false)
        {
            return JsonConvert.SerializeObject(body, SettingsFor(verbose, omitNulls));
        }

        static JsonSerializerSettings SettingsFor(bool verbose, bool omitNulls)
        {
            return verbose
                ? (omitNulls ? m_VerboseOmitNullsSettings : m_VerboseSettings)
                : (omitNulls ? m_LeanOmitNullsSettings : m_LeanSettings);
        }

        /// <summary>
        /// Serialize what a command returned, as the command's output: the JSON a default
        /// (lean, nulls kept) reply carries as its <c>result</c>.
        /// </summary>
        public static string SerializeResult(object result)
        {
            return SerializeResult(result, verbose: false, omitNulls: false);
        }

        /// <summary>
        /// Serialize what a command returned, as the command's output for a reply with these flags:
        /// the JSON that reply carries as its <c>result</c>, so the HTTP transport embeds it byte
        /// for byte. The settings are the envelope's, which apply to the whole graph.
        /// </summary>
        public static string SerializeResult(object result, bool verbose, bool omitNulls)
        {
            return JsonConvert.SerializeObject(result, SettingsFor(verbose, omitNulls));
        }

        /// <summary>
        /// <see cref="SerializeResult(object, bool, bool)"/>'s JSON as a token, dates kept as strings.
        /// </summary>
        public static JToken ResultToToken(object result, bool verbose, bool omitNulls)
        {
            using (var reader = new JsonTextReader(new StringReader(SerializeResult(result, verbose, omitNulls))) { DateParseHandling = DateParseHandling.None })
                return JToken.ReadFrom(reader);
        }

        /// <summary>
        /// The <c>result</c> of a reply, from a command's output: embedded as it is, or with its
        /// null keys dropped when <paramref name="omitNulls"/> asks for it after the output was
        /// written, as a job's poll does.
        /// </summary>
        public static JToken ResultFromOutput(string output, bool omitNulls)
        {
            if (!omitNulls)
                return new JRaw(output);

            // Read as written: a date-like string stays the string the command returned.
            using (var reader = new JsonTextReader(new StringReader(output)) { DateParseHandling = DateParseHandling.None })
            {
                var token = JToken.ReadFrom(reader);
                RemoveNullProperties(token);
                return token;
            }
        }

        /// <summary>Drops the null keys of <paramref name="token"/>'s objects, at any depth.</summary>
        public static void RemoveNullProperties(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var property in obj.Properties().ToList())
                    {
                        if (property.Value.Type == JTokenType.Null)
                            property.Remove();
                        else
                            RemoveNullProperties(property.Value);
                    }
                    break;
                case JArray array:
                    foreach (var item in array)
                        RemoveNullProperties(item);
                    break;
            }
        }
    }
}
