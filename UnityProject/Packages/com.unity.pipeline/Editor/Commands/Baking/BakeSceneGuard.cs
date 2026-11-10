using System.Linq;
using Unity.Pipeline.Commands;
using UnityEngine.SceneManagement;

namespace Unity.Pipeline.Editor.Commands.Baking
{
    /// <summary>
    /// Shared helpers for the CLI-215 bake commands (lighting / NavMesh / occlusion). All three bakes
    /// act on the <b>currently open scene(s)</b> — there is no per-asset target — so before triggering
    /// any bake we verify at least one open, saved scene exists. A bake against no open/saved scene is
    /// rejected up front with the documented <c>{ code: "no_scene" }</c> result (returned, not thrown,
    /// to match the CLI-215 spec), and nothing is started.
    /// </summary>
    internal static class BakeSceneGuard
    {
        /// <summary>The structured "no_scene" error payload returned by a trigger with no bakeable scene.</summary>
        internal static object NoSceneResult()
        {
            return new
            {
                code = "no_scene",
                message = "No open, saved scene to bake. Open a scene first (" + CommandNames.SceneOpen + ") and ensure it is saved to disk."
            };
        }

        /// <summary>
        /// True when there is at least one open, loaded scene that has been saved to disk (a non-empty
        /// path). An untitled/never-saved scene has an empty <see cref="Scene.path"/> and cannot host a
        /// bake (its data has nowhere to live), so it does not count.
        /// </summary>
        internal static bool HasBakeableScene()
        {
            return OpenScenes().Any(s => s.isLoaded && !string.IsNullOrEmpty(s.path));
        }

        /// <summary>Enumerate every open scene by index.</summary>
        internal static System.Collections.Generic.IEnumerable<Scene> OpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                yield return SceneManager.GetSceneAt(i);
        }
    }
}
