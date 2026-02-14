using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class InputTouchInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Input Touch Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Touch Supported", Input.touchSupported.ToString());
                    DrawItem("Touch Pressure Supported", Input.touchPressureSupported.ToString());
                    DrawItem("Stylus Touch Supported", Input.stylusTouchSupported.ToString());
                    DrawItem("Simulate Mouse With Touches", Input.simulateMouseWithTouches.ToString());
                    DrawItem("Multi Touch Enabled", Input.multiTouchEnabled.ToString());
                    DrawItem("Touch Count", Input.touchCount.ToString());
                    DrawItem("Touches", GetTouchesString(Input.touches));
                }
                GUILayout.EndVertical();
            }

            private string GetTouchString(Touch touch)
            {
                return Utility.Text.Format("{0}, {1}, {2}, {3}, {4}", touch.position, touch.deltaPosition, touch.rawPosition, touch.pressure, touch.phase);
            }

            private string GetTouchesString(Touch[] touches)
            {
                string[] touchStrings = new string[touches.Length];
                for (int i = 0; i < touches.Length; i++)
                {
                    touchStrings[i] = GetTouchString(touches[i]);
                }

                return string.Join("; ", touchStrings);
            }
        }
    }
}
