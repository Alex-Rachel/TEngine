using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Commands.Materials
{
    /// <summary>
    /// Materials authoring commands (CLI-213): read and write a material's shader, shader properties
    /// (floats, colors, vectors, textures, ints), keywords, and render queue.
    ///
    /// These sit on the CLI-190 authoring foundation:
    /// - the material is addressed with an <see cref="ObjectRef"/> resolved by <see cref="ObjectResolver"/>
    ///   and confined to the authoring root (a GUID/globalId handle can't edit a material outside the sandbox),
    /// - texture property values are <see cref="ObjectRef"/>s likewise confined to the root,
    /// - writes are wrapped in an <see cref="AuthoringUndoScope"/> with <see cref="Undo.RecordObject"/>
    ///   (material property changes ARE covered by Undo.RecordObject, unlike AssetDatabase ops), then
    ///   <see cref="EditorUtility.SetDirty"/> + <see cref="AssetDatabase.SaveAssetIfDirty"/> persist them,
    /// - unknown property names and type mismatches are reported in <c>unknown[]</c> rather than failing
    ///   the whole call (as long as at least one input applied).
    ///
    /// Material CREATION already exists in <c>create_asset</c> (CLI-221); this command set is the
    /// read/write surface on top of an existing material.
    /// </summary>
    static class MaterialCommands
    {
        const string Group = "material";

        [CliGroupCommand(Group,
            "Read a material's shader, render queue, enabled keywords, and all shader properties with their current values (Color as [r,g,b,a], Vector as [x,y,z,w], Texture as an object reference).",
            Tags = new[] { "materials" })]
        public static MaterialPropertiesResult GetProperties(
            [CliArg("material", "Reference to the .mat asset (or a loaded material) to read (path / guid / globalId / instanceId).", Required = true)] ObjectRef material)
        {
            var mat = ResolveMaterial(material, out var assetPath);

            var result = new MaterialPropertiesResult
            {
                AssetPath = assetPath,
                Shader = mat.shader != null ? mat.shader.name : null,
                // rawRenderQueue is -1 when the material inherits from the shader and a positive
                // integer when explicitly overridden — returning the raw value preserves the
                // round-trip contract with set_material_properties renderQueue:-1 (inherit).
                RenderQueue = GetRawRenderQueue(mat),
                EnabledKeywords = GetEnabledKeywords(mat),
            };

            var shader = mat.shader;
            if (shader != null)
            {
                // Enumerate the shader's declared properties via the Shader instance APIs (single index
                // space, matching get_shader_properties), so name/type/range-limits always line up.
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    var name = shader.GetPropertyName(i);
                    var propType = shader.GetPropertyType(i);

                    var entry = new MaterialPropertyValue
                    {
                        Name = name,
                        Type = PublicValueType(propType),
                        Value = MaterialValueConverter.ReadValue(mat, name, propType),
                    };

                    if (propType == ShaderPropertyType.Range)
                    {
                        var limits = shader.GetPropertyRangeLimits(i);
                        entry.Range = new MaterialRange
                        {
                            Min = limits.x,
                            Max = limits.y,
                        };
                    }

                    result.Properties.Add(entry);
                }
            }

            return result;
        }

        [CliGroupCommand(Group,
            "Set shader properties on a material (Float/Range/Int=number; Color=[r,g,b,a] or \"#RRGGBBAA\" hex; Vector=[x,y,z,w]; Texture=an object reference or null to clear), optionally reassign the shader, set the render queue, and toggle keywords. Unknown names / type mismatches are reported in unknown[].",
            Tags = new[] { "materials" })]
        public static SetMaterialPropertiesResult SetProperties(
            [CliArg("material", "Reference to the .mat asset (or a loaded material) to edit (path / guid / globalId / instanceId).", Required = true)] ObjectRef material,
            [CliArg("shader", "Reassign the material's shader by name (e.g. \"Standard\", \"Universal Render Pipeline/Lit\", or a Shader Graph shader name). Applied before properties so new property names resolve against the new shader.")] string shader = null,
            [CliArg("properties", "JSON object of shader property name -> value. Names must include the leading underscore (e.g. _BaseColor). Float/Range/Int=number; Color=[r,g,b,a] or hex string; Vector=[x,y,z,w]; Texture=an object reference {guid/path} or null.")] JObject properties = null,
            [CliArg("render-queue", "Explicit render queue, or -1 to inherit from the shader. Omit to leave unchanged.")] int? renderQueue = null,
            [CliArg("enable-keywords", "Shader keywords to enable (e.g. _NORMALMAP, _EMISSION).")] string[] enableKeywords = null,
            [CliArg("disable-keywords", "Shader keywords to disable.")] string[] disableKeywords = null,
            [CliArg("confirm", "Reserved for parity; editing an existing material is non-destructive and undoable, so it is not required.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate the shader, resolve property names and texture refs, and report applied[]/unknown[] without writing anything.")] bool dryRun = false)
        {
            var mat = ResolveMaterial(material, out var assetPath);

            // Resolve the (optional) replacement shader UP FRONT so dry-run validates it too, and a
            // shader_not_found writes nothing (acceptance #9). Shader.Find only finds compiled/included
            // shaders — surface that in the hint.
            Shader newShader = null;
            if (shader != null)
            {
                newShader = Shader.Find(shader);
                if (newShader == null)
                    throw new ShaderNotFoundException(
                        $"Shader '{shader}' not found. Shader.Find only resolves shaders that are compiled/included in the build (or referenced by a material/scene). Use {CommandNames.MaterialListShaders} to discover valid names.");
            }

            var result = new SetMaterialPropertiesResult();

            // ---- dry-run: validate against the would-be shader, never mutate ----------------------
            if (dryRun)
            {
                var effectiveShader = newShader ?? mat.shader;
                ValidateProperties(effectiveShader, properties, result);
                if (renderQueue.HasValue)
                    result.Applied.Add("render-queue");
                AddKeywordTokens(result.Applied, enableKeywords, disableKeywords);

                GuardAtLeastOneApplied(result, shaderReassigned: newShader != null);

                CopyIdentity(mat, assetPath, result);
                result.Shader = effectiveShader != null ? effectiveShader.name : null;
                return result;
            }

            // ---- apply --------------------------------------------------------------------------
            using (new AuthoringUndoScope("Set Material Properties"))
            {
                // Material property/shader/keyword/renderQueue changes are all covered by Undo.RecordObject.
                Undo.RecordObject(mat, "Set Material Properties");

                if (newShader != null)
                    mat.shader = newShader;

                if (properties != null)
                {
                    foreach (var pair in properties)
                    {
                        if (pair.Key == "token") // injected by the server; never a material property.
                            continue;

                        var name = pair.Key;
                        if (!mat.HasProperty(name))
                        {
                            result.Unknown.Add($"{name}: unknown property (not declared by shader '{(mat.shader != null ? mat.shader.name : "<none>")}')");
                            continue;
                        }

                        var propType = GetPropertyType(mat.shader, name);
                        if (propType == null)
                        {
                            // HasProperty was true but the type couldn't be read off the shader (e.g. a
                            // built-in property like unity_Lightmaps not declared in the shader's list).
                            result.Unknown.Add($"{name}: property is not a settable shader property on '{(mat.shader != null ? mat.shader.name : "<none>")}'");
                            continue;
                        }

                        try
                        {
                            MaterialValueConverter.ApplyValue(mat, name, propType.Value, pair.Value);
                            result.Applied.Add(name);
                        }
                        catch (MaterialPropertyTypeMismatch mismatch)
                        {
                            result.Unknown.Add(mismatch.Message);
                        }
                    }
                }

                if (renderQueue.HasValue)
                {
                    mat.renderQueue = renderQueue.Value;
                    result.Applied.Add("render-queue");
                }

                ApplyKeywords(mat, enableKeywords, disableKeywords, result.Applied);

                GuardAtLeastOneApplied(result, shaderReassigned: newShader != null);

                EditorUtility.SetDirty(mat);
                if (!string.IsNullOrEmpty(assetPath))
                    AssetDatabase.SaveAssetIfDirty(mat);
            }

            CopyIdentity(mat, assetPath, result);
            result.Shader = mat.shader != null ? mat.shader.name : null;
            return result;
        }

        /// <summary>
        /// Resolve an <see cref="ObjectRef"/> to a <see cref="Material"/>, confining any on-disk asset to
        /// the authoring root. Returns the project-relative asset path (null for a non-asset/in-memory
        /// material). Throws a clear <see cref="ArgumentException"/> on miss / wrong type / out-of-root.
        /// </summary>
        internal static Material ResolveMaterial(ObjectRef material, out string assetPath)
        {
            if (material == null || material.IsEmpty)
                throw new ArgumentException("'material' is required.");

            if (!ObjectResolver.TryResolve(material, out var obj, out var error))
                throw new ArgumentException($"Could not resolve material: {error}");

            if (!(obj is Material mat))
                throw new ArgumentException($"Reference '{material}' resolved to a {obj.GetType().Name}, not a Material.");

            assetPath = AssetDatabase.GetAssetPath(mat);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var confined = ProjectPaths.Resolve(assetPath, out var confineError);
                if (confined == null)
                    throw new ArgumentException(
                        $"Material '{assetPath}' is outside the authoring root '{ProjectPaths.AuthoringRoot}': {confineError}");
                assetPath = confined;
            }

            return mat;
        }

        /// <summary>
        /// Read the material's enabled keyword set. Uses <see cref="Material.enabledKeywords"/>
        /// (LocalKeyword[]) which is the supported API on the package's min Unity version (Unity
        /// 2022.3); the legacy <c>shaderKeywords</c> string[] is deprecated.
        /// </summary>
        private static int GetRawRenderQueue(Material mat)
        {
#if UNITY_6000_0_OR_NEWER
            return mat.rawRenderQueue;
#else
            // Unity 2022 exposes only the effective queue through Material.renderQueue.
            return new SerializedObject(mat).FindProperty("m_CustomRenderQueue").intValue;
#endif
        }

        private static List<string> GetEnabledKeywords(Material mat)
        {
            var keywords = new List<string>();
            foreach (var keyword in mat.enabledKeywords)
                keywords.Add(keyword.name);
            keywords.Sort(StringComparer.Ordinal);
            return keywords;
        }

        private static void ApplyKeywords(Material mat, string[] enable, string[] disable, List<string> applied)
        {
            // Use the LocalKeyword API (Unity 2022.1+) so that Enable/Disable and the
            // enabledKeywords reader are talking to the same keyword table. The legacy
            // string-based EnableKeyword(string) affects global keywords, whereas
            // mat.enabledKeywords returns LocalKeyword[], so they would disagree on round-trip.
            if (enable != null)
            {
                foreach (var keyword in enable)
                {
                    if (string.IsNullOrWhiteSpace(keyword) || mat.shader == null)
                        continue;
                    mat.EnableKeyword(new LocalKeyword(mat.shader, keyword));
                    applied.Add($"+keyword:{keyword}");
                }
            }

            if (disable != null)
            {
                foreach (var keyword in disable)
                {
                    if (string.IsNullOrWhiteSpace(keyword) || mat.shader == null)
                        continue;
                    mat.DisableKeyword(new LocalKeyword(mat.shader, keyword));
                    applied.Add($"-keyword:{keyword}");
                }
            }
        }

        private static void AddKeywordTokens(List<string> applied, string[] enable, string[] disable)
        {
            if (enable != null)
                foreach (var keyword in enable.Where(k => !string.IsNullOrWhiteSpace(k)))
                    applied.Add($"+keyword:{keyword}");
            if (disable != null)
                foreach (var keyword in disable.Where(k => !string.IsNullOrWhiteSpace(k)))
                    applied.Add($"-keyword:{keyword}");
        }

        /// <summary>
        /// dry-run validation: classify each supplied property as applicable (name resolves AND value
        /// shape matches the declared type) or unknown, WITHOUT mutating the material.
        /// </summary>
        private static void ValidateProperties(Shader shader, JObject properties, SetMaterialPropertiesResult result)
        {
            if (properties == null)
                return;

            foreach (var pair in properties)
            {
                if (pair.Key == "token")
                    continue;

                var name = pair.Key;
                var propType = shader != null ? GetPropertyType(shader, name) : null;
                if (propType == null)
                {
                    result.Unknown.Add($"{name}: unknown property (not declared by shader '{(shader != null ? shader.name : "<none>")}')");
                    continue;
                }

                // Validate the value shape against the type by attempting a conversion on a throwaway
                // material instance — never the caller's material (dry-run must leave no trace).
                Material probe = null;
                try
                {
                    probe = new Material(shader);
                    MaterialValueConverter.ApplyValue(probe, name, propType.Value, pair.Value);
                    result.Applied.Add(name);
                }
                catch (MaterialPropertyTypeMismatch mismatch)
                {
                    result.Unknown.Add(mismatch.Message);
                }
                finally
                {
                    if (probe != null)
                        Object.DestroyImmediate(probe);
                }
            }
        }

        /// <summary>
        /// Look up a property's <see cref="UnityEngine.Rendering.ShaderPropertyType"/> by name on a shader.
        /// Returns null when the shader has no property with that exact name (case-sensitive; names include
        /// the leading underscore).
        /// </summary>
        internal static ShaderPropertyType? GetPropertyType(Shader shader, string name)
        {
            if (shader == null)
                return null;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyName(i) == name)
                    return shader.GetPropertyType(i);
            }
            return null;
        }

        private static void GuardAtLeastOneApplied(SetMaterialPropertiesResult result, bool shaderReassigned)
        {
            // A shader reassignment is itself a successful change, so it satisfies the "at least one
            // thing applied" guard even when every supplied property is unknown for the new shader.
            if (!shaderReassigned && result.Applied.Count == 0 && result.Unknown.Count > 0)
                throw new ArgumentException(
                    $"None of the supplied inputs could be applied (unknown property name or type mismatch): {string.Join("; ", result.Unknown)}.");
        }

        private static void CopyIdentity(Material mat, string assetPath, SetMaterialPropertiesResult result)
        {
            var identity = ObjectResolver.Describe(mat);
            if (identity != null)
            {
                result.GlobalId = identity.GlobalId;
                result.Guid = identity.Guid;
                result.FileId = identity.FileId;
                result.InstanceId = identity.InstanceId;
                result.HierarchyPath = identity.HierarchyPath;
                result.Type = identity.Type;
            }
            else
            {
                result.Type = nameof(Material);
            }

            // Prefer the confined asset path computed by ResolveMaterial.
            if (!string.IsNullOrEmpty(assetPath))
                result.AssetPath = assetPath;
        }

        /// <summary>Map a <see cref="UnityEngine.Rendering.ShaderPropertyType"/> to the value-shape label used by get_material_properties.</summary>
        private static string PublicValueType(ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color: return "Color";
                case ShaderPropertyType.Vector: return "Vector";
                case ShaderPropertyType.Float: return "Float";
                case ShaderPropertyType.Range: return "Range";
                case ShaderPropertyType.Texture: return "Texture";
                case ShaderPropertyType.Int: return "Int";
                default: return type.ToString();
            }
        }
    }

    /// <summary>
    /// Thrown when a requested shader name does not resolve via <see cref="Shader.Find"/>. Carries the
    /// stable <c>shader_not_found</c> code (also prefixed onto the message, since the server propagates
    /// only the exception message over the wire) so a client can distinguish it from other failures.
    /// </summary>
    sealed class ShaderNotFoundException : Exception
    {
        public const string CodeValue = "shader_not_found";

        public string Code => CodeValue;

        public ShaderNotFoundException(string message) : base($"[{CodeValue}] {message}") { }
    }
}
