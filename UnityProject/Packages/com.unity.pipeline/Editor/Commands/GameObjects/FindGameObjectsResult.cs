using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Pipeline.Models;

namespace Unity.Pipeline.Editor.Commands.GameObjects
{
    /// <summary>
    /// Structured result for <c>find_gameobjects</c>. WHY a dedicated envelope rather than a bare
    /// array: it carries the match <see cref="Count"/> alongside the list so an agent can branch on
    /// "found / not found / ambiguous" without inspecting array length, and leaves room to add query
    /// metadata later without breaking the shape. Each entry is the same canonical
    /// <see cref="AuthoringResult"/> identity returned by the single-object commands, so a result can
    /// be fed straight back as an <see cref="ObjectRef"/> in a follow-up call.
    /// </summary>
    sealed class FindGameObjectsResult
    {
        /// <summary>Number of GameObjects that matched the query.</summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>Canonical identities of the matching GameObjects.</summary>
        [JsonProperty("gameObjects")]
        public List<AuthoringResult> GameObjects { get; set; } = new List<AuthoringResult>();
    }
}
