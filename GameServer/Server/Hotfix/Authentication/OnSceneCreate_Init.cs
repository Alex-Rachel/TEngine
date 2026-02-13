using Fantasy;
using Fantasy.Async;
using Fantasy.Event;

namespace System;

public class OnSceneCreate_Init : AsyncEventSystem<OnCreateScene>
{
    protected override async FTask Handler(OnCreateScene self)
    {
        var scene = self.Scene;
        switch (scene.SceneType)
        {
            case SceneType.Authentication:
                // 用于鉴权服务器注册和登录相关逻辑的组件
                scene.AddComponent<AuthenticationComponent>();
                break;

            case SceneType.Gate:
                Log.Debug("Gate服务器启动成功");
                break;
        }

        await FTask.CompletedTask;
    }
}