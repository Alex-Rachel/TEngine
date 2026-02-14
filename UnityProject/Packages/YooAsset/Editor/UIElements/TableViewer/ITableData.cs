#if UNITY_2019_4_OR_NEWER
using System;
using System.Collections.Generic;

namespace YooAsset.Editor
{
    public interface ITableData
    {
        /// <summary>
        /// 是否可见
        /// </summary>
        bool Visible { set; get; }

        /// <summary>
        /// 单元格集合
        /// </summary>
        IList<ITableCell> Cells { set; get; }
    }
}
#endif