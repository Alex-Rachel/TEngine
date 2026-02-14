using UnityEditor;
using UnityEngine;

namespace TEngine.Editor
{
    internal sealed class DragAreaGetObject
    {
        public static Object[] GetObjects(string meg = null)
        {
            Event aEvent = Event.current;
            GUI.contentColor = Color.white;
            if (aEvent.type is EventType.DragUpdated or EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                bool needReturn = false;
                if (aEvent.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    needReturn = true;
                }

                Event.current.Use();
                if (needReturn) return DragAndDrop.objectReferences;
            }

            return null;
        }
    }
}