using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Pipeline.Models;

namespace Unity.Pipeline.Editor.Commands.Assets
{
    /// <summary>
    /// Structured result for the <c>find_assets</c> command (CLI-191): a list of matched assets, each
    /// described as the canonical <see cref="AuthoringResult"/> (path / GUID / type) so an agent can
    /// feed any entry straight back into another command as an <see cref="ObjectRef"/>.
    ///
    /// Lives in the Editor command assembly (rather than Runtime/Models) because find_assets is an
    /// Editor-only command and the foundation Runtime/Models are shared/frozen for parallel work.
    /// </summary>
    [System.Serializable]
    class FindAssetsResult
    {
        /// <summary>The AssetDatabase filter string that was executed (for diagnostics).</summary>
        [JsonProperty("filter")]
        public string Filter { get; set; }

        /// <summary>Number of assets returned (after the limit is applied).</summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>True when the result was capped by the <c>limit</c> argument.</summary>
        [JsonProperty("truncated")]
        public bool Truncated { get; set; }

        /// <summary>The matched assets, each with path / GUID / type.</summary>
        [JsonProperty("assets")]
        public List<AuthoringResult> Assets { get; set; } = new List<AuthoringResult>();
    }
}
