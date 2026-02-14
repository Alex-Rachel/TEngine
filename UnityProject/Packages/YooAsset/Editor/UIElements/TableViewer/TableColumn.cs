#if UNITY_2019_4_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace YooAsset.Editor
{
    public class TableColumn
    {
        /// <summary>
        /// 单元列索引值
        /// </summary>
        internal int ColumnIndex;

        /// <summary>
        /// 单元元素集合
        /// </summary>
        internal List<VisualElement> CellElements = new List<VisualElement>(1000);
        
        /// <summary>
        /// UI元素名称
        /// </summary>
        public string ElementName { private set; get; }

        /// <summary>
        /// 标题名称
        /// </summary>
        public string HeaderTitle { private set; get; }

        /// <summary>
        /// 单元列样式
        /// </summary>
        public ColumnStyle ColumnStyle { private set; get; }

        /// <summary>
        /// 制作单元格元素
        /// </summary>
        public Func<VisualElement> MakeCell;

        /// <summary>
        /// 绑定数据到单元格
        /// </summary>
        public Action<VisualElement, ITableData, ITableCell> BindCell;

        public TableColumn(string elementName, string headerTitle, ColumnStyle columnStyle)
        {
            this.ElementName = elementName;
            this.HeaderTitle = headerTitle;
            this.ColumnStyle = columnStyle;
        }
    }
}
#endif