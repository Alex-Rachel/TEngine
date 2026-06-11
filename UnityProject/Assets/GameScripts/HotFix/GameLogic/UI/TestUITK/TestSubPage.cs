using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 子页面测试。全屏，会遮挡主界面（触发全屏遮挡逻辑）。
    /// </summary>
    [UIWindow(UILayer.UI, FullScreen = true)]
    public class TestSubPage : UITKWindow
    {
        protected override UITKAnimationType ShowAnimation => UITKAnimationType.SlideFromRight;
        protected override UITKAnimationType HideAnimation => UITKAnimationType.SlideToRight;
        protected override int AnimationDuration => 300;

        private Button _btnBack;

        protected override void OnCreate()
        {
            _btnBack = RootElement.Q<Button>("btn-back");
            _btnBack.clicked += OnBack;
        }

        private void OnBack()
        {
            UITKModule.Instance.CloseUIWithAnimation<TestSubPage>().Forget();
        }

        protected override void OnSetVisible(bool visible)
        {
            TEngine.Log.Info($"[TestSubPage] OnSetVisible: {visible}");
        }
    }
}
