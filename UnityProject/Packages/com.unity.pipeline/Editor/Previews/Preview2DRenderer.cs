using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Previews
{
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class Preview2DRenderer
    {
        public const int PreviewSize = 512;

        // AssetPreviewUpdater is internal to the editor.
        static readonly MethodInfo s_CreatePreview =
            Type.GetType("UnityEditor.AssetPreviewUpdater, UnityEditor.CoreModule")?.GetMethod("CreatePreview",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Object), typeof(Object[]), typeof(string), typeof(int), typeof(int) }, null);

        public static bool CanRender(out string reason)
        {
            reason = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? "no graphics device"
                : s_CreatePreview == null ? "AssetPreviewUpdater.CreatePreview not found"
                : !ShaderUtil.hardwareSupportsRectRenderTexture ? "render textures not supported"
                : null;
            return reason == null;
        }

        public static byte[] RenderAssetToPng(Object asset, string assetPath)
        {
            EnsureRenderPipelineReady();

            // An async-compiled shader renders as a placeholder, which would then be cached as the preview.
            var allowAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                // Some inspectors, such as the one for Polygon sprite textures, preview from the visible sub-assets.
                var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
                var preview = (Texture2D)s_CreatePreview.Invoke(null, new object[] { asset, subAssets, assetPath, PreviewSize, PreviewSize });
                if (preview == null)
                    return null;

                // CreatePreview allocates a new texture on every call.
                try
                {
                    return EncodeToPng(preview);
                }
                finally
                {
                    Object.DestroyImmediate(preview);
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = allowAsync;
            }
        }

        static byte[] EncodeToPng(Texture2D preview)
        {
            if (preview.isReadable)
                return preview.EncodeToPNG();

            var rt = RenderTexture.GetTemporary(preview.width, preview.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousActive = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(preview, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, preview.width, preview.height), 0, 0);
                readable.Apply();
                return readable.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null)
                    Object.DestroyImmediate(readable);
            }
        }

        // Batch-mode workers never pump a frame, so the render pipeline is never created and CreatePreview returns null.
        static void EnsureRenderPipelineReady()
        {
            if (s_CreatePreview == null)
                throw new InvalidOperationException("Could not resolve UnityEditor.AssetPreviewUpdater.CreatePreview via reflection");

            if (RenderPipelineManager.pipelineSwitchCompleted)
                return;

            WarmUpRenderPipeline();

            if (!RenderPipelineManager.pipelineSwitchCompleted)
                throw new InvalidOperationException("Render pipeline did not finish initializing after a warm-up render; check GraphicsSettings/QualitySettings.");
        }

        static void WarmUpRenderPipeline()
        {
            const int maxAttempts = 3;

            var go = new GameObject("__PreviewPipelineWarmup__") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = null;
            var previousActive = RenderTexture.active;
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                rt = RenderTexture.GetTemporary(16, 16, 24);
                camera.targetTexture = rt;

                for (int i = 0; i < maxAttempts && !RenderPipelineManager.pipelineSwitchCompleted; i++)
                    camera.Render();
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(go);
            }
        }
    }
}
