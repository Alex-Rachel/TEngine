#if UNITY_6000_7_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Runtime.Commands
{
    /// <summary>
    /// Shared, runtime-safe helpers behind the visual-element capture commands
    /// (<c>capture_editor_element</c> and <c>capture_runtime_element</c>). Lives in the runtime
    /// assembly with no <c>UnityEditor</c> dependency so both the Editor command and the
    /// player-side runtime command can use it.
    ///
    /// Capture uses <see cref="VisualElementCaptureExtensions.CaptureToRenderTexture"/> (runtime
    /// module) and encodes the PNG here, rather than the editor-only
    /// <c>VisualElementCaptureEditorExtensions.CaptureToPNG</c>, so the same path works in a Player.
    ///
    /// Element selection maps a small USS-like selector string onto UQuery's public
    /// <see cref="UQueryBuilder{T}"/> (the engine's USS string parser is internal). Supported:
    /// descendant (space) and child (<c>&gt;</c>) combinators; each simple part is
    /// <c>Type#name.class1.class2</c> (any subset); optional pseudo-state suffixes
    /// <c>:checked :hover :focus :active :enabled :disabled</c> and <c>:not(&lt;state&gt;)</c>.
    /// </summary>
    static class VisualElementCaptureSupport
    {
        /// <summary>
        /// Returns a failure response when no GPU is available (batchmode/headless), where a capture
        /// would read back a blank image; otherwise returns null (capture may proceed).
        /// </summary>
        public static CaptureElementResponse GpuUnavailableResponse()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return CaptureElementResponse.Fail("No GPU available (batchmode/headless); cannot capture.");
            return null;
        }

        /// <summary>
        /// Resolve the first <see cref="VisualElement"/> under <paramref name="root"/> matching
        /// <paramref name="selector"/>, or null if the selector is empty/invalid or nothing matches.
        /// </summary>
        public static VisualElement ResolveElement(VisualElement root, string selector)
        {
            if (root == null || string.IsNullOrWhiteSpace(selector))
                return null;

            if (!TryBuildQuery(root, selector, out var query))
                return null;

            return query.First();
        }

        /// <summary>
        /// Resolve the selector against each root in order, returning the first match (and the root
        /// it was found under via <paramref name="matchedRoot"/>), or null if none match.
        /// </summary>
        public static VisualElement ResolveElement(IEnumerable<VisualElement> roots, string selector,
            out VisualElement matchedRoot)
        {
            matchedRoot = null;
            if (roots == null)
                return null;

            foreach (var root in roots)
            {
                var element = ResolveElement(root, selector);
                if (element != null)
                {
                    matchedRoot = root;
                    return element;
                }
            }

            return null;
        }

        /// <summary>
        /// Capture <paramref name="element"/> to PNG bytes at its own pixel size. Restores the active
        /// RenderTexture and releases all temporaries so the capture leaves no global render state.
        /// Throws <see cref="InvalidOperationException"/> for camera-drawn (world-space) or detached
        /// panels (surfaced from <see cref="VisualElementCaptureExtensions.CaptureToRenderTexture"/>).
        /// </summary>
        public static byte[] CaptureElementToPng(VisualElement element, out int width, out int height)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            var rt = element.CaptureToRenderTexture();
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                width = rt.width;
                height = rt.height;

                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply(false, false);
                return tex.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prevActive;
                rt.Release();
                DestroyObject(rt);
                if (tex != null)
                    DestroyObject(tex);
            }
        }

        /// <summary>
        /// Resolve the output path. An explicit rooted path is used as-is; an explicit relative path
        /// is resolved against <paramref name="relativeBaseDir"/>; an empty path produces a
        /// timestamped <c>&lt;prefix&gt;_yyyyMMdd_HHmmss_fff.png</c> file under
        /// <paramref name="defaultDir"/>.
        /// </summary>
        public static string ResolveOutputPath(string output, string relativeBaseDir, string defaultDir, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(output))
            {
                return Path.IsPathRooted(output)
                    ? output
                    : Path.GetFullPath(Path.Combine(relativeBaseDir, output));
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            return Path.Combine(defaultDir, $"{prefix}_{stamp}.png");
        }

        /// <summary>Write PNG bytes to <paramref name="path"/>, creating the directory if needed.</summary>
        public static void WritePng(string path, byte[] png)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, png);
        }

        /// <summary>
        /// Capture the resolved <paramref name="element"/>, write the PNG to disk, and build a
        /// populated success response (or a failure response if the panel cannot be captured). This
        /// is the shared tail both commands run once they have an element + a panel root.
        /// </summary>
        public static CaptureElementResponse CaptureAndRespond(VisualElement element, string selector,
            string source, string output, string relativeBaseDir, string defaultDir, string prefix)
        {
            var gpuGuard = GpuUnavailableResponse();
            if (gpuGuard != null)
                return gpuGuard;

            try
            {
                var png = CaptureElementToPng(element, out var width, out var height);
                var path = ResolveOutputPath(output, relativeBaseDir, defaultDir, prefix);
                WritePng(path, png);

                return new CaptureElementResponse
                {
                    Success = true,
                    Selector = selector,
                    Source = source,
                    Path = path,
                    Width = width,
                    Height = height,
                    Encoding = "png",
                    Base64 = Convert.ToBase64String(png),
                    Bytes = png.Length,
                    Message = $"Captured {source} to {path}"
                };
            }
            catch (InvalidOperationException ex)
            {
                // The capture API throws this for camera-drawn (world-space) or detached panels.
                return CaptureElementResponse.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                return CaptureElementResponse.Fail($"Failed to capture '{selector}' from {source}: {ex.Message}");
            }
        }

        // ---- Selector parsing -------------------------------------------------------------------

        // Build a UQuery from the selector, mapping tokens onto the public UQueryBuilder fluent API.
        static bool TryBuildQuery(VisualElement root, string selector, out UQueryBuilder<VisualElement> query)
        {
            query = root.Query();

            var parts = Tokenize(selector);
            if (parts.Count == 0)
                return false;

            for (int i = 0; i < parts.Count; i++)
            {
                var (isChild, partStr) = parts[i];
                if (!TryParsePart(partStr, out var part))
                    return false;

                if (i == 0)
                    query = query.Name(part.Name);
                else
                    query = isChild
                        ? query.Children<VisualElement>(part.Name)
                        : query.Descendents<VisualElement>(part.Name);

                foreach (var cls in part.Classes)
                    query = query.Class(cls);

                if (part.Type != null)
                {
                    var typeName = part.Type;
                    query = query.Where(e => TypeNameMatches(e, typeName));
                }

                foreach (var pseudo in part.Pseudos)
                {
                    if (!TryApplyPseudo(ref query, pseudo.Name, pseudo.Negate))
                        return false;
                }
            }

            return true;
        }

        // Split a selector into simple parts, flagging each with whether it follows a '>' (child)
        // combinator. Whitespace separates descendant parts; '>' is treated as its own token.
        static List<(bool isChild, string part)> Tokenize(string selector)
        {
            var result = new List<(bool, string)>();
            var raw = selector.Replace(">", " > ")
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            bool nextIsChild = false;
            foreach (var token in raw)
            {
                if (token == ">")
                {
                    nextIsChild = true;
                    continue;
                }

                result.Add((nextIsChild, token));
                nextIsChild = false;
            }

            return result;
        }

        class SelectorPart
        {
            public string Type;
            public string Name;
            public readonly List<string> Classes = new List<string>();
            public readonly List<(bool Negate, string Name)> Pseudos = new List<(bool, string)>();
        }

        // Parse "Type#name.class1.class2" with optional ":pseudo" / ":not(pseudo)" suffixes.
        static bool TryParsePart(string raw, out SelectorPart part)
        {
            part = new SelectorPart();

            var simple = ExtractPseudos(raw, part.Pseudos);
            if (simple == null)
                return false;

            int i = 0;
            var sb = new StringBuilder();

            // Leading run (before any '#' or '.') is the type token.
            while (i < simple.Length && simple[i] != '#' && simple[i] != '.')
                sb.Append(simple[i++]);
            if (sb.Length > 0)
            {
                var type = sb.ToString();
                if (type != "*")
                    part.Type = type;
            }

            while (i < simple.Length)
            {
                char kind = simple[i++];
                sb.Clear();
                while (i < simple.Length && simple[i] != '#' && simple[i] != '.')
                    sb.Append(simple[i++]);

                var value = sb.ToString();
                if (value.Length == 0)
                    return false; // dangling '#' or '.'

                if (kind == '#')
                {
                    if (part.Name != null)
                        return false; // more than one id
                    part.Name = value;
                }
                else
                {
                    part.Classes.Add(value);
                }
            }

            return true;
        }

        // Strip ":pseudo" and ":not(pseudo)" suffixes into 'pseudos', returning the simple-selector
        // remainder, or null if a pseudo is malformed.
        static string ExtractPseudos(string raw, List<(bool Negate, string Name)> pseudos)
        {
            int idx;
            while ((idx = raw.IndexOf(':')) >= 0)
            {
                var head = raw.Substring(0, idx);
                var rest = raw.Substring(idx + 1);

                if (rest.StartsWith("not(", StringComparison.Ordinal))
                {
                    int close = rest.IndexOf(')');
                    if (close < 0)
                        return null;
                    var inner = rest.Substring(4, close - 4).Trim().TrimStart(':');
                    if (inner.Length == 0)
                        return null;
                    pseudos.Add((true, inner.ToLowerInvariant()));
                    raw = head + rest.Substring(close + 1);
                }
                else
                {
                    int next = rest.IndexOf(':');
                    var name = next < 0 ? rest : rest.Substring(0, next);
                    if (name.Length == 0)
                        return null;
                    pseudos.Add((false, name.ToLowerInvariant()));
                    raw = head + (next < 0 ? string.Empty : rest.Substring(next));
                }
            }

            return raw;
        }

        // Apply one pseudo-state to the query, honoring negation. Returns false for an unknown state.
        static bool TryApplyPseudo(ref UQueryBuilder<VisualElement> query, string name, bool negate)
        {
            bool on = !negate;
            switch (name)
            {
                case "checked": query = on ? query.Checked() : query.NotChecked(); return true;
                case "hover": query = on ? query.Hovered() : query.NotHovered(); return true;
                case "focus": query = on ? query.Focused() : query.NotFocused(); return true;
                case "active": query = on ? query.Active() : query.NotActive(); return true;
                case "enabled": query = on ? query.Enabled() : query.NotEnabled(); return true;
                case "disabled": query = on ? query.NotEnabled() : query.Enabled(); return true;
                default: return false;
            }
        }

        // Match a type token against the element's runtime type name or any of its base type names,
        // so "Button" matches a Button and "VisualElement" matches everything. Avoids reflecting the
        // generic OfType<T> over a runtime-resolved Type.
        static bool TypeNameMatches(VisualElement element, string typeName)
        {
            for (var t = element.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                if (string.Equals(t.Name, typeName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static void DestroyObject(Object obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
#endif
            Object.Destroy(obj);
        }
    }

    /// <summary>
    /// Result of a visual-element capture command: the PNG payload (base64), its dimensions, the
    /// panel/source it was rendered from, and the path it was written to. JSON property names mirror
    /// the GameView capture command's result shape (PR #13) for easy convergence later.
    /// </summary>
    [Serializable]
    class CaptureElementResponse
    {
        /// <summary>Whether the capture succeeded.</summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>Human-readable message (the error text on failure).</summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>The selector used to locate the element.</summary>
        [JsonProperty("selector")]
        public string Selector { get; set; }

        /// <summary>What was captured, e.g. "window:Inspector" or "panel:MainUI".</summary>
        [JsonProperty("source")]
        public string Source { get; set; }

        /// <summary>Absolute filesystem path the PNG was written to.</summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        /// <summary>Captured width in pixels.</summary>
        [JsonProperty("width")]
        public int Width { get; set; }

        /// <summary>Captured height in pixels.</summary>
        [JsonProperty("height")]
        public int Height { get; set; }

        /// <summary>Image encoding; always "png".</summary>
        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        /// <summary>Base64-encoded PNG bytes.</summary>
        [JsonProperty("base64")]
        public string Base64 { get; set; }

        /// <summary>Length of the raw PNG byte array.</summary>
        [JsonProperty("bytes")]
        public int Bytes { get; set; }

        public static CaptureElementResponse Fail(string message) => new CaptureElementResponse
        {
            Success = false,
            Message = message
        };
    }
}
#endif
