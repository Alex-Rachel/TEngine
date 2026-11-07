using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// One-time notice for projects still carrying the pre-AUTHAPI-66 EditorPipelineManager.asset.
    /// The editor server's configuration now lives in ProjectSettings/, and that asset is inert —
    /// it is not migrated (the package is experimental, same call as the RuntimePipelineManager
    /// migration removed in 0573c8c6), so the only thing owed to the user is being told once that
    /// it does nothing and can be deleted.
    /// </summary>
    static class EditorPipelineLegacyAssetNotice
    {
        // SessionState, not EditorPrefs: survives domain reloads so a recompile does not re-nag,
        // resets on editor restart so an ignored notice is seen again next session.
        private const string WarnedKey = "Unity.Pipeline.Editor.LegacySettingsAssetWarned";

        /// <summary>
        /// Warn once per editor session if this project still has a legacy settings asset. Deferred
        /// to delayCall because it runs from [InitializeOnLoad], where the asset database is not
        /// necessarily ready to be queried yet.
        /// </summary>
        public static void WarnOncePerSession()
        {
            if (SessionState.GetBool(WarnedKey, false))
                return;
            SessionState.SetBool(WarnedKey, true);

            EditorApplication.delayCall += () =>
            {
                if (TryBuildMessage(FindLegacyAssetPaths(), out var message))
                    Debug.LogWarning(message);
            };
        }

        /// <summary>
        /// Every saved <see cref="EditorPipelineConfig"/> asset in the project — all of which are
        /// legacy by definition, since the settings now live in ProjectSettings/ and nothing creates
        /// such an asset any more.
        ///
        /// Searched by type rather than by file name, because the pre-AUTHAPI-66 loader resolved the
        /// asset with `FindAssets("t:EditorPipelineManager")`: a user could rename or move the
        /// generated asset and it stayed effective, so `Assets/Config/MyEditorServer.asset` is just
        /// as much a legacy settings asset as one still called EditorPipelineManager.asset, and has
        /// to be reported too.
        /// </summary>
        /// <returns>Asset paths of every persisted config asset; empty when there are none.</returns>
        internal static string[] FindLegacyAssetPaths()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(EditorPipelineConfig)}");
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            return paths;
        }

        /// <summary>
        /// Build the notice naming <paramref name="legacyAssetPaths"/>, or return false when there
        /// is nothing to report.
        /// </summary>
        /// <param name="legacyAssetPaths">Paths from <see cref="FindLegacyAssetPaths"/>.</param>
        /// <param name="message">The warning to log, or null when there is nothing to warn about.</param>
        /// <returns>True if at least one legacy settings asset was found.</returns>
        internal static bool TryBuildMessage(IReadOnlyList<string> legacyAssetPaths, out string message)
        {
            List<string> legacyAssets = null;
            for (int i = 0; i < legacyAssetPaths.Count; i++)
            {
                var path = legacyAssetPaths[i];
                // GUIDToAssetPath returns empty for a guid the database no longer resolves.
                if (string.IsNullOrEmpty(path))
                    continue;

                legacyAssets ??= new List<string>();
                legacyAssets.Add(path);
            }

            if (legacyAssets == null)
            {
                message = null;
                return false;
            }

            var builder = new StringBuilder(
                "Pipeline: the editor server's settings now live in Project Settings > Pipeline > Editor, " +
                "stored under ProjectSettings/ so they no longer ship inside your build. ");
            builder.Append(legacyAssets.Count == 1
                ? "This asset is left over from the old layout, has no effect any more, and can be deleted: "
                : "These assets are left over from the old layout, have no effect any more, and can be deleted: ");
            builder.Append(string.Join(", ", legacyAssets));

            message = builder.ToString();
            return true;
        }
    }
}
