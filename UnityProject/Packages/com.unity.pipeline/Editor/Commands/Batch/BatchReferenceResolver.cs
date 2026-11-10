using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Editor.Commands.Batch
{
    /// <summary>
    /// Resolves cross-operation result references in a batch operation's parameters (AUTHAPI-27).
    ///
    /// A string value that begins with a single <c>'$'</c> is a reference of the form
    /// <c>"$&lt;selector&gt;"</c> or <c>"$&lt;selector&gt;.&lt;jsonPath&gt;"</c>, where
    /// <c>&lt;selector&gt;</c> names an earlier operation by its <c>id</c> or its 0-based index and
    /// <c>&lt;jsonPath&gt;</c> is a Newtonsoft SelectToken path into that op's result
    /// (e.g. <c>"$0.instanceId"</c>, <c>"$createHead.components[0].instanceId"</c>). A leading
    /// <c>"$$"</c> escapes a literal <c>'$'</c>. References are backward-only: a selector must resolve
    /// to an operation strictly before the current one.
    ///
    /// References are resolved as WHOLE values (the entire string must be the reference), and the
    /// substituted JSON token preserves its type — so a numeric <c>instanceId</c> stays a number and
    /// deserializes into the target parameter (e.g. an <c>ObjectRef</c>) exactly as a literal would.
    /// The walk recurses through nested objects and arrays, so a reference can appear anywhere in the
    /// parameter tree.
    /// </summary>
    internal static class BatchReferenceResolver
    {
        /// <summary>
        /// Validate the reference topology of <paramref name="parameters"/> without substituting any
        /// values (used by dry run). Throws <see cref="ArgumentException"/> on an unknown selector,
        /// a forward/self reference, or a malformed reference.
        /// </summary>
        public static void Validate(JToken parameters, int currentIndex,
            IReadOnlyList<BatchOperationInput> operations, IReadOnlyDictionary<string, int> idToIndex)
        {
            if (parameters == null)
                return;
            // No DeepClone: in validate mode (null resultsByIndex) TransformString always returns the
            // original token, so Walk never reassigns a child — the walk is read-only.
            Walk(parameters, currentIndex, operations, idToIndex, null, null);
        }

        /// <summary>
        /// Return a copy of <paramref name="parameters"/> with every reference replaced by the
        /// referenced value from <paramref name="resultsByIndex"/>. Throws
        /// <see cref="ArgumentException"/> on an unknown selector, a forward/self reference, a
        /// reference whose path does not resolve, or a reference into an operation that failed, was
        /// skipped, or returned no result (per-op outcomes come from <paramref name="outcomes"/>).
        /// The input token is not mutated.
        /// </summary>
        public static JObject Resolve(JObject parameters, int currentIndex,
            IReadOnlyList<BatchOperationInput> operations, IReadOnlyDictionary<string, int> idToIndex,
            JToken[] resultsByIndex, IReadOnlyList<BatchOperationResult> outcomes)
        {
            if (parameters == null)
                return null;
            var clone = (JObject)parameters.DeepClone();
            return (JObject)Walk(clone, currentIndex, operations, idToIndex, resultsByIndex, outcomes);
        }

        /// <summary>
        /// Recursively transform a token tree. <paramref name="resultsByIndex"/> is null in validate
        /// mode (topology check only) and non-null in resolve mode (value substitution).
        /// </summary>
        private static JToken Walk(JToken node, int currentIndex,
            IReadOnlyList<BatchOperationInput> operations, IReadOnlyDictionary<string, int> idToIndex,
            JToken[] resultsByIndex, IReadOnlyList<BatchOperationResult> outcomes)
        {
            switch (node.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)node).Properties().ToList())
                    {
                        var replaced = Walk(prop.Value, currentIndex, operations, idToIndex, resultsByIndex, outcomes);
                        if (!ReferenceEquals(replaced, prop.Value))
                            prop.Value = replaced;
                    }
                    return node;
                case JTokenType.Array:
                    var arr = (JArray)node;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var child = arr[i];
                        var replaced = Walk(child, currentIndex, operations, idToIndex, resultsByIndex, outcomes);
                        if (!ReferenceEquals(replaced, child))
                            arr[i] = replaced;
                    }
                    return node;
                case JTokenType.String:
                    return TransformString((string)node, node, currentIndex, operations, idToIndex, resultsByIndex, outcomes);
                default:
                    return node;
            }
        }

        private static JToken TransformString(string s, JToken original, int currentIndex,
            IReadOnlyList<BatchOperationInput> operations, IReadOnlyDictionary<string, int> idToIndex,
            JToken[] resultsByIndex, IReadOnlyList<BatchOperationResult> outcomes)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '$')
                return original; // not a reference

            // "$$..." escapes a literal '$'. Only substitute when actually resolving; validation
            // has nothing to check for a literal.
            if (s.Length >= 2 && s[1] == '$')
                return resultsByIndex == null ? original : (JToken)new JValue(s.Substring(1));

            var rest = s.Substring(1);
            var dot = rest.IndexOf('.');
            var selector = dot < 0 ? rest : rest.Substring(0, dot);
            var path = dot < 0 ? null : rest.Substring(dot + 1);

            if (string.IsNullOrEmpty(selector))
                throw new ArgumentException(
                    $"Invalid reference '{s}': missing operation selector after '$'. " +
                    "Use \"$<id-or-index>.<field>\", or \"$$\" for a literal '$'.");

            var targetIndex = ResolveSelector(selector, operations, idToIndex);
            if (targetIndex < 0)
                throw new ArgumentException(
                    $"Reference '{s}' points to unknown operation '{selector}'. " +
                    "A reference must name an earlier operation by its id or 0-based index.");
            if (targetIndex >= currentIndex)
                throw new ArgumentException(
                    $"Reference '{s}' is a forward reference to operation '{selector}' (index {targetIndex}); " +
                    "only backward references to earlier operations are allowed.");

            // Validate mode: topology is fine, and there is no result to substitute yet.
            if (resultsByIndex == null)
                return original;

            // A failed or skipped operation has no result to reference — substituting null silently
            // would feed a bogus value into this op's parameters. Error explicitly instead. In the
            // real Execute() loop only Success=false outcomes are observable here (under
            // on-error=continue — earlier failures don't stop the batch); Skipped entries are
            // appended AFTER the loop, so the "was skipped" wording is defensive, reachable only
            // when the resolver is driven directly (unit tests / future callers).
            var outcome = outcomes != null && targetIndex < outcomes.Count ? outcomes[targetIndex] : null;
            if (outcome != null && !outcome.Success)
            {
                var reason = outcome.Skipped == true ? "was skipped" : "failed";
                throw new ArgumentException(
                    $"Reference '{s}' cannot be resolved: operation '{selector}' {reason}; " +
                    "its result cannot be referenced.");
            }

            // Distinct from failure: the op SUCCEEDED but produced nothing to reference (void/null
            // result, or a result that could not be captured as a token).
            var opResult = resultsByIndex[targetIndex];
            if (opResult == null || opResult.Type == JTokenType.Null)
                throw new ArgumentException(
                    $"Reference '{s}' cannot be resolved: operation '{selector}' returned no result.");

            var picked = string.IsNullOrEmpty(path) ? opResult : opResult.SelectToken(path);
            if (picked == null)
                throw new ArgumentException(
                    $"Reference '{s}' did not resolve: operation '{selector}' result has no field '{path}'.");

            return picked.DeepClone();
        }

        /// <summary>
        /// Resolve a selector to an operation index: an id match takes precedence over a numeric
        /// index. Returns -1 when the selector names no operation.
        /// </summary>
        private static int ResolveSelector(string selector,
            IReadOnlyList<BatchOperationInput> operations, IReadOnlyDictionary<string, int> idToIndex)
        {
            if (idToIndex != null && idToIndex.TryGetValue(selector, out var byId))
                return byId;
            if (int.TryParse(selector, out var idx) && idx >= 0 && idx < operations.Count)
                return idx;
            return -1;
        }
    }
}
