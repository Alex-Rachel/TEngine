#if UNITY_2019_4_OR_NEWER

namespace YooAsset.Editor
{
    public interface ITableCell
    {
        /// <summary>
        /// 单元格数值
        /// </summary>
        object CellValue { set; get; }

        /// <summary>
        /// 获取界面显示对象
        /// </summary>
        object GetDisplayObject();
    }
}
#endif