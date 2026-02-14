using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace TEngine.Editor
{
    internal sealed class AssetTreeView : TreeView
    {
        private const float K_ICON_WIDTH = 18f;
        private const float K_ROW_HEIGHTS = 20f;
        private readonly GUIStyle _stateGuiStyle = new GUIStyle { richText = true, alignment = TextAnchor.MiddleCenter };
        public AssetViewItem assetRoot;

        public AssetTreeView(TreeViewState state, MultiColumnHeader multicolumnHeader) : base(state, multicolumnHeader)
        {
            rowHeight = K_ROW_HEIGHTS;
            columnIndexForTreeFoldouts = 0;
            showAlternatingRowBackgrounds = true;
            showBorder = false;
            customFoldoutYOffset = (K_ROW_HEIGHTS - EditorGUIUtility.singleLineHeight) * 0.5f;
            extraSpaceBeforeIconAndLabel = K_ICON_WIDTH;
        }

        protected override void DoubleClickedItem(int id)
        {
            AssetViewItem item = (AssetViewItem)FindItem(id, rootItem);
            if (item != null)
            {
                Object assetObject = AssetDatabase.LoadAssetAtPath(item.data.path, typeof(Object));
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = assetObject;
                EditorGUIUtility.PingObject(assetObject);
            }
        }

        protected override void ExpandedStateChanged() => SortExpandItem();

        public void SortExpandItem()
        {
            if (SortHelper.CurSortType == SortType.None) return;
            IList<int> expandItemList = GetExpanded();
            foreach (int i in expandItemList)
            {
                AssetViewItem item = (AssetViewItem)FindItem(i, rootItem);
                SortHelper.SortChild(item.data);
            }

            ResourceReferenceInfo curWindow = EditorWindow.GetWindow<ResourceReferenceInfo>();
            curWindow.needUpdateAssetTree = true;
        }

        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState(float treeViewWidth, bool isDepend)
        {
            List<MultiColumnHeaderState.Column> columns = new List<MultiColumnHeaderState.Column>
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("名称"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    width = 200,
                    minWidth = 60,
                    autoResize = false,
                    allowToggleVisibility = false,
                    canSort = true,
                    sortingArrowAlignment = TextAlignment.Center
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("路径"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    width = 360,
                    minWidth = 60,
                    autoResize = false,
                    allowToggleVisibility = false,
                    canSort = true,
                    sortingArrowAlignment = TextAlignment.Center
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("状态"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    width = 60,
                    minWidth = 60,
                    autoResize = false,
                    allowToggleVisibility = true,
                    canSort = false
                }
            };
            if (!isDepend)
            {
                columns.Add(new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("引用数量"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    width = 60,
                    minWidth = 60,
                    autoResize = true,
                    allowToggleVisibility = true,
                    canSort = false
                });
            }

            MultiColumnHeaderState state = new MultiColumnHeaderState(columns.ToArray());
            return state;
        }

        protected override TreeViewItem BuildRoot() => assetRoot;

        protected override void RowGUI(RowGUIArgs args)
        {
            AssetViewItem item = (AssetViewItem)args.item;
            for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
                CellGUI(args.GetCellRect(i), item, (MyColumns)args.GetColumn(i), ref args);
        }

        private void CellGUI(Rect cellRect, AssetViewItem item, MyColumns column, ref RowGUIArgs args)
        {
            CenterRectUsingSingleLineHeight(ref cellRect);
            switch (column)
            {
                case MyColumns.Name:
                    Rect iconRect = cellRect;
                    iconRect.x += GetContentIndent(item);
                    iconRect.width = K_ICON_WIDTH;
                    if (iconRect.x < cellRect.xMax)
                    {
                        Texture2D icon = GetIcon(item.data.path);
                        if (icon != null)
                            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                    }

                    args.rowRect = cellRect;
                    base.RowGUI(args);
                    break;
                case MyColumns.Path:
                    GUI.Label(cellRect, item.data.path);
                    break;
                case MyColumns.State:
                    GUI.Label(cellRect, ReferenceFinderData.GetInfoByState(item.data.state), _stateGuiStyle);
                    break;
                case MyColumns.RefCount:
                    GUI.Label(cellRect, ResourceReferenceInfo.Data.GetRefCount(item.data, (item.parent as AssetViewItem)?.data), _stateGuiStyle);
                    break;
            }
        }

        private Texture2D GetIcon(string path)
        {
            Object obj = AssetDatabase.LoadAssetAtPath(path, typeof(Object));
            if (obj)
            {
                Texture2D icon = AssetPreview.GetMiniThumbnail(obj);
                if (!icon)
                    icon = AssetPreview.GetMiniTypeThumbnail(obj.GetType());
                return icon;
            }

            return null;
        }

        private enum MyColumns
        {
            Name,
            Path,
            State,
            RefCount
        }
    }
}