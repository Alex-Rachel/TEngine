#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network.Interface;

namespace Fantasy.Roaming.Handler;

internal sealed class I_UnLinkRoamingRequestHandler : AddressRPC<Scene, I_UnLinkRoamingRequest, I_UnLinkRoamingResponse>
{
    protected override async FTask Run(Scene scene, I_UnLinkRoamingRequest request, I_UnLinkRoamingResponse response, Action reply)
    {
        scene.TerminusComponent.RemoveTerminus(request.RoamingId, request.DisposeRoaming);
        await FTask.CompletedTask;
    }
}
#endif
