using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.Pipeline.Editor.Commands.Scenes
{
    /// <summary>
    /// Serializable snapshot of an open scene's GameObject tree, returned by
    /// <c>get_scene_hierarchy</c>.
    ///
    /// WHY a dedicated tree type (rather than reusing <see cref="Unity.Pipeline.Models.AuthoringResult"/>):
    /// the hierarchy is meant to be the *input* to GameObject-targeting commands. Every node carries
    /// a stable <see cref="SceneHierarchyNode.InstanceId"/> and a <see cref="SceneHierarchyNode.HierarchyPath"/>,
    /// either of which is directly accepted by
    /// <see cref="Unity.Pipeline.Models.ObjectRef"/> / <see cref="Unity.Pipeline.Editor.Authoring.ObjectResolver"/>.
    /// That lets an agent fetch the tree once and then address any node without a second round-trip.
    /// The shape is kept intentionally small and JSON-friendly (no UnityEngine references) so it
    /// survives the HTTP boundary cleanly.
    /// </summary>
    [Serializable]
    class SceneHierarchy
    {
        /// <summary>Scene name (the file name without extension for a saved scene).</summary>
        [JsonProperty("sceneName")]
        public string SceneName { get; set; }

        /// <summary>Project-relative scene asset path, or empty for an unsaved scene.</summary>
        [JsonProperty("scenePath")]
        public string ScenePath { get; set; }

        /// <summary>Whether this scene currently has unsaved changes.</summary>
        [JsonProperty("isDirty")]
        public bool IsDirty { get; set; }

        /// <summary>Whether this scene is the active scene.</summary>
        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        /// <summary>Root GameObjects of the scene, each with their descendants.</summary>
        [JsonProperty("roots")]
        public List<SceneHierarchyNode> Roots { get; set; } = new List<SceneHierarchyNode>();
    }

    /// <summary>
    /// One GameObject node in a <see cref="SceneHierarchy"/>, with enough identity to be used as an
    /// <see cref="Unity.Pipeline.Models.ObjectRef"/> handle (instanceId or hierarchyPath) by
    /// follow-up GameObject commands.
    /// </summary>
    [Serializable]
    class SceneHierarchyNode
    {
        /// <summary>GameObject name.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Runtime instance id of the GameObject. Stable for the lifetime of the loaded scene and
        /// directly usable as <c>ObjectRef.instanceId</c>.
        /// </summary>
        [JsonProperty("instanceId")]
        public ObjectId InstanceId { get; set; }

        /// <summary>
        /// Slash-delimited scene path (e.g. "/Root/Child/Leaf"), directly usable as
        /// <c>ObjectRef.hierarchyPath</c>.
        /// </summary>
        [JsonProperty("hierarchyPath")]
        public string HierarchyPath { get; set; }

        /// <summary>Whether the GameObject is active in the hierarchy.</summary>
        [JsonProperty("activeSelf")]
        public bool ActiveSelf { get; set; }

        /// <summary>Component type names attached to this GameObject (e.g. "Transform", "Camera").</summary>
        [JsonProperty("components")]
        public List<string> Components { get; set; } = new List<string>();

        /// <summary>Child nodes.</summary>
        [JsonProperty("children")]
        public List<SceneHierarchyNode> Children { get; set; } = new List<SceneHierarchyNode>();
    }
}
