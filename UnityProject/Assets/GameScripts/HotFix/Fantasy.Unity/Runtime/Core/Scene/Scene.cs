using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Event;
using Fantasy.EventAwaiter;
using Fantasy.IdFactory;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Pool;
using Fantasy.Scheduler;
using Fantasy.Timer;
#if FANTASY_NET
using Fantasy.Database;
using Fantasy.Platform.Net;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Fantasy.Network.Route;
using Fantasy.Network.Roaming;
using Fantasy.SeparateTable;
using Fantasy.Sphere;
#endif
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
namespace Fantasy
{
    /// <summary>
    /// 当Scene创建完成后发送的事件参数
    /// </summary>
    public struct OnCreateScene
    {
        /// <summary>
        /// 获取与事件关联的场景实体。
        /// </summary>
        public readonly Scene Scene;
        /// <summary>
        /// 初始化一个新的 OnCreateScene 实例。
        /// </summary>
        /// <param name="scene"></param>
        public OnCreateScene(Scene scene)
        {
            Scene = scene;
        }
    }
    
    /// <summary>
    /// 表示一个场景实体，用于创建与管理特定的游戏场景信息。
    /// </summary>
    [SuppressMessage("Compiler", "CS8618:Non-nullable field must contain a non-null value when exiting constructor. Consider adding the \'required\' modifier or declaring as nullable.")]
    public partial class Scene : Entity
    {
        #region Members
        /// <summary>
        /// Scene的运行类型
        /// </summary>
        public SceneRuntimeType SceneRuntimeType { get; protected set; }
#if FANTASY_NET
        /// <summary>
        /// Scene类型，对应SceneConfig的SceneType
        /// </summary>
        public int SceneType { get; protected set; }
        /// <summary>
        /// 所属的世界
        /// </summary>
        public World World { get; protected set; }
        /// <summary>
        /// 所在的Process
        /// </summary>
        public Process Process { get; protected set; }
        /// <summary>
        /// SceneConfig的Id
        /// </summary>
        public uint SceneConfigId { get; protected set; }
        internal ANetwork InnerNetwork { get; private set; }
        internal ANetwork OuterNetwork { get; private set; }
        /// <summary>
        /// 获取Scene对应的SceneConfig
        /// </summary>
        public SceneConfig SceneConfig => SceneConfigData.Instance.Get(SceneConfigId);
        private readonly Dictionary<uint, ProcessSessionInfo> _processSessionInfos = new ();
#endif
        /// <summary>
        /// 当前Scene的上下文
        /// </summary>
        public ThreadSynchronizationContext ThreadSynchronizationContext { get; internal set; }
        /// <summary>
        /// 当前Scene的下创建的Entity
        /// </summary>
        private readonly Dictionary<long, Entity> _entities = new Dictionary<long, Entity>();
        #endregion

        #region IdFactory

        /// <summary>
        /// Entity实体Id的生成器
        /// </summary>
        public IEntityIdFactory EntityIdFactory { get; protected set; }
        /// <summary>
        /// Entity实体RuntimeId的生成器
        /// </summary>
        public IRuntimeIdFactory RuntimeIdFactory { get; protected set; }

        #endregion
        
        #region Pool

        internal EntityPool EntityPool;
        internal EventAwaiterPool EventAwaiterPool;

        #endregion
        
        #region Component

        /// <summary>
        /// Scene下的任务调度器系统组件
        /// </summary>
        public TimerComponent TimerComponent { get; internal set; }
        /// <summary>
        /// Scene下的事件系统组件
        /// </summary>
        public EventComponent EventComponent { get; internal set; }
        /// <summary>
        /// Scene下的ESC系统组件
        /// </summary>
        public EntityComponent EntityComponent { get; internal set; }
        /// <summary>
        /// Scene下的协程锁组件
        /// </summary>
        public CoroutineLockComponent CoroutineLockComponent { get; internal set; }
        /// <summary>
        /// Scene下的网络消息派发组件 改成 public 方便客户端通过GameClient和协议号直接监听服务器数据的下发
        /// </summary>
        public MessageDispatcherComponent MessageDispatcherComponent { get; set; }
        /// <summary>
        /// 池生成器组件
        /// </summary>
        internal PoolGeneratorComponent PoolGeneratorComponent  { get; set; }
#if FANTASY_NET
        /// <summary>
        /// Scene下的Entity分表组件
        /// </summary>
        public SeparateTableComponent SeparateTableComponent { get; internal set; }
        /// <summary>
        /// Scene下的内网消息发送组件
        /// </summary>
        public NetworkMessagingComponent NetworkMessagingComponent { get; internal set; }
        /// <summary>
        /// Scene下的漫游终端管理组件
        /// </summary>
        public TerminusComponent TerminusComponent { get; internal set; }
        /// <summary>
        /// Scene下的Session漫游组件
        /// </summary>
        public RoamingComponent RoamingComponent { get; internal set; }
        /// <summary>
        /// Scene下的领域事件组件
        /// </summary>
        public SphereEventComponent SphereEventComponent  { get; internal set; }
#endif
        #endregion

        #region Initialize

        private async FTask Initialize()
        {
            EntityPool = new EntityPool();
            EventAwaiterPool = new EventAwaiterPool();
            EntityComponent = await Create<EntityComponent>(this, false, false).Initialize();
            SceneUpdate = EntityComponent;
#if FANTASY_UNITY
            SceneLateUpdate = EntityComponent;
#endif
            EventComponent = await Create<EventComponent>(this,false,true).Initialize();
            TimerComponent = Create<TimerComponent>(this, false, true).Initialize();
            CoroutineLockComponent = Create<CoroutineLockComponent>(this, false, true).Initialize();
            MessageDispatcherComponent = await Create<MessageDispatcherComponent>(this, false, true).Initialize();
            PoolGeneratorComponent = await Create<PoolGeneratorComponent>(this,false,false).Initialize();
#if FANTASY_NET
            NetworkMessagingComponent = Create<NetworkMessagingComponent>(this, false, true);
            SeparateTableComponent = await Create<SeparateTableComponent>(this, false, true).Initialize();
            TerminusComponent = Create<TerminusComponent>(this, false, true);
            RoamingComponent = Create<RoamingComponent>(this, false, true).Initialize();
            SphereEventComponent = await Create<SphereEventComponent>(this, false, true).Initialize();
#endif
        }
        
        /// <summary>
        /// Scene的关闭方法
        /// </summary>
        public virtual async FTask Close()
        {
#if FANTASY_NET
            await SphereEventComponent?.Close();
#endif
            await FTask.CompletedTask;
            Dispose();
        }

        /// <summary>
        /// Scene销毁方法，执行了该方法会把当前Scene下的所有实体都销毁掉。
        /// </summary>
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            
            base.Dispose();
            _entities.Remove(RuntimeId);

            switch (SceneRuntimeType)
            {
                case SceneRuntimeType.Root:
                {
#if FANTASY_NET
                    foreach (var (_, processSessionInfo) in _processSessionInfos.ToList())
                    {
                        processSessionInfo.Dispose();
                    }

                    _processSessionInfos.Clear();
#endif
                    _entities.Remove(EntityComponent.RuntimeId);

                    foreach (var (runtimeId, entity) in _entities.ToList())
                    {
                        if (runtimeId != entity.RuntimeId)
                        {
                            continue;
                        }
                        entity.Dispose();
                    }

                    _entities.Clear();
#if FANTASY_UNITY
                    _unityWorldId--;
                    _unitySceneId--;
#endif
#if FANTASY_NET
                    Process.RemoveScene(this, false);
                    Process.RemoveSceneToProcess(this);
#endif
                    EntityComponent.Dispose();
                    EntityPool.Dispose();
                    EventAwaiterPool.Dispose();
                    break;
                }
                case SceneRuntimeType.SubScene:
                {
                    break;
                }
                default:
                {
                    Log.Error($"SceneRuntimeType: {SceneRuntimeType} The unsupported SceneRuntimeType of the Scene executed Dispose.");
                    break;
                }
            }

            SceneUpdate = null;
            EntityIdFactory = null;
            RuntimeIdFactory = null;

            EntityPool = null;
            EventAwaiterPool = null;
            EntityComponent = null;
            TimerComponent = null;
            EventComponent = null;
            CoroutineLockComponent = null;
            MessageDispatcherComponent = null;
            PoolGeneratorComponent = null;
#if FANTASY_NET
            World = null;
            Process = null;
            SceneType = 0;
            SceneConfigId = 0;
            SeparateTableComponent = null;
            NetworkMessagingComponent = null;
            TerminusComponent = null;
            RoamingComponent = null;
            SphereEventComponent = null;
#elif FANTASY_UNITY
            Session = null;
            UnityNetwork = null;
            SceneLateUpdate = null;
#endif
            ThreadSynchronizationContext = null;
            SceneRuntimeType = SceneRuntimeType.None;
        }

        #endregion

        internal ISceneUpdate SceneUpdate { get; set; }
        internal void Update()
        {
            try
            {
                SceneUpdate.Update();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
#if FANTASY_UNITY
        internal ISceneLateUpdate SceneLateUpdate { get; set; }
        internal void LateUpdate()
        {
            try
            {
                SceneLateUpdate.LateUpdate();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
#endif
        #region Create

#if FANTASY_UNITY || FANTASY_CONSOLE
        private static uint _unitySceneId = 0;
        private static byte _unityWorldId = 0;
        public Session Session { get; private set; }
        private AClientNetwork UnityNetwork { get; set; }
        /// <summary>
        /// 创建一个Unity的Scene，注意:该方法只能在主线程下使用。
        /// </summary>
        /// <param name="sceneRuntimeMode">选择Scene的运行方式</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async FTask<Scene> Create(string sceneRuntimeMode = SceneRuntimeMode.MainThread)
        {
            var world = ++_unityWorldId;
            var sceneId = ++_unitySceneId;

            if (sceneId > 65535)
            {
                throw new Exception($"Scene ID ({sceneId}) exceeds the maximum allowed value of 65535.");
            }

            //
            // if (world > byte.MaxValue - 1)
            // {
            //     throw new Exception($"World ID ({world}) exceeds the maximum allowed value of 255.");
            // }

            // var sceneId = (uint)(++_unitySceneId + world * 1000);
            //
            // if (sceneId > 255255)
            // {
            //     throw new Exception($"Scene ID ({sceneId}) exceeds the maximum allowed value of 255255.");
            // }

            var scene = new Scene();
            scene.Scene = scene;
            scene.Parent = scene;
            scene.Type = typeof(Scene);
            scene.SceneRuntimeType = SceneRuntimeType.Root;
            scene.EntityIdFactory =  IdFactoryHelper.EntityIdFactory(sceneId, world);
            scene.RuntimeIdFactory = IdFactoryHelper.RuntimeIdFactory(0, sceneId, world);
            scene.Id = IdFactoryHelper.EntityId(0, sceneId, world, 0);
            scene.RuntimeId = IdFactoryHelper.RuntimeId(false, 0, sceneId, world, 0);
            scene.AddEntity(scene);
            await SetScheduler(scene, sceneRuntimeMode);
            scene.ThreadSynchronizationContext.Post(() =>
            {
                scene.EventComponent.PublishAsync(new OnCreateScene(scene)).Coroutine();
            });
            return scene;
        }
        public Session Connect(string remoteAddress, NetworkProtocolType networkProtocolType, Action onConnectComplete, Action onConnectFail, Action onConnectDisconnect, bool isHttps, int connectTimeout = 5000)
        {
            UnityNetwork?.Dispose();
            UnityNetwork = NetworkProtocolFactory.CreateClient(this, networkProtocolType, NetworkTarget.Outer);
            Session = UnityNetwork.Connect(remoteAddress, onConnectComplete, onConnectFail, onConnectDisconnect, isHttps, connectTimeout);
            return Session;
        }
#endif
#if FANTASY_NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Scene Create(Process process, byte worldId, uint sceneConfigId)
        {
            var scene = new Scene();
            scene.Scene = scene;
            scene.Parent = scene;
            scene.Type = typeof(Scene);
            scene.Process = process;
            scene.SceneRuntimeType = SceneRuntimeType.Root;
            scene.EntityIdFactory = IdFactoryHelper.EntityIdFactory(sceneConfigId, worldId);
            scene.RuntimeIdFactory = IdFactoryHelper.RuntimeIdFactory(0, sceneConfigId, worldId);
            scene.Id = IdFactoryHelper.EntityId(0, sceneConfigId, worldId, 0);
            scene.RuntimeId = IdFactoryHelper.RuntimeId(false, 0, sceneConfigId, worldId, 0);
            scene.AddEntity(scene);
            return scene;
        }
        /// <summary>
        /// 创建一个新的Scene
        /// </summary>
        /// <param name="process">所属的Process</param>
        /// <param name="machineConfig">对应的MachineConfig配置文件</param>
        /// <param name="sceneConfig">对应的SceneConfig配置文件</param>
        /// <returns>创建成功后会返回创建的Scene的实例</returns>
        public static async FTask<Scene> Create(Process process, MachineConfig machineConfig, SceneConfig sceneConfig)
        {
            var scene = Create(process, (byte)sceneConfig.WorldConfigId, sceneConfig.Id);
            scene.SceneType = sceneConfig.SceneType;
            scene.SceneConfigId = sceneConfig.Id;
            await SetScheduler(scene, sceneConfig.SceneRuntimeMode);
            
            if (sceneConfig.WorldConfigId != 0)
            {
                scene.World = World.Create(scene, (byte)sceneConfig.WorldConfigId);
            }

            if (sceneConfig.InnerPort != 0)
            {
                // 创建内网网络服务器
                scene.InnerNetwork = NetworkProtocolFactory.CreateServer(scene, ProgramDefine.InnerNetwork, NetworkTarget.Inner, machineConfig.InnerBindIP, sceneConfig.InnerPort);
            }

            if (sceneConfig.OuterPort != 0)
            {
                // 创建外网网络服务
                var networkProtocolType = Enum.Parse<NetworkProtocolType>(sceneConfig.NetworkProtocol);
                scene.OuterNetwork = NetworkProtocolFactory.CreateServer(scene, networkProtocolType, NetworkTarget.Outer, machineConfig.OuterBindIP, sceneConfig.OuterPort);
            }
            
            Process.AddScene(scene);
            process.AddSceneToProcess(scene);
            
            scene.ThreadSynchronizationContext.Post(() =>
            {
                if (sceneConfig.SceneTypeString == "Addressable")
                {
                    // 如果是AddressableScene,自动添加上AddressableManageComponent。
                    scene.AddComponent<AddressableManageComponent>(); 
                }
                
                scene.EventComponent.PublishAsync(new OnCreateScene(scene)).Coroutine();
            });
            
            return scene;
        }
        /// <summary>
        /// 在Scene下面创建一个子Scene，一般用于副本，或者一些特殊的场景。
        /// </summary>
        /// <param name="parentScene">主Scene的实例</param>
        /// <param name="sceneType">SceneType，可以在SceneType里找到，例如:SceneType.Addressable</param>
        /// <param name="onSubSceneComplete">子Scene创建成功后执行的委托，可以传递null</param>
        /// <returns></returns>
        public static async FTask<SubScene> CreateSubScene(Scene parentScene, int sceneType, Action<SubScene, Scene> onSubSceneComplete = null)
        {
            var tcs = FTask<SubScene>.Create(false);
            var scene = new SubScene();
            scene.Scene = scene;
            scene.Parent = scene;
            scene.RootScene = parentScene;
            scene.Type = typeof(SubScene);
            scene.SceneType = sceneType;
            scene.World = parentScene.World;
            scene.Process = parentScene.Process;
            scene.SceneRuntimeType = SceneRuntimeType.SubScene;
            scene.EntityIdFactory = parentScene.EntityIdFactory;
            scene.RuntimeIdFactory = parentScene.RuntimeIdFactory;
            scene.Id = scene.EntityIdFactory.Create;
            scene.RuntimeId = scene.RuntimeIdFactory.Create(false);
            scene.AddEntity(scene);
            scene.Initialize(parentScene);
            scene.ThreadSynchronizationContext.Post(() => OnEvent().Coroutine());
            return await tcs;
            async FTask OnEvent()
            {
                await scene.EventComponent.PublishAsync(new OnCreateScene(scene));
                onSubSceneComplete?.Invoke(scene, parentScene);
                tcs.SetResult(scene);
            }
        }
#endif
        private static async FTask SetScheduler(Scene scene, string sceneRuntimeMode)
        {
            switch (sceneRuntimeMode)
            {
                case "MainThread":
                {
                    scene.ThreadSynchronizationContext = ThreadScheduler.MainScheduler.ThreadSynchronizationContext;
                    scene.SceneUpdate = new EmptySceneUpdate();
#if FANTASY_UNITY
                    scene.SceneLateUpdate = new EmptySceneLateUpdate();
#endif
                    ThreadScheduler.AddMainScheduler(scene);
                    await scene.Initialize();
                    break;
                }
                case "MultiThread":
                {
#if !FANTASY_WEBGL
                    scene.ThreadSynchronizationContext = new ThreadSynchronizationContext();
#endif
                    scene.SceneUpdate = new EmptySceneUpdate();
#if FANTASY_UNITY
                    scene.SceneLateUpdate = new EmptySceneLateUpdate();
#endif
                    ThreadScheduler.AddToMultiThreadScheduler(scene);
                    await scene.Initialize();
                    break;
                }
                case "ThreadPool":
                {
#if !FANTASY_WEBGL
                    scene.ThreadSynchronizationContext = new ThreadSynchronizationContext();   
#endif
                    scene.SceneUpdate = new EmptySceneUpdate();
#if FANTASY_UNITY
                    scene.SceneLateUpdate = new EmptySceneLateUpdate();
#endif
                    ThreadScheduler.AddToThreadPoolScheduler(scene);
                    await scene.Initialize();
                    break;
                }
            }
        }
        #endregion

        #region Entities

        /// <summary>
        /// 添加一个实体到当前Scene下
        /// </summary>
        /// <param name="entity">实体实例</param>
        public virtual void AddEntity(Entity entity)
        {
            _entities.Add(entity.RuntimeId, entity);
        }

        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <returns>返回的实体</returns>
        public virtual Entity GetEntity(long runTimeId)
        {
            return _entities.TryGetValue(runTimeId, out var entity) ? entity : null;
        }

        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <param name="entity">实体实例</param>
        /// <returns>返回一个bool值来提示是否查找到这个实体</returns>
        public virtual bool TryGetEntity(long runTimeId, out Entity entity)
        {
            return _entities.TryGetValue(runTimeId, out entity);
        }

        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <typeparam name="T">要查询实体的泛型类型</typeparam>
        /// <returns>返回的实体</returns>
        public virtual T GetEntity<T>(long runTimeId) where T : Entity
        {
            return _entities.TryGetValue(runTimeId, out var entity) ? (T)entity : null;
        }

        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <param name="entity">实体实例</param>
        /// <typeparam name="T">要查询实体的泛型类型</typeparam>
        /// <returns>返回一个bool值来提示是否查找到这个实体</returns>
        public virtual bool TryGetEntity<T>(long runTimeId, out T entity) where T : Entity
        {
            if (_entities.TryGetValue(runTimeId, out var getEntity))
            {
                entity = (T)getEntity;
                return true;
            }

            entity = null;
            return false;
        }

        /// <summary>
        /// 删除一个实体，仅是删除不会指定实体的销毁方法
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <returns>返回一个bool值来提示是否删除了这个实体</returns>
        public virtual bool RemoveEntity(long runTimeId)
        {
            return _entities.Remove(runTimeId);
        }

        /// <summary>
        /// 删除一个实体，仅是删除不会指定实体的销毁方法
        /// </summary>
        /// <param name="entity">实体实例</param>
        /// <returns>返回一个bool值来提示是否删除了这个实体</returns>
        public virtual bool RemoveEntity(Entity entity)
        {
            return _entities.Remove(entity.RuntimeId);
        }

        #endregion

        #region InnerSession

#if FANTASY_NET
        /// <summary>
        /// 根据runTimeId获得Session
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        internal virtual Session GetSession(long address)
        {
            var sceneId = IdFactoryHelper.RuntimeIdTool.GetSceneId(ref address);

            if (_processSessionInfos.TryGetValue(sceneId, out var processSessionInfo))
            {
                if (!processSessionInfo.Session.IsDisposed)
                {
                    return processSessionInfo.Session;
                }

                _processSessionInfos.Remove(sceneId);
            }

            if (Process.IsInAppliaction(ref sceneId))
            {
                // 如果在同一个Process下，不需要通过Socket发送了，直接通过Process下转发。
                var processSession = Session.CreateInnerSession(Scene);
                _processSessionInfos.Add(sceneId, new ProcessSessionInfo(processSession, null));
                return processSession;
            }

            if (!SceneConfigData.Instance.TryGet(sceneId, out var sceneConfig))
            {
                throw new Exception($"The scene with sceneId {sceneId} was not found in the configuration file");
            }

            if (!ProcessConfigData.Instance.TryGet(sceneConfig.ProcessConfigId, out var processConfig))
            {
                throw new Exception($"The process with processId {sceneConfig.ProcessConfigId} was not found in the configuration file");
            }

            if (!MachineConfigData.Instance.TryGet(processConfig.MachineId, out var machineConfig))
            {
                throw new Exception($"The machine with machineId {processConfig.MachineId} was not found in the configuration file");
            }
            
            var remoteAddress = $"{machineConfig.InnerBindIP}:{sceneConfig.InnerPort}";
            var client = NetworkProtocolFactory.CreateClient(Scene, ProgramDefine.InnerNetwork, NetworkTarget.Inner);
            var session = client.Connect(remoteAddress, null, () =>
            {
                Log.Error($"Unable to connect to the target server sourceServerId:{Scene.Process.Id} targetServerId:{sceneConfig.ProcessConfigId}");
            }, null, false);
            _processSessionInfos.Add(sceneId, new ProcessSessionInfo(session, client));
            return session;
        }
#endif
        #endregion

        #region SceneType
#if FANTASY_NET
        
        /// <summary>
        /// SceneType字符串字典，key为SceneType的字符串名字，value为对应的索引
        /// </summary>
        public static FrozenDictionary<string, int> SceneTypeDictionary { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="message"></param>
        /// <typeparam name="T"></typeparam>
        public void Send<T>(long address, T message) where T : IAddressMessage
        {
            NetworkMessagingComponent.Send<T>(address, message);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="addressCollection"></param>
        /// <param name="message"></param>
        /// <typeparam name="T"></typeparam>
        public void Send<T>(ICollection<long> addressCollection, T message) where T : IAddressMessage
        {
            NetworkMessagingComponent.Send<T>(addressCollection, message);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="request"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public FTask<IResponse> Call<T>(long address, T request) where T : IAddressRequest
        {
            return NetworkMessagingComponent.Call<T>(address, request);
        }
#endif
        #endregion
    }
}