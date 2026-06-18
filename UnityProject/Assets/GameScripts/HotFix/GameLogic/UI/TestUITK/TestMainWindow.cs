using System.Text;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 主测试入口窗口。测试层级跳转、弹窗、窗口栈。
    /// </summary>
    [UIWindow(UILayer.UI, FullScreen = true)]
    public class TestMainWindow : UITKWindow
    {
        private Label _lblTitle;
        private Label _lblInfo;
        private Label _lblStack;
        private Button _btnOpenTips;
        private Button _btnOpenSystem;
        private Button _btnOpenSub;
        private Button _btnOpenAutoBind;
        private Button _btnOpenFull;
        private Button _btnClose;

        protected override void OnCreate()
        {
            _lblTitle = RootElement.Q<Label>("lbl-title");
            _lblInfo = RootElement.Q<Label>("lbl-info");
            _lblStack = RootElement.Q<Label>("lbl-stack");
            _btnOpenTips = RootElement.Q<Button>("btn-open-tips");
            _btnOpenSystem = RootElement.Q<Button>("btn-open-system");
            _btnOpenSub = RootElement.Q<Button>("btn-open-sub");
            _btnOpenAutoBind = RootElement.Q<Button>("btn-open-autobind");
            _btnOpenFull = RootElement.Q<Button>("btn-open-full");
            _btnClose = RootElement.Q<Button>("btn-close");

            _btnOpenTips.clicked += () => UITKModule.Instance.ShowUIAsync<TestTipsPopup>();
            _btnOpenSystem.clicked += () => UITKModule.Instance.ShowUIAsync<TestSystemDialog>();
            _btnOpenSub.clicked += () => UITKModule.Instance.ShowUIAsync<TestSubPage>();
            _btnOpenAutoBind.clicked += () => UITKModule.Instance.ShowUIAsync<TestAutoBindWindow>();
            _btnOpenFull.clicked += () => UITKModule.Instance.ShowUIAsync<TestFullFeatureWindow>();
            _btnClose.clicked += () => UITKModule.Instance.CloseUI<TestMainWindow>();
        }

        protected override void OnRefresh()
        {
            RefreshStack();
        }

        protected override void OnSetVisible(bool visible)
        {
            TEngine.Log.Info($"[TestMainWindow] OnSetVisible: {visible}");
            if (visible) RefreshStack();
        }

        protected override void OnUpdate()
        {
            // 每帧刷新栈信息（方便观察）
            RefreshStack();
        }

        private void RefreshStack()
        {
            if (_lblStack == null) return;
            var sb = new StringBuilder();
            // 通过反射或公开方法获取栈信息 - 这里简单显示当前状态
            sb.Append($"MainWindow visible={Visible}");
            _lblStack.text = sb.ToString();
        }
    }
}
