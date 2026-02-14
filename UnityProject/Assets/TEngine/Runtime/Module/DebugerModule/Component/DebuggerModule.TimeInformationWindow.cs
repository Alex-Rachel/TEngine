using System.Globalization;
using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class TimeInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Time Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Time Scale", Utility.Text.Format("{0} [{1}]", Time.timeScale, GetTimeScaleDescription(Time.timeScale)));
                    DrawItem("Realtime Since Startup", Time.realtimeSinceStartup.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Time Since Level Load", Time.timeSinceLevelLoad.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Time", Time.time.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Fixed Time", Time.fixedTime.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Unscaled Time", Time.unscaledTime.ToString(CultureInfo.InvariantCulture));
#if UNITY_5_6_OR_NEWER
                    DrawItem("Fixed Unscaled Time", Time.fixedUnscaledTime.ToString(CultureInfo.InvariantCulture));
#endif
                    DrawItem("Delta Time", Time.deltaTime.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Fixed Delta Time", Time.fixedDeltaTime.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Unscaled Delta Time", Time.unscaledDeltaTime.ToString(CultureInfo.InvariantCulture));
#if UNITY_5_6_OR_NEWER
                    DrawItem("Fixed Unscaled Delta Time", Time.fixedUnscaledDeltaTime.ToString(CultureInfo.InvariantCulture));
#endif
                    DrawItem("Smooth Delta Time", Time.smoothDeltaTime.ToString(CultureInfo.InvariantCulture));
                    DrawItem("Maximum Delta Time", Time.maximumDeltaTime.ToString(CultureInfo.InvariantCulture));
#if UNITY_5_5_OR_NEWER
                    DrawItem("Maximum Particle Delta Time", Time.maximumParticleDeltaTime.ToString(CultureInfo.InvariantCulture));
#endif
                    DrawItem("Frame Count", Time.frameCount.ToString());
                    DrawItem("Rendered Frame Count", Time.renderedFrameCount.ToString());
                    DrawItem("Capture Framerate", Time.captureFramerate.ToString());
#if UNITY_2019_2_OR_NEWER
                    DrawItem("Capture Delta Time", Time.captureDeltaTime.ToString(CultureInfo.InvariantCulture));
#endif
#if UNITY_5_6_OR_NEWER
                    DrawItem("In Fixed Time Step", Time.inFixedTimeStep.ToString());
#endif
                }
                GUILayout.EndVertical();
            }

            private string GetTimeScaleDescription(float timeScale)
            {
                if (timeScale <= 0f)
                {
                    return "Pause";
                }

                if (timeScale < 1f)
                {
                    return "Slower";
                }

                if (timeScale > 1f)
                {
                    return "Faster";
                }

                return "Normal";
            }
        }
    }
}
