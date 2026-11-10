using Unity.Pipeline.Config;
using Unity.Pipeline.Editor.BuildProcessors;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Draws an experimental-status warning, then a status HelpBox above two toggles deciding whether
    /// the Pipeline is compiled into development and release builds, followed by
    /// RuntimePipelineConfig's fields, grouped "Server" / "Runtime Behavior" via [Header]. The toggles
    /// are not config fields: each writes a scripting define symbol for every installed build target,
    /// because a define is the only thing that can keep the Pipeline's assemblies out of a player. The
    /// remaining fields are disabled while neither toggle is on — there is nothing to configure for a
    /// server that will not be built — and, separately, every field except maxWorkItemsPerFrame is also
    /// disabled during Play Mode: RuntimePipelineBootstrap already loaded its own (separate) config
    /// instance by the time this page could be edited, so changes here would silently do nothing until
    /// the next Play session or build — same as a built Player, whose config is frozen at build time.
    /// maxWorkItemsPerFrame is the one exception to the Play Mode rule — RuntimePipelineDriver.Update()
    /// polls it live (see RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame), so it stays editable
    /// and takes effect immediately.
    /// Never reached by clicking an asset in the Project window (the config is never a saved asset)
    /// — hosted directly by RuntimePipelineSettingsProvider's Project Settings page instead, via
    /// Editor.CreateEditor, which works fine on an in-memory instance.
    /// </summary>
    [CustomEditor(typeof(RuntimePipelineConfig))]
    class RuntimePipelineConfigEditor : UnityEditor.Editor
    {
        internal const string DevelopmentToggleLabel = "Enable in Development & Profiled Builds";
        internal const string ReleaseToggleLabel = "Enable in Release Builds";
        internal const string ExperimentalNotice =
            "Runtime Pipeline is experimental: this workflow may change in future versions. " +
            "It currently targets desktop (Windows, macOS, Linux) Player builds; " +
            "other platforms are not officially supported yet.";

        private string m_LastWriteReport;

        /// <summary>
        /// The width of the label column on this page. A toggle draws its box at labelWidth, and a
        /// property label narrower than the text is clipped, so the column has to fit the longest
        /// label drawn here. Measured rather than fixed: the toggle labels are long enough that a
        /// constant goes stale as soon as one is reworded.
        /// </summary>
        internal static float LabelWidth()
        {
            // Covers the config fields, whose labels Unity generates from the property names.
            var widest = 200f;

            foreach (var label in new[] { DevelopmentToggleLabel, ReleaseToggleLabel })
                widest = Mathf.Max(widest, EditorStyles.label.CalcSize(new GUIContent(label)).x);

            // Keeps the toggle box off the last character.
            return widest + 8f;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth();

            var inDevelopmentBuilds = PipelineBuildSymbols.IsDefinedForActiveTarget(PipelineBuildSymbols.Development);
            var inReleaseBuilds = PipelineBuildSymbols.IsDefinedForActiveTarget(PipelineBuildSymbols.Release);

            foreach (var notice in GetPageNotices(inDevelopmentBuilds, inReleaseBuilds))
            {
                EditorGUILayout.HelpBox(notice.Message, notice.Type);
                EditorGUILayout.Space();
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                DrawBuildToggle(DevelopmentToggleLabel, PipelineBuildSymbols.Development, inDevelopmentBuilds);
                DrawBuildToggle(ReleaseToggleLabel, PipelineBuildSymbols.Release, inReleaseBuilds);

                EditorGUILayout.HelpBox(
                    "Each toggle adds or removes a scripting define symbol for every installed build target, so turning one on recompiles the project.",
                    MessageType.None);

                if (!string.IsNullOrEmpty(m_LastWriteReport))
                    EditorGUILayout.HelpBox(m_LastWriteReport, MessageType.Info);

                DrawDivergenceWarning(PipelineBuildSymbols.Development, inDevelopmentBuilds, "development");
                DrawDivergenceWarning(PipelineBuildSymbols.Release, inReleaseBuilds, "release");
            }

            var inAnyBuild = inDevelopmentBuilds || inReleaseBuilds;

            // Nothing else here matters until the Pipeline is actually built into something — grey
            // it all out rather than let you tune settings for a server that will not run.
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !inAnyBuild))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("port"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableAuditLogging"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("autoStart"));
            }

            // Polled live every frame by RuntimePipelineDriver.Update(), so it stays editable (and
            // useful to tune) even while playing, unlike everything else on this page — but only
            // when there's actually a driver that could be running to tune.
            using (new EditorGUI.DisabledScope(!inAnyBuild))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxWorkItemsPerFrame"));
            }

            // The on-demand method-reload link.xml. Build-time only, so Play Mode does not
            // freeze it; applied to development builds only, and pointless unless they carry the
            // Pipeline to receive reload pushes, hence the gate.
            using (new EditorGUI.DisabledScope(!inDevelopmentBuilds))
            {
                DrawMethodReloadLinkXmlSection();
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Read-only while in Play Mode, except Max Work Items Per Frame: the running " +
                    "driver already loaded its own settings, so edits to the other fields would only " +
                    "take effect on the next Play session or build. Max Work Items Per Frame is " +
                    "re-read live every frame, so it can be tuned while playing.",
                    MessageType.Info);
            }

            // WithoutUndo on purpose: this config is never an asset, and Unity files an undo record
            // for a non-persistent object under the active scene, dirtying it.
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private void DrawBuildToggle(string label, string symbol, bool current)
        {
            var next = EditorGUILayout.Toggle(label, current);
            if (next == current)
                return;

            var changed = PipelineDefineSymbols.SetForAllTargets(symbol, next);
            if (next && symbol == PipelineBuildSymbols.Release)
                RuntimePipelineSettingsMigration.MarkCurrentVersion();
            m_LastWriteReport = changed.Length == 0
                ? $"{symbol} was already {(next ? "defined" : "undefined")} for every installed build target."
                : $"{(next ? "Defined" : "Undefined")} {symbol} for: {string.Join(", ", changed)}.";
        }

        private static void DrawDivergenceWarning(string symbol, bool expected, string description)
        {
            var differing = PipelineDefineSymbols.TargetsWhereDiffers(symbol, expected);
            if (differing.Length == 0)
                return;

            EditorGUILayout.HelpBox(
                $"The {description} setting shows the active build target, but these targets disagree: " +
                $"{string.Join(", ", differing)}. Toggle it to apply the same value everywhere.",
                MessageType.Warning);
        }

        /// <summary>The "Method Reload" section: what reloaded code can reach on an IL2CPP device, and
        /// the on-demand link.xml (<see cref="MethodReloadLinkXml"/>) that widens it — status, Generate /
        /// Regenerate, Delete.</summary>
        static void DrawMethodReloadLinkXmlSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Method Reload", EditorStyles.boldLabel);
            var summary = MethodReloadLinkXml.Describe();
            EditorGUILayout.HelpBox(
                "On an IL2CPP device, stripping can remove code you intend to use with Method Reload. " +
                "This will generate a link.xml file that tries to preserve the types of your assemblies you are using.\n\n" +
                (summary == null
                    ? "No link.xml for Method Reload at the moment."
                    : "Stripping exceptions in place: " + summary + ". Regenerate after larger code changes; " +
                      "Delete to fall back to plain stripping.") +
                "\n\nApplies to development builds only, while " + DevelopmentToggleLabel + " is on. File: " +
                MethodReloadLinkXml.FileRelativePath, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(summary == null ? "Generate link.xml" : "Regenerate link.xml"))
                {
                    var stats = MethodReloadLinkXml.Generate();
                    Debug.Log($"Pipeline: wrote {MethodReloadLinkXml.FileRelativePath} — {stats}");
                }
                using (new EditorGUI.DisabledScope(summary == null))
                {
                    if (GUILayout.Button("Delete link.xml"))
                        MethodReloadLinkXml.Delete();
                }

                if (GUILayout.Button("Open the documentation about stripping"))
                {
                    var unityVersion = InternalEditorUtility.GetUnityVersion();
                    Application.OpenURL($"https://docs.unity3d.com/{unityVersion.Major}.{unityVersion.Minor}/Documentation/Manual/managed-code-stripping.html");
                }
            }
        }

        /// <summary>
        /// The HelpBoxes drawn at the top of the page, in order: the experimental-status warning,
        /// then the build status. OnInspectorGUI draws exactly this list, so a test of it is a test
        /// of what the page shows without rendering IMGUI.
        /// </summary>
        /// <param name="inDevelopmentBuilds">Whether the Pipeline is compiled into development builds.</param>
        /// <param name="inReleaseBuilds">Whether the Pipeline is compiled into release builds.</param>
        internal static (string Message, MessageType Type)[] GetPageNotices(bool inDevelopmentBuilds, bool inReleaseBuilds)
        {
            return new[]
            {
                (ExperimentalNotice, MessageType.Warning),
                GetStatusMessage(inDevelopmentBuilds, inReleaseBuilds),
            };
        }

        /// <summary>
        /// Core status-message logic, factored out so it's unit-testable without rendering IMGUI.
        /// Always returns a message: an informative description of the feature when the Pipeline is
        /// in no build, and a security Warning naming the build types it will ship in otherwise,
        /// because shipping it means shipping remote code execution.
        /// </summary>
        /// <param name="inDevelopmentBuilds">Whether the Pipeline is compiled into development builds.</param>
        /// <param name="inReleaseBuilds">Whether the Pipeline is compiled into release builds.</param>
        public static (string Message, MessageType Type) GetStatusMessage(bool inDevelopmentBuilds, bool inReleaseBuilds)
        {
            if (!inDevelopmentBuilds && !inReleaseBuilds)
            {
                return ("Runtime Pipeline lets a Player build be remotely controlled over HTTP — " +
                    "method-reload code, run commands, inspect state — without a domain reload. Enable " +
                    "it for the build types that should include it.",
                    MessageType.Info);
            }

            if (inReleaseBuilds)
            {
                return (inDevelopmentBuilds
                        ? "The Pipeline will be built into development, profiled AND release builds. A release " +
                          "build that carries it exposes code execution and method reload over a local " +
                          "HTTP server to anyone who can reach the machine."
                        : "The Pipeline will be built into release builds. A release build that " +
                          "carries it exposes code execution and method reload over a local HTTP server " +
                          "to anyone who can reach the machine.",
                    MessageType.Warning);
            }

            return ("The Pipeline will be built into development and profiled builds. Only ship those " +
                "to trusted testers — it exposes code execution and method reload over a local HTTP server.",
                MessageType.Warning);
        }
    }
}
