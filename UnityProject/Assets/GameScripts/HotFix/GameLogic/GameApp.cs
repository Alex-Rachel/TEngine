using System.Collections.Generic;
using System.Reflection;
using GameLogic;
using GameLogic.Regicide;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
#pragma warning disable CS0436

/// <summary>
/// 游戏App。
/// </summary>
#if ENABLE_OBFUZ
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;

    /// <summary>
    /// 热更游戏App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];
        Log.Warning("======= Entrance GameApp =======");
        Utility.Unity.AddDestroyListener(Release);
        StartGameLogic();
    }

    private static void StartGameLogic()
    {
        RegicideBootstrap.Start();
    }

    private static void Release()
    {
        RegicideBootstrap.Shutdown();
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
