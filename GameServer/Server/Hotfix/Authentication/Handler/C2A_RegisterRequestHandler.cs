using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace System;

public sealed class C2A_RegisterRequestHandler : MessageRPC<C2A_RegisterRequest, A2C_RegisterResponse>
{
    protected override async FTask Run(Session session, C2A_RegisterRequest request, A2C_RegisterResponse response, Action reply)
    {
        response.ErrorCode = await AuthenticationHelper.Register(session.Scene, request.UserName, request.Password, "用户注册");
        Log.Debug($"Register 当前的服务器是: {session.Scene.SceneConfigId}");
    }
}