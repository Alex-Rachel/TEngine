#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network;
using Fantasy.Network.Interface;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Roaming.Handler;

/// <summary>
/// 内部网络漫游解锁的请求处理。
/// </summary>
internal sealed class I_UnLockTerminusIdRequestHandler : AddressRPC<Scene, I_UnLockTerminusIdRequest, I_UnLockTerminusIdResponse>
{
    protected override async FTask Run(Scene scene, I_UnLockTerminusIdRequest request, I_UnLockTerminusIdResponse response, Action reply)
    {
        if (!scene.RoamingComponent.TryGet(request.RoamingId, out var sessionRoamingComponent) ||
            !sessionRoamingComponent.TryGetRoaming(request.RoamingType, out var sessionRoaming))
        {
            response.ErrorCode = InnerErrorCode.ErrLockTerminusIdNotFoundRoamingType;
            return;
        }
        
        sessionRoaming.UnLockTerminusId(request.TerminusId, request.TargetSceneAddress);
        await FTask.CompletedTask;
    }
}
#endif