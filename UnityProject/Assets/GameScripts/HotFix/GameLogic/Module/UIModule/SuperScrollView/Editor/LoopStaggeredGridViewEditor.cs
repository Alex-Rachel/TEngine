using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditorInternal;


namespace SuperScrollView
{

    [CustomEditor(typeof(LoopStaggeredGridView))]
    public class LoopStaggeredGridViewEditor : Editor
    {

        SerializedProperty mArrangeType;
        SerializedProperty mItemPrefabDataList;

        GUIContent mArrangeTypeGuiContent = new GUIContent("ArrangeType");
        GUIContent mItemPrefabListContent = new GUIContent("ItemPrefabList");

        protected virtual void OnEnable()
        {
            mArrangeType = serializedObject.FindProperty("mArrangeType");
            mItemPrefabDataList = serializedObject.FindProperty("mItemPrefabDataList");
        }


        void ShowItemPrefabDataList(LoopStaggeredGridView listView)
        {
            EditorGUILayout.PropertyField(mItemPrefabDataList, mItemPrefabListContent,false);
            /*if (mItemPrefabDataList.isExpanded == false)
            {
                return;
            }*/
            EditorGUI.indentLevel += 1;
            if (GUILayout.Button("Add New"))
            {
                mItemPrefabDataList.InsertArrayElementAtIndex(mItemPrefabDataList.arraySize);
                if (mItemPrefabDataList.arraySize > 0)
                {
                    SerializedProperty itemData = mItemPrefabDataList.GetArrayElementAtIndex(mItemPrefabDataList.arraySize - 1);
                    SerializedProperty mItemPrefab = itemData.FindPropertyRelative("mItemPrefab");
                    mItemPrefab.objectReferenceValue = null;
                }
            }
            int removeIndex = -1;
            EditorGUILayout.PropertyField(mItemPrefabDataList.FindPropertyRelative("Array.size"));
            for (int i = 0; i < mItemPrefabDataList.arraySize; i++)
            {
                SerializedProperty itemData = mItemPrefabDataList.GetArrayElementAtIndex(i);
                SerializedProperty mInitCreateCount = itemData.FindPropertyRelative("mInitCreateCount");
                SerializedProperty mItemPrefab = itemData.FindPropertyRelative("mItemPrefab");
                SerializedProperty mItemPrefabPadding = itemData.FindPropertyRelative("mPadding");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(itemData,false);
                if (GUILayout.Button("Remove"))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
                if (itemData.isExpanded == false)
                {
                    continue;
                }
                mItemPrefab.objectReferenceValue = EditorGUILayout.ObjectField("ItemPrefab", mItemPrefab.objectReferenceValue, typeof(GameObject), true);
                mItemPrefabPadding.floatValue = EditorGUILayout.FloatField("ItemPadding", mItemPrefabPadding.floatValue);
                mInitCreateCount.intValue = EditorGUILayout.IntField("InitCreateCount", mInitCreateCount.intValue);
                EditorGUILayout.Space();
                EditorGUILayout.Space();
            }
            if (removeIndex >= 0)
            {
                mItemPrefabDataList.DeleteArrayElementAtIndex(removeIndex);
            }
            EditorGUI.indentLevel -= 1;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            LoopStaggeredGridView tListView = serializedObject.targetObject as LoopStaggeredGridView;
            if (tListView == null)
            {
                return;
            }
            ShowItemPrefabDataList(tListView);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(mArrangeType, mArrangeTypeGuiContent);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
