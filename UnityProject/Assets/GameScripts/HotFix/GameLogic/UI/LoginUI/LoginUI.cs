using GameLogic.Regicide;
using TEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.UI, location: "LoginUI")]
    public sealed class LoginUI : UIWindow
    {
        private Button _btnHost;
        private Button _btnClient;
        private Button _btnServer;
        private Text _txtMode;
        private Text _txtHostLabel;
        private Text _txtClientLabel;
        private Text _txtServerLabel;

        protected override void ScriptGenerator()
        {
            _btnHost = FindChildComponent<Button>("m_btnHost");
            _btnClient = FindChildComponent<Button>("m_btnClient");
            _btnServer = FindChildComponent<Button>("m_btnServer");
            _txtMode = FindChildComponent<Text>("m_txtMode");
            _txtHostLabel = FindChildComponent<Text>("m_btnHost/m_txtLabel");
            _txtClientLabel = FindChildComponent<Text>("m_btnClient/m_txtLabel");
            _txtServerLabel = FindChildComponent<Text>("m_btnServer/m_txtLabel");
        }

        protected override void RegisterEvent()
        {
            if (_btnHost != null)
            {
                _btnHost.onClick.AddListener(OnSelectHost);
            }

            if (_btnClient != null)
            {
                _btnClient.onClick.AddListener(OnSelectClient);
            }

            if (_btnServer != null)
            {
                _btnServer.onClick.AddListener(OnSelectServer);
            }
        }

        protected override void OnCreate()
        {
            if (_txtMode != null)
            {
                _txtMode.text = "请选择联机模式：主机 / 客户端 / 服务器";
            }

            if (_txtHostLabel != null)
            {
                _txtHostLabel.text = "主机模式";
            }

            if (_txtClientLabel != null)
            {
                _txtClientLabel.text = "客户端";
            }

            if (_txtServerLabel != null)
            {
                _txtServerLabel.text = "仅服务器";
            }
        }

        private void OnSelectHost()
        {
            ApplyMode(RegicideMirrorLaunchMode.Host, "已选择主机模式，正在启动...");
        }

        private void OnSelectClient()
        {
            ApplyMode(RegicideMirrorLaunchMode.Client, "已选择客户端，正在连接...");
        }

        private void OnSelectServer()
        {
            ApplyMode(RegicideMirrorLaunchMode.Server, "已选择仅服务器模式，正在启动...");
        }

        private void ApplyMode(RegicideMirrorLaunchMode mode, string message)
        {
            if (_txtMode != null)
            {
                _txtMode.text = message;
            }

            RegicideBootstrap.SelectMirrorMode(mode);
            Close();
        }
    }
}
