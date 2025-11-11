using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
using UnityEngine;

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
    private static ErrorLogger _errorLogger;

    /// <summary>
    /// 热更域App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _errorLogger = new ErrorLogger();
        _hotfixAssembly = (List<Assembly>)objects[0];
        Log.Warning("======= 看到此条日志代表你成功运行了热更新代码 =======");
        Log.Warning("======= Entrance GameApp =======");
        Utility.Unity.AddDestroyListener(Release);
        Log.Warning("======= StartGameLogic =======");
        StartGameLogic().Forget();
    }

    private static async UniTaskVoid StartGameLogic()
    {
        await GameModule.UI.ShowUIAsync<BattleMainUI>();
        GameEvent.Get<ILoginUI>().ShowLoginUI();
    }

    private static void Release()
    {
        SingletonSystem.Release();
        if (_errorLogger != null)
        {
            _errorLogger.Dispose();
        }

        Log.Warning("======= Release GameApp =======");
    }
}