#if UNITY_2019_4_OR_NEWER
using UnityEditor;
using System;

namespace YooAsset.Editor
{
    public class AssetObjectCell : ITableCell, IComparable
    {
        public string SearchTag { private set; get; }
        public object CellValue { set; get; }
        public string StringValue
        {
            get
            {
                return (string)CellValue;
            }
        }

        public AssetObjectCell(string searchTag, string assetPath)
        {
            SearchTag = searchTag;
            CellValue = assetPath;
        }

        public object GetDisplayObject()
        {
            return AssetDatabase.LoadMainAssetAtPath(StringValue);
        }
        public int CompareTo(object other)
        {
            if (other is AssetObjectCell cell)
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