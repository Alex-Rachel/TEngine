#if UNITY_2019_4_OR_NEWER
using System;

namespace YooAsset.Editor
{
    public class IntegerValueCell : ITableCell, IComparable
    {
        public object CellValue { set; get; }
        public string SearchTag { private set; get; }
        public long IntegerValue
        {
            get
            {
                return (long)CellValue;
            }
        }

        public IntegerValueCell(string searchTag, object cellValue)
        {
            SearchTag = searchTag;
            CellValue = cellValue;
        }
        public object GetDisplayObject()
        {
            return CellValue.ToString();
        }
        public int CompareTo(object other)
        {
            if (other is IntegerValueCell cell)
            {
                return this.IntegerValue.CompareTo(cell.IntegerValue);
            }
            else
            {
                return 0;
            }
        }
    }
}
#endif