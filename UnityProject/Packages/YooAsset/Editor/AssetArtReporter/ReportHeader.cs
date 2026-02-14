using System;
using UnityEditor;

namespace YooAsset.Editor
{
    [Serializable]
    public class ReportHeader
    {
        public const int MaxValue = 8388608;

        /// <summary>
        /// 标题
        /// </summary>
        public string HeaderTitle;

        /// <summary>
        /// 标题宽度
        /// </summary>
        public int Width;

        /// <summary>
        /// 单元列最小宽度
        /// </summary>
        public int MinWidth = 50;

        /// <summary>
        /// 单元列最大宽度
        /// </summary>
        public int MaxWidth = MaxValue;

        /// <summary>
        /// 可伸缩选项
        /// </summary>
        public bool Stretchable = false;

        /// <summary>
        /// 可搜索选项
        /// </summary>
        public bool Searchable = false;

        /// <summary>
        /// 可排序选项
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

        /// <summary>
        /// 数值类型
        /// </summary>
        public EHeaderType HeaderType = EHeaderType.StringValue;


        public ReportHeader(string headerTitle, int width)
        {
            HeaderTitle = headerTitle;
            Width = width;
            MinWidth = width;
            MaxWidth = width;
        }
        public ReportHeader(string headerTitle, int width, int minWidth, int maxWidth)
        {
            HeaderTitle = headerTitle;
            Width = width;
            MinWidth = minWidth;
            MaxWidth = maxWidth;
        }

        public ReportHeader SetMinWidth(int value)
        {
            MinWidth = value;
            return this;
        }
        public ReportHeader SetMaxWidth(int value)
        {
            MaxWidth = value;
            return this;
        }
        public ReportHeader SetStretchable()
        {
            Stretchable = true;
            return this;
        }
        public ReportHeader SetSearchable()
        {
            Searchable = true;
            return this;
        }
        public ReportHeader SetSortable()
        {
            Sortable = true;
            return this;
        }
        public ReportHeader SetCounter()
        {
            Counter = true;
            return this;
        }
        public ReportHeader SetUnits(string units)
        {
            Units = units;
            return this;
        }
        public ReportHeader SetHeaderType(EHeaderType value)
        {
            HeaderType = value;
            return this;
        }

        /// <summary>
        /// 检测数值有效性
        /// </summary>
        public void CheckValueValid(string value)
        {
            if (HeaderType == EHeaderType.AssetPath)
            {
                string guid = AssetDatabase.AssetPathToGUID(value);
                if (string.IsNullOrEmpty(guid))
                    throw new Exception($"{HeaderTitle} value is invalid asset path : {value}");
            }
            else if (HeaderType == EHeaderType.AssetObject)
            {
                string guid = AssetDatabase.AssetPathToGUID(value);
                if (string.IsNullOrEmpty(guid))
                    throw new Exception($"{HeaderTitle} value is invalid asset object : {value}");
            }
            else if (HeaderType == EHeaderType.DoubleValue)
            {
                if (double.TryParse(value, out double doubleValue) == false)
                    throw new Exception($"{HeaderTitle} value is invalid double value : {value}");
            }
            else if (HeaderType == EHeaderType.LongValue)
            {
                if (long.TryParse(value, out long longValue) == false)
                    throw new Exception($"{HeaderTitle} value is invalid long value : {value}");
            }
            else if (HeaderType == EHeaderType.StringValue)
            {
            }
            else
            {
                throw new System.NotImplementedException(HeaderType.ToString());
            }
        }
    }
}