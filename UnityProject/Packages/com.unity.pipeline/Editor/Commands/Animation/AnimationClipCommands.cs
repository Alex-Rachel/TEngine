using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Commands.GameObjects;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.Animation
{
    /// <summary>
    /// Group A of CLI-214: author <see cref="AnimationClip"/> assets and their float curves.
    ///
    /// These sit on the CLI-190 authoring foundation, so:
    /// - every agent-supplied asset path is funnelled through <see cref="ProjectPaths.Resolve"/>
    ///   (sandboxed to the authoring root),
    /// - an existing clip is addressed with an <see cref="ObjectRef"/> resolved by
    ///   <see cref="ObjectResolver"/> and re-confined to the authoring root,
    /// - destructive/overwriting operations require an explicit <c>confirm</c> argument and every
    ///   command supports <c>dry-run</c>.
    ///
    /// NOTE: AnimationClip sub-object edits (curves, clip settings) are NOT covered by Unity's Undo,
    /// so changes are persisted with <see cref="EditorUtility.SetDirty"/> + <see cref="AssetDatabase.SaveAssets"/>
    /// rather than wrapped in an <see cref="AuthoringUndoScope"/>. Only float curves are supported in
    /// v1 (object-reference / PPtr curves are out of scope).
    /// </summary>
    static class AnimationClipCommands
    {
        const string Group = "animation.clip";

        [CliGroupCommand(Group,
            "Create an empty .anim AnimationClip asset under the authoring root, with an optional frame rate and loop flag.",
            MainThreadRequired = true,
            Tags = new[] { "animation/clip" })]
        public static AuthoringResult Create(
            [CliArg("path", "Asset path ending in .anim, relative to the authoring root. The Assets/ prefix is optional.", Required = true)] string path,
            [CliArg("frame-rate", "Sampling frame rate of the clip (default 60).")] float frameRate = 60f,
            [CliArg("loop", "If true, set the clip's loop-time flag in its AnimationClipSettings (default false).")] bool loop = false,
            [CliArg("confirm", "Required (true) only when overwriting an existing asset at the path.")] bool confirm = false,
            [CliArg("dry-run", "If true, validate inputs and report what would be created without writing anything.")] bool dryRun = false)
        {
            var normalized = ProjectPaths.Resolve(path, out var error);
            if (normalized == null)
                throw new ArgumentException(error);

            if (!string.Equals(Path.GetExtension(normalized), ".anim", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Animation clip path '{normalized}' must end in .anim.");

            if (frameRate <= 0f)
                throw new ArgumentException($"frame-rate must be positive (got {frameRate}).");

            var exists = AssetDatabase.LoadMainAssetAtPath(normalized) != null;
            if (exists && !confirm)
                throw new ArgumentException($"An asset already exists at '{normalized}'. Pass confirm=true to overwrite it.");

            if (dryRun)
                return new AuthoringResult { AssetPath = normalized, Type = nameof(AnimationClip) };

            EnsureParentFolder(normalized);

            var clip = new AnimationClip { frameRate = frameRate };

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            // CreateAsset does not reliably overwrite, so remove an existing asset first (the confirm
            // guard above gates that one already exists).
            if (exists)
                AssetDatabase.DeleteAsset(normalized);

            AssetDatabase.CreateAsset(clip, normalized);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(normalized);

            var loaded = AssetDatabase.LoadMainAssetAtPath(normalized);
            var result = ObjectResolver.Describe(loaded) ?? new AuthoringResult { Type = nameof(AnimationClip) };
            result.AssetPath = normalized;
            return result;
        }

        [CliGroupCommand(Group,
            "Add or replace a single float curve binding on an AnimationClip (via AnimationUtility.SetEditorCurve). " +
            "Replacing an existing binding overwrites it rather than duplicating.",
            MainThreadRequired = true,
            Tags = new[] { "animation/clip" })]
        public static SetAnimationCurveResult SetCurve(
            [CliArg("clip", "Reference to the AnimationClip to edit (path / guid / globalId).", Required = true)] ObjectRef clip,
            [CliArg("path", "GameObject path relative to the animated root the property lives on. Empty string (default) targets the root.")] string path = "",
            [CliArg("type", "Component type the property lives on, e.g. \"Transform\", \"UnityEngine.Light\". Resolved via the component TypeResolver.", Required = true)] string type = null,
            [CliArg("property", "Curve property name, e.g. \"m_LocalPosition.x\", \"m_LocalScale.y\", \"localEulerAnglesRaw.z\".", Required = true)] string property = null,
            [CliArg("keys", "Keyframes: [{ time, value, inTangent?, outTangent?, weightedMode?: \"None\"|\"In\"|\"Out\"|\"Both\" }]. Omitted tangents default to 0 (flat); this is NOT Unity's Auto tangent mode.", Required = true)] JArray keys = null,
            [CliArg("dry-run", "If true, validate type/property/keys without writing the curve.")] bool dryRun = false)
        {
            var (clipAsset, clipPath) = ResolveClip(clip);
            var componentType = ResolveCurveType(type);
            if (string.IsNullOrWhiteSpace(property))
                throw new ArgumentException("property is required.");

            var bindingPath = path ?? string.Empty;
            var curve = BuildCurve(keys, out var keyCount);

            var result = new SetAnimationCurveResult
            {
                AssetPath = clipPath,
                Type = nameof(AnimationClip),
                Binding = new CurveBinding { Path = bindingPath, Type = componentType.Name, Property = property },
                KeyCount = keyCount
            };

            if (dryRun)
                return result;

            var binding = EditorCurveBinding.FloatCurve(bindingPath, componentType, property);
            AnimationUtility.SetEditorCurve(clipAsset, binding, curve);

            EditorUtility.SetDirty(clipAsset);
            AssetDatabase.SaveAssets();

            var described = ObjectResolver.Describe(clipAsset);
            if (described != null)
            {
                result.Guid = described.Guid;
                result.FileId = described.FileId;
                result.GlobalId = described.GlobalId;
            }

            return result;
        }

        [CliGroupCommand(Group,
            "Read an AnimationClip's metadata and all float curve bindings (optionally with keyframes).",
            MainThreadRequired = true,
            Tags = new[] { "animation/clip" })]
        public static AnimationClipInfo Get(
            [CliArg("clip", "Reference to the AnimationClip to read (path / guid / globalId).", Required = true)] ObjectRef clip,
            [CliArg("include-keys", "If true, include each binding's keyframes (default false).")] bool includeKeys = false)
        {
            var (clipAsset, clipPath) = ResolveClip(clip);

            var settings = AnimationUtility.GetAnimationClipSettings(clipAsset);
            var info = new AnimationClipInfo
            {
                AssetPath = clipPath,
                FrameRate = clipAsset.frameRate,
                Length = clipAsset.length,
                Loop = settings.loopTime
            };

            foreach (var binding in AnimationUtility.GetCurveBindings(clipAsset))
            {
                var curve = AnimationUtility.GetEditorCurve(clipAsset, binding);
                var keyCount = curve?.length ?? 0;

                var entry = new CurveBindingInfo
                {
                    Path = binding.path,
                    Type = binding.type != null ? binding.type.Name : null,
                    Property = binding.propertyName,
                    KeyCount = keyCount
                };

                if (includeKeys && curve != null)
                {
                    entry.Keys = new List<KeyframeInfo>(keyCount);
                    foreach (var key in curve.keys)
                    {
                        entry.Keys.Add(new KeyframeInfo
                        {
                            Time = key.time,
                            Value = key.value,
                            InTangent = key.inTangent,
                            OutTangent = key.outTangent
                        });
                    }
                }

                info.Bindings.Add(entry);
            }

            return info;
        }

        [CliGroupCommand(Group,
            "Remove a float curve binding from an AnimationClip (SetEditorCurve(clip, binding, null)). Destructive: requires confirm=true.",
            MainThreadRequired = true,
            Tags = new[] { "animation/clip" })]
        public static SetAnimationCurveResult RemoveCurve(
            [CliArg("clip", "Reference to the AnimationClip to edit (path / guid / globalId).", Required = true)] ObjectRef clip,
            [CliArg("path", "GameObject path relative to the animated root the binding lives on. Empty string (default) targets the root.")] string path = "",
            [CliArg("type", "Component type of the binding to remove, e.g. \"Transform\". Resolved via the component TypeResolver.", Required = true)] string type = null,
            [CliArg("property", "Curve property name to remove, e.g. \"m_LocalPosition.x\".", Required = true)] string property = null,
            [CliArg("confirm", "Must be true to actually remove the binding (destructive guard).")] bool confirm = false,
            [CliArg("dry-run", "If true, report the binding that would be removed without removing it.")] bool dryRun = false)
        {
            var (clipAsset, clipPath) = ResolveClip(clip);
            var componentType = ResolveCurveType(type);
            if (string.IsNullOrWhiteSpace(property))
                throw new ArgumentException("property is required.");

            var bindingPath = path ?? string.Empty;
            var binding = EditorCurveBinding.FloatCurve(bindingPath, componentType, property);
            var existing = AnimationUtility.GetEditorCurve(clipAsset, binding);
            if (existing == null)
                throw new ArgumentException(
                    $"No curve binding for path='{bindingPath}', type='{componentType.Name}', property='{property}' on '{clipPath}'.");

            var keyCount = existing.length;
            var result = new SetAnimationCurveResult
            {
                AssetPath = clipPath,
                Type = nameof(AnimationClip),
                Binding = new CurveBinding { Path = bindingPath, Type = componentType.Name, Property = property },
                KeyCount = keyCount
            };

            if (dryRun)
                return result;

            if (!confirm)
                throw new ArgumentException(
                    $"Refusing to remove curve '{property}' from '{clipPath}'. Pass confirm=true (destructive, not undoable via Unity's Undo).");

            AnimationUtility.SetEditorCurve(clipAsset, binding, null);
            EditorUtility.SetDirty(clipAsset);
            AssetDatabase.SaveAssets();

            return result;
        }

        /// <summary>
        /// Resolve a clip handle to a loaded <see cref="AnimationClip"/> and its sandbox-confined asset
        /// path, rejecting handles that don't point at an on-disk AnimationClip or that escape the root.
        /// </summary>
        private static (AnimationClip clip, string path) ResolveClip(ObjectRef clip)
        {
            if (clip == null || clip.IsEmpty)
                throw new ArgumentException("clip is required.");

            if (!ObjectResolver.TryResolve(clip, out var obj, out var error))
                throw new ArgumentException(error);

            if (!(obj is AnimationClip clipAsset))
                throw new ArgumentException($"Reference '{clip}' resolved to a {obj.GetType().Name}, not an AnimationClip.");

            var assetPath = AssetDatabase.GetAssetPath(clipAsset);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Reference '{clip}' does not point at an on-disk AnimationClip.");

            var confined = ProjectPaths.Resolve(assetPath, out var confineError);
            if (confined == null)
                throw new ArgumentException(
                    $"Clip '{assetPath}' is outside the authoring root '{ProjectPaths.AuthoringRoot}': {confineError}");

            return (clipAsset, confined);
        }

        /// <summary>
        /// Resolve a curve-binding component type. Reuses the component <see cref="TypeResolver"/>,
        /// which only accepts <see cref="Component"/> subclasses (curves bind to component properties).
        /// </summary>
        private static Type ResolveCurveType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("type is required.");

            var resolved = TypeResolver.ResolveComponentType(type);
            if (resolved == null)
                throw new ArgumentException(
                    $"Could not resolve component type '{type}'. Use a short name (e.g. Transform) or a fully-qualified name (e.g. UnityEngine.Light).");

            return resolved;
        }

        /// <summary>Build an <see cref="AnimationCurve"/> from the agent-supplied keyframe array.</summary>
        private static AnimationCurve BuildCurve(JArray keys, out int keyCount)
        {
            if (keys == null || keys.Count == 0)
                throw new ArgumentException("keys must be a non-empty array of { time, value, ... }.");

            var keyframes = new List<Keyframe>(keys.Count);
            foreach (var token in keys)
            {
                if (!(token is JObject keyObj))
                    throw new ArgumentException("Each key must be an object: { time, value, inTangent?, outTangent?, weightedMode? }.");

                if (keyObj["time"] == null || keyObj["value"] == null)
                    throw new ArgumentException("Each key requires a 'time' and a 'value'.");

                var time = keyObj["time"].ToObject<float>();
                var value = keyObj["value"].ToObject<float>();
                var inTangent = keyObj["inTangent"]?.ToObject<float>() ?? 0f;
                var outTangent = keyObj["outTangent"]?.ToObject<float>() ?? 0f;

                var keyframe = new Keyframe(time, value, inTangent, outTangent);

                var weightedToken = keyObj["weightedMode"];
                if (weightedToken != null && weightedToken.Type != JTokenType.Null)
                {
                    var name = weightedToken.ToObject<string>();
                    if (!Enum.TryParse<WeightedMode>(name, ignoreCase: true, out var weightedMode))
                        throw new ArgumentException($"Unknown weightedMode '{name}'. Use None | In | Out | Both.");
                    keyframe.weightedMode = weightedMode;
                }

                keyframes.Add(keyframe);
            }

            keyCount = keyframes.Count;
            return new AnimationCurve(keyframes.ToArray());
        }

        /// <summary>Create the asset's parent folder chain if it does not yet exist.</summary>
        private static void EnsureParentFolder(string assetPath)
        {
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
                return;

            CreateFolderRecursive(parent);
        }

        private static void CreateFolderRecursive(string assetsPath)
        {
            if (AssetDatabase.IsValidFolder(assetsPath))
                return;

            var parent = Path.GetDirectoryName(assetsPath)?.Replace('\\', '/');
            var name = Path.GetFileName(assetsPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new ArgumentException($"Invalid folder path '{assetsPath}'.");

            if (!AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);

            AssetDatabase.CreateFolder(parent, name);
        }
    }

    /// <summary>
    /// Result of <c>set_animation_curve</c> / <c>remove_animation_curve</c>: the clip identity extended
    /// with the affected binding and its key count.
    /// </summary>
    [Serializable]
    class SetAnimationCurveResult : AuthoringResult
    {
        [JsonProperty("binding")]
        public CurveBinding Binding { get; set; }

        [JsonProperty("keyCount")]
        public int KeyCount { get; set; }
    }

    /// <summary>A curve binding's address: GameObject path, component type, and property name.</summary>
    [Serializable]
    class CurveBinding
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("property")]
        public string Property { get; set; }
    }

    /// <summary>Result of <c>get_animation_clip</c>: clip metadata and its curve bindings.</summary>
    [Serializable]
    class AnimationClipInfo
    {
        [JsonProperty("assetPath")]
        public string AssetPath { get; set; }

        [JsonProperty("frameRate")]
        public float FrameRate { get; set; }

        [JsonProperty("length")]
        public float Length { get; set; }

        [JsonProperty("loop")]
        public bool Loop { get; set; }

        [JsonProperty("bindings")]
        public List<CurveBindingInfo> Bindings { get; set; } = new List<CurveBindingInfo>();
    }

    /// <summary>A single curve binding in <see cref="AnimationClipInfo"/>, optionally with its keys.</summary>
    [Serializable]
    class CurveBindingInfo
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("property")]
        public string Property { get; set; }

        [JsonProperty("keyCount")]
        public int KeyCount { get; set; }

        [JsonProperty("keys", NullValueHandling = NullValueHandling.Ignore)]
        public List<KeyframeInfo> Keys { get; set; }
    }

    /// <summary>A single keyframe in a curve binding.</summary>
    [Serializable]
    class KeyframeInfo
    {
        [JsonProperty("time")]
        public float Time { get; set; }

        [JsonProperty("value")]
        public float Value { get; set; }

        [JsonProperty("inTangent")]
        public float InTangent { get; set; }

        [JsonProperty("outTangent")]
        public float OutTangent { get; set; }
    }
}
