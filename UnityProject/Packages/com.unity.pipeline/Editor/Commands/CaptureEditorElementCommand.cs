#if UNITY_6000_7_OR_NEWER
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Runtime.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Captures a UI Toolkit <see cref="VisualElement"/> from an <see cref="EditorWindow"/> to a PNG
    /// and returns it base64-encoded (and writes it to disk). The element is located by a USS-like
    /// selector within the window's <see cref="EditorWindow.rootVisualElement"/>.
    ///
    /// Selection, capture, and encoding are shared with the runtime command via
    /// <see cref="VisualElementCaptureSupport"/>; this command only resolves the target window.
    /// </summary>
    static class CaptureEditorElementCommand
    {
        const string Group = "capture";

        [CliGroupCommand(Group,
            "Capture a UI Toolkit VisualElement (by selector) from an EditorWindow to a PNG; returns path + base64.",
            MainThreadRequired = true,
            Tags = new[] { "capture", "ui" })]
        public static CaptureElementResponse EditorElement(
            [CliArg("window", "EditorWindow type name (e.g. InspectorWindow) or window title to capture from.", Required = true)] string window = "",
            [CliArg("selector", "Element selector: '#name', '.class', a type name (e.g. Button), descendant (space) / child ('>') chains, optional pseudo-states (:checked,:hover,:focus,:active,:enabled,:disabled,:not(...)).", Required = true)] string selector = "",
            [CliArg("output-path", "Output PNG path (absolute, or relative to the project root). Defaults to a timestamped file under <project>/Temp/pipeline-screenshots/.")] string output = "")
        {
            if (string.IsNullOrWhiteSpace(window))
                return CaptureElementResponse.Fail("A 'window' (EditorWindow type name or title) is required.");
            if (string.IsNullOrWhiteSpace(selector))
                return CaptureElementResponse.Fail("A 'selector' is required.");

            var target = ResolveWindow(window);
            if (target == null)
                return CaptureElementResponse.Fail(
                    $"No open EditorWindow matched '{window}' (by type name or title). Open the window first.");

            var root = target.rootVisualElement;
            if (root == null)
                return CaptureElementResponse.Fail($"EditorWindow '{window}' has no rootVisualElement.");

            var element = VisualElementCaptureSupport.ResolveElement(root, selector);
            if (element == null)
                return CaptureElementResponse.Fail(
                    $"No element matched selector '{selector}' in window '{window}'.");

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var defaultDir = Path.Combine(projectRoot, "Temp", "pipeline-screenshots");
            return VisualElementCaptureSupport.CaptureAndRespond(
                element, selector, $"window:{window}", output,
                relativeBaseDir: projectRoot, defaultDir: defaultDir, prefix: "element");
        }

        /// <summary>
        /// Find an open EditorWindow whose runtime type name or title matches <paramref name="window"/>.
        /// Uses <c>Resources.FindObjectsOfTypeAll</c> so hidden/utility windows are included too, but
        /// prefers a match whose root is actually attached to a panel — <c>FindObjectsOfTypeAll</c> can
        /// also return stale/backup window instances whose <c>rootVisualElement</c> has no panel (and
        /// therefore cannot be captured).
        /// </summary>
        static EditorWindow ResolveWindow(string window)
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();

            bool Matches(EditorWindow w) =>
                w.GetType().Name == window ||
                (w.titleContent != null && w.titleContent.text == window);

            return windows.FirstOrDefault(w => Matches(w)
                       && w.rootVisualElement != null && w.rootVisualElement.panel != null)
                   ?? windows.FirstOrDefault(Matches);
        }
    }
}
#endif
