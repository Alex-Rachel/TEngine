#if FANTASY_NET
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network;
using Fantasy.Network.Interface;
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

namespace Fantasy.Roaming.Handler;

internal sealed class I_StopForwardingRequestHandler : AddressRPC<Scene, I_StopForwardingRequest, I_StopForwardingResponse>
{
    protected override async FTask Run(Scene scene, I_StopForwardingRequest request, I_StopForwardingResponse response, Action reply)
    {
        if (!scene.TerminusComponent.TryGetTerminus(request.RoamingId, out var terminus))
        {
            // 没有找到需要返回对应错误码
            response.ErrorCode = InnerErrorCode.ErrSetForwardSessionAddressNotFoundTerminus;
            return;
        }

        terminus.StopForwarding = true;
        await FTask.CompletedTask;
    }
}
#endif