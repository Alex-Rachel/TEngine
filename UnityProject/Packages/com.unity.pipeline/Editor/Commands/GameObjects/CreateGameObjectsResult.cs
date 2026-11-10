using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Pipeline.Models;

namespace Unity.Pipeline.Editor.Commands.GameObjects
{
    /// <summary>
    /// Structured result for the batch <c>create_gameobjects</c> command (CLI-223). WHY a dedicated
    /// envelope rather than a bare array: it carries the created <see cref="Count"/> alongside the
    /// list so an agent can confirm the whole batch landed in one round-trip without inspecting array
    /// length, and mirrors <see cref="FindGameObjectsResult"/> so the GameObject command surface stays
    /// consistent. Each entry is the same canonical <see cref="AuthoringResult"/> identity returned by
    /// the single-object <c>create_gameobject</c>, so any created object can be fed straight back as an
    /// <see cref="ObjectRef"/> in a follow-up call.
    /// </summary>
    sealed class CreateGameObjectsResult
    {
        /// <summary>Number of GameObjects created in the batch.</summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>Canonical identities of the created GameObjects, in creation order.</summary>
        [JsonProperty("gameObjects")]
        public List<AuthoringResult> GameObjects { get; set; } = new List<AuthoringResult>();
    }
}
