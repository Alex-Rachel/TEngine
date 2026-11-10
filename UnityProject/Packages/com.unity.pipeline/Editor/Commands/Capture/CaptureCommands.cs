using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Commands.Capture
{
    /// <summary>
    /// Visual-feedback commands (CLI-199): render the composited game view, a specific camera, or the
    /// Scene View to a PNG and return it base64-encoded, so an agent can "see" the editor without a
    /// display. The default game-view source is the screen — the game view's own render target, which
    /// is what the Game window actually shows, including Screen Space - Overlay UI (HUDs) that never
    /// renders through a camera. Optionally writes the PNG to a file: relative paths resolve from the
    /// project root, absolute paths are used as-is, and only a file under Assets/ is imported.
    ///
    /// Captures require a GPU. Batchmode has one unless the Editor runs with -nographics, where the
    /// device type is <see cref="GraphicsDeviceType.Null"/> and the commands throw so callers get a
    /// clear message instead of an empty image.
    /// </summary>
    static class CaptureCommands
    {
        const string Group = "capture";

        private const int MaxDimension = 4096;

        [CliGroupCommand(Group, "Capture the game view as a PNG. source=screen (default) captures what the Game window shows, incl. Screen Space - Overlay UI; source=camera renders a single camera instead, with overlay UI drawn on top on URP/HDRP (overlay=auto|true|false). Returns it inline as base64, unless output-path is set (path-only result; pass include-inline-image=true to get both).", Tags = new[] { "capture" })]
        public static CaptureResult GameView(
            [CliArg("width", "Output width in px (default 1280; capped 4096).")] int width = 1280,
            [CliArg("height", "Output height in px (default 720; capped 4096).")] int height = 720,
            [CliArg("camera", "Optional camera name (source=camera only); defaults to Camera.main, else the first enabled camera.")] string camera = null,
            [CliArg("output-path", "Optional path to write the PNG: absolute, or relative to the project root (e.g. Screenshots/foo.png, outside Assets/ so no .meta is created). When set, the result omits the inline base64 image unless include-inline-image=true.")] string savePath = null,
            [CliArg("include-inline-image", "Also return the image inline as base64 when output-path is set (default false: path-only result). Only meaningful together with output-path.")] bool includeInlineImage = false,
            [CliArg("max-resolution", "Cap on the inline image's longest edge (e.g. 512). Only applies when an inline image is returned (no output-path, or output-path + include-inline-image=true); the output-path file keeps the requested resolution.")] int maxResolution = 0,
            [CliArg("source", "What to capture: 'screen' (default) captures the game view's own render target — what the Game window shows, incl. Screen Space - Overlay UI (HUDs) in Play Mode, opening a Game view window when none is open, in batchmode too; 'camera' renders a single camera off-screen instead, with overlay UI drawn on top on URP/HDRP (see overlay). Defaults to 'camera' when a camera name is given, or when a just-opened Game view has not rendered a frame yet; the result's message then says so.")] string source = null,
            [CliArg("overlay", "Camera captures only: draw Screen Space - Overlay UI on top of the camera image. 'auto' (default) draws it for a camera that renders to the screen, 'true' for any camera, 'false' never. Needs a Scriptable Render Pipeline (URP/HDRP).")] string overlay = null)
        {
            // The screen source draws the Game view before reading it, opening one when none exists (in
            // batchmode too) and leaving it open. Camera requests never touch the window layout.
            var wantsScreen = ResolveGameViewSource(source, camera, true, out _) == "screen";
            var gameView = GameViewState.Ready;
            if (wantsScreen)
            {
                GuardHasGpu();
                gameView = PrepareGameView();
            }

            var screenAvailable = gameView == GameViewState.Ready || gameView == GameViewState.Opened;
            var src = ResolveGameViewSource(source, camera, screenAvailable, out var fellBack);
            switch (src)
            {
                case "camera":
                {
                    GuardHasGpu();

                    var cam = ResolveCamera(camera);
                    if (cam == null)
                    {
                        if (fellBack)
                            throw new InvalidOperationException($"{ScreenUnavailableReason(gameView)}, and no camera found to capture instead.");

                        throw new ArgumentException("No camera found to capture.");
                    }

                    var messages = new List<string>();
                    var drawOverlay = ShouldDrawOverlay(overlay, GraphicsSettings.currentRenderPipeline != null,
                        cam.targetTexture == null, out var overlayWarning);
                    if (overlayWarning != null)
                        messages.Add(overlayWarning);

                    var overlayDrawn = false;
                    var result = Capture((w, h) => EncodeCameraToPng(cam, w, h, drawOverlay, ref overlayDrawn),
                        $"camera:{cam.name}", width, height, savePath, includeInlineImage, maxResolution);
                    if (drawOverlay && !overlayDrawn)
                        messages.Add("Screen Space - Overlay UI could not be drawn: the render pipeline did not report rendering the camera.");

                    if (fellBack)
                    {
                        var overlayNote = overlayDrawn
                            ? "Screen Space - Overlay UI was drawn on top of it."
                            : "Screen Space - Overlay UI is not included.";
                        var retry = gameView == GameViewState.CouldNotOpen ? "" : " Capture again to read the screen.";
                        messages.Insert(0, $"{ScreenUnavailableReason(gameView)}, so camera '{cam.name}' was captured instead of the screen. {overlayNote}{retry}");
                    }

                    result.Message = string.Join(" ", messages);

                    return result;
                }
                case "screen":
                {
                    GuardHasGpu();

                    if (!screenAvailable)
                    {
                        var advice = gameView == GameViewState.CouldNotOpen
                            ? "pass source=\"camera\" to render a camera instead."
                            : "capture again, or pass source=\"camera\" to render a camera instead.";
                        throw new InvalidOperationException($"{ScreenUnavailableReason(gameView)}: {advice}");
                    }

                    var result = Capture(EncodeScreenToPng, "screen",
                        width, height, savePath, includeInlineImage, maxResolution);
                    if (gameView == GameViewState.Opened)
                        result.Message = "No Game view window was showing, so one was opened to capture the screen. It stays open.";

                    return result;
                }
                default:
                    throw new ArgumentException($"Unknown source '{source}'. Use 'camera' or 'screen'.");
            }
        }

        /// <summary>
        /// The game-view source to capture. An omitted, empty or whitespace source means the default
        /// rather than an error: the no-argument call is the common one and should just capture the
        /// game view. Naming a camera states the intent unambiguously, so it selects the camera source
        /// rather than being silently ignored by a screen capture. When the default screen source has
        /// nothing to read (<paramref name="screenAvailable"/> false, e.g. a just-opened Game view that
        /// has not rendered a frame yet), it falls back
        /// to the camera source and sets <paramref name="fellBack"/> so the result can say so. An
        /// explicit source never falls back.
        /// </summary>
        internal static string ResolveGameViewSource(string source, string camera, bool screenAvailable, out bool fellBack)
        {
            fellBack = false;
            if (!string.IsNullOrWhiteSpace(source))
                return source.Trim().ToLowerInvariant();

            if (!string.IsNullOrEmpty(camera))
                return "camera";

            if (screenAvailable)
                return "screen";

            fellBack = true;
            return "camera";
        }

        /// <summary>
        /// Whether a camera capture draws the Screen Space - Overlay UI. Overlay canvases belong to a
        /// display, not to a camera, so 'auto' (the default) draws them only for a camera that renders
        /// to the screen (<paramref name="rendersToScreen"/>), never over a camera that feeds a texture
        /// such as a minimap. Drawing needs a Scriptable Render Pipeline; an explicit 'true' without
        /// one returns false with <paramref name="warning"/> set, while 'auto' stays quiet.
        /// </summary>
        internal static bool ShouldDrawOverlay(string overlay, bool srpActive, bool rendersToScreen, out string warning)
        {
            warning = null;
            var mode = string.IsNullOrWhiteSpace(overlay) ? "auto" : overlay.Trim().ToLowerInvariant();
            switch (mode)
            {
                case "false":
                    return false;
                case "true":
                    if (srpActive)
                        return true;

                    warning = "Screen Space - Overlay UI is not included: drawing it into a camera capture needs a Scriptable Render Pipeline (URP or HDRP).";
                    return false;
                case "auto":
                    return srpActive && rendersToScreen;
                default:
                    throw new ArgumentException($"Unknown overlay '{overlay}'. Use 'auto', 'true' or 'false'.");
            }
        }

        private const int DefaultWidth = 1280;
        private const int DefaultHeight = 720;

        [CliGroupCommand(Group, "Capture the active Scene View as a PNG. source=camera (default) renders the Scene view camera: the scene only, at width x height; source=screen captures the Scene window as drawn, with grid, gizmos and toolbars, at the window's size (as " + CommandNames.CaptureEditorWindow + " does). Returns it inline as base64, unless output-path is set (path-only result; pass include-inline-image=true to get both).", Tags = new[] { "capture" })]
        public static CaptureResult SceneView(
            [CliArg("width", "Output width in px (default 1280; capped 4096). source=camera only.")] int width = DefaultWidth,
            [CliArg("height", "Output height in px (default 720; capped 4096). source=camera only.")] int height = DefaultHeight,
            [CliArg("output-path", "Optional path to write the PNG: absolute, or relative to the project root (e.g. Screenshots/foo.png, outside Assets/ so no .meta is created). When set, the result omits the inline base64 image unless include-inline-image=true.")] string savePath = null,
            [CliArg("include-inline-image", "Also return the image inline as base64 when output-path is set (default false: path-only result). Only meaningful together with output-path.")] bool includeInlineImage = false,
            [CliArg("max-resolution", "Cap on the inline image's longest edge (e.g. 512). Only applies when an inline image is returned (no output-path, or output-path + include-inline-image=true); the output-path file keeps its full resolution.")] int maxResolution = 0,
            [CliArg("source", "What to capture: 'camera' (default) renders the Scene view camera, the scene only, at width x height; 'screen' captures the Scene window as drawn, with grid, gizmos, icons, handles and toolbars, at the window's size. Not available in batchmode.")] string source = null)
        {
            var src = string.IsNullOrWhiteSpace(source) ? "camera" : source.Trim().ToLowerInvariant();
            if (src != "camera" && src != "screen")
                throw new ArgumentException($"Unknown source '{source}'. Use 'camera' or 'screen'.");

            if (src == "screen" && Application.isBatchMode)
                throw new InvalidOperationException("Editor windows are not drawn in batchmode, so there is no Scene window to capture: use source=\"camera\".");

            GuardHasGpu();

            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
                throw new ArgumentException("No active Scene View to capture.");

            if (src == "camera")
            {
                var cam = sv.camera;
                return Capture((w, h) => EncodeCameraToPng(cam, w, h), "sceneView",
                    width, height, savePath, includeInlineImage, maxResolution);
            }

            var result = CaptureWindow(sv, Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>(), "sceneView:screen",
                savePath, includeInlineImage, maxResolution);

            // The window is read at its own size, so a size asked for here has nothing to apply to.
            if (width != DefaultWidth || height != DefaultHeight)
                result.Message = "width and height are ignored with source=screen: the Scene window is captured at its own size.";

            return result;
        }

        [CliGroupCommand(Group, "Capture an open Editor window of any type (Inspector, Console, Project, a custom window...) as a PNG at the window's size. A window behind another tab is brought to the front while it is read, then the previous tab is put back. Not available in batchmode. Returns it inline as base64, unless output-path is set (path-only result; pass include-inline-image=true to get both).", Tags = new[] { "capture" })]
        public static CaptureResult EditorWindow(
            [CliArg("window", "The window to capture: its title (e.g. Inspector) or its type name (e.g. InspectorWindow).", Required = true)] string window = null,
            [CliArg("output-path", "Optional path to write the PNG: absolute, or relative to the project root (e.g. Screenshots/foo.png, outside Assets/ so no .meta is created). When set, the result omits the inline base64 image unless include-inline-image=true.")] string savePath = null,
            [CliArg("include-inline-image", "Also return the image inline as base64 when output-path is set (default false: path-only result). Only meaningful together with output-path.")] bool includeInlineImage = false,
            [CliArg("max-resolution", "Cap on the inline image's longest edge (e.g. 512). Only applies when an inline image is returned (no output-path, or output-path + include-inline-image=true); the output-path file keeps the window's resolution.")] int maxResolution = 0)
        {
            if (string.IsNullOrWhiteSpace(window))
                throw new ArgumentException("Give the window to capture: its title (e.g. Inspector) or its type name (e.g. InspectorWindow).");

            // Batchmode opens no windows, so none has pixels to read.
            if (Application.isBatchMode)
                throw new InvalidOperationException("Editor windows are not drawn in batchmode, so there is no window to capture.");

            GuardHasGpu();

            var windows = Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>();
            var target = FindEditorWindow(windows, window);
            if (target == null)
            {
                var open = windows.Where(IsDrawable).Select(w => w.titleContent.text).Where(t => !string.IsNullOrEmpty(t))
                    .Distinct().OrderBy(t => t, StringComparer.OrdinalIgnoreCase);
                throw new ArgumentException($"No open Editor window matched '{window}' by title or type name. Open windows: {string.Join(", ", open)}.");
            }

            return CaptureWindow(target, windows, $"window:{target.titleContent.text}", savePath, includeInlineImage, maxResolution);
        }

        /// <summary>
        /// Capture <paramref name="target"/> as drawn, at its size in pixels. Only the visible tab of a
        /// dock area has pixels: a background tab is brought to the front and drawn now, so its back
        /// buffer holds the window when it is read, then the tab that was in front is put back.
        /// <paramref name="windows"/> are the open windows, to find that tab.
        /// </summary>
        private static CaptureResult CaptureWindow(UnityEditor.EditorWindow target, IEnumerable<UnityEditor.EditorWindow> windows,
            string source, string savePath, bool includeInlineImage, int maxResolution)
        {
            var previousFront = target.hasFocus ? null : windows.FirstOrDefault(w => w != target && w.hasFocus && SameHost(w, target));
            target.ShowTab();
            RepaintImmediately(target);
            try
            {
                // The window is grabbed at its full size; only the image is capped, keeping its aspect.
                var (pixelW, pixelH) = WindowPixelSize(target);
                var (outW, outH) = ClampToLongEdge(pixelW, pixelH, MaxDimension);
                return Capture((w, h) => EncodeEditorWindowToPng(target, w, h), source,
                    outW, outH, savePath, includeInlineImage, maxResolution);
            }
            finally
            {
                if (previousFront != null)
                    previousFront.ShowTab();
            }
        }

        // The view hosting the window: the dock area for a docked tab. Null when the window is closed.
        private static Object HostView(UnityEditor.EditorWindow window)
            => typeof(UnityEditor.EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) as Object;

        private static bool SameHost(UnityEditor.EditorWindow a, UnityEditor.EditorWindow b)
        {
            var host = HostView(a);
            return host != null && host == HostView(b);
        }

        /// <summary>
        /// The open window <paramref name="window"/> names, by title or type name (case-insensitive),
        /// or null. <c>FindObjectsOfTypeAll</c> also returns closed windows still waiting to be
        /// destroyed, so only a window in a dock area or window of its own is a match; among matches,
        /// the one already in front wins.
        /// </summary>
        internal static UnityEditor.EditorWindow FindEditorWindow(IEnumerable<UnityEditor.EditorWindow> windows, string window)
        {
            var name = window.Trim();
            return windows
                .Where(IsDrawable)
                .Where(w => string.Equals(w.titleContent?.text, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(w.GetType().Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(w => w.hasFocus)
                .FirstOrDefault();
        }

        // A window with no host view is closed or not shown yet: it has nothing to draw.
        private static bool IsDrawable(UnityEditor.EditorWindow window)
            => window != null && window.position.width >= 1f && window.position.height >= 1f && HostView(window) != null;

        /// <summary>
        /// The window's size in pixels: its position is in points, scaled by the display's DPI.
        /// <c>pixelsPerPoint</c> is the scale of the last view drawn, so this is right for a window on
        /// any monitor only because the window has just been drawn (see <see cref="CaptureWindow"/>).
        /// Not capped: it is the grab rectangle, and a smaller one would crop the window.
        /// </summary>
        private static (int w, int h) WindowPixelSize(UnityEditor.EditorWindow window)
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            return (Mathf.Max(1, Mathf.RoundToInt(window.position.width * scale)),
                Mathf.Max(1, Mathf.RoundToInt(window.position.height * scale)));
        }

        /// <summary>
        /// Copy the window's back buffer into <paramref name="target"/>, sized in pixels, the way
        /// <c>InternalEditorUtility.CaptureEditorWindow</c> does: grab it from the window's host view,
        /// then blit it through the same material, which flips it where the graphics API needs it.
        /// That public method passes the window's size in points as the grab rect, which crops the
        /// window whenever the display is scaled (at 150% it reads two thirds of it), so it is only
        /// the fallback when the internal grab is unavailable. Returns false when the window is not
        /// the visible tab of its dock area.
        /// </summary>
        private static bool GrabEditorWindow(UnityEditor.EditorWindow window, RenderTexture target)
        {
            if (!window.hasFocus)
                return false;

            object parent = null;
            MethodInfo grab = null;
            Material blit = null;
            try
            {
                parent = HostView(window);
                grab = parent?.GetType().GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                blit = EditorGUIUtility.LoadRequired("SceneView/BlitSceneViewCapture.mat") as Material;
            }
            catch
            {
                // Internal API changed: fall back to the public capture below.
            }

            if (grab == null || blit == null)
            {
                // Through reflection too: the public method is newer than the oldest supported Editor.
                var capture = typeof(UnityEditorInternal.InternalEditorUtility).GetMethod("CaptureEditorWindow",
                    BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(UnityEditor.EditorWindow), typeof(RenderTexture) }, null);
                return capture != null && (bool)capture.Invoke(null, new object[] { window, target });
            }

            var grabbed = RenderTexture.GetTemporary(target.descriptor);
            try
            {
                grab.Invoke(parent, new object[] { grabbed, new Rect(0, 0, target.width, target.height) });
                Graphics.Blit(grabbed, target, blit);
                return true;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(grabbed);
            }
        }

        // Draws the window now. False when the internal API is unavailable: the window then still holds
        // its last drawn frame.
        private static bool RepaintImmediately(UnityEditor.EditorWindow window)
        {
            try
            {
                var repaint = typeof(UnityEditor.EditorWindow).GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.NonPublic);
                if (repaint == null)
                    return false;

                repaint.Invoke(window, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Read the window's back buffer, orient it, then scale it to <paramref name="w"/> x
        /// <paramref name="h"/> and encode it. See <see cref="GrabEditorWindow"/>.
        /// </summary>
        private static byte[] EncodeEditorWindowToPng(UnityEditor.EditorWindow window, int w, int h)
        {
            var (pixelW, pixelH) = WindowPixelSize(window);
            var grabRt = RenderTexture.GetTemporary(pixelW, pixelH, 0, RenderTextureFormat.ARGB32);
            RenderTexture outRt = null;
            var prevActive = RenderTexture.active;
            try
            {
                if (!GrabEditorWindow(window, grabRt))
                    throw new InvalidOperationException($"'{window.titleContent.text}' could not be read: it is not the visible tab of its window.");

                var readRt = grabRt;
                if (w != pixelW || h != pixelH)
                {
                    outRt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(grabRt, outRt);
                    readRt = outRt;
                }

                RenderTexture.active = readRt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                try
                {
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    return ImageConversion.EncodeToPNG(tex);
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(grabRt);
                if (outRt != null)
                    RenderTexture.ReleaseTemporary(outRt);
            }
        }

        /// <summary>
        /// Shared capture core. Renders at the requested (clamped) size via <paramref name="renderPng"/>
        /// and writes the file when <paramref name="savePath"/> is set. The image is inlined as base64
        /// only when there is no file, or on <paramref name="includeInlineImage"/> — an output-path result
        /// is otherwise path-only, so agent tool results stay small (AUTHAPI-8).
        /// <paramref name="maxResolution"/> caps the inline image's longest edge (no-op when no inline
        /// image is returned); the saved file keeps the requested resolution. <paramref name="renderPng"/>
        /// is the source-specific encoder — the game view's render target (source=screen, incl.
        /// overlay canvases) or an off-screen camera render (source=camera / scene view).
        /// </summary>
        private static CaptureResult Capture(Func<int, int, byte[]> renderPng, string source,
            int width, int height, string savePath, bool includeInlineImage, int maxResolution)
        {
            var w = Mathf.Clamp(width, 1, MaxDimension);
            var h = Mathf.Clamp(height, 1, MaxDimension);

            // Same test as WriteIfRequested: a blank path is no file, so the image comes back inline.
            var wantsFile = !string.IsNullOrWhiteSpace(savePath);
            var wantsInline = !wantsFile || includeInlineImage;

            // Without a file the inline image is the only artifact: the cap applies to the render itself.
            if (!wantsFile && maxResolution > 0)
                (w, h) = ClampToLongEdge(w, h, maxResolution);

            var png = renderPng(w, h);
            var savedPath = WriteIfRequested(png, savePath);

            string base64 = null;
            int? inlineW = null, inlineH = null;
            if (wantsInline)
            {
                var inlinePng = png;
                if (wantsFile && maxResolution > 0 && maxResolution < Mathf.Max(w, h))
                {
                    // Re-render small for the inline copy; the file already has the full resolution.
                    var (iw, ih) = ClampToLongEdge(w, h, maxResolution);
                    inlinePng = renderPng(iw, ih);
                    inlineW = iw;
                    inlineH = ih;
                }
                base64 = Convert.ToBase64String(inlinePng);
            }

            return new CaptureResult
            {
                Width = w,
                Height = h,
                Encoding = "png",
                Base64 = base64,
                Bytes = png.Length,
                Source = source,
                SavedPath = savedPath,
                InlineWidth = inlineW,
                InlineHeight = inlineH
            };
        }

        /// <summary>Scale (w, h) down so the longest edge is at most <paramref name="maxEdge"/>, preserving aspect.</summary>
        private static (int w, int h) ClampToLongEdge(int w, int h, int maxEdge)
        {
            var longest = Mathf.Max(w, h);
            if (maxEdge <= 0 || longest <= maxEdge)
                return (w, h);

            var scale = (float)maxEdge / longest;
            return (Mathf.Max(1, Mathf.RoundToInt(w * scale)), Mathf.Max(1, Mathf.RoundToInt(h * scale)));
        }

        /// <summary>Throw when no GPU is available (-nographics), where a render would be blank.</summary>
        private static void GuardHasGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("No GPU available (the Editor runs with -nographics); cannot capture.");
        }

        /// <summary>
        /// Resolve the camera to capture: by name (if provided), else <see cref="Camera.main"/>,
        /// else the first enabled camera. Returns null when nothing matches.
        /// </summary>
        private static Camera ResolveCamera(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                // Camera.allCameras only includes enabled cameras; also scan all loaded cameras so a
                // disabled-but-named camera can still be targeted explicitly.
                var byName = Camera.allCameras.FirstOrDefault(c => c.name == name)
                    ?? PipelineUtils.FindObjectsByType<Camera>().FirstOrDefault(c => c.name == name);
                return byName;
            }

            if (Camera.main != null)
                return Camera.main;

            return Camera.allCameras.FirstOrDefault();
        }

        /// <summary>
        /// Render <paramref name="cam"/> off-screen into a temporary RenderTexture and encode the
        /// result to PNG bytes. The camera's target texture and the active RenderTexture are restored
        /// in a finally block so capturing never leaves global render state dirty.
        /// </summary>
        private static byte[] EncodeCameraToPng(Camera cam, int w, int h)
        {
            var overlayDrawn = false;
            return EncodeCameraToPng(cam, w, h, false, ref overlayDrawn);
        }

        /// <summary>
        /// <see cref="EncodeCameraToPng(Camera,int,int)"/>, optionally drawing the Screen Space -
        /// Overlay UI on top. A camera rendering into a texture never gets the overlay from the render
        /// pipeline (URP draws it only when the camera resolves to the screen), so the capture draws it
        /// itself with <see cref="ScriptableRenderContext.DrawUIOverlay"/> once the camera has rendered.
        /// Requires a Scriptable Render Pipeline; <paramref name="overlayDrawn"/> reports whether the
        /// pipeline gave the capture a chance to draw it.
        ///
        /// Drawing the overlay resizes every overlay canvas to the render target, then draws the meshes
        /// already built at the canvases' current size. So when there is overlay UI, the capture renders
        /// at the game view's size, where the canvases are laid out, and scales the result to the
        /// requested size: the layout matches the game and the live canvases keep their size. Without
        /// overlay UI it renders at the requested size, so the image is neither stretched nor upscaled.
        /// </summary>
        private static byte[] EncodeCameraToPng(Camera cam, int w, int h, bool drawOverlay, ref bool overlayDrawn)
        {
            var (renderW, renderH) = CameraRenderSize(w, h, drawOverlay && HasOverlayUi(), GetGameViewTargetSize);
            var rt = RenderTexture.GetTemporary(renderW, renderH, 24);
            RenderTexture scaledRt = null;
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            var drawn = false;
            Action<ScriptableRenderContext, Camera> drawOverlayUi = (context, rendered) =>
            {
                if (rendered != cam)
                    return;

                var cmd = new CommandBuffer { name = "Pipeline capture overlay UI" };
                cmd.SetRenderTarget(rt);
                context.ExecuteCommandBuffer(cmd);
                cmd.Release();
                context.DrawUIOverlay(cam);
                context.Submit();
                drawn = true;
            };

            if (drawOverlay)
                RenderPipelineManager.endCameraRendering += drawOverlayUi;
            try
            {
                cam.targetTexture = rt;
                cam.Render();

                var readRt = rt;
                if (renderW != w || renderH != h)
                {
                    scaledRt = RenderTexture.GetTemporary(w, h, 0);
                    Graphics.Blit(rt, scaledRt);
                    readRt = scaledRt;
                }

                RenderTexture.active = readRt;

                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                try
                {
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    return ImageConversion.EncodeToPNG(tex);
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                if (drawOverlay)
                    RenderPipelineManager.endCameraRendering -= drawOverlayUi;
                overlayDrawn = drawn;
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (scaledRt != null)
                    RenderTexture.ReleaseTemporary(scaledRt);
            }
        }

        /// <summary>
        /// The size a camera capture renders at: the game view's size when overlay UI is laid out into
        /// it (see <see cref="EncodeCameraToPng(Camera,int,int,bool,ref bool)"/>), else the requested size.
        /// </summary>
        internal static (int w, int h) CameraRenderSize(int w, int h, bool laysOutOverlayUi, Func<(int w, int h)> gameViewSize)
            => laysOutOverlayUi ? gameViewSize() : (w, h);

        /// <summary>
        /// Whether the scene has UI that <see cref="ScriptableRenderContext.DrawUIOverlay"/> lays out at
        /// the render target's size: an active Screen Space - Overlay canvas, or a UI Toolkit document.
        /// </summary>
        internal static bool HasOverlayUi()
            => PipelineUtils.FindObjectsByType<Canvas>().Any(c => c.isActiveAndEnabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
               || PipelineUtils.FindObjectsByType<UnityEngine.UIElements.UIDocument>().Any(d => d.isActiveAndEnabled);

        /// <summary>
        /// Capture the composited game view — including Screen Space - Overlay canvases and other
        /// screen-space UI that never render through a camera — and encode it to PNG, scaled to the
        /// requested size. Works in Edit Mode as well as Play Mode: the game view keeps a render
        /// target either way, and overlay UI simply does not exist yet outside Play Mode.
        ///
        /// The primary source is the game view's own render target (see
        /// <see cref="GetGameViewTargetTexture"/>): the most recently rendered game frame at its true
        /// pixel resolution, independent of window focus and OS display scaling. The old
        /// <see cref="ScreenCapture.CaptureScreenshotAsTexture()"/> path had two field-reported bugs:
        /// it reads a Screen-sized rect in GUI <em>points</em> from a backbuffer sized in physical
        /// <em>pixels</em>, cropping the frame on scaled displays (e.g. 150% OS scaling), and its
        /// result texture round-trips gamma twice in Linear projects, washing out every capture.
        /// </summary>
        private static byte[] EncodeScreenToPng(int w, int h)
        {
            var outRt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            RenderTexture fallbackRt = null;
            try
            {
                var source = GetGameViewTargetTexture(out var lookupSucceeded);
                if (ShouldRefuseScreenCapture(source != null, lookupSucceeded))
                {
                    // With no game view render target the ScreenCapture fallback below has nothing
                    // game-shaped to return: it hands back whichever editor view renders next
                    // (Hierarchy, Inspector). Refuse instead of returning a wrong image.
                    throw new InvalidOperationException(NoGameViewTargetMessage());
                }

                if (source == null)
                {
                    // Reached only when the internal PlayModeView API is unavailable, so nothing is
                    // known about the game view: ask ScreenCapture for the composited view. Less
                    // deterministic — when the editor runs unfocused it can capture whichever editor
                    // view renders next — but keeps the command working across API changes.
                    var (screenW, screenH) = GetGameViewTargetSize();
                    fallbackRt = RenderTexture.GetTemporary(screenW, screenH, 0, RenderTextureFormat.ARGB32);

                    // GetTemporary pools RTs: clear before capturing so a skipped/partial capture can
                    // never leak a stale frame from an earlier capture.
                    RenderTexture.active = fallbackRt;
                    GL.Clear(true, true, Color.black);

                    // Unbind before capturing: CaptureScreenshotIntoRenderTexture silently writes
                    // nothing while its destination is the currently-active RenderTexture (verified
                    // empirically — the capture comes back all black).
                    RenderTexture.active = prevActive;
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(fallbackRt);
                    source = fallbackRt;
                }

                // Scale to the requested output size. Default read/write semantics keep the blit
                // conversion-neutral, so the display-ready values reach the PNG unchanged (no double
                // gamma). The source holds GPU-UV-oriented content: on APIs where UV(0,0) is the top
                // (D3D, Metal, Vulkan) it arrives vertically flipped, so un-flip in the blit.
                if (SystemInfo.graphicsUVStartsAtTop)
                    Graphics.Blit(source, outRt, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else
                    Graphics.Blit(source, outRt);
                RenderTexture.active = outRt;

                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                try
                {
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    return ImageConversion.EncodeToPNG(tex);
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(outRt);
                if (fallbackRt != null)
                    RenderTexture.ReleaseTemporary(fallbackRt);
            }
        }

        /// <summary>
        /// Whether the screen source has a game view render target to read: false whenever
        /// <see cref="ShouldRefuseScreenCapture"/> would refuse. Batchmode is no exception: a Game view
        /// opened there renders into its target like an interactive one, overlay UI included.
        /// </summary>
        private static bool HasGameViewTarget()
        {
            var target = GetGameViewTargetTexture(out var lookupSucceeded);
            return !ShouldRefuseScreenCapture(target != null, lookupSucceeded);
        }

        /// <summary>What <see cref="PrepareGameView"/> found or did, which decides what the screen source can read.</summary>
        internal enum GameViewState
        {
            /// <summary>A Game view exists and its target holds a frame.</summary>
            Ready,
            /// <summary>No Game view existed: one was opened and drew a frame.</summary>
            Opened,
            /// <summary>No Game view existed: one was opened but has no frame yet.</summary>
            OpenedNotRendered,
            /// <summary>A Game view exists but has no frame yet.</summary>
            NotRendered,
            /// <summary>No Game view existed and none could be opened.</summary>
            CouldNotOpen
        }

        /// <summary>
        /// Draws the main play mode view now, so the screen source reads this frame rather than whatever
        /// the view last drew: nothing else repaints it in batchmode Edit Mode, or while it is a
        /// background tab. It is the view the screen source reads (see <see cref="GetGameViewTargetTexture"/>):
        /// a Game view, or the Device Simulator when that is the one in use. Opens a Game view when no
        /// play mode view exists and leaves it open. A background tab is brought to the front for the
        /// repaint and the previous tab put back, as the window captures do; keyboard focus does not
        /// move. In Play Mode the view only draws from its GUI when the player loop has a frame due, in
        /// which case its target still holds the player loop's latest frame.
        /// </summary>
        private static GameViewState PrepareGameView()
        {
            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            var windows = Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>();
            var window = FindPlayModeView(windows, gameViewType);
            var existed = window != null;
            if (!existed)
            {
                if (gameViewType == null)
                {
                    // Internal API changed: let the screen path's own lookup decide.
                    return HasGameViewTarget() ? GameViewState.Ready : GameViewState.CouldNotOpen;
                }

                try
                {
                    window = UnityEditor.EditorWindow.GetWindow(gameViewType, false, null, false);
                }
                catch
                {
                    window = null;
                }

                if (window == null)
                    return GameViewState.CouldNotOpen;
            }

            // Show() makes a window restored invisible from a saved layout visible again (GetWindow only
            // does that when it also takes focus); ShowTab() brings a background tab to the front.
            var previousFront = window.hasFocus ? null : windows.FirstOrDefault(w => w != window && w.hasFocus && SameHost(w, window));
            window.Show();
            window.ShowTab();
            var drew = RendersInGui() && RepaintImmediately(window);
            if (previousFront != null)
                previousFront.ShowTab();

            if (existed)
                return HasGameViewTarget() ? GameViewState.Ready : GameViewState.NotRendered;

            return drew && HasGameViewTarget() ? GameViewState.Opened : GameViewState.OpenedNotRendered;
        }

        /// <summary>
        /// The open view the screen source reads: the main play mode view (the Game view or Device
        /// Simulator last in use), else any open play mode view, else null. Falls back to Game views
        /// when the internal play mode view API is unavailable.
        /// </summary>
        private static UnityEditor.EditorWindow FindPlayModeView(UnityEditor.EditorWindow[] windows, Type gameViewType)
        {
            var playModeViewType = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
            UnityEditor.EditorWindow main = null;
            try
            {
                main = playModeViewType?.GetMethod("GetMainPlayModeView", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    ?.Invoke(null, null) as UnityEditor.EditorWindow;
            }
            catch
            {
                // Internal API changed: look the views up by type below.
            }

            if (main != null && IsDrawable(main))
                return main;

            var viewType = playModeViewType ?? gameViewType;
            return viewType == null ? null : windows.FirstOrDefault(w => viewType.IsInstanceOfType(w) && IsDrawable(w));
        }

        // Whether a Game view repaint draws the game at all: in Play Mode only when the player loop has a frame due.
        private static bool RendersInGui()
        {
            try
            {
                var playModeView = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
                var rendersInGui = playModeView?.GetProperty("renderViewCallNeededInOnGUI", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.GetValue(null) as bool?;
                // Unity 2022 renders an Edit Mode Game view directly from OnGUI.
                return rendersInGui ?? !EditorApplication.isPlaying;
            }
            catch
            {
                return false;
            }
        }

        private static string ScreenUnavailableReason(GameViewState state)
        {
            switch (state)
            {
                case GameViewState.OpenedNotRendered:
                    return "No Game view window was showing, so one was opened (it stays open), but it has not rendered a frame yet";
                case GameViewState.NotRendered:
                    return "The Game view has not rendered a frame yet";
                default:
                    return "No Game view window was showing, and none could be opened";
            }
        }

        private static string NoGameViewTargetMessage()
            => "No Game view render target to capture (no Game view window is open): open a Game view window, or pass source=\"camera\" to render a camera instead.";

        /// <summary>
        /// Whether a screen capture must refuse rather than fall back to <c>ScreenCapture</c>: the
        /// <c>PlayModeView</c> lookup answered and there is no game view render target, so the
        /// fallback would return an editor panel labelled as a game view capture. A failed lookup
        /// proves nothing and keeps the fallback.
        /// </summary>
        internal static bool ShouldRefuseScreenCapture(bool hasTarget, bool lookupSucceeded)
            => !hasTarget && lookupSucceeded;

        /// <summary>
        /// The game view's own render target (via the internal <c>PlayModeView</c> API), or null when
        /// there is none or the API is unavailable — <paramref name="lookupSucceeded"/> tells the two
        /// apart. This is the most reliable "what the game view shows" source: the
        /// <c>ScreenCapture</c> APIs capture whichever editor view happens to render, which is
        /// focus-dependent — an unfocused/background editor (the normal state for an agent-driven
        /// editor) can hand back an editor panel (Hierarchy, Inspector) instead of the game. The
        /// target texture holds the game view's most recently rendered frame — overlay canvases
        /// included — at its true pixel resolution, independent of focus and display scaling.
        /// </summary>
        private static RenderTexture GetGameViewTargetTexture(out bool lookupSucceeded)
        {
            lookupSucceeded = false;
            try
            {
                var playModeView = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
                var getMain = playModeView?.GetMethod("GetMainPlayModeView",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (getMain == null)
                    return null;

                var view = getMain.Invoke(null, null);
                if (view == null)
                {
                    // The API answered: there is no game view window at all.
                    lookupSucceeded = true;
                    return null;
                }

                var field = playModeView.GetField("m_TargetTexture",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null)
                    return null;

                lookupSucceeded = true;
                var rt = field.GetValue(view) as RenderTexture;
                return rt != null && rt.IsCreated() ? rt : null;
            }
            catch
            {
                // Internal API changed — callers fall back to the ScreenCapture path.
                lookupSucceeded = false;
                return null;
            }
        }

        /// <summary>
        /// Size of the game view's actual render target, in pixels. <c>Screen.width/height</c> in
        /// editor main-thread code report the CURRENT GUI view — which is focus-dependent and can be
        /// the main editor window rather than the game view (observed as black bars / truncated
        /// captures). The internal PlayModeView API reports the true target size regardless of focus
        /// and display scaling; fall back to <c>Screen</c> when the internal API is unavailable.
        /// Rounded away from zero, as the engine rounds it when it allocates the target, so a
        /// fractional size (475.5) gives the target's real size and the size overlay canvases are laid
        /// out at. Not capped to <see cref="MaxDimension"/>: overlay UI must be drawn at the size it is
        /// laid out at, and the image is scaled to the (capped) requested size afterwards.
        /// </summary>
        private static (int w, int h) GetGameViewTargetSize()
        {
            var w = Screen.width;
            var h = Screen.height;
            try
            {
                var playModeView = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
                var method = playModeView?.GetMethod("GetMainPlayModeViewTargetSize",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (method != null)
                {
                    var size = (Vector2)method.Invoke(null, null);
                    if (size.x >= 1f && size.y >= 1f)
                    {
                        w = (int)Math.Round(size.x, MidpointRounding.AwayFromZero);
                        h = (int)Math.Round(size.y, MidpointRounding.AwayFromZero);
                    }
                }
            }
            catch
            {
                // Internal API changed/unavailable — the Screen fallback still works whenever the
                // game view is the current context.
            }

            return (Mathf.Max(1, w), Mathf.Max(1, h));
        }

        /// <summary>
        /// When <paramref name="savePath"/> is set, write the PNG there (absolute, or relative to the
        /// project root; not confined to the project), import it when it lies under Assets/, and return
        /// the absolute path. Returns null when no path was requested.
        /// </summary>
        private static string WriteIfRequested(byte[] png, string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
                return null;

            // A relative path resolves from the project root, so a capture lands under Assets/ only when
            // the path names it explicitly.
            var absolute = ProjectPaths.ResolveFile(savePath);
            var directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(absolute, png);

            // Only a file under Assets/ is an asset; import it so the AssetDatabase sees it. Anywhere
            // else the capture is a plain file and gets no .meta.
            var assetPath = ToAssetPath(absolute, Application.dataPath, FileSystemIgnoresCase(Application.dataPath));
            if (assetPath != null)
                AssetDatabase.ImportAsset(assetPath);

            return absolute;
        }

        /// <summary>
        /// The "Assets/..." path of <paramref name="absolutePath"/> when it lies under
        /// <paramref name="dataPath"/> (the project's Assets folder), or null when it does not.
        /// <paramref name="ignoreCase"/> says whether the file system treats "assets/" as the same
        /// folder as "Assets/": on a case-sensitive one it is another folder, outside the AssetDatabase.
        /// </summary>
        internal static string ToAssetPath(string absolutePath, string dataPath, bool ignoreCase)
        {
            var file = Path.GetFullPath(absolutePath).Replace('\\', '/');
            var assets = Path.GetFullPath(dataPath).Replace('\\', '/').TrimEnd('/');
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return file.StartsWith(assets + "/", comparison)
                ? "Assets" + file.Substring(assets.Length)
                : null;
        }

        /// <summary>
        /// Whether the file system holding <paramref name="directory"/> ignores case: true on Windows and on
        /// macOS by default, false on Linux. Probed on disk rather than guessed from the OS, since macOS
        /// volumes vary. The upper- and lower-case spellings of an existing mixed-case path (Assets is one)
        /// both exist only when case is ignored.
        /// </summary>
        internal static bool FileSystemIgnoresCase(string directory)
            => Directory.Exists(directory.ToUpperInvariant()) && Directory.Exists(directory.ToLowerInvariant());
    }

    /// <summary>
    /// Result of a capture command: the rendered dimensions, the source, the absolute path
    /// the PNG was written to (null when not saved), and the base64 payload — omitted from the JSON
    /// when an output-path result is path-only (AUTHAPI-8).
    /// </summary>
    [Serializable]
    class CaptureResult
    {
        /// <summary>Rendered width in pixels (after clamping).</summary>
        [JsonProperty("width")]
        public int Width { get; set; }

        /// <summary>Rendered height in pixels (after clamping).</summary>
        [JsonProperty("height")]
        public int Height { get; set; }

        /// <summary>Image encoding; always "png".</summary>
        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        /// <summary>Base64-encoded PNG bytes; null (omitted) when output-path is set without include-inline-image.</summary>
        [JsonProperty("base64", NullValueHandling = NullValueHandling.Ignore)]
        public string Base64 { get; set; }

        /// <summary>Length of the raw PNG byte array.</summary>
        [JsonProperty("bytes")]
        public int Bytes { get; set; }

        /// <summary>What was captured, e.g. "camera:Main Camera" or "sceneView".</summary>
        [JsonProperty("source")]
        public string Source { get; set; }

        /// <summary>Absolute path the PNG was also written to, or null.</summary>
        [JsonProperty("savedPath")]
        public string SavedPath { get; set; }

        /// <summary>Inline image width when max-resolution downscaled it below the saved file's; omitted otherwise.</summary>
        [JsonProperty("inlineWidth", NullValueHandling = NullValueHandling.Ignore)]
        public int? InlineWidth { get; set; }

        /// <summary>Inline image height when max-resolution downscaled it below the saved file's; omitted otherwise.</summary>
        [JsonProperty("inlineHeight", NullValueHandling = NullValueHandling.Ignore)]
        public int? InlineHeight { get; set; }

        /// <summary>
        /// What the caller should know about the capture, e.g. that a Game view was opened for it, or
        /// that a camera was captured instead of the screen. Empty when there is nothing to say.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; } = "";
    }
}
