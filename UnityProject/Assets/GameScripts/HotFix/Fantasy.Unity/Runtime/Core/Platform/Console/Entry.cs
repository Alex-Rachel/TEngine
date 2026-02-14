#if FANTASY_CONSOLE
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.Serialize;
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace Fantasy.Platform.Console
{
    public struct OnFantasyInit
    {
        public Scene Scene;
    }
    
    /// <summary>
    /// 一般的控制台启动入口，可以适用大部分客户端环境
    /// </summary>
    public sealed class Entry
    {
        private static bool _isInit;
        private static Thread _updateThread;
        public static Scene Scene { get; private set; }
        
        /// <summary>
        /// 初始化框架
        /// </summary>
        /// <param name="assemblies"></param>
        public static async FTask Initialize(params System.Reflection.Assembly[] assemblies)
        {
            if (_isInit)
            {
                Log.Error("Fantasy has already been initialized and does not need to be initialized again!");
                return;
            }
            Log.Info($"Fantasy Version:{Define.VERSION}");
            // 初始化程序集管理系统
            await AssemblySystem.InnerInitialize(assemblies);
            // 初始化序列化
            SerializerManager.Initialize();
            _isInit = true;
            Log.Debug("Fantasy Initialize Complete!");
        }

        /// <summary>
        /// 启动框架。
        /// 如果您的平台有每帧更新逻辑的方法，请不要调用这个方法。
        /// 如果没有实现每帧执行方法平台需要调用这个方法，目的是开启一个新的线程来每帧执行Update。
        /// 注意因为开启了一个新的线程来处理更新逻辑，所以要注意多线程的问题。
        /// </summary>
        public static void StartUpdate()
        {
            _updateThread = new Thread(() =>
            {
                while (_isInit)
                {
                    ThreadScheduler.Update();
                    Thread.Sleep(1);
                }
            })
            {
                IsBackground = true
            };
            _updateThread.Start();
        }
        
        /// <summary>
        /// 在Entry中创建一个Scene，如果Scene已经被创建过，将先销毁Scene再创建。
        /// </summary>
        /// <param name="sceneRuntimeMode"></param>
        /// <returns></returns>
        public static async FTask<Scene> CreateScene(string sceneRuntimeMode = SceneRuntimeMode.MainThread)
        {
            Scene?.Dispose();
            Scene = await Scene.Create(sceneRuntimeMode);
            await Scene.EventComponent.PublishAsync(new OnFantasyInit()
            {
                Scene = Scene
            });
            return Scene;
        }
        
        /// <summary>
        /// 如果有的话一定要在每帧执行这个方法
        /// </summary>
        public void Update()
        {
            ThreadScheduler.Update();
        }
        
        public static void Dispose()
        {
            AssemblySystem.Dispose();
            SerializerManager.Dispose();
            Scene?.Dispose();
            Scene = null;
            _isInit = false;
        }
    }
}
#endif