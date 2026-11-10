using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Previews
{
    // Non-primary: its only extension is override-only, so it never imports a file and never conflicts with another importer's extension.
    // Produced on demand via AssetDatabaseExperimental.ProduceArtifact(new ArtifactKey(guid, typeof(Preview2DArtifactImporter))).
    [ScriptedImporter(Version, exts: new string[0], overrideExts: new[] { Extension }, AllowCaching = true)]
    sealed class Preview2DArtifactImporter : ScriptedImporter
    {
        internal const string Extension = "unitypipeline_preview2d";

        public const int Version = 1;
        public const string OutputExt = "png";

        // Failed results depend on this. Its value is random per Editor process, so no other Editor ever matches them.
        internal const string FailedDependency = "Unity.Pipeline.Editor.Previews/Preview2DArtifactImporter.Failed";

        // Survives domain reloads: changing any custom dependency makes the next refresh discard the Editor's own asset thumbnails, in every project with this package.
        internal const string FailedValueSessionKey = "Unity.Pipeline.Editor.Previews.Preview2DArtifactImporter.FailedValue";

        // Registered by the AssetDatabase: the active render pipeline asset and the project's color space.
        const string RenderPipelineDependency = "srp/default-pipeline";
        const string ColorSpaceDependency = "graphics/active-color-space";

        [InitializeOnLoadMethod]
        static void RegisterFailedDependency()
        {
            try
            {
                var value = SessionState.GetString(FailedValueSessionKey, "");
                if (value.Length == 0)
                {
                    value = Guid.NewGuid().ToString("N");
                    SessionState.SetString(FailedValueSessionKey, value);
                }
                AssetDatabase.RegisterCustomDependency(FailedDependency, Hash128.Compute(value));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Preview2DArtifactImporter] RegisterCustomDependency failed: {e.GetType().Name}: {e.Message}. Failed previews may be reused by other Editors.");
            }
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            ctx.DependsOnCustomDependency(RenderPipelineDependency);
            ctx.DependsOnCustomDependency(ColorSpaceDependency);

            ctx.DependsOnArtifact(ctx.assetPath);

            if (IsSkipped(ctx.assetPath))
                return;

            if (!Preview2DRenderer.CanRender(out var reason))
            {
                Fail(ctx, reason);
                return;
            }

            Object asset = AssetDatabase.LoadMainAssetAtPath(ctx.assetPath);
            if (asset == null)
            {
                Fail(ctx, "the asset could not be loaded");
                return;
            }

            byte[] png;
            try
            {
                png = Preview2DRenderer.RenderAssetToPng(asset, ctx.assetPath);
            }
            catch (Exception e)
            {
                Fail(ctx, e.ToString());
                return;
            }

            // The Editor draws no preview for this asset type: a real result, safe to cache.
            if (png == null)
                return;

            DependOnReferencedAssets(ctx, asset);
            WritePng(ctx, png);
        }

        // Files that have no preview: scenes, scripts, assembly definitions and plugins.
        static readonly Regex s_SkippedPaths = new Regex(@"\.(cs|cpp|c|h|m|mm|asmref|asmdef|rsp|dll|unity|dylib|bundle|xcprivacy|androidlib|so(\.[0-9]+)*)$", RegexOptions.IgnoreCase);

        static bool IsSkipped(string assetPath) => s_SkippedPaths.IsMatch(assetPath) || AssetDatabase.IsValidFolder(assetPath);

        // Walks the loaded object graph, which also finds references that only exist in another asset's import result.
        static void DependOnReferencedAssets(AssetImportContext ctx, Object asset)
        {
            var self = AssetDatabase.GUIDFromAssetPath(ctx.assetPath);
            var registered = new HashSet<GUID>();
            var collected = EditorUtility.CollectDependencies(new[] { asset });
            foreach (var dependency in collected)
            {
                if (dependency == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(dependency, out string guidString, out long _))
                    continue;

                var guid = new GUID(guidString);
                if (guid == self || !IsProjectAsset(guid) || !registered.Add(guid))
                    continue;

                // A native-format asset's artifact can stay byte-identical when its source changes, so also depend on the source.
                if (AssetDatabase.IsNativeAsset(dependency))
                    ctx.DependsOnSourceAsset(guid);
                ctx.DependsOnArtifact(guid);
            }

            // A missing asset has no path, so it bypasses the project-asset filter; depending on it regenerates the preview when the asset is restored.
            foreach (var guid in MissingReferences(collected))
            {
                if (guid != self && registered.Add(guid))
                    ctx.DependsOnArtifact(guid);
            }
        }

        // CollectDependencies skips references it can't load; their instance ids still map to the missing asset's GUID.
        static IEnumerable<GUID> MissingReferences(Object[] objects)
        {
            foreach (var obj in objects)
            {
                if (obj == null)
                    continue;

                using var serialized = new SerializedObject(obj);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != null)
                        continue;
#if UNITY_6000_4_OR_NEWER
                    var id = property.objectReferenceEntityIdValue;
#elif UNITY_6000_3_OR_NEWER
                    EntityId id = property.objectReferenceInstanceIDValue;
#else
                    var id = property.objectReferenceInstanceIDValue;
#endif
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(id, out string guid, out long _))
                        yield return new GUID(guid);
                }
            }
        }

        // Excludes the built-in and editor resources, which are not project assets.
        static bool IsProjectAsset(GUID guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal);
        }

        static void Fail(AssetImportContext ctx, string reason)
        {
            ctx.DependsOnCustomDependency(FailedDependency);
            ctx.LogImportError($"No preview for {ctx.assetPath}: {reason}");
        }

        static void WritePng(AssetImportContext ctx, byte[] png)
        {
#if UNITY_6000_5_OR_NEWER
            ctx.SetOutputArtifactData(OutputExt, png);
#else
            File.WriteAllBytes(ctx.GetOutputArtifactFilePath(OutputExt), png);
#endif
        }
    }
}
