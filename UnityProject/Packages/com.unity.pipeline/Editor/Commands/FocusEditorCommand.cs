using System;
using System.Reflection;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Brings the Unity Editor application to the foreground (and restores it if minimized) by
    /// focusing the main window's HostView.
    ///
    /// ContainerWindow / HostView / GUIView are internal, so this uses reflection. GUIView.Focus()
    /// maps to the native MonoGUIView::Focus, which activates the OS window. Useful for headless
    /// automation: the editor throttles/defers some work (notably script compilation) while
    /// unfocused, so a client can call this to bring it forward first.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class FocusEditorCommand
    {
        const string Group = "editor";

        const BindingFlags k_All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static Type s_ContainerWindowType;
        static Type s_HostViewType;
        static MethodInfo s_IsMainWindow;
        static MethodInfo s_GuiViewFocus;
        static bool s_Resolved;

        [CliGroupCommand(Group, "Bring the Unity Editor window to the foreground", MainThreadRequired = true, Tags = new[] { "editor" })]
        public static string Focus()
        {
            if (!ResolveReflection(out var error))
                return error;

            try
            {
                object mainWindow = null;
                foreach (var w in Resources.FindObjectsOfTypeAll(s_ContainerWindowType))
                {
                    if ((bool)s_IsMainWindow.Invoke(w, null)) { mainWindow = w; break; }
                }
                if (mainWindow == null)
                    return "Error: main editor window not found";

                var rootView = GetRootView(mainWindow);
                if (rootView == null)
                    return "Error: main window has no root view";

                var host = FindHostView(rootView);
                if (host == null)
                    return "Error: no HostView found in main window";

                s_GuiViewFocus.Invoke(host, null);
                return $"Editor focused via {host.GetType().Name}";
            }
            catch (Exception ex)
            {
                return $"Error: failed to focus editor: {ex.Message}";
            }
        }

        static bool ResolveReflection(out string error)
        {
            error = null;
            if (s_Resolved) return true;

            var asm = typeof(EditorWindow).Assembly;
            s_ContainerWindowType = asm.GetType("UnityEditor.ContainerWindow");
            s_HostViewType = asm.GetType("UnityEditor.HostView");
            var guiViewType = asm.GetType("UnityEditor.GUIView");

            if (s_ContainerWindowType == null || s_HostViewType == null || guiViewType == null)
            {
                error = "Error: internal editor window types not found (Unity version mismatch?)";
                return false;
            }

            s_IsMainWindow = s_ContainerWindowType.GetMethod("IsMainWindow", k_All);
            s_GuiViewFocus = guiViewType.GetMethod("Focus", k_All, null, Type.EmptyTypes, null);

            if (s_IsMainWindow == null || s_GuiViewFocus == null)
            {
                error = "Error: ContainerWindow.IsMainWindow or GUIView.Focus not found (internal API changed?)";
                return false;
            }

            s_Resolved = true;
            return true;
        }

        static object GetRootView(object mainWindow)
        {
            var prop = s_ContainerWindowType.GetProperty("rootView", k_All);
            if (prop != null)
                return prop.GetValue(mainWindow);
            return s_ContainerWindowType.GetField("m_RootView", k_All)?.GetValue(mainWindow);
        }

        static object FindHostView(object rootView)
        {
            if (s_HostViewType.IsInstanceOfType(rootView))
                return rootView;

            if (!(rootView.GetType().GetProperty("allChildren", k_All)?.GetValue(rootView) is Array allChildren))
                return null;

            // Prefer a DockArea (an actual docked panel) over the Toolbar, but any HostView works
            // to bring the main window — and thus the application — to the foreground.
            object firstHost = null;
            foreach (var c in allChildren)
            {
                if (c == null || !s_HostViewType.IsInstanceOfType(c)) continue;
                if (firstHost == null) firstHost = c;
                if (c.GetType().Name == "DockArea") return c;
            }
            return firstHost;
        }
    }
}
