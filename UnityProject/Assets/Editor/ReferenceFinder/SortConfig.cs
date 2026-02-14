using System.Collections.Generic;

namespace TEngine.Editor
{
    internal sealed class SortConfig
    {
        public static readonly Dictionary<SortType, SortType> SortTypeChangeByNameHandler = new Dictionary<SortType, SortType>
        {
            { SortType.None, SortType.AscByName },
            { SortType.AscByName, SortType.DescByName },
            { SortType.DescByName, SortType.AscByName }
        };

        public static readonly Dictionary<SortType, SortType> SortTypeChangeByPathHandler = new Dictionary<SortType, SortType>
        {
            { SortType.None, SortType.AscByPath },
            { SortType.AscByPath, SortType.DescByPath },
            { SortType.DescByPath, SortType.AscByPath }
        };

        public static readonly Dictionary<SortType, short> SortTypeGroup = new Dictionary<SortType, short>
        {
            { SortType.None, 0 },
            { SortType.AscByPath, 1 },
            { SortType.DescByPath, 1 },
            { SortType.AscByName, 2 },
            { SortType.DescByName, 2 }
        };

        public const short TYPE_BY_NAME_GROUP = 2;
        public const short TYPE_BY_PATH_GROUP = 1;
    }
}