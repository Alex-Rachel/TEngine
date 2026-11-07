using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Models;

namespace Unity.Pipeline.Editor.Commands.GameObjects
{
    /// <summary>
    /// Structured result for <c>get_component_properties</c> / <c>set_component_properties</c>.
    /// WHY it pairs the component identity with the property map: the caller gets back both the
    /// canonical <see cref="AuthoringResult"/> handle (to reference the component again) and the
    /// current serialized values in one round trip, which also makes set return the committed state
    /// for a read-after-write check.
    /// </summary>
    sealed class ComponentPropertiesResult
    {
        /// <summary>Canonical identity of the component the properties belong to.</summary>
        [JsonProperty("component")]
        public AuthoringResult Component { get; set; }

        /// <summary>Map of serialized property name to its JSON value.</summary>
        [JsonProperty("properties")]
        public Dictionary<string, JToken> Properties { get; set; } = new Dictionary<string, JToken>();
    }
}
