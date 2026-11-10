using System;
using Newtonsoft.Json;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// Agent-supplied handle that references an existing Unity object — an asset or a
    /// loaded/scene object. Supply ONE form; <see cref="Unity.Pipeline.Editor.Authoring.ObjectResolver"/>
    /// tries them in order: globalId, path, guid (+ optional fileId), instanceId, hierarchyPath.
    /// </summary>
    [Serializable]
    [JsonConverter(typeof(ObjectRefConverter))]
    public class ObjectRef
    {
        /// <summary>Canonical GlobalObjectId string (addresses assets and scene objects uniformly).</summary>
        [JsonProperty("globalId")]
        public string GlobalId { get; set; }

        /// <summary>Project-relative asset path, e.g. "Assets/Foo/Bar.prefab".</summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        /// <summary>Asset GUID.</summary>
        [JsonProperty("guid")]
        public string Guid { get; set; }

        /// <summary>Local file id, used with <see cref="Guid"/> to address a sub-asset.</summary>
        [JsonProperty("fileId")]
        public long? FileId { get; set; }

        /// <summary>Instance id of a loaded/scene object.</summary>
        [JsonProperty("instanceId")]
        public ObjectId? InstanceId { get; set; }

        /// <summary>Scene hierarchy path, e.g. "/Root/Child/Leaf".</summary>
        [JsonProperty("hierarchyPath")]
        public string HierarchyPath { get; set; }

        /// <summary>True when none of the reference forms above are set.</summary>
        [JsonIgnore]
        public bool IsEmpty =>
            string.IsNullOrEmpty(GlobalId) &&
            string.IsNullOrEmpty(Path) &&
            string.IsNullOrEmpty(Guid) &&
            InstanceId == null &&
            string.IsNullOrEmpty(HierarchyPath);

        /// <summary>The reference in its most specific set form, for diagnostics/logging.</summary>
        /// <returns>The formatted string.</returns>
        public override string ToString()
        {
            if (!string.IsNullOrEmpty(GlobalId)) return GlobalId;
            if (!string.IsNullOrEmpty(Path)) return Path;
            if (!string.IsNullOrEmpty(Guid)) return FileId.HasValue ? $"guid:{Guid}:{FileId}" : $"guid:{Guid}";
            if (InstanceId.HasValue) return $"instanceId:{InstanceId}";
            if (!string.IsNullOrEmpty(HierarchyPath)) return HierarchyPath;
            return "<empty>";
        }
    }
}
