#if UNITY_2019_4_OR_NEWER
using System;
using System.Collections.Generic;

namespace YooAsset.Editor
{
    public class DefaultTableData : ITableData
    {
        /// <summary>
        /// 是否可见
        /// </summary>
        public bool Visible { set; get; } = true;

        /// <summary>
        /// 单元格集合
        /// </summary>
        public IList<ITableCell> Cells { set; get; } = new List<ITableCell>();


        /// <summary>
        /// 添加单元格数据
        /// </summary>
        public void AddCell(ITableCell cell)
        {
            Cells.Add(cell);
        }

        #region 添加默认的单元格数据
        public void AddButtonCell(string searchTag)
        {
            var cell = new ButtonCell(searchTag);
            Cells.Add(cell);
        }
        public void AddAssetPathCell(string searchTag, string assetPath)
        {
            var cell = new AssetPathCell(searchTag, assetPath);
            Cells.Add(cell);
        }
        public void AddAssetObjectCell(string searchTag, string assetPath)
        {
            var cell = new AssetObjectCell(searchTag, assetPath);
            Cells.Add(cell);
        }
        public void AddStringValueCell(string searchTag, string value)
        {
            var cell = new StringValueCell(searchTag, value);
            Cells.Add(cell);
        }
        public void AddLongValueCell(string searchTag, long value)
        {
            var cell = new IntegerValueCell(searchTag, value);
            Cells.Add(cell);
        }
        public void AddDoubleValueCell(string searchTag, double value)
        {
            var cell = new SingleValueCell(searchTag, value);
            Cells.Add(cell);
        }
        public void AddBoolValueCell(string searchTag, bool value)
        {
            var cell = new BooleanValueCell(searchTag, value);
            Cells.Add(cell);
        }
        #endregion
    }
}
#endif