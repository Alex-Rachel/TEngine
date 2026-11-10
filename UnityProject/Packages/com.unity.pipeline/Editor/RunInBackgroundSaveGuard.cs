using System;
using UnityEditor;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Keeps the Run In Background override (CLI-1432) out of ProjectSettings.asset (AUTHAPI-119).
    /// In the Editor, Application.runInBackground is the project's own Player Settings value, so
    /// while the override is active any save of Player Settings would write it to a
    /// version-controlled file, and from there into every build made from a checkout of it.
    ///
    /// Just before ProjectSettings.asset is written, puts the project's own value back
    /// (<see cref="PipelineServerStartup.RestoreProjectRunInBackground"/>), then re-applies the
    /// override on the next Editor update, once the save is done. Same restore-then-reapply as
    /// <see cref="BuildProcessors.PipelineRuntimeBuildProcessor"/> does around a build.
    /// </summary>
    class RunInBackgroundSaveGuard : AssetModificationProcessor
    {
        internal const string ProjectSettingsAssetPath = "ProjectSettings/ProjectSettings.asset";

        internal static string[] OnWillSaveAssets(string[] paths)
        {
            if (Array.Exists(paths, p => string.Equals(p, ProjectSettingsAssetPath, StringComparison.OrdinalIgnoreCase))
                && PipelineServerStartup.RestoreProjectRunInBackground())
            {
                EditorApplication.delayCall += ReapplyAfterSave;
            }

            return paths;
        }

        internal static void ReapplyAfterSave() =>
            PipelineServerStartup.ReconcileRunInBackground(EditorApplication.isPlayingOrWillChangePlaymode);
    }
}
