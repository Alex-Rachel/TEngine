using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditorInternal;


namespace SuperScrollView
{

    [CustomEditor(typeof(LoopListView2))]
    public class LoopListViewEditor2 : Editor
    {

        SerializedProperty mSupportScrollBar;
        SerializedProperty mItemSnapEnable;
        SerializedProperty mArrangeType;
        SerializedProperty mItemPrefabDataList;
        SerializedProperty mItemSnapPivot;
        SerializedProperty mViewPortSnapPivot;

        GUIContent mSupportScrollBarContent = new GUIContent("SupportScrollBar");
        GUIContent mItemSnapEnableContent = new GUIContent("ItemSnapEnable");
        GUIContent mArrangeTypeGuiContent = new GUIContent("ArrangeType");
        GUIContent mItemPrefabListContent = new GUIContent("ItemPrefabList");
        GUIContent mItemSnapPivotContent = new GUIContent("ItemSnapPivot");
        GUIContent mViewPortSnapPivotContent = new GUIContent("ViewPortSnapPivot");

        protected virtual void OnEnable()
        {
            mSupportScrollBar = serializedObject.FindProperty("mSupportScrollBar");
            mItemSnapEnable = serializedObject.FindProperty("mItemSnapEnable");
            mArrangeType = serializedObject.FindProperty("mArrangeType");
            mItemPrefabDataList = serializedObject.FindProperty("mItemPrefabDataList");
            mItemSnapPivot = serializedObject.FindProperty("mItemSnapPivot");
            mViewPortSnapPivot = serializedObject.FindProperty("mViewPortSnapPivot");
        }


        void ShowItemPrefabDataList(LoopListView2 listView)
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
                if(mItemPrefabDataList.arraySize > 0)
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
                SerializedProperty mItemStartPosOffset = itemData.FindPropertyRelative("mStartPosOffset");
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
                if(listView.ArrangeType == ListItemArrangeType.TopToBottom || listView.ArrangeType == ListItemArrangeType.BottomToTop)
                {
                    mItemStartPosOffset.floatValue = EditorGUILayout.FloatField("XPosOffset", mItemStartPosOffset.floatValue);
                }
                else
                {
                    mItemStartPosOffset.floatValue = EditorGUILayout.FloatField("YPosOffset", mItemStartPosOffset.floatValue);
                }
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
            LoopListView2 tListView = serializedObject.targetObject as LoopListView2;
            if (tListView == null)
            {
                return;
            }
            ShowItemPrefabDataList(tListView);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(mSupportScrollBar, mSupportScrollBarContent);
            EditorGUILayout.PropertyField(mItemSnapEnable, mItemSnapEnableContent);
            if(mItemSnapEnable.boolValue == true)
            {
                EditorGUILayout.PropertyField(mItemSnapPivot, mItemSnapPivotContent);
                EditorGUILayout.PropertyField(mViewPortSnapPivot, mViewPortSnapPivotContent);
            }
            EditorGUILayout.PropertyField(mArrangeType, mArrangeTypeGuiContent);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
