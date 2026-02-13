#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network.Interface;

namespace Fantasy.Network.Route
{
    /// <summary>
    /// 声明一个 sealed 类 I_AddressableAddHandler，继承自 AddressRPC 类，并指定泛型参数
    /// </summary>
    public sealed class I_AddressableAddHandler : AddressRPC<Scene, I_AddressableAdd_Request, I_AddressableAdd_Response>
    {
        /// <summary>
        /// 在收到地址映射添加请求时执行的逻辑。
        /// </summary>
        /// <param name="scene">当前场景实例。</param>
        /// <param name="request">包含请求信息的 I_AddressableAdd_Request 实例。</param>
        /// <param name="response">用于构建响应的 I_AddressableAdd_Response 实例。</param>
        /// <param name="reply">执行响应的回调操作。</param>
        protected override async FTask Run(Scene scene, I_AddressableAdd_Request request, I_AddressableAdd_Response response, Action reply)
        {
            await scene.GetComponent<AddressableManageComponent>().Add(request.AddressableId, request.Address, request.IsLock);
        }
    }
}
#endif