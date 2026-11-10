using UnityEngine;
using Unity.Pipeline.MethodReload;

namespace Unity.Pipeline
{
    /// <summary>
    /// Minimal on-screen method-reload status for players: what is happening now (compiling → applied,
    /// or failed) and how many method overrides are active. Deliberately IMGUI — no canvas,
    /// EventSystem or render-pipeline dependency, and it is development-only anyway (the body
    /// compiles out of release players; the type itself stays so AddComponent call sites compile).
    /// Added automatically by <see cref="RuntimePipelineDriver"/> when its
    /// reload-overlay flag is on. Hidden entirely while idle with no overrides.
    /// </summary>
    [DisallowMultipleComponent]
    class MethodReloadStatusOverlay : MonoBehaviour
    {
#if UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE
        // How long the "applied" confirmation stays up before falling back to the count line.
        private const int AppliedFlashMs = 3500;
        // A "compiling…" with no follow-up payload or failure notice goes stale (editor died,
        // connection dropped) — stop showing it after this.
        private const int CompilingStaleMs = 15000;
        private const float StatsPollSeconds = 1f;
        private const int MaxMessageLength = 96;

        private int m_OverrideCount;
        private int m_CallsPerSecond;
        private long m_LastCallCount;
        private float m_LastPollTime;
        private float m_NextStatsPoll;
        private GUIStyle m_Style;

        private void Update()
        {
            // GetStats() allocates; poll at 1 Hz instead of per frame.
            if (Time.unscaledTime >= m_NextStatsPoll)
            {
                m_NextStatsPoll = Time.unscaledTime + StatsPollSeconds;
                m_OverrideCount = MethodReloadRegistry.GetStats().ActiveOverrideCount;

                long calls = MethodReloadActivity.OverrideCallCount;
                float elapsed = Time.unscaledTime - m_LastPollTime;
                if (m_LastPollTime > 0 && elapsed > 0)
                    m_CallsPerSecond = (int)((calls - m_LastCallCount) / elapsed);
                m_LastCallCount = calls;
                m_LastPollTime = Time.unscaledTime;
            }
        }

        private void OnGUI()
        {
            var phase = MethodReloadActivity.Snapshot(out var message, out var ageMs);

            string activity = null;
            Color activityColor = Color.white;
            switch (phase)
            {
                case MethodReloadActivity.Phase.Compiling when ageMs < CompilingStaleMs:
                    activity = $"compiling {Truncate(message)}…";
                    activityColor = new Color(1f, 0.8f, 0.25f); // amber
                    break;
                case MethodReloadActivity.Phase.Applied when ageMs < AppliedFlashMs:
                    activity = Truncate(message);
                    activityColor = new Color(0.45f, 1f, 0.45f); // green
                    break;
                case MethodReloadActivity.Phase.Failed:
                    activity = Truncate(message); // sticky until the next reload attempt
                    activityColor = new Color(1f, 0.45f, 0.45f); // red
                    break;
            }

            if (activity == null && m_OverrideCount == 0)
                return; // nothing to show

            m_Style ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };

            var lines = activity != null
                ? new[] { ($"MethodReload: {activity}", activityColor), (CountLine(), Color.white) }
                : new[] { (CountLine(), Color.white) };

            float width = 0, height = 4;
            foreach (var l in lines)
            {
                var s = m_Style.CalcSize(new GUIContent(l.Item1));
                if (s.x > width) width = s.x;
                height += s.y;
            }

            var box = new Rect(8, 8, width + 12, height + 4);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);

            float y = box.y + 4;
            foreach (var l in lines)
            {
                var size = m_Style.CalcSize(new GUIContent(l.Item1));
                var r = new Rect(box.x + 6, y, size.x, size.y);
                // 1px shadow keeps it readable over bright scenes.
                GUI.color = new Color(0f, 0f, 0f, 0.9f);
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), l.Item1, m_Style);
                GUI.color = l.Item2;
                GUI.Label(r, l.Item1, m_Style);
                y += size.y;
            }
            GUI.color = Color.white;
        }

        // "N overrides · 0 calls/s" is a diagnostic in itself: bound but never dispatched.
        private string CountLine() => $"{m_OverrideCount} override(s) active · {m_CallsPerSecond} calls/s";

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s.Substring(0, nl); // failures can carry multi-line diagnostics
            return s.Length <= MaxMessageLength ? s : s.Substring(0, MaxMessageLength) + "…";
        }
#endif
    }
}
