using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace TEngine.Editor
{
    internal sealed class ClickColumn : MultiColumnHeader
    {
        public delegate void SortInColumn();

        public static Dictionary<int, SortInColumn> SortWithIndex = new Dictionary<int, SortInColumn>
        {
            { 0, SortByName },
            { 1, SortByPath }
        };

        public ClickColumn(MultiColumnHeaderState state) : base(state) => canSort = true;

        protected override void ColumnHeaderClicked(MultiColumnHeaderState.Column column, int columnIndex)
        {
            base.ColumnHeaderClicked(column, columnIndex);
            if (SortWithIndex.ContainsKey(columnIndex))
            {
                SortWithIndex[columnIndex].Invoke();
                ResourceReferenceInfo curWindow = EditorWindow.GetWindow<ResourceReferenceInfo>();
                curWindow.mAssetTreeView.SortExpandItem();
            }
        }

        public static void SortByName() => SortHelper.SortByName();

        public static void SortByPath() => SortHelper.SortByPath();
    }
}