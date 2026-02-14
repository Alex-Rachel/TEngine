using System;
using System.Collections.Generic;

namespace TEngine.Editor
{
    internal sealed class SortHelper
    {
        public delegate int SortCompare(string lString, string rString);

        public static readonly HashSet<string> SortedGuid = new HashSet<string>();
        public static readonly Dictionary<string, SortType> SortedAsset = new Dictionary<string, SortType>();
        public static SortType CurSortType = SortType.None;
        public static SortType PathType = SortType.None;
        public static SortType NameType = SortType.None;

        public static readonly Dictionary<SortType, SortCompare> CompareFunction = new Dictionary<SortType, SortCompare>
        {
            { SortType.AscByPath, CompareWithPath },
            { SortType.DescByPath, CompareWithPathDesc },
            { SortType.AscByName, CompareWithName },
            { SortType.DescByName, CompareWithNameDesc }
        };

        public static void Init()
        {
            SortedGuid.Clear();
            SortedAsset.Clear();
        }

        public static void ChangeSortType(short sortGroup, Dictionary<SortType, SortType> handler, ref SortType recoverType)
        {
            if (SortConfig.SortTypeGroup[CurSortType] == sortGroup)
            {
                CurSortType = handler[CurSortType];
            }
            else
            {
                CurSortType = recoverType;
                if (CurSortType == SortType.None) CurSortType = handler[CurSortType];
            }

            recoverType = CurSortType;
        }

        public static void SortByName() => ChangeSortType(SortConfig.TYPE_BY_NAME_GROUP, SortConfig.SortTypeChangeByNameHandler, ref NameType);

        public static void SortByPath() => ChangeSortType(SortConfig.TYPE_BY_PATH_GROUP, SortConfig.SortTypeChangeByPathHandler, ref PathType);

        public static void SortChild(ReferenceFinderData.AssetDescription data)
        {
            if (data == null) return;
            if (SortedAsset.ContainsKey(data.path))
            {
                if (SortedAsset[data.path] == CurSortType) return;
                SortType oldSortType = SortedAsset[data.path];
                if (SortConfig.SortTypeGroup[oldSortType] == SortConfig.SortTypeGroup[CurSortType])
                {
                    FastSort(data.dependencies);
                    FastSort(data.references);
                }
                else
                {
                    NormalSort(data.dependencies);
                    NormalSort(data.references);
                }

                SortedAsset[data.path] = CurSortType;
            }
            else
            {
                NormalSort(data.dependencies);
                NormalSort(data.references);
                SortedAsset.Add(data.path, CurSortType);
            }
        }

        public static void NormalSort(List<string> strList)
        {
            SortCompare curCompare = CompareFunction[CurSortType];
            strList.Sort((l, r) => curCompare(l, r));
        }

        public static void FastSort(List<string> strList)
        {
            int i = 0;
            int j = strList.Count - 1;
            while (i < j)
            {
                (strList[i], strList[j]) = (strList[j], strList[i]);
                i++;
                j--;
            }
        }

        public static int CompareWithName(string lString, string rString)
        {
            Dictionary<string, ReferenceFinderData.AssetDescription> asset = ResourceReferenceInfo.Data.assetDict;
            return string.Compare(asset[lString].name, asset[rString].name, StringComparison.Ordinal);
        }

        public static int CompareWithNameDesc(string lString, string rString) => 0 - CompareWithName(lString, rString);

        public static int CompareWithPath(string lString, string rString)
        {
            Dictionary<string, ReferenceFinderData.AssetDescription> asset = ResourceReferenceInfo.Data.assetDict;
            return string.Compare(asset[lString].path, asset[rString].path, StringComparison.Ordinal);
        }

        public static int CompareWithPathDesc(string lString, string rString) => 0 - CompareWithPath(lString, rString);
    }
}