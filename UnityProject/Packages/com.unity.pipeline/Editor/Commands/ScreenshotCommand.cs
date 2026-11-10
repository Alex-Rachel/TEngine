using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Captures the Scene or Game view of a running Editor as a PNG and returns the file path.
    /// Backs the <c>unity screenshot</c> CLI command (CLI-112): the CLI only forwards the request
    /// via <c>unity request screenshot</c> — all capture logic lives here in the package, per the
    /// two-commands design.
    ///
    /// Capture renders the relevant view camera into an offscreen RenderTexture and reads it back,
    /// rather than using ScreenCapture.CaptureScreenshot. CaptureScreenshot only targets the Game
    /// view and writes asynchronously on the next frame — a single request/response can't reliably
    /// wait for that, and it does nothing for the Scene view in edit mode. Camera.Render() into a
    /// RenderTexture is synchronous, needs no play mode, and handles both views identically.
    ///
    /// Deprecated: <c>capture game_view</c> and <c>capture scene_view</c> with <c>output-path</c> cover it,
    /// and the game view capture also includes the overlay UI this command misses. Every response
    /// carries <see cref="DeprecationWarning"/>.
    /// </summary>
    static class ScreenshotCommand
    {
        const string Group = "capture";

        internal const string DeprecationWarning =
            CommandNames.CaptureScreenshot + " is deprecated: use " + CommandNames.CaptureGameView + " or " +
            CommandNames.CaptureSceneView + " with --output-path.";

        const int k_DefaultWidth = 1920;
        const int k_DefaultHeight = 1080;

        [CliGroupCommand(Group, "Deprecated: use " + CommandNames.CaptureGameView + " or " + CommandNames.CaptureSceneView + " with output-path. Capture the Scene or Game view as a PNG and return its file path", MainThreadRequired = true, Tags = new[] { "capture" })]
        public static ScreenshotResponse Screenshot(
            [CliArg("view", "Which view to capture: 'game' (default) or 'scene'")] string view = "game",
            [CliArg("output-path", "Output PNG path (absolute, or relative to the project root). Defaults to a timestamped file under <project>/Temp/pipeline-screenshots/.")] string output = "",
            [CliArg("width", "Output width in pixels. 0 (default) uses the view camera's current width.")] int width = 0,
            [CliArg("height", "Output height in pixels. 0 (default) uses the view camera's current height.")] int height = 0)
        {
            var normalizedView = string.IsNullOrWhiteSpace(view) ? "game" : view.Trim().ToLowerInvariant();
            if (normalizedView != "game" && normalizedView != "scene")
                return ScreenshotResponse.Fail($"Invalid view '{view}'. Expected 'game' or 'scene'.");

            if (width < 0 || height < 0)
                return ScreenshotResponse.Fail("width and height must be >= 0 (0 = use the view's current size).");

            var camera = ResolveCamera(normalizedView, out var resolveError);
            if (camera == null)
                return ScreenshotResponse.Fail(resolveError);

            // Default the size to the camera's current pixel size, falling back to 1080p when the
            // camera reports nothing useful (can happen for an off-screen scene camera).
            var w = width > 0 ? width : camera.pixelWidth;
            var h = height > 0 ? height : camera.pixelHeight;
            if (w <= 1) w = k_DefaultWidth;
            if (h <= 1) h = k_DefaultHeight;

            var path = ResolveOutputPath(output, normalizedView);

            try
            {
                var png = RenderToPng(camera, w, h);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, png);
            }
            catch (Exception ex)
            {
                return ScreenshotResponse.Fail($"Failed to capture {normalizedView} view: {ex.Message}");
            }

            return new ScreenshotResponse
            {
                Success = true,
                Path = path,
                View = normalizedView,
                Width = w,
                Height = h,
                Message = $"Captured {normalizedView} view to {path}"
            };
        }

        /// <summary>
        /// Resolve the camera to render for the requested view, or null with an explanatory error.
        /// </summary>
        static Camera ResolveCamera(string view, out string error)
        {
            error = null;

            if (view == "scene")
            {
                var sv = SceneView.lastActiveSceneView;
                if (sv == null || sv.camera == null)
                {
                    error = "No active Scene view to capture. Open a Scene view window and try again.";
                    return null;
                }
                return sv.camera;
            }

            // Game view: prefer the main camera, otherwise the first enabled camera in the open scenes.
            var cam = Camera.main;
            if (cam == null && Camera.allCamerasCount > 0)
            {
                var all = Camera.allCameras;
                if (all.Length > 0)
                    cam = all[0];
            }

            if (cam == null)
                error = "No camera found to capture the Game view (no MainCamera and no enabled cameras in the open scenes).";

            return cam;
        }

        /// <summary>
        /// Render <paramref name="camera"/> into an offscreen RenderTexture at the given size and
        /// encode it to PNG. Restores the camera's previous target and the active RenderTexture so
        /// the capture leaves no side effects on the live editor.
        /// </summary>
        static byte[] RenderToPng(Camera camera, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24);
            var prevTarget = camera.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;

            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                return tex.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Resolve the output path: an explicit path is used as-is (rooted) or relative to the
        /// project root; an empty path defaults to a timestamped file under Temp.
        /// </summary>
        static string ResolveOutputPath(string output, string view)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);

            if (!string.IsNullOrWhiteSpace(output))
                return ProjectPaths.ResolveFile(output, projectRoot);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            return Path.Combine(projectRoot, "Temp", "pipeline-screenshots", $"screenshot_{view}_{stamp}.png");
        }
    }

    /// <summary>
    /// Result of the <c>screenshot</c> command. Serialized to JSON for the CLI's --format json
    /// output; the human formatter reads <see cref="Path"/>.
    /// </summary>
    [Serializable]
    class ScreenshotResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("view")]
        public string View { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>Always carries the deprecation notice, on success and failure alike.</summary>
        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; } = new List<string> { ScreenshotCommand.DeprecationWarning };

        public static ScreenshotResponse Fail(string message) => new ScreenshotResponse
        {
            Success = false,
            Message = message
        };
    }
}
