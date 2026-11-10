using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Executes an Editor menu item by its path (e.g. "Assets/Reimport All"), or — when called with
    /// no path — lists the available menu items. Backs the <c>unity menu</c> CLI command (CLI-110):
    /// <c>unity menu</c> lists options, <c>unity menu "&lt;path&gt;"</c> executes one. The CLI only
    /// forwards the request; all logic lives here, per the two-commands design.
    ///
    /// Named <c>MenuItemCommand</c> (not <c>MenuCommand</c>) to avoid colliding with the built-in
    /// <see cref="UnityEditor.MenuCommand"/> type, which would make unqualified references ambiguous
    /// in any file that also imports <c>UnityEditor</c>.
    ///
    /// Execution wraps <see cref="EditorApplication.ExecuteMenuItem"/>, which returns <c>false</c>
    /// when the item could not be invoked — either because the path does not exist or because the
    /// item is currently disabled (its validation function returned false). The API does not
    /// distinguish those cases, so a failure is reported as "not found or disabled"; the CLI maps a
    /// failed result to its "menu item does not exist" exit code.
    ///
    /// Listing reflects the <em>actual</em> Editor menu via the internal
    /// <c>UnityEditor.Menu.GetMenuItems</c> API (accessed by reflection, mirroring
    /// <see cref="FocusEditorCommand"/>). Unlike scanning <c>[MenuItem]</c> attributes, this also
    /// covers menus Unity defines natively (much of <c>GameObject/…</c>, <c>Assets/Create/…</c>,
    /// <c>File/…</c>, etc.), which carry no attribute and would otherwise be missed.
    ///
    /// Note: some menu items are heavy, show a modal confirmation, or trigger a domain reload/quit
    /// (which tears the server down before it can reply). That is the nature of the invoked item;
    /// such cases are a known limitation of driving arbitrary menus over a request/response call.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class MenuItemCommand
    {
        const string Group = "editor";
        const string k_QuitMenuPath = "File/Exit";

        [CliGroupCommand(Group, "Execute an Editor menu item by path, or list available items when no path is given", MainThreadRequired = true, Tags = new[] { "editor" })]
        public static MenuResponse InvokeMenu(
            [CliArg("path", "Menu item path to execute, e.g. \"Assets/Reimport All\". Omit to list available menu items.")] string path = "")
        {
            // No path → discovery mode: return the available menu items instead of executing.
            if (string.IsNullOrWhiteSpace(path))
            {
                List<string> items;
                try
                {
                    items = DiscoverMenuItems();
                }
                catch (Exception ex)
                {
                    return MenuResponse.Fail(null, $"Failed to list menu items: {ex.Message}");
                }

                return new MenuResponse
                {
                    Success = true,
                    Items = items,
                    Message = $"{items.Count} menu item(s) available."
                };
            }

            // File/Exit opens a save prompt for dirty scenes, which an unattended Editor silently cancels.
            if (string.Equals(path.Trim(), k_QuitMenuPath, StringComparison.OrdinalIgnoreCase))
                return MenuResponse.Fail(path,
                    $"'{k_QuitMenuPath}' is not run through this command. Use '{CommandNames.EditorQuit}' " +
                    "(with --save or --discard when there are unsaved changes).");

            bool executed;
            try
            {
                executed = EditorApplication.ExecuteMenuItem(path);
            }
            catch (Exception ex)
            {
                return MenuResponse.Fail(path, $"Failed to execute menu item '{path}': {ex.Message}");
            }

            if (!executed)
                return MenuResponse.Fail(path, $"Menu item '{path}' was not found or is currently disabled.");

            return new MenuResponse
            {
                Success = true,
                Path = path,
                Message = $"Executed menu item '{path}'"
            };
        }

        const BindingFlags k_All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Reflection handles for the internal UnityEditor.Menu.GetMenuItems(string, bool, bool) API,
        // which returns an array of internal UnityEditor.ScriptingMenuItem. Resolved once and cached;
        // we read each item's `path` and `isSeparator` member (property or field, depending on the
        // Unity version). See FocusEditorCommand for the same reflect-into-internal-API approach.
        static MethodInfo s_GetMenuItems;
        static Func<object, string> s_GetPath;
        static Func<object, bool> s_GetIsSeparator;
        static bool s_Resolved;

        static void EnsureReflection()
        {
            if (s_Resolved)
                return;
            s_Resolved = true;

            var editorAsm = typeof(EditorApplication).Assembly;

            var menuType = editorAsm.GetType("UnityEditor.Menu");
            s_GetMenuItems = menuType?.GetMethod(
                "GetMenuItems", k_All, null, new[] { typeof(string), typeof(bool), typeof(bool) }, null);

            var itemType = editorAsm.GetType("UnityEditor.ScriptingMenuItem");
            s_GetPath = BuildAccessor<string>(itemType, "path", "m_Path");
            s_GetIsSeparator = BuildAccessor<bool>(itemType, "isSeparator", "m_IsSeparator");
        }

        /// <summary>
        /// Build a getter for a member that may be exposed as a property or a backing field, so the
        /// reflection survives the internal type's shape changing across Unity versions.
        /// </summary>
        static Func<object, T> BuildAccessor<T>(Type type, string propertyName, string fieldName)
        {
            var prop = type?.GetProperty(propertyName, k_All);
            if (prop != null)
                return o => (T)prop.GetValue(o);

            var field = type?.GetField(propertyName, k_All) ?? type?.GetField(fieldName, k_All);
            if (field != null)
                return o => (T)field.GetValue(o);

            return null;
        }

        // Standard top-level Editor menus. Unity defines these natively (no [MenuItem]), so they are
        // not discoverable from attributes; we seed them so their native children are enumerated.
        static readonly string[] k_NativeRoots =
        {
            "File", "Edit", "Assets", "GameObject", "Component", "Window", "Help"
        };

        /// <summary>
        /// Collect the distinct menu paths from the live Editor menu. <c>Menu.GetMenuItems</c> returns
        /// the full (recursive) set of items under a given root — including ones Unity defines
        /// natively — but it does not enumerate the roots themselves, and no internal API does. So we
        /// union the native roots above with the first path segment of every <c>[MenuItem]</c> (which
        /// surfaces custom/package roots), then expand each root through the internal API. The items
        /// always come from the live menu, so native entries are included.
        /// </summary>
        static List<string> DiscoverMenuItems()
        {
            EnsureReflection();

            if (s_GetMenuItems == null || s_GetPath == null)
                throw new InvalidOperationException(
                    "UnityEditor.Menu.GetMenuItems is unavailable in this Unity version.");

            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in CollectRoots())
                CollectItemsUnder(root, paths);

            return paths.ToList();
        }

        /// <summary>
        /// The top-level menu names to expand: the native roots, plus the first segment of every
        /// attributed menu item (covering custom/package menus). Component context menus
        /// (<c>CONTEXT/…</c>) are excluded, as they are not part of the main menu bar.
        /// </summary>
        static IEnumerable<string> CollectRoots()
        {
            var roots = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in k_NativeRoots)
                roots.Add(r);

            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (var attr in method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                {
                    if (attr.validate || string.IsNullOrEmpty(attr.menuItem))
                        continue;

                    var slash = attr.menuItem.IndexOf('/');
                    var root = slash > 0 ? attr.menuItem.Substring(0, slash) : attr.menuItem;
                    if (root.StartsWith("CONTEXT", StringComparison.Ordinal))
                        continue;

                    roots.Add(root);
                }
            }

            return roots;
        }

        /// <summary>
        /// Append every non-separator menu path under <paramref name="root"/> to
        /// <paramref name="paths"/>. A root that isn't present in this Editor simply contributes
        /// nothing (the API returns an empty array).
        /// </summary>
        static void CollectItemsUnder(string root, SortedSet<string> paths)
        {
            if (!(s_GetMenuItems.Invoke(null, new object[] { root, false, false }) is Array items))
                return;

            foreach (var item in items)
            {
                if (item == null)
                    continue;
                if (s_GetIsSeparator != null && s_GetIsSeparator(item))
                    continue;

                var path = s_GetPath(item);
                if (string.IsNullOrEmpty(path))
                    continue;

                paths.Add(path);
            }
        }
    }

    /// <summary>
    /// Result of the <c>menu</c> command. On execution, <see cref="Success"/> false means the item
    /// could not be invoked (not found or disabled). In list mode, <see cref="Items"/> holds the
    /// available menu paths.
    /// </summary>
    [Serializable]
    class MenuResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("items")]
        public List<string> Items { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public static MenuResponse Fail(string path, string message) => new MenuResponse
        {
            Success = false,
            Path = path,
            Message = message
        };
    }
}
