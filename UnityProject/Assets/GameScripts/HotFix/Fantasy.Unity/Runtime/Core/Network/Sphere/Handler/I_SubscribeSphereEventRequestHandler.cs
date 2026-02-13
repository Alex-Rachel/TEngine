#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network;
using Fantasy.Network.Interface;
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace
namespace Fantasy.Sphere;

/// <summary>
/// 处理领域事件订阅请求的路由消息处理器。
/// 用于远程节点订阅本地场景的领域事件。
/// </summary>
internal sealed class I_SubscribeSphereEventRequestHandler : AddressRPC<Scene, I_SubscribeSphereEventRequest, I_SubscribeSphereEventResponse>
{
    protected override async FTask Run(Scene scene, I_SubscribeSphereEventRequest request, I_SubscribeSphereEventResponse response, Action reply)
    {
        // 验证 Address 是否有效
        // Address 用于标识订阅者,不能为 0
        if (request.Address == 0)
        {
            response.ErrorCode = InnerErrorCode.ErrSubscribeSphereEventInvalidAddress;
            return;
        }

        // 验证 TypeHashCode 是否有效
        // TypeHashCode 是事件类型的哈希值,不能为 0
        if (request.TypeHashCode == 0)
        {
            response.ErrorCode = InnerErrorCode.ErrSubscribeSphereEventInvalidTypeHashCode;
            return;
        }

        // 注册远程订阅者到领域事件组件
        // 当本地场景触发对应类型的领域事件时,会将事件推送到该订阅者
        scene.SphereEventComponent.RegisterRemoteSubscriber(request.Address, request.TypeHashCode);
        await FTask.CompletedTask;
    }
}
#endif