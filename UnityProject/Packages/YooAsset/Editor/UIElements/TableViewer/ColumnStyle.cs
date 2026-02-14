#if UNITY_2019_4_OR_NEWER
using System;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace YooAsset.Editor
{
    public class ColumnStyle
    {
        public const float MaxValue = 8388608f;

        /// <summary>
        /// 单元列宽度
        /// </summary>
        public Length Width;

        /// <summary>
        /// 单元列最小宽度
        /// </summary>
        public Length MinWidth;

        /// <summary>
        /// 单元列最大宽度
        /// </summary>
        public Length MaxWidth;

        /// <summary>
        /// 可伸缩
        /// </summary>
        public bool Stretchable = false;

        /// <summary>
        /// 可搜索
        /// </summary>
        public bool Searchable = false;

        /// <summary>
        /// 可排序
        /// </summary>
        public bool Sortable = false;

        /// <summary>
        /// 统计数量
        /// </summary>
        public bool Counter = false;

        /// <summary>
        /// 展示单位
        /// </summary>
        public string Units = string.Empty;

        public ColumnStyle(Length width)
        {
            if (width.value > MaxValue)
                width = MaxValue;

            Width = width;
            MinWidth = width;
            MaxWidth = width;
        }
        public ColumnStyle(Length width, Length minWidth, Length maxWidth)
        {
            if (maxWidth.value > MaxValue)
                maxWidth = MaxValue;

            Width = width;
            MinWidth = minWidth;
            MaxWidth = maxWidth;
        }
    }
}
#endif