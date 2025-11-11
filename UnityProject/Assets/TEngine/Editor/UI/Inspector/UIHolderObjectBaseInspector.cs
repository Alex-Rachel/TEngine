using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.Reflection;
using TEngine;

[CustomEditor(typeof(UIHolderObjectBase), true)]
public class UIHolderObjectBaseEditor : Editor
{
    private SerializedProperty[] serializedProperties;
    private Dictionary<string, ReorderableList> reorderableDic = new Dictionary<string, ReorderableList>();

    private void OnEnable()
    {
        var fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        serializedProperties = new SerializedProperty[fields.Length];

        for (int i = 0; i < fields.Length; i++)
        {
            SerializedProperty prop = serializedObject.FindProperty(fields[i].Name);
            if (prop != null)
            {
                serializedProperties[i] = prop;


                if (prop.isArray)
                {
                    string arrayElementTypeName = prop.GetArrayElementAtIndex(0).type;

                    ReorderableList reorderableList = new ReorderableList(serializedObject, prop, true, true, true, true);
                    reorderableList.drawElementCallback = (rect, index, isActive, isFocused) => DrawElementCallback(rect, index, prop, isActive, isFocused);
                    reorderableList.drawHeaderCallback = (rect) => DrawHeaderCallback(rect, arrayElementTypeName);

                    reorderableDic.Add(prop.propertyPath, reorderableList);
                }
            }
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginDisabledGroup(true);

        for (int i = 0; i < serializedProperties.Length; i++)
        {
            var property = serializedProperties[i];
            if (property != null)
            {
                if (property.isArray && reorderableDic.TryGetValue(property.propertyPath, out var reorderableList))
                {
                    reorderableList.DoLayoutList();
                }
                else
                {
                    EditorGUILayout.PropertyField(property, GUIContent.none, true);
                }
            }
        }

        EditorGUI.EndDisabledGroup();
        serializedObject.ApplyModifiedProperties();
    }


    private void DrawElementCallback(Rect rect, int index, SerializedProperty arrayProperty, bool isActive, bool isFocused)
    {
        var element = arrayProperty.GetArrayElementAtIndex(index);
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), element, GUIContent.none);
    }


    private void DrawHeaderCallback(Rect rect, string arrayElementTypeName)
    {
        string arrayTypeName = $"{arrayElementTypeName}[]";
        EditorGUI.LabelField(rect, arrayTypeName);
    }
}
