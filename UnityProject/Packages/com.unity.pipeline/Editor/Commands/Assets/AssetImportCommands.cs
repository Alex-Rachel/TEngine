using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Assets
{
    /// <summary>
    /// <para>
    /// Import-settings authoring commands (CLI-191 + CLI-212). Reads and edits the
    /// <see cref="AssetImporter"/> for an asset.
    /// </para>
    ///
    /// <para>
    /// <c>set_import_settings</c> sets named members on the importer from a JSON object and re-imports.
    /// For the <c>Default</c> platform it sets top-level public properties/fields via reflection (works
    /// across every importer kind). For a real platform on a <see cref="TextureImporter"/> /
    /// <see cref="AudioImporter"/>, keys are applied onto the platform-override struct
    /// (<see cref="TextureImporterPlatformSettings"/> / <see cref="AudioImporterSampleSettings"/>) which
    /// is unreachable by top-level reflection. <see cref="ModelImporter"/> has no per-platform overrides.
    /// </para>
    ///
    /// <para>
    /// <c>get_import_settings</c> reads the importer state structured by importer type (texture / model /
    /// audio), including the default-platform fields and, for textures/audio, one platform override block.
    /// </para>
    ///
    /// <para>
    /// Unknown keys are reported back rather than silently ignored, so an agent gets actionable feedback.
    /// </para>
    ///
    /// <para>
    /// NOTE: import settings live in the asset's .meta file via the importer; this is an AssetDatabase
    /// operation and is not part of Unity's Undo system. SaveAndReimport is not destructive to the
    /// source file, so no <c>confirm</c> is required.
    /// </para>
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class AssetImportCommands
    {
        const string Group = "asset";

        // Caller-facing platform names accepted by both commands. "Default" means the default platform
        // (top-level importer properties / default sample settings); the rest are real build platforms.
        private static readonly HashSet<string> s_SupportedPlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Default", "Standalone", "iOS", "Android", "WebGL", "tvOS"
        };

        [CliGroupCommand(Group, "Set import settings on an asset's AssetImporter (default platform top-level properties, or a texture/audio per-platform override) and re-import it.", Tags = new[] { "assets/import" })]
        public static SetImportSettingsResult SetImportSettings(
            [CliArg("asset", "Reference to the asset whose importer to edit (path / guid / globalId).", Required = true)] ObjectRef asset,
            [CliArg("settings", "JSON object of importer property/field names to values, e.g. {\"isReadable\": true, \"textureType\": \"NormalMap\"}. For platform != Default on a texture/audio importer, keys map onto the platform-settings struct (e.g. maxTextureSize, format, compressionFormat, quality, and overridden).", Required = true)] JObject settings,
            [CliArg("platform", "Target platform: Default | Standalone | iOS | Android | WebGL | tvOS. Defaults to Default (top-level importer properties). A real platform writes a per-platform override (textures/audio only).")] string platform = "Default",
            [CliArg("dry-run", "If true, validate which settings would apply (and which are unknown) without writing or re-importing.")] bool dryRun = false)
        {
            var importer = ResolveImporter(asset, out var assetPath);

            if (settings == null || settings.Count == 0)
                throw new ArgumentException("settings must be a non-empty JSON object.");

            if (!s_SupportedPlatforms.Contains(platform))
                throw new ArgumentException(
                    Serialize(new ErrorResult { Code = "unknown_platform", Message = $"Unknown platform '{platform}'. Supported: {string.Join(", ", s_SupportedPlatforms)}." }));

            var canonicalPlatform = Canonical(platform);
            var isDefault = string.Equals(canonicalPlatform, "Default", StringComparison.OrdinalIgnoreCase);

            var result = new SetImportSettingsResult
            {
                AssetPath = assetPath,
                ImporterType = importer.GetType().Name,
                Platform = canonicalPlatform
            };

            // Strip the server-injected token before iterating: it is never an importer setting.
            var keys = new List<string>();
            foreach (var pair in settings)
            {
                if (pair.Key == "token")
                    continue;
                keys.Add(pair.Key);
            }
            if (keys.Count == 0)
                throw new ArgumentException("settings must be a non-empty JSON object.");

            if (isDefault)
            {
                ApplyDefaultPlatform(importer, settings, keys, result, write: !dryRun);
            }
            else
            {
                switch (importer)
                {
                    case TextureImporter texture:
                        ApplyTexturePlatform(texture, settings, keys, canonicalPlatform, result, write: !dryRun);
                        break;
                    case AudioImporter audio:
                        ApplyAudioPlatform(audio, settings, keys, canonicalPlatform, result, write: !dryRun);
                        break;
                    case ModelImporter _:
                        throw new ArgumentException(
                            Serialize(new ErrorResult { Code = "no_platform_overrides", Message = "ModelImporter has no per-platform overrides; use platform=Default." }));
                    default:
                        throw new ArgumentException(
                            Serialize(new ErrorResult { Code = "no_platform_overrides", Message = $"Importer '{result.ImporterType}' has no per-platform overrides; use platform=Default." }));
                }
            }

            if (result.Unknown.Count > 0 && result.Applied.Count == 0)
                throw new ArgumentException($"None of the supplied settings could be applied to '{result.ImporterType}' (unknown key or invalid value): {string.Join(", ", result.Unknown)}.");

            if (dryRun)
                return result;

            importer.SaveAndReimport();
            return result;
        }

        [CliGroupCommand(Group, "Read an asset's import settings, structured by importer type (texture/model/audio), including the default-platform fields and (for textures/audio) one platform override block.", Tags = new[] { "assets/import" })]
        public static GetImportSettingsResult GetImportSettings(
            [CliArg("asset", "Reference to the asset whose importer to read (path / guid / globalId).", Required = true)] ObjectRef asset,
            [CliArg("platform", "Platform whose override to read: Default | Standalone | iOS | Android | WebGL | tvOS. Defaults to Default.")] string platform = "Default")
        {
            var importer = ResolveImporter(asset, out var assetPath);

            if (!s_SupportedPlatforms.Contains(platform))
                throw new ArgumentException(
                    Serialize(new ErrorResult { Code = "unknown_platform", Message = $"Unknown platform '{platform}'. Supported: {string.Join(", ", s_SupportedPlatforms)}." }));

            var canonicalPlatform = Canonical(platform);
            var isDefault = string.Equals(canonicalPlatform, "Default", StringComparison.OrdinalIgnoreCase);

            var result = new GetImportSettingsResult
            {
                AssetPath = assetPath,
                ImporterType = importer.GetType().Name,
                Platform = canonicalPlatform
            };

            switch (importer)
            {
                case TextureImporter texture:
                    result.Settings = ReadTextureDefaults(texture);
                    result.PlatformOverride = ReadTexturePlatformOverride(texture, canonicalPlatform, isDefault);
                    break;
                case ModelImporter model:
                    result.Settings = ReadModelDefaults(model);
                    result.PlatformOverride = null; // ModelImporter has no per-platform overrides.
                    break;
                case AudioImporter audio:
                    result.Settings = ReadAudioDefaults(audio);
                    result.PlatformOverride = ReadAudioPlatformOverride(audio, canonicalPlatform, isDefault);
                    break;
                default:
                    // Non-import asset (e.g. a ScriptableObject via NativeFormatImporter): return a
                    // minimal settings block rather than throwing.
                    result.Settings = new Dictionary<string, object>
                    {
                        ["userData"] = importer.userData,
                        ["assetBundleName"] = importer.assetBundleName
                    };
                    result.PlatformOverride = null;
                    break;
            }

            return result;
        }

        // ------------------------------------------------------------------ resolution / helpers

        /// <summary>
        /// Resolve an <see cref="ObjectRef"/> to its <see cref="AssetImporter"/>, confined to the
        /// authoring root. Throws an actionable <see cref="ArgumentException"/> on any failure.
        /// </summary>
        private static AssetImporter ResolveImporter(ObjectRef asset, out string assetPath)
        {
            if (!ObjectResolver.TryResolve(asset, out var obj, out var error))
                throw new ArgumentException(error);

            assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Reference '{asset}' does not point at an on-disk asset.");

            // Confine to the authoring root so a GUID/globalId handle cannot reach an asset outside the
            // sandbox.
            assetPath = ProjectPaths.Resolve(assetPath, out var confineError);
            if (assetPath == null)
                throw new ArgumentException(
                    $"Asset '{asset}' is outside the authoring root '{ProjectPaths.AuthoringRoot}': {confineError}");

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                throw new ArgumentException($"No AssetImporter found for '{assetPath}'.");

            return importer;
        }

        /// <summary>Normalize a caller platform name to its canonical capitalization (e.g. "ios" -> "iOS").</summary>
        private static string Canonical(string platform)
        {
            foreach (var supported in s_SupportedPlatforms)
            {
                if (string.Equals(supported, platform, StringComparison.OrdinalIgnoreCase))
                    return supported;
            }
            return platform;
        }

        /// <summary>
        /// Map a caller-facing platform name to the build-platform string Unity's importer APIs expect.
        /// Most match 1:1; iOS is "iPhone" to those APIs.
        /// </summary>
        private static string UnityPlatformName(string canonicalPlatform)
        {
            return string.Equals(canonicalPlatform, "iOS", StringComparison.OrdinalIgnoreCase) ? "iPhone" : canonicalPlatform;
        }

        // ------------------------------------------------------------------ default-platform reflection

        private static void ApplyDefaultPlatform(AssetImporter importer, JObject settings, List<string> keys, SetImportSettingsResult result, bool write)
        {
            var importerType = importer.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            foreach (var key in keys)
            {
                // In dry-run we only validate the member exists and the value converts — never mutate the
                // (possibly cached) importer instance, so a dry run leaves no trace.
                var applied = TryApplyMember(importer, importerType, flags, key, settings[key], write);
                if (applied)
                    result.Applied.Add(key);
                else
                    result.Unknown.Add(key);
            }
        }

        /// <summary>
        /// Set a single named property or field on the target from a JSON token. Returns false when no
        /// writable member with that name exists or the value cannot be converted to the member type.
        /// Enum members accept the value name (case-insensitive) as well as numeric values.
        /// </summary>
        private static bool TryApplyMember(object target, Type type, BindingFlags flags, string memberName, JToken value, bool write)
        {
            var property = type.GetProperty(memberName, flags);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                return TryConvertAndSet(property.PropertyType, value,
                    converted => { if (write) property.SetValue(target, converted); });
            }

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                return TryConvertAndSet(field.FieldType, value,
                    converted => { if (write) field.SetValue(target, converted); });
            }

            return false;
        }

        /// <summary>
        /// Convert a JSON token to <paramref name="targetType"/> (with case-insensitive enum-name support)
        /// and, on success, invoke <paramref name="set"/>. Returns false when the value cannot convert.
        /// </summary>
        private static bool TryConvertAndSet(Type targetType, JToken value, Action<object> set)
        {
            try
            {
                if (!TryConvert(targetType, value, out var converted))
                    return false;
                set(converted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Convert a JSON token to a CLR value. Enums accept their value name (case-insensitive) or an
        /// integral value; everything else defers to Newtonsoft's <see cref="JToken.ToObject(Type)"/>.
        /// </summary>
        private static bool TryConvert(Type targetType, JToken value, out object converted)
        {
            converted = null;
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying.IsEnum)
            {
                if (value.Type == JTokenType.String)
                {
                    var name = value.Value<string>();
                    if (string.IsNullOrEmpty(name))
                        return false;
                    try
                    {
                        converted = Enum.Parse(underlying, name, ignoreCase: true);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (value.Type == JTokenType.Integer)
                {
                    converted = Enum.ToObject(underlying, value.Value<long>());
                    return true;
                }

                return false;
            }

            converted = value.ToObject(targetType);
            return true;
        }

        // ------------------------------------------------------------------ texture platform overrides

        private static void ApplyTexturePlatform(TextureImporter texture, JObject settings, List<string> keys, string canonicalPlatform, SetImportSettingsResult result, bool write)
        {
            var unityPlatform = UnityPlatformName(canonicalPlatform);
            // Boxed struct so reflection SetValue mutates the local copy; written back via SetPlatformTextureSettings.
            object boxed = texture.GetPlatformTextureSettings(unityPlatform);
            var type = typeof(TextureImporterPlatformSettings);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            // Only treat "overridden" as explicitly provided when the caller passes a valid boolean.
            // A non-boolean value is caught by TrySetBoolMember (reported to unknown[]), so we must
            // not suppress the default overridden=true in that case or the override is silently skipped.
            var explicitOverridden = settings.TryGetValue("overridden", StringComparison.OrdinalIgnoreCase, out var overriddenTok)
                && overriddenTok.Type == JTokenType.Boolean;

            foreach (var key in keys)
            {
                if (string.Equals(key, "overridden", StringComparison.OrdinalIgnoreCase))
                {
                    // Handled explicitly below; still report it as applied.
                    if (TrySetBoolMember(type, flags, boxed, "overridden", settings[key], write))
                        result.Applied.Add(key);
                    else
                        result.Unknown.Add(key);
                    continue;
                }

                if (TryApplyMember(boxed, type, flags, key, settings[key], write))
                    result.Applied.Add(key);
                else
                    result.Unknown.Add(key);
            }

            // Default the override on (the caller intends to set platform-specific values) unless they
            // explicitly passed overridden:false (in which case the value they passed is already in the
            // boxed copy from the loop above).
            if (!explicitOverridden)
                SetBool(type, flags, boxed, "overridden", true);

            if (write && result.Applied.Count > 0)
            {
                var settingsStruct = (TextureImporterPlatformSettings)boxed;
                settingsStruct.name = unityPlatform;
                texture.SetPlatformTextureSettings(settingsStruct);
            }
        }

        /// <summary>
        /// Set a named boolean member from a JSON token. Returns false when the token is not a boolean or
        /// no settable bool member with that name exists, so a non-existent member is reported as unknown
        /// rather than falsely applied. In <paramref name="write"/>=false (dry-run) mode the member is only
        /// probed for existence, never mutated.
        /// </summary>
        private static bool TrySetBoolMember(Type type, BindingFlags flags, object boxed, string member, JToken token, bool write)
        {
            if (token.Type != JTokenType.Boolean)
                return false;
            if (!write)
                return BoolMemberExists(type, flags, member);
            return SetBool(type, flags, boxed, member, token.Value<bool>());
        }

        /// <summary>True when a settable boolean property or field with that name exists on the type.</summary>
        private static bool BoolMemberExists(Type type, BindingFlags flags, string member)
        {
            var property = type.GetProperty(member, flags);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool)
                && property.GetIndexParameters().Length == 0)
                return true;

            var field = type.GetField(member, flags);
            return field != null && field.FieldType == typeof(bool);
        }

        /// <summary>
        /// Write a boolean property or field (matching <see cref="TryApplyMember"/>'s property-then-field
        /// resolution so a bool implemented as a property is also honored). Returns false — without
        /// mutating anything — when no settable bool member with that name exists.
        /// </summary>
        private static bool SetBool(Type type, BindingFlags flags, object boxed, string member, bool value)
        {
            var property = type.GetProperty(member, flags);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool)
                && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(boxed, value);
                return true;
            }

            var field = type.GetField(member, flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(boxed, value);
                return true;
            }

            return false;
        }

        private static Dictionary<string, object> ReadTextureDefaults(TextureImporter texture)
        {
            return new Dictionary<string, object>
            {
                ["textureType"] = texture.textureType.ToString(),
                ["textureShape"] = texture.textureShape.ToString(),
                ["sRGBTexture"] = texture.sRGBTexture,
                ["alphaSource"] = texture.alphaSource.ToString(),
                ["alphaIsTransparency"] = texture.alphaIsTransparency,
                ["isReadable"] = texture.isReadable,
                ["streamingMipmaps"] = texture.streamingMipmaps,
                ["mipmapEnabled"] = texture.mipmapEnabled,
                ["wrapMode"] = texture.wrapMode.ToString(),
                ["filterMode"] = texture.filterMode.ToString(),
                ["anisoLevel"] = texture.anisoLevel,
                ["spriteImportMode"] = texture.spriteImportMode.ToString(),
                ["spritePixelsPerUnit"] = texture.spritePixelsPerUnit,
                ["npotScale"] = texture.npotScale.ToString(),
                ["maxTextureSize"] = texture.maxTextureSize
            };
        }

        private static Dictionary<string, object> ReadTexturePlatformOverride(TextureImporter texture, string canonicalPlatform, bool isDefault)
        {
            var settings = isDefault
                ? texture.GetDefaultPlatformTextureSettings()
                : texture.GetPlatformTextureSettings(UnityPlatformName(canonicalPlatform));

            return new Dictionary<string, object>
            {
                ["overridden"] = !isDefault && settings.overridden,
                ["maxTextureSize"] = settings.maxTextureSize,
                ["resizeAlgorithm"] = settings.resizeAlgorithm.ToString(),
                ["format"] = settings.format.ToString(),
                ["textureCompression"] = settings.textureCompression.ToString(),
                ["compressionQuality"] = settings.compressionQuality,
                ["crunchedCompression"] = settings.crunchedCompression,
                ["androidETC2FallbackOverride"] = settings.androidETC2FallbackOverride.ToString()
            };
        }

        // ------------------------------------------------------------------ audio platform overrides

        private static void ApplyAudioPlatform(AudioImporter audio, JObject settings, List<string> keys, string canonicalPlatform, SetImportSettingsResult result, bool write)
        {
            var unityPlatform = UnityPlatformName(canonicalPlatform);

            // overridden:false clears the override entirely (and ignores any other keys).
            if (settings.TryGetValue("overridden", StringComparison.OrdinalIgnoreCase, out var overriddenToken)
                && overriddenToken.Type == JTokenType.Boolean
                && !overriddenToken.Value<bool>())
            {
                result.Applied.Add("overridden");
                if (write)
                    audio.ClearSampleSettingOverride(unityPlatform);
                return;
            }

            object boxed = audio.GetOverrideSampleSettings(unityPlatform);
            var type = typeof(AudioImporterSampleSettings);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            foreach (var key in keys)
            {
                if (string.Equals(key, "overridden", StringComparison.OrdinalIgnoreCase))
                {
                    // Only accept a boolean; anything else is a misuse and goes to unknown[].
                    if (settings[key].Type == JTokenType.Boolean)
                        result.Applied.Add(key);
                    else
                        result.Unknown.Add(key);
                    continue;
                }

                if (TryApplyMember(boxed, type, flags, key, settings[key], write))
                    result.Applied.Add(key);
                else
                    result.Unknown.Add(key);
            }

            if (write && result.Applied.Count > 0)
            {
                var settingsStruct = (AudioImporterSampleSettings)boxed;
                audio.SetOverrideSampleSettings(unityPlatform, settingsStruct);
            }
        }

        private static Dictionary<string, object> ReadAudioDefaults(AudioImporter audio)
        {
            var sample = audio.defaultSampleSettings;
            return new Dictionary<string, object>
            {
                ["forceToMono"] = audio.forceToMono,
                ["loadInBackground"] = audio.loadInBackground,
                ["ambisonic"] = audio.ambisonic,
                ["loadType"] = sample.loadType.ToString(),
                ["compressionFormat"] = sample.compressionFormat.ToString(),
                ["quality"] = sample.quality,
                ["sampleRateSetting"] = sample.sampleRateSetting.ToString(),
                ["sampleRateOverride"] = sample.sampleRateOverride
            };
        }

        private static Dictionary<string, object> ReadAudioPlatformOverride(AudioImporter audio, string canonicalPlatform, bool isDefault)
        {
            if (isDefault)
            {
                var sample = audio.defaultSampleSettings;
                return new Dictionary<string, object>
                {
                    ["overridden"] = false,
                    ["loadType"] = sample.loadType.ToString(),
                    ["compressionFormat"] = sample.compressionFormat.ToString(),
                    ["quality"] = sample.quality,
                    ["sampleRateSetting"] = sample.sampleRateSetting.ToString(),
                    ["sampleRateOverride"] = sample.sampleRateOverride
                };
            }

            var unityPlatform = UnityPlatformName(canonicalPlatform);
            var overridden = audio.ContainsSampleSettingsOverride(unityPlatform);
            var settings = audio.GetOverrideSampleSettings(unityPlatform);
            return new Dictionary<string, object>
            {
                ["overridden"] = overridden,
                ["loadType"] = settings.loadType.ToString(),
                ["compressionFormat"] = settings.compressionFormat.ToString(),
                ["quality"] = settings.quality,
                ["sampleRateSetting"] = settings.sampleRateSetting.ToString(),
                ["sampleRateOverride"] = settings.sampleRateOverride
            };
        }

        // ------------------------------------------------------------------ model defaults (no overrides)

        private static Dictionary<string, object> ReadModelDefaults(ModelImporter model)
        {
            return new Dictionary<string, object>
            {
                // Meshes
                ["globalScale"] = model.globalScale,
                ["useFileScale"] = model.useFileScale,
                ["meshCompression"] = model.meshCompression.ToString(),
                ["isReadable"] = model.isReadable,
                ["meshOptimizationFlags"] = model.meshOptimizationFlags.ToString(),
                ["weldVertices"] = model.weldVertices,
                ["indexFormat"] = model.indexFormat.ToString(),
                ["keepQuads"] = model.keepQuads,
                // Normals / Tangents
                ["importNormals"] = model.importNormals.ToString(),
                ["normalCalculationMode"] = model.normalCalculationMode.ToString(),
                ["normalSmoothingAngle"] = model.normalSmoothingAngle,
                ["importTangents"] = model.importTangents.ToString(),
                ["importBlendShapes"] = model.importBlendShapes,
                // Geometry
                ["importCameras"] = model.importCameras,
                ["importLights"] = model.importLights,
                ["swapUVChannels"] = model.swapUVChannels,
                ["generateSecondaryUV"] = model.generateSecondaryUV,
                // Materials
                ["materialImportMode"] = model.materialImportMode.ToString(),
                ["materialLocation"] = model.materialLocation.ToString(),
                // Animation
                ["importAnimation"] = model.importAnimation,
                ["animationType"] = model.animationType.ToString(),
                ["importConstraints"] = model.importConstraints,
                ["importVisibility"] = model.importVisibility
            };
        }

        private static string Serialize(object value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value);
        }
    }

    /// <summary>
    /// Result of <c>set_import_settings</c>: target platform plus which keys were applied vs. unknown.
    /// </summary>
    [Serializable]
    class SetImportSettingsResult
    {
        [Newtonsoft.Json.JsonProperty("assetPath")]
        public string AssetPath { get; set; }

        [Newtonsoft.Json.JsonProperty("importerType")]
        public string ImporterType { get; set; }

        [Newtonsoft.Json.JsonProperty("platform")]
        public string Platform { get; set; }

        [Newtonsoft.Json.JsonProperty("applied")]
        public List<string> Applied { get; set; } = new List<string>();

        [Newtonsoft.Json.JsonProperty("unknown")]
        public List<string> Unknown { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of <c>get_import_settings</c>: the importer type, default-platform settings, and (for
    /// textures/audio) one platform-override block. <c>platformOverride</c> is null for ModelImporter and
    /// non-import assets.
    /// </summary>
    [Serializable]
    class GetImportSettingsResult
    {
        [Newtonsoft.Json.JsonProperty("assetPath")]
        public string AssetPath { get; set; }

        [Newtonsoft.Json.JsonProperty("importerType")]
        public string ImporterType { get; set; }

        [Newtonsoft.Json.JsonProperty("settings")]
        public Dictionary<string, object> Settings { get; set; }

        [Newtonsoft.Json.JsonProperty("platform")]
        public string Platform { get; set; }

        [Newtonsoft.Json.JsonProperty("platformOverride")]
        public Dictionary<string, object> PlatformOverride { get; set; }
    }

    /// <summary>Structured error payload serialized into the thrown ArgumentException message.</summary>
    [Serializable]
    class ErrorResult
    {
        [Newtonsoft.Json.JsonProperty("code")]
        public string Code { get; set; }

        [Newtonsoft.Json.JsonProperty("message")]
        public string Message { get; set; }
    }
}
