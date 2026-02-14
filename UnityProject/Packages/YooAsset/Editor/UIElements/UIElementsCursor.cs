#if UNITY_2019_4_OR_NEWER
using System.Reflection;
using UnityEditor;

namespace YooAsset.Editor
{
    public static class UIElementsCursor
    {
        private static PropertyInfo _defaultCursorId;
        private static PropertyInfo DefaultCursorId
        {
            get
            {
                if (_defaultCursorId != null)
                    return _defaultCursorId;

                _defaultCursorId = typeof(UnityEngine.UIElements.Cursor).GetProperty("defaultCursorId", BindingFlags.NonPublic | BindingFlags.Instance);
                return _defaultCursorId;
            }
        }

        public static UnityEngine.UIElements.Cursor CreateCursor(MouseCursor cursorType)
        {
            var ret = (object)new UnityEngine.UIElements.Cursor();
            DefaultCursorId.SetValue(ret, (int)cursorType);
            return (UnityEngine.UIElements.Cursor)ret;
        }
    }
}
#endif