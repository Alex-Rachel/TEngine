#if UNITY_2019_4_OR_NEWER
using System;

namespace YooAsset.Editor
{
    public class ButtonCell : ITableCell, IComparable
    {
        public object CellValue { set; get; }
        public string SearchTag { private set; get; }

        public ButtonCell(string searchTag)
        {
            SearchTag = searchTag;
        }
        public object GetDisplayObject()
        {
            return string.Empty;
        }
        public int CompareTo(object other)
        {
            return 1;
        }
    }
}
#endif