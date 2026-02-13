#if FANTASY_NET
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network;
using Fantasy.Network.Interface;
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

namespace Fantasy.Roaming.Handler;

internal sealed class
    I_SetForwardSessionAddressRequestHandler : AddressRPC<Scene, I_SetForwardSessionAddressRequest,
    I_SetForwardSessionAddressResponse>
{
    protected override async FTask Run(Scene scene, I_SetForwardSessionAddressRequest request,
        I_SetForwardSessionAddressResponse response,
        Action reply)
    {
        if (!scene.TerminusComponent.TryGetTerminus(request.RoamingId, out var terminus))
        {
            // 没有找到需要返回对应错误码
            response.ErrorCode = InnerErrorCode.ErrSetForwardSessionAddressNotFoundTerminus;
            return;
        }

        terminus.StopForwarding = false;
        terminus.ForwardSessionAddress = request.ForwardSessionAddress;
        await FTask.CompletedTask;
    }
}
#endif