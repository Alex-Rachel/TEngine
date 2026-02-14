#if FANTASY_UNITY
using System;
using Fantasy.Async;
using Fantasy.Network;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
// ReSharper disable InconsistentNaming

namespace Fantasy
{
    /// <summary>
    /// Fantasy框架的运行时静态访问类，提供对Scene、Session和网络连接的快捷访问
    /// </summary>
    public static class Runtime
    {
        /// <summary>
        /// 获取当前的Fantasy场景实例
        /// </summary>
        /// <exception cref="InvalidOperationException">当Scene未初始化时抛出异常</exception>
        /// <remarks>
        /// 在访问此属性前，必须先调用 'await Fantasy.Platform.Unity.Entry.Initialize()' 和 'Runtime.Scene = await Scene.Create()'
        /// </remarks>
        public static Scene Scene
        {
            get
            {
                if (_scene == null)
                {
                    throw new InvalidOperationException(
                        "Fantasy Scene is not initialized. Please call 'await Fantasy.Platform.Unity.Entry.Initialize()' " +
                        "and 'Runtime.Scene = await Scene.Create()' before accessing Runtime.Scene.");
                }

                return _scene;
            }
            internal set => _scene = value;
        }

        /// <summary>
        /// 获取当前的网络会话实例
        /// </summary>
        /// <exception cref="InvalidOperationException">当Session未连接时抛出异常</exception>
        /// <remarks>
        /// 在访问此属性前，必须先调用 'Runtime.Connect()' 建立网络连接
        /// </remarks>
        public static Session Session
        {
            get
            {
                if (_session == null)
                {
                    throw new InvalidOperationException(
                        "Fantasy Session is not connected. Please call 'Runtime.Connect()' to establish a connection " +
                        "before accessing Runtime.Session.");
                }

                return _session;
            }
            internal set => _session = value;
        }
        
        /// <summary>
        /// 获取当前的会话心跳组件
        /// </summary>
        /// <exception cref="InvalidOperationException">当心跳组件未初始化时抛出异常</exception>
        /// <remarks>
        /// 需要在FantasyRuntime中启用心跳组件才能访问此属性
        /// </remarks>
        public static SessionHeartbeatComponent SessionHeartbeatComponent
        {
            get
            {
                if (_sessionHeartbeatComponent == null)
                {
                    throw new InvalidOperationException(
                        "SessionHeartbeatComponent is not initialized. Please enable heartbeat in FantasyRuntime settings " +
                        "or ensure the connection is established before accessing SessionHeartbeatComponent.");
                }

                return _sessionHeartbeatComponent;
            }
            internal set => _sessionHeartbeatComponent = value;
        }

        /// <summary>
        /// 获取当前的FantasyRuntime实例
        /// </summary>
        /// <exception cref="InvalidOperationException">当FantasyRuntime实例未设置时抛出异常</exception>
        /// <remarks>
        /// 只有当FantasyRuntime组件的isRuntimeInstance设置为true时，此属性才会被赋值
        /// </remarks>
        public static FantasyRuntime FantasyRuntime
        {
            get
            {
                if (_fantasyRuntime == null)
                {
                    throw new InvalidOperationException(
                        "FantasyRuntime instance is not set. Please ensure a FantasyRuntime component exists " +
                        "with isRuntimeInstance enabled before accessing Runtime.FantasyRuntime.");
                }

                return _fantasyRuntime;
            }
            internal set => _fantasyRuntime = value;
        }

        /// <summary>
        /// 获取当前网络延迟（单位：秒）
        /// </summary>
        /// <exception cref="InvalidOperationException">当心跳组件未初始化时抛出异常</exception>
        /// <remarks>
        /// 需要在FantasyInitialize中启用心跳组件才能访问此属性
        /// </remarks>
        public static float PingSeconds
        {
            get
            {
                if (_sessionHeartbeatComponent == null)
                {
                    throw new InvalidOperationException(
                        "Heartbeat component is not initialized. Please enable heartbeat in FantasyInitialize settings " +
                        "or ensure the connection is established before accessing ping information.");
                }

                return _sessionHeartbeatComponent.PingSeconds;
            }
        }

        /// <summary>
        /// 获取当前网络延迟（单位：毫秒）
        /// </summary>
        /// <exception cref="InvalidOperationException">当心跳组件未初始化时抛出异常</exception>
        /// <remarks>
        /// 需要在FantasyInitialize中启用心跳组件才能访问此属性
        /// </remarks>
        public static int PingMilliseconds
        {
            get
            {
                if (_sessionHeartbeatComponent == null)
                {
                    throw new InvalidOperationException(
                        "Heartbeat component is not initialized. Please enable heartbeat in FantasyInitialize settings " +
                        "or ensure the connection is established before accessing ping information.");
                }

                return _sessionHeartbeatComponent.PingMilliseconds;
            }
        }
        
        // 私有静态字段
        private static Scene _scene;
        private static Session _session;
        private static SessionHeartbeatComponent _sessionHeartbeatComponent;
        private static FantasyRuntime _fantasyRuntime;
        private static bool isInitialize;

        // 心跳配置参数
        private static bool _enableHeartbeat;
        private static int _heartbeatInterval;
        private static int _heartbeatTimeOut;
        private static int _heartbeatTimeOutInterval;
        private static int _maxPingSamples;

        // 连接事件回调
        private static Action _onConnectComplete;
        private static Action _onConnectFail;
        private static Action _onConnectDisconnect;

        /// <summary>
        /// 初始化Fantasy框架
        /// </summary>
        /// <returns>异步任务</returns>
        internal static async FTask Initialize()
        {
            if(!isInitialize)
            {
                await Platform.Unity.Entry.Initialize();
                isInitialize = true;
            }
        }

        /// <summary>
        /// 连接到远程服务器
        /// </summary>
        /// <param name="remoteIP">远程服务器IP地址</param>
        /// <param name="remotePort">远程服务器端口号</param>
        /// <param name="protocol">网络协议类型（TCP/KCP/WebSocket）</param>
        /// <param name="isHttps">是否启用HTTPS（仅WebSocket协议有效）</param>
        /// <param name="connectTimeout">连接超时时间（单位：毫秒）</param>
        /// <param name="enableHeartbeat">是否启用心跳组件</param>
        /// <param name="heartbeatInterval">心跳请求发送间隔（单位：毫秒）</param>
        /// <param name="heartbeatTimeOut">通信超时时间，超过此时间将自动断开会话（单位：毫秒）</param>
        /// <param name="heartbeatTimeOutInterval">检测连接超时的频率（单位：毫秒）</param>
        /// <param name="maxPingSamples">Ping包的采样数量，用于计算平均延迟</param>
        /// <param name="onConnectComplete">连接成功时的回调函数</param>
        /// <param name="onConnectFail">连接失败时的回调函数</param>
        /// <param name="onConnectDisconnect">连接断开时的回调函数</param>
        /// <returns>返回创建的Session实例</returns>
        /// <remarks>
        /// 此方法会自动处理旧连接的断开，并根据配置启用心跳组件
        /// </remarks>
        public static async FTask<Session> Connect(
            string remoteIP,
            int remotePort,
            FantasyRuntime.NetworkProtocolType protocol,
            bool isHttps,
            int connectTimeout,
            bool enableHeartbeat,
            int heartbeatInterval,
            int heartbeatTimeOut,
            int heartbeatTimeOutInterval,
            int maxPingSamples,
            [CanBeNull] Action onConnectComplete = null,
            [CanBeNull] Action onConnectFail = null,
            [CanBeNull] Action onConnectDisconnect = null
        )
        {
            if (_fantasyRuntime != null)
            {
                OnDestroy();
            }
            
            await Initialize();
            Scene = await Scene.Create();

            _enableHeartbeat = enableHeartbeat;
            _heartbeatInterval = heartbeatInterval;
            _heartbeatTimeOut = heartbeatTimeOut;
            _heartbeatTimeOutInterval = heartbeatTimeOutInterval;
            _maxPingSamples = maxPingSamples;

            _onConnectComplete = onConnectComplete;
            _onConnectFail = onConnectFail;
            _onConnectDisconnect = onConnectDisconnect;

            if (_session is { IsDisposed: false })
            {
                _session.Dispose();
            }

            Session = CreateSession(Scene, remoteIP, remotePort, protocol, isHttps, connectTimeout, OnConnectComplete,
                OnConnectFail, OnConnectDisconnect);

            return Session;
        }

        /// <summary>
        /// 创建网络会话连接
        /// </summary>
        /// <param name="scene">场景实例</param>
        /// <param name="remoteIP">远程服务器IP地址</param>
        /// <param name="remotePort">远程服务器端口号</param>
        /// <param name="protocol">网络协议类型（TCP/KCP/WebSocket）</param>
        /// <param name="isHttps">是否启用HTTPS（仅WebSocket协议有效）</param>
        /// <param name="connectTimeout">连接超时时间（单位：毫秒）</param>
        /// <param name="onConnectComplete">连接成功时的回调函数</param>
        /// <param name="onConnectFail">连接失败时的回调函数</param>
        /// <param name="onConnectDisconnect">连接断开时的回调函数</param>
        /// <returns>返回创建的Session实例</returns>
        /// <exception cref="ArgumentException">当协议类型不支持时抛出异常</exception>
        public static Session CreateSession(
            Scene scene,
            string remoteIP,
            int remotePort,
            FantasyRuntime.NetworkProtocolType protocol,
            bool isHttps,
            int connectTimeout,
            [CanBeNull] Action onConnectComplete = null,
            [CanBeNull] Action onConnectFail = null,
            [CanBeNull] Action onConnectDisconnect = null)
        {
            NetworkProtocolType networkProtocolType;

            switch (protocol)
            {
                case FantasyRuntime.NetworkProtocolType.TCP:
                {
                    networkProtocolType = NetworkProtocolType.TCP;
                    break;
                }
                case FantasyRuntime.NetworkProtocolType.KCP:
                {
                    networkProtocolType = NetworkProtocolType.KCP;
                    break;
                }
                case FantasyRuntime.NetworkProtocolType.WebSocket:
                {
                    networkProtocolType = NetworkProtocolType.WebSocket;
                    break;
                }
                default:
                {
                    throw new ArgumentException(
                        $"Unsupported network protocol type: {protocol}. Only TCP, KCP, and WebSocket are supported.",
                        nameof(protocol));
                }
            }
            
            return scene.Connect(
                string.Concat(remoteIP, ":", remotePort),
                networkProtocolType,
                onConnectComplete,
                onConnectFail,
                onConnectDisconnect,
                isHttps, connectTimeout);
        }

        /// <summary>
        /// 连接成功的内部回调处理
        /// </summary>
        private static void OnConnectComplete()
        {
            Log.Info($"[Fantasy] Connection established successfully");
            
            if (_enableHeartbeat && Session.GetComponent<SessionHeartbeatComponent>() == null)
            {
                _sessionHeartbeatComponent = Session.AddComponent<SessionHeartbeatComponent>();
                _sessionHeartbeatComponent.Start(
                    _heartbeatInterval, _heartbeatTimeOut, _heartbeatTimeOutInterval, _maxPingSamples);
                Log.Info($"[Fantasy] Heartbeat component started - Interval: {_heartbeatInterval}ms, Timeout: {_heartbeatTimeOut}ms");
            }

            _onConnectComplete?.Invoke();
        }

        /// <summary>
        /// 连接失败的内部回调处理
        /// </summary>
        private static void OnConnectFail()
        {
            Log.Error("[Fantasy] Connection failed - Unable to establish connection to server");
            _onConnectFail?.Invoke();
        }

        /// <summary>
        /// 连接断开的内部回调处理
        /// </summary>
        private static void OnConnectDisconnect()
        {
            Log.Info("[Fantasy] Connection disconnected from server");
            _onConnectDisconnect?.Invoke();
        }

        /// <summary>
        /// EventHandler格式的销毁方法（用于事件订阅）
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        internal static void OnDestroy(object sender, EventArgs e)
        {
            if (_fantasyRuntime == null)
            {
                return;
            }

            OnDestroy();
        }

        /// <summary>
        /// 清理Runtime的所有资源
        /// </summary>
        public static void OnDestroy()
        {
            if (_scene == null)
            {
                return;
            }
            
            if (_fantasyRuntime != null)
            {
                _fantasyRuntime = null;
            }
            else
            {
                _scene.Dispose();
            }
            
            _scene = null;
            _session = null;
            _sessionHeartbeatComponent = null;
            
            _enableHeartbeat = false;
            _heartbeatInterval = 0;
            _heartbeatTimeOut = 0;
            _heartbeatTimeOutInterval = 0;
            _maxPingSamples = 0;

            _onConnectComplete = null;
            _onConnectFail = null;
            _onConnectDisconnect = null;
        }
    }

    /// <summary>
    /// Fantasy框架的Unity运行时组件
    /// </summary>
    /// <remarks>
    /// 将此组件附加到GameObject上，可以在Unity中配置和管理Fantasy网络连接。
    /// 支持TCP、KCP、WebSocket等多种网络协议，可配置心跳、超时等参数。
    /// </remarks>
    public class FantasyRuntime : MonoBehaviour
    {
        /// <summary>
        /// 网络协议类型枚举
        /// </summary>
        public enum NetworkProtocolType
        {
            /// <summary>TCP协议</summary>
            TCP,
            /// <summary>KCP协议（低延迟UDP）</summary>
            KCP,
            /// <summary>WebSocket协议</summary>
            WebSocket
        }

        [Header("Instance Settings")]
        [Tooltip("Instance name for identification when multiple FantasyRuntime instances exist")]
        public string runtimeName = "FantasyRuntime";

        [Header("Network Settings")]
        [FormerlySerializedAs("RemoteIP")]
        public string remoteIP = "127.0.0.1";

        [FormerlySerializedAs("RemotePort")]
        public int remotePort = 20000;

        [Tooltip("Select network protocol type")]
        public NetworkProtocolType protocol = NetworkProtocolType.TCP;

        [Tooltip("Enable HTTPS for WebSocket protocol")]
        public bool enableHttps;

        [Header("Connection Settings")]
        [Tooltip("Connection timeout in milliseconds")]
        public int connectTimeout = 5000;

        [Header("Heartbeat Settings")]
        [Tooltip("Enable heartbeat component to keep connection alive")]
        public bool enableHeartbeat = true;

        [Tooltip("Heartbeat request send interval in milliseconds")]
        public int heartbeatInterval = 2000;

        [Tooltip("Communication timeout with server in milliseconds. Session will disconnect if exceeded")]
        public int heartbeatTimeOut = 30000;

        [Tooltip("Frequency to check connection timeout in milliseconds")]
        public int heartbeatTimeOutInterval = 5000;

        [Tooltip("Number of ping samples for latency calculation")]
        public int maxPingSamples = 4;

        [Header("Runtime Settings")]
        [Tooltip("Set this instance as the global Runtime instance for easy static access via Runtime.Scene and Runtime.Session")]
        public bool isRuntimeInstance;

        [Header("Event Callbacks")]
        [Tooltip("Event triggered when connection is established successfully")]
        public UnityEvent onConnectComplete;

        [Tooltip("Event triggered when connection fails")]
        public UnityEvent onConnectFail;

        [Tooltip("Event triggered when connection is disconnected")]
        public UnityEvent onConnectDisconnect;

        /// <summary>
        /// 获取当前Scene实例
        /// </summary>
        public Scene Scene { get; private set; }

        /// <summary>
        /// 获取当前Session实例
        /// </summary>
        public Session Session { get; private set; }
        
        /// <summary>
        /// 获取当前网络延迟（单位：秒）
        /// </summary>
        /// <exception cref="InvalidOperationException">当心跳组件未初始化时抛出异常</exception>
        /// <remarks>
        /// 需要在FantasyInitialize中启用心跳组件才能访问此属性
        /// </remarks>
        public float PingSeconds
        {
            get
            {
                if (_sessionHeartbeatComponent == null)
                {
                    throw new InvalidOperationException(
                        "Heartbeat component is not initialized. Please enable heartbeat in FantasyInitialize settings " +
                        "or ensure the connection is established before accessing ping information.");
                }

                return _sessionHeartbeatComponent.PingSeconds;
            }
        }

        /// <summary>
        /// 获取当前网络延迟（单位：毫秒）
        /// </summary>
        /// <exception cref="InvalidOperationException">当心跳组件未初始化时抛出异常</exception>
        /// <remarks>
        /// 需要在FantasyInitialize中启用心跳组件才能访问此属性
        /// </remarks>
        public int PingMilliseconds
        {
            get
            {
                if (_sessionHeartbeatComponent == null)
                {
                    throw new InvalidOperationException(
                        "Heartbeat component is not initialized. Please enable heartbeat in FantasyInitialize settings " +
                        "or ensure the connection is established before accessing ping information.");
                }

                return _sessionHeartbeatComponent.PingMilliseconds;
            }
        }

        // 私有字段
        private bool isStart;
        private EventHandler OnDestroyEvent;
        private SessionHeartbeatComponent _sessionHeartbeatComponent;

        /// <summary>
        /// Unity生命周期：组件启动时调用
        /// </summary>
        private void Start()
        {
            StartAsync().Coroutine();
        }

        /// <summary>
        /// 异步启动方法，初始化Scene和Session
        /// </summary>
        /// <returns>异步任务</returns>
        private async FTask StartAsync()
        {
            if (isStart)
            { 
                return;
            }
            
            isStart = true;
            await Runtime.Initialize();
           
            Scene = await Scene.Create();
            Session = Runtime.CreateSession(Scene, remoteIP, remotePort, protocol,
                enableHttps, connectTimeout,
                OnConnectComplete,
                OnConnectFail,
                OnConnectDisconnect);
            
            if (isRuntimeInstance)
            {
                Runtime.OnDestroy();
                Runtime.Scene = Scene;
                Runtime.Session = Session;
                Runtime.FantasyRuntime = this;
                
                OnDestroyEvent += Runtime.OnDestroy;
            }
        }

        /// <summary>
        /// 连接成功的回调处理
        /// </summary>
        private void OnConnectComplete()
        {
            Log.Info($"[{runtimeName}] Connection established successfully");

            if (enableHeartbeat && Session.GetComponent<SessionHeartbeatComponent>() == null)
            {
                _sessionHeartbeatComponent = Session.AddComponent<SessionHeartbeatComponent>();
                _sessionHeartbeatComponent.Start(
                    heartbeatInterval, heartbeatTimeOut, heartbeatTimeOutInterval, maxPingSamples);

                if (isRuntimeInstance)
                {
                    Runtime.SessionHeartbeatComponent = _sessionHeartbeatComponent;
                }

                Log.Info($"[{runtimeName}] Heartbeat component started - Interval: {heartbeatInterval}ms, Timeout: {heartbeatTimeOut}ms");
            }

            onConnectComplete?.Invoke();
        }

        /// <summary>
        /// 连接失败的回调处理
        /// </summary>
        private void OnConnectFail()
        {
            Log.Error($"[{runtimeName}] Connection failed - Unable to establish connection to server");
            onConnectFail?.Invoke();
        }

        /// <summary>
        /// 连接断开的回调处理
        /// </summary>
        private void OnConnectDisconnect()
        {
            Log.Info($"[{runtimeName}] Connection disconnected from server");
            onConnectDisconnect?.Invoke();
        }

        /// <summary>
        /// Unity生命周期：组件销毁时调用，清理所有资源
        /// </summary>
        public void OnDestroy()
        {
            isStart = false;
            
            if (Scene != null)
            {
                Scene.Dispose();
                Scene = null;
                Session = null;
                _sessionHeartbeatComponent = null;
            }

            if (!isRuntimeInstance)
            {
                return;
            }

            isRuntimeInstance = false;

            if (OnDestroyEvent != null)
            {
                OnDestroyEvent(this, EventArgs.Empty);
                OnDestroyEvent -= Runtime.OnDestroy;
            }
        }
    }
}
#endif
