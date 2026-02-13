using Fantasy.Pool;

#if !FANTASY_WEBGL
namespace Fantasy.Http
{
    /// <summary>
    /// 一个JsonRPC的接口
    /// </summary>
    public interface IJsonRpcRequest : IPool
    {
        /// <summary>
        /// 用于初始化这个Json对象
        /// </summary>
        /// <param name="method"></param>
        /// <param name="id"></param>
        /// <param name="params"></param>
        void Init(string method, int id, params object[] @params);
    }
}
#endif