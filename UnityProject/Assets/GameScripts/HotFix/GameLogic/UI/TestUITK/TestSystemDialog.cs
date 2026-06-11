using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// System 层确认框测试。最高层级。
    /// </summary>
    [UIWindow(UILayer.System)]
    public class TestSystemDialog : UITKWindow
    {
        private Button _btnConfirm;
        private Button _btnCancel;

        protected override void OnCreate()
        {
            _btnConfirm = RootElement.Q<Button>("btn-confirm");
            _btnCancel = RootElement.Q<Button>("btn-cancel");

            _btnConfirm.clicked += OnConfirm;
            _btnCancel.clicked += OnCancel;
        }

        private void OnConfirm()
        {
            TEngine.Log.Info("[TestSystemDialog] 用户点击了确认");
            UITKModule.Instance.CloseUI<TestSystemDialog>();
        }

        private void OnCancel()
        {
            TEngine.Log.Info("[TestSystemDialog] 用户点击了取消");
            UITKModule.Instance.CloseUI<TestSystemDialog>();
        }
    }
}
