#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Network.Interface;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Roaming.Handler;

/// <summary>
/// 传送漫游Terminus的请求处理
/// </summary>
internal sealed class I_TransferTerminusRequestHandler : AddressRPC<Scene, I_TransferTerminusRequest, I_TransferTerminusResponse>
{
    protected override async FTask Run(Scene scene, I_TransferTerminusRequest request, I_TransferTerminusResponse response, Action reply)
    {
        // 添加Terminus到当前Scene下。
        scene.TerminusComponent.AddTerminus(request.Terminus);
        // 执行Terminus的传送完成逻辑。
        await request.Terminus.TransferComplete(scene);
    }
}
#endif