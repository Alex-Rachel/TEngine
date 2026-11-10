using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Pipeline.Models;

namespace Unity.Pipeline.Editor.Commands.Materials
{
    /// <summary>
    /// Result of <c>get_material_properties</c> (CLI-213): the material's shader, render queue, enabled
    /// keywords, and the full list of shader properties with their current values.
    /// </summary>
    [Serializable]
    class MaterialPropertiesResult
    {
        [JsonProperty("assetPath")]
        public string AssetPath { get; set; }

        /// <summary>Shader name, e.g. "Universal Render Pipeline/Lit".</summary>
        [JsonProperty("shader")]
        public string Shader { get; set; }

        /// <summary>Render queue; -1 means "inherit from shader".</summary>
        [JsonProperty("renderQueue")]
        public int RenderQueue { get; set; }

        [JsonProperty("enabledKeywords")]
        public List<string> EnabledKeywords { get; set; } = new List<string>();

        [JsonProperty("properties")]
        public List<MaterialPropertyValue> Properties { get; set; } = new List<MaterialPropertyValue>();
    }

    /// <summary>A single shader property of a material with its current value.</summary>
    [Serializable]
    class MaterialPropertyValue
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>"Float" | "Range" | "Color" | "Vector" | "Texture" | "Int".</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// number | [r,g,b,a] | [x,y,z,w] | { texture: ObjectRef } | null, per the property type.
        /// </summary>
        [JsonProperty("value")]
        public object Value { get; set; }

        /// <summary>Min/max for a Range property; null otherwise.</summary>
        [JsonProperty("range", NullValueHandling = NullValueHandling.Ignore)]
        public MaterialRange Range { get; set; }
    }

    /// <summary>Inclusive min/max bounds of a Range shader property.</summary>
    [Serializable]
    class MaterialRange
    {
        [JsonProperty("min")]
        public float Min { get; set; }

        [JsonProperty("max")]
        public float Max { get; set; }
    }

    /// <summary>
    /// Result of <c>set_material_properties</c> (CLI-213): the canonical <see cref="AuthoringResult"/>
    /// identity of the edited material, extended with which properties applied vs. were unknown (with a
    /// reason), and the material's (possibly reassigned) shader name.
    /// </summary>
    [Serializable]
    class SetMaterialPropertiesResult : AuthoringResult
    {
        /// <summary>Shader name after any reassignment.</summary>
        [JsonProperty("shader")]
        public string Shader { get; set; }

        /// <summary>Property names (and keyword/renderQueue tokens) that were applied.</summary>
        [JsonProperty("applied")]
        public List<string> Applied { get; set; } = new List<string>();

        /// <summary>
        /// Inputs that could not be applied, each as "name: reason" (unknown property name, or a type
        /// mismatch like "_Metallic: expected Float, got array").
        /// </summary>
        [JsonProperty("unknown")]
        public List<string> Unknown { get; set; } = new List<string>();
    }

    /// <summary>A single discovered shader entry from <c>list_shaders</c>.</summary>
    [Serializable]
    class ShaderInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Project asset path for a project shader; null for a built-in/engine shader.</summary>
        [JsonProperty("assetPath")]
        public string AssetPath { get; set; }

        [JsonProperty("isBuiltin")]
        public bool IsBuiltin { get; set; }

        /// <summary>Reflects <c>Shader.isSupported</c> for the active render pipeline.</summary>
        [JsonProperty("isSupported")]
        public bool IsSupported { get; set; }
    }

    /// <summary>Result of <c>get_shader_properties</c> (CLI-213): a shader's declared property list.</summary>
    [Serializable]
    class ShaderPropertiesResult
    {
        [JsonProperty("shader")]
        public string Shader { get; set; }

        [JsonProperty("properties")]
        public List<ShaderPropertyInfo> Properties { get; set; } = new List<ShaderPropertyInfo>();
    }

    /// <summary>A single declared shader property, introspected via <c>ShaderUtil</c>.</summary>
    [Serializable]
    class ShaderPropertyInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>ShaderUtil property description (the UI label).</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>"Color" | "Vector" | "Float" | "Range" | "TexEnv" | "Int".</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>Min/max for a Range property; null otherwise.</summary>
        [JsonProperty("range", NullValueHandling = NullValueHandling.Ignore)]
        public MaterialRange Range { get; set; }

        /// <summary>"Tex2D" | "Cube" | "Tex3D" | "Tex2DArray" for a TexEnv property; null otherwise.</summary>
        [JsonProperty("textureDimension", NullValueHandling = NullValueHandling.Ignore)]
        public string TextureDimension { get; set; }

        /// <summary>Property flags, e.g. "HideInInspector", "Normal", "HDR", "NoScaleOffset".</summary>
        [JsonProperty("flags")]
        public List<string> Flags { get; set; } = new List<string>();
    }
}
