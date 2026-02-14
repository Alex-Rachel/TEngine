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
        public GameClientStatus Status { get; set; } = GameClientStatus.StatusInit;

        private string m_lastAddress = string.Empty;
        private int m_lastPort = 0;
        private float m_lastLogDisconnectErrTime = 0f;
        private ClientConnectWatcher m_clientConnectWatcher;

        public Scene Scene { get; private set; }

        public bool IsStatusEnter => Status == GameClientStatus.StatusEnter;

        public int LastNetErrorCode { get; private set; }

        protected override void OnInit()
        {
            m_clientConnectWatcher = new ClientConnectWatcher(this);
        }

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

        public void Reconnect()
        {
            if (string.IsNullOrEmpty(m_lastAddress) || m_lastPort <= 0)
            {
                return;
            }
            m_clientConnectWatcher?.Reconnect();
            Connect(m_lastAddress, m_lastPort, true);
        }

        public void CheckClientVersion()
        {
            m_clientConnectWatcher?.CheckClientVersion();
        }

        /// <summary>
        /// 设置是否监控网络重连
        /// 登录成功后 开启监控 可以自动重连或者提示玩家重连
        /// </summary>
        /// <param name="needWatch"></param>
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
        }

        private void OnConnectFail()
        {
            Status = GameClientStatus.StatusClose;
            Log.Info("[GameClient] Connected to server fail");
        }

        private void OnConnectDisconnect()
        {
            Status = GameClientStatus.StatusClose;
            Log.Info("[GameClient] Disconnected to server");
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
            m_clientConnectWatcher = null;
            Scene?.Dispose();
        }

        public void RegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (!CheckSceneIsValid())
            {
                return;
            }
            Scene.MessageDispatcherComponent?.RegisterMsgHandler(protocolCode, ctx);
        }

        public void UnRegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (!CheckSceneIsValid())
            {
                return;
            }
            Scene.MessageDispatcherComponent?.UnRegisterMsgHandler(protocolCode, ctx);
        }

        public void OnUpdate()
        {
            m_clientConnectWatcher?.Update();
        }
    }
}