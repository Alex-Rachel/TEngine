#if UNITY_2019_4_OR_NEWER
using System;

namespace YooAsset.Editor
{
    public class StringValueCell : ITableCell, IComparable
    {
        public object CellValue { set; get; }
        public string SearchTag { private set; get; }
        public string StringValue
        {
            get
            {
                return (string)CellValue;
            }
        }
        
        public StringValueCell(string searchTag, object cellValue)
        {
            SearchTag = searchTag;
            CellValue = cellValue;
        }
        public object GetDisplayObject()
        {
            return CellValue;
        }
        public int CompareTo(object other)
        {
            if (other is StringValueCell cell)
            {
                return this.StringValue.CompareTo(cell.StringValue);
            }
            else
            {
                return 0;
            }
        }
    }
}
#endif