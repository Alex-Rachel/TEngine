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
/// 处理撤销远程订阅者请求的路由消息处理器。
/// 用于在不通知订阅者的情况下,强制移除远程节点对本地场景领域事件的订阅。
/// </summary>
internal sealed class I_RevokeRemoteSubscriberRequestHandler : AddressRPC<Scene, I_RevokeRemoteSubscriberRequest, I_RevokeRemoteSubscriberResponse>
{
    protected override async FTask Run(Scene scene, I_RevokeRemoteSubscriberRequest request, I_RevokeRemoteSubscriberResponse response, Action reply)
    {
        // 验证 Address是否有效
        // Address 用于标识订阅者,不能为 0
        if (request.Address == 0)
        {
            response.ErrorCode = InnerErrorCode.ErrRevokeRemoteSubscriberInvalidAddress;
            return;
        }

        // 验证 TypeHashCode 是否有效
        // TypeHashCode 是事件类型的哈希值,不能为 0
        if (request.TypeHashCode == 0)
        {
            response.ErrorCode = InnerErrorCode.ErrRevokeRemoteSubscriberInvalidTypeHashCode;
            return;
        }

        // 撤销远程订阅者 (不发送通知)
        // 参数 false 表示不通知订阅者,直接移除订阅关系
        // 这通常用于订阅者已经断开连接或异常情况下的清理
        await scene.SphereEventComponent.Unsubscribe(request.Address, request.TypeHashCode, false);
    }
}
#endif