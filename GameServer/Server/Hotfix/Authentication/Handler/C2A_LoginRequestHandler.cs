using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace System;

public sealed class C2A_LoginRequestHandler : MessageRPC<C2A_LoginRequest, A2C_LoginResponse>
{
    protected override async FTask Run(Session session, C2A_LoginRequest request, A2C_LoginResponse response, Action reply)
    {
        var scene = session.Scene;
        var result = await AuthenticationHelper.Login(scene, request.UserName, request.Password);
        response.ErrorCode = result.errorCode;

        if (response.ErrorCode == 0)
        {
            session.Send(new G2C_LoginMessage()
            {
                Msg = "Hello TEngine"
            });
        }
        Log.Debug($"Login 当前的服务器是: {scene.SceneConfigId}");
    }
}