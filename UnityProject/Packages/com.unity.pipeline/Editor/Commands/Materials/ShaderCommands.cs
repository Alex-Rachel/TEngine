using System;
using System.Collections.Generic;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.Pipeline.Editor.Commands.Materials
{
    /// <summary>
    /// Shader discovery / introspection commands (CLI-213): <c>list_shaders</c> so an agent can pick a
    /// valid shader name, and <c>get_shader_properties</c> so it knows a shader's settable property
    /// names / types / ranges / texture dimensions / flags before authoring a material.
    ///
    /// Property introspection uses <see cref="Shader"/> instance APIs exclusively:
    /// <c>GetPropertyCount</c>, <c>GetPropertyName</c>, <c>GetPropertyType</c>,
    /// <c>GetPropertyDescription</c>, <c>GetPropertyRangeLimits</c>,
    /// <c>GetPropertyTextureDimension</c>, and <c>GetPropertyFlags</c>. The reported type set is
    /// <see cref="UnityEngine.Rendering.ShaderPropertyType"/>: Color, Vector, Float, Range, Texture, Int.
    /// </summary>
    static class ShaderCommands
    {
        const string Group = "material";

        [CliGroupCommand(Group,
            "Discover available shaders so an agent can pick a valid name for " + CommandNames.MaterialSetProperties + " / " + CommandNames.AssetCreate + ". Returns [{ name, assetPath|null, isBuiltin, isSupported }].",
            Tags = new[] { "materials/shaders" })]
        public static List<ShaderInfo> ListShaders(
            [CliArg("filter", "Case-insensitive substring matched against the shader name (e.g. \"URP\", \"Lit\").")] string filter = null,
            [CliArg("include-builtin", "Include built-in/engine shaders (those with no project asset path). Default true.")] bool includeBuiltin = true,
            [CliArg("limit", "Maximum number of shaders to return (default 200).")] int limit = 200)
        {
            if (limit <= 0)
                limit = 200;

            var results = new List<ShaderInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // AssetDatabase.FindAssets("t:Shader") enumerates project + package shaders (each with an
            // asset path); ShaderUtil.GetAllShaderInfo() additionally surfaces built-in engine shaders
            // that have no asset path (e.g. "Standard", "Sprites/Default"). Merge both, dedup by name.
            foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                    continue;

                if (!seen.Add(shader.name))
                    continue;

                if (!PassesFilter(shader.name, filter))
                    continue;

                results.Add(new ShaderInfo
                {
                    Name = shader.name,
                    AssetPath = path,
                    IsBuiltin = false,
                    IsSupported = shader.isSupported,
                });

                if (results.Count >= limit)
                    return results;
            }

            if (includeBuiltin)
            {
                // GetAllShaderInfo() enumerates every shader the editor knows about, including built-in
                // engine shaders that have no project/package asset path. Any name NOT already added by
                // the FindAssets("t:Shader") pass above is a built-in (assetPath null). `info.supported`
                // already reflects Shader::IsSupported(), so we don't re-run Shader.Find per entry.
                foreach (var info in ShaderUtil.GetAllShaderInfo())
                {
                    if (!seen.Add(info.name))
                        continue;

                    if (!PassesFilter(info.name, filter))
                        continue;

                    results.Add(new ShaderInfo
                    {
                        Name = info.name,
                        AssetPath = null,
                        IsBuiltin = true,
                        IsSupported = info.supported,
                    });

                    if (results.Count >= limit)
                        break;
                }
            }

            return results;
        }

        [CliGroupCommand(Group,
            "Introspect a shader's declared property list (name, description, type Color|Vector|Float|Range|TexEnv|Int, range, textureDimension, flags). Provide 'shader' (by name) OR 'material' (read the shader off that material).",
            Tags = new[] { "materials/shaders" })]
        public static ShaderPropertiesResult GetShaderProperties(
            [CliArg("shader", "Shader name (e.g. \"Universal Render Pipeline/Lit\"). Provide this OR 'material'.")] string shader = null,
            [CliArg("material", "Reference to a material to read the shader from instead of naming it. Provide this OR 'shader'.")] ObjectRef material = null)
        {
            Shader resolved;

            if (!string.IsNullOrWhiteSpace(shader))
            {
                resolved = Shader.Find(shader);
                if (resolved == null)
                    throw new ShaderNotFoundException(
                        $"Shader '{shader}' not found. Shader.Find only resolves shaders that are compiled/included in the build (or referenced by a material/scene). Use {CommandNames.MaterialListShaders} to discover valid names.");
            }
            else if (material != null && !material.IsEmpty)
            {
                var mat = MaterialCommands.ResolveMaterial(material, out _);
                resolved = mat.shader;
                if (resolved == null)
                    throw new ArgumentException($"Material '{material}' has no shader assigned.");
            }
            else
            {
                throw new ArgumentException("Provide either 'shader' (a shader name) or 'material' (a material reference).");
            }

            var result = new ShaderPropertiesResult { Shader = resolved.name };

            int count = resolved.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                var propType = resolved.GetPropertyType(i);
                var entry = new ShaderPropertyInfo
                {
                    Name = resolved.GetPropertyName(i),
                    Description = resolved.GetPropertyDescription(i),
                    Type = MaterialValueConverter.TypeLabel(propType),
                    Flags = DescribeFlags(resolved.GetPropertyFlags(i)),
                };

                if (propType == ShaderPropertyType.Range)
                {
                    var limits = resolved.GetPropertyRangeLimits(i);
                    entry.Range = new MaterialRange
                    {
                        Min = limits.x,
                        Max = limits.y,
                    };
                }

                if (propType == ShaderPropertyType.Texture)
                    entry.TextureDimension = resolved.GetPropertyTextureDimension(i).ToString();

                result.Properties.Add(entry);
            }

            return result;
        }

        private static bool PassesFilter(string name, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;
            return name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Decompose a <see cref="ShaderPropertyFlags"/> bitmask into its individual flag names (the
        /// enum is a [Flags] type). <c>None</c> is reported as an empty list rather than ["None"].
        /// </summary>
        private static List<string> DescribeFlags(ShaderPropertyFlags flags)
        {
            var list = new List<string>();
            if (flags == ShaderPropertyFlags.None)
                return list;

            foreach (ShaderPropertyFlags flag in Enum.GetValues(typeof(ShaderPropertyFlags)))
            {
                if (flag == ShaderPropertyFlags.None)
                    continue;
                if ((flags & flag) == flag)
                    list.Add(flag.ToString());
            }

            return list;
        }
    }
}
