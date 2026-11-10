using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// What <c>/api/exec</c> returns when a raw request names a group and nothing after it
    /// (<c>unity command animation</c>): the group's direct subgroups and direct commands.
    /// </summary>
    internal sealed class GroupListing
    {
        [JsonProperty("group")]
        public string Group { get; set; }

        /// <summary>Direct subgroups, as full group names (<c>animation.clip</c>), sorted.</summary>
        [JsonProperty("subgroups")]
        public List<string> Subgroups { get; set; }

        /// <summary>Commands directly in the group, sorted by full name.</summary>
        [JsonProperty("commands")]
        public List<GroupListingCommand> Commands { get; set; }
    }

    /// <summary>One command in a <see cref="GroupListing"/>.</summary>
    internal sealed class GroupListingCommand
    {
        /// <summary>The command's full name (<c>animation.animator add_state</c>), the same field name the exec envelope uses.</summary>
        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
