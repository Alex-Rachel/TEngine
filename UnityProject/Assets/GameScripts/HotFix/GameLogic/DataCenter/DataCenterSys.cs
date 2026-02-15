using System.Collections.Generic;
using Fantasy;
using Fantasy.Async;
using TEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    /// <summary>
    /// 数据中心模块
    /// </summary>
    public class DataCenterSys : Singleton<DataCenterSys>, IUpdate
    {
        private readonly List<IDataCenterModule> m_dataCenterModuleList = new List<IDataCenterModule>();

        protected override void OnInit()
        {
            RegCmdHandle();
            InitModule();
            InitOtherModule();
        }

        private void RegCmdHandle()
        {

        }

        #region 网络操作

        /// <summary>
        /// 注册新账号。
        /// </summary>
        /// <param name="address">服务器地址</param>
        /// <param name="port">服务器端口</param>
        /// <param name="userName">用户名</param>
        /// <param name="password">密码</param>
        public async FTask Register(string address, int port, string userName, string password)
        {
            await GameClient.Instance.ConnectAsync(address, port);
            GameClient.Instance.Status = GameClientStatus.StatusRegister;
            var response = (A2C_RegisterResponse)await GameClient.Instance.Call(new C2A_RegisterRequest()
            {
                UserName = userName,
                Password = password
            });
            if (response.ErrorCode != 0)
            {
                Log.Warning($"Error: {response.ErrorCode}");
                return;
            }
            Log.Info("Registered Successfully");
        }

        /// <summary>
        /// 登录账号并连接到 Gate 服务器。
        /// </summary>
        /// <param name="address">认证服务器地址</param>
        /// <param name="port">认证服务器端口</param>
        /// <param name="userName">用户名</param>
        /// <param name="password">密码</param>
        public async FTask Login(string address, int port, string userName, string password)
        {
            await GameClient.Instance.ConnectAsync(address, port);
            GameClient.Instance.Status = GameClientStatus.StatusLogin;
            var response = (A2C_LoginResponse)await GameClient.Instance.Call(new C2A_LoginRequest()
            {
                UserName = userName,
                Password = password,
                LoginType = 1
            });

            if (response.ErrorCode != 0)
            {
                Log.Warning($"Error: {response.ErrorCode}");
                return;
            }
            Log.Info("Login Successfully");
            GameEvent.Get<ILoginUI>().OnLoginSuccess();
        }

        #endregion

        #region Module相关

        private void InitOtherModule()
        {
        }

        private void InitModule()
        {
            RegisterModule(LoginNetMgr.Instance);
        }

        /// <summary>
        /// 注册数据中心模块。
        /// </summary>
        /// <param name="module">要注册的模块</param>
        public void RegisterModule(IDataCenterModule module)
        {
            if (m_dataCenterModuleList.Contains(module))
            {
                return;
            }

            module.OnInit();
            m_dataCenterModuleList.Add(module);
        }

        #endregion

        /// <summary>
        /// 每帧更新所有已注册的模块。
        /// </summary>
        public void OnUpdate()
        {
            foreach (var module in m_dataCenterModuleList)
            {
                module.OnUpdate();
            }
        }

        /// <summary>
        /// 清除客户端数据，关闭所有窗口并通知所有模块角色登出。
        /// </summary>
        public void ClearClientData()
        {
            UIModule.Instance.CloseAll();
            for (int i = 0; i < m_dataCenterModuleList.Count; i++)
            {
                m_dataCenterModuleList[i].OnRoleLogout();
            }
        }
    }
}