#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network.Interface;
using Fantasy.Network.Roaming;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Roaming.Handler;

internal sealed class I_LinkRoamingRequestHandler : AddressRPC<Scene, I_LinkRoamingRequest, I_LinkRoamingResponse>
{
    protected override async FTask Run(Scene scene, I_LinkRoamingRequest request, I_LinkRoamingResponse response, Action reply)
    {
        uint errorCode = 0;
        Terminus roamingTerminal = null;

        if (request.LinkType == 1)
        {
            (errorCode, roamingTerminal) = await scene.TerminusComponent.ReLink(
                request.RoamingId, request.RoamingType,
                request.ForwardSessionAddress, request.SceneAddress, request.Args);
        }
        else
        {
            (errorCode, roamingTerminal) = await scene.TerminusComponent.Create(
                request.RoamingId, request.RoamingType,
                request.ForwardSessionAddress, request.SceneAddress, request.Args);
        }

        if (errorCode != 0)
        {
            response.ErrorCode = errorCode;
            return;
        }

        response.TerminusId = roamingTerminal.TerminusId;
    }
}
#endif
