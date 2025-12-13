#if !UNITY_6000_3_OR_NEWER

using UnityEditor;
using UnityToolbarExtender;

namespace TEngine
{
    [InitializeOnLoad]
    public partial class UnityToolbarExtenderLeft
    {
        static UnityToolbarExtenderLeft()
        {
            ToolbarExtender.LeftToolbarGUI.Add(OnToolbarGUI_SceneLauncher);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuit;
        }
    }
}

#endif