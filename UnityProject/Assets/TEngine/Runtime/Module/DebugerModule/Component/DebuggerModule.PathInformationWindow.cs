using System;
using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class PathInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Path Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Current Directory", Utility.Path.GetRegularPath(Environment.CurrentDirectory));
                    DrawItem("Data Path", Utility.Path.GetRegularPath(Application.dataPath));
                    DrawItem("Persistent Data Path", Utility.Path.GetRegularPath(Application.persistentDataPath));
                    DrawItem("Streaming Assets Path", Utility.Path.GetRegularPath(Application.streamingAssetsPath));
                    DrawItem("Temporary Cache Path", Utility.Path.GetRegularPath(Application.temporaryCachePath));
#if UNITY_2018_3_OR_NEWER
                    DrawItem("Console Log Path", Utility.Path.GetRegularPath(Application.consoleLogPath));
#endif
                }
                GUILayout.EndVertical();
            }
        }
    }
}
