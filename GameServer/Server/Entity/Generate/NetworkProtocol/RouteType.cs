using System.Collections.Generic;
namespace Fantasy
{
    /// <summary>
    /// 本代码有编辑器生成,请不要再这里进行编辑。
    /// Route协议定义(需要定义1000以上、因为1000以内的框架预留)
    /// </summary>
    public static partial class RouteType
    {
        public const int ChatRouteType = 10001; // ChatRouteType

        public static IEnumerable<int> RoamingTypes
        {
            get
            {
                yield return ChatRouteType;
            }
        }
    }
}