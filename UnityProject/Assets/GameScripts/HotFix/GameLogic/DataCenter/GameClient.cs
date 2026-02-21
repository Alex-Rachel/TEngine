using System;
using System.Collections.Generic;
using System.Reflection;
using TEngine;
using Fantasy;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Log = TEngine.Log;

namespace GameLogic
{
   /// <summary>
    /// 网络客户端状态。
    /// </summary>
    public enum GameClientStatus
    {
        /// <summary>
        /// 初始化
        /// </summary>
        StatusInit,

        /// <summary>
        /// 连接成功服务器
        /// </summary>
        StatusConnected,

        /// <summary>
        /// 重新连接
        /// </summary>
        StatusReconnect,

        /// <summary>
        /// 断开连接
        /// </summary>
        StatusClose,

        /// <summary>
        /// 登录中
        /// </summary>
        StatusLogin,

        /// <summary>
        /// 注册
        /// </summary>
        StatusRegister,

        /// <summary>
        /// AccountLogin成功，进入服务器了
        /// </summary>
        StatusEnter,
    }

    public sealed class GameClient : Singleton<GameClient>, IUpdate
    {
        private readonly NetworkProtocolType ProtocolType = NetworkProtocolType.KCP;
        private string m_lastAddress = string.Empty;
        private int m_lastPort = 0;
        private float m_lastLogDisconnectErrTime = 0f;
        private ClientConnectWatcher m_clientConnectWatcher;
        private FTask<bool> m_connectTask;

        /// <summary>
        /// 网络连接状态
        /// </summary>
        public GameClientStatus Status { get; set; } = GameClientStatus.StatusInit;

        /// <summary>
        /// Fantasy 网络框架的 Scene 网络由此驱动
        /// </summary>
        public Scene Scene { get; private set; }

        /// <summary>
        /// 网络连接是否正常
        /// </summary>
        public bool IsStatusEnter => Status == GameClientStatus.StatusEnter;

        /// <summary>
        /// 最后一次返回的网络错误码
        /// </summary>
        public int LastNetErrorCode { get; private set; }

        protected override void OnInit()
        {
            m_clientConnectWatcher = new ClientConnectWatcher(this);
        }

        /// <summary>
        /// 异步初始化 Fantasy 网络框架
        /// </summary>
        /// <param name="assemblies">热更程序集</param>
        public async FTask InitAsync(List<Assembly> assemblies)
        {
            // ⚠️ 重要: 手动加载程序集必须手动触发 Fantasy 注册
            // RuntimeInitializeOnLoadMethod 只在 Unity 启动时自动执行一次
            // 手动加载的 DLL 不会触发 RuntimeInitializeOnLoadMethod
            // 调用 Assembly.EnsureLoaded() 来触发该程序集中的 Fantasy 框架注册
            if (assemblies != null && assemblies.Count > 0)
            {
                foreach (var assembly in assemblies)
                {
                    assembly.EnsureLoaded();
                }
            }
            await Fantasy.Platform.Unity.Entry.Initialize();
            Scene = await Fantasy.Platform.Unity.Entry.CreateScene();
            Log.Info("Fantasy 初始化完成!");
        }

        /// <summary>
        /// 同步连接网络
        /// </summary>
        /// <param name="address">ip地址</param>
        /// <param name="port">端口号</param>
        /// <param name="reconnect">是否重连</param>
        public void Connect(string address, int port, bool reconnect = false)
        {
            if (Status == GameClientStatus.StatusConnected || Status == GameClientStatus.StatusLogin ||
                Status == GameClientStatus.StatusEnter)
            {
                return;
            }

            // 关闭重连监控
            if (!reconnect)
            {
                SetWatchReconnect(false);
            }

            m_lastAddress = address;
            m_lastPort = port;
            Status = reconnect ? GameClientStatus.StatusReconnect : GameClientStatus.StatusInit;
            if (Scene.Session == null || Scene.Session.IsDisposed)
            {
                Scene.Connect($"{address}:{port}", ProtocolType, OnConnectComplete, OnConnectFail, OnConnectDisconnect, false);
            }
        }

        /// <summary>
        /// 异步连接网络
        /// </summary>
        /// <param name="address">ip地址</param>
        /// <param name="port">端口号</param>
        /// <param name="reconnect">是否重连</param>
        /// <returns></returns>
        public async FTask<bool> ConnectAsync(string address, int port, bool reconnect = false)
        {
            // 防止重复多次重连
            if (m_connectTask != null && !m_connectTask.IsCompleted)
            {
                Log.Warning("Connecting... Please do not initiate the connection again.");
                return await m_connectTask;
            }

            if (Status == GameClientStatus.StatusConnected || Status == GameClientStatus.StatusLogin ||
                Status == GameClientStatus.StatusEnter)
            {
                return true;
            }

            // 关闭重连监控
            if (!reconnect)
            {
                SetWatchReconnect(false);
            }

            m_lastAddress = address;
            m_lastPort = port;
            Status = reconnect ? GameClientStatus.StatusReconnect : GameClientStatus.StatusInit;
            // 创建待完成的 Task
            m_connectTask = FTask<bool>.Create(isPool: false);
            if (Scene.Session == null || Scene.Session.IsDisposed)
            {
                Scene.Connect($"{address}:{port}", ProtocolType, OnConnectComplete, OnConnectFail, OnConnectDisconnect, false);
            }
            return await m_connectTask;
        }

        /// <summary>
        /// 断开当前网络连接
        /// </summary>
        public void Disconnect()
        {
            m_connectTask?.SetResult(false);
            m_connectTask = null;
            SetWatchReconnect(false);
            if (Scene != null && !Scene.IsDisposed && Scene.Session != null && !Scene.Session.IsDisposed)
            {
                Scene.Session.Dispose();
            }
        }

        /// <summary>
        /// 网络重连
        /// </summary>
        public void Reconnect()
        {
            if (string.IsNullOrEmpty(m_lastAddress) || m_lastPort <= 0)
            {
                Log.Warning("Invalid reconnect param");
                return;
            }
            Disconnect();
            m_clientConnectWatcher?.Reconnect();
            Connect(m_lastAddress, m_lastPort, true);
        }

        /// <summary>
        /// 检查客户端资源版本
        /// </summary>
        public void CheckClientVersion()
        {
            m_clientConnectWatcher?.CheckClientVersion();
        }

        /// <summary>
        /// 设置是否监控网络重连
        /// 登录成功后 开启监控 可以自动重连或者提示玩家重连
        /// </summary>
        /// <param name="needWatch">是否需要监控</param>
        public void SetWatchReconnect(bool needWatch)
        {
            if (m_clientConnectWatcher == null)
            {
                return;
            }
            m_clientConnectWatcher.Enabled = needWatch;
        }

        private void OnConnectComplete()
        {
            Status = GameClientStatus.StatusConnected;
            Log.Info("[GameClient] Connected to server success");
            m_connectTask?.SetResult(true);
            m_connectTask = null;
        }

        private void OnConnectFail()
        {
            Status = GameClientStatus.StatusClose;
            Log.Info("[GameClient] Connected to server fail");
            m_connectTask?.SetResult(false);
            m_connectTask = null;
        }

        private void OnConnectDisconnect()
        {
            Status = GameClientStatus.StatusClose;
            Log.Info("[GameClient] Disconnected to server");
            m_connectTask?.SetResult(false);
            m_connectTask = null;
        }

        public bool IsStatusCanSendMsg(uint protocolCode)
        {
            bool canSend = false;

            if (Status == GameClientStatus.StatusLogin)
            {
                canSend = protocolCode == OuterOpcode.C2A_LoginRequest;
            }
            if (Status == GameClientStatus.StatusRegister)
            {
                canSend = protocolCode == OuterOpcode.C2A_RegisterRequest;
            }
            if (Status == GameClientStatus.StatusEnter)
            {
                canSend = true;
            }

            if (!canSend)
            {
                float nowTime = GameTime.unscaledTime;
                if (m_lastLogDisconnectErrTime + 5f < nowTime)
                {
                    Log.Error($"[GameClient] GameClient disconnect, send msg failed, protocolCode[{protocolCode}]");
                    m_lastLogDisconnectErrTime = nowTime;
                }
            }

            return canSend;
        }

        /// <summary>
        /// 发送消息到服务器（不需要响应）
        /// </summary>
        /// <typeparam name="T">消息类型，必须实现 IMessage 接口</typeparam>
        /// <param name="message">要发送的消息实例</param>
        /// <param name="rpcID">RPC ID</param>
        /// <param name="routeID">路由 ID，用于消息路由</param>
        public void Send<T>(T message, uint rpcID = 0, long routeID = 0) where T : IMessage
        {
            if (!CheckSceneIsValid())
            {
                Log.Error("Send Message Failed Because Session Is Null");
                return;
            }

            if (IsStatusCanSendMsg(rpcID))
            {
                Scene.Session.Send(message, rpcID, routeID);
            }
        }

        /// <summary>
        /// 发送 RPC 请求到服务器并等待响应
        /// </summary>
        /// <typeparam name="T">请求类型，必须实现 IRequest 接口</typeparam>
        /// <param name="request">请求实例</param>
        /// <param name="routeId">路由 ID，用于消息路由</param>
        /// <returns>服务器的响应结果</returns>
        public async FTask<IResponse> Call<T>(T request, long routeId = 0) where T : IRequest
        {
            if (!CheckSceneIsValid())
            {
                return null;
            }

            if (IsStatusCanSendMsg(request.OpCode()))
            {
                var requestCallback = await Scene.Session.Call(request, routeId);
                return requestCallback;
            }

            return null;
        }

        private bool CheckSceneIsValid()
        {
            if (Scene == null || Scene.Session == null || Scene.Session.IsDisposed)
            {
                return false;
            }

            return true;
        }

        protected override void OnRelease()
        {
            Disconnect();
            m_clientConnectWatcher = null;
            Scene?.Dispose();
        }

        /// <summary>
        /// 注册网络消息处理器
        /// </summary>
        /// <param name="protocolCode">协议码</param>
        /// <param name="ctx">消息处理回调</param>
        public void RegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (Scene == null || Scene.IsDisposed)
            {
                return;
            }
            Scene.MessageDispatcherComponent?.RegisterMsgHandler(protocolCode, ctx);
        }

        /// <summary>
        /// 取消注册网络消息处理器
        /// </summary>
        /// <param name="protocolCode">协议码</param>
        /// <param name="ctx">消息处理回调</param>
        public void UnRegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (Scene == null || Scene.IsDisposed)
            {
                return;
            }
            Scene.MessageDispatcherComponent?.UnRegisterMsgHandler(protocolCode, ctx);
        }

        /// <summary>
        /// 每帧更新，用于处理网络重连监控
        /// </summary>
        public void OnUpdate()
        {
            m_clientConnectWatcher?.Update();
        }
    }
}