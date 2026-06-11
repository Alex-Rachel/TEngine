using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// Tips 层弹窗测试。非全屏，下层仍可见。
    /// </summary>
    [UIWindow(UILayer.Tips)]
    public class TestTipsPopup : UITKWindow
    {
        private Button _btnClose;

        protected override void OnCreate()
        {
            _btnClose = RootElement.Q<Button>("btn-close");
            _btnClose.clicked += () => UITKModule.Instance.CloseUI<TestTipsPopup>();
        }

        protected override void OnDestroy()
        {
            TEngine.Log.Info("[TestTipsPopup] OnDestroy");
        }
    }
}
