#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network;
using Fantasy.Network.Interface;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy.Roaming.Handler;

internal sealed class I_GetTerminusIdRequestHandler : AddressRPC<Scene, I_GetTerminusIdRequest, I_GetTerminusIdResponse>
{
    protected override async FTask Run(Scene scene, I_GetTerminusIdRequest request, I_GetTerminusIdResponse response, Action reply)
    {
        if (!scene.RoamingComponent.TryGet(request.RoamingId, out var sessionRoamingComponent) ||  !sessionRoamingComponent.TryGetRoaming(request.RoamingType, out var sessionRoaming))
        {
            response.ErrorCode = InnerErrorCode.ErrLockTerminusIdNotFoundRoamingType;
            return;
        }

        response.TerminusId = await sessionRoaming.GetTerminusId();
    }
}
#endif