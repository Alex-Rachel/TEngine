using UnityEngine;
using UnityEditor;
using System.IO;

[CanEditMultipleObjects, CustomEditor(typeof(TextAsset))]
public class TextAssetInspector : Editor
{
    private const int MAX_COLUM = 10240;
    
    private GUIStyle _textStyle;

    public override void OnInspectorGUI()
    {
        if (_textStyle == null)
        {
            _textStyle = "ScriptText";
        }

        bool enabled = GUI.enabled;
        GUI.enabled = true;
        string assetPath = AssetDatabase.GetAssetPath(target);
        if (assetPath.EndsWith(".md")
            || assetPath.EndsWith(".xml")
            || assetPath.EndsWith(".txt")
            || assetPath.EndsWith(".html")
            || assetPath.EndsWith(".csv")
            )
        {
            string luaFile = File.ReadAllText(assetPath);
            string text;
            if (base.targets.Length > 1)
            {
                text = Path.GetFileName(assetPath);
            }
            else
            {
                text = luaFile;
                if (text.Length > MAX_COLUM)
                {
                    text = text.Substring(0, MAX_COLUM) + "...\n\n<...etc...>";
                }
            }

            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), _textStyle);
            rect.x = 0f;
            rect.y -= 3f;
            rect.width = EditorGUIUtility.currentViewWidth + 1f;
            GUI.Box(rect, text, _textStyle);
        }

        GUI.enabled = enabled;
    }
}