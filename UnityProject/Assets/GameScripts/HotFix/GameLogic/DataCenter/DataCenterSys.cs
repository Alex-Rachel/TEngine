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

        public async FTask Register(string address, int port, string userName, string password)
        {
            GameClient.Instance.Connect(address, port);
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

        public async FTask Login(string address, int port, string userName, string password)
        {
            GameClient.Instance.Connect(address, port);
            // todo:这里注册网络监听模块是为了测试 实际情况应该在InitModule里注册 DataCenterSys的初始化时机也应该自行把握
            RegisterModule(LoginNetMgr.Instance);
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
            // RegisterModule(LoginNetMgr.Instance);
        }

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

        public void OnUpdate()
        {
            foreach (var module in m_dataCenterModuleList)
            {
                module.OnUpdate();
            }
        }

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