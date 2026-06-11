using UnityEngine.UIElements;

namespace GameLogic
{
    [UIWindow(UILayer.UI)]
    public class TestUITKWindow : UITKWindow
    {
        private Label _lblTitle;
        private Button _btnClose;

        protected override void OnCreate()
        {
            _lblTitle = RootElement.Q<Label>("lbl-title");
            _btnClose = RootElement.Q<Button>("btn-close");

            _btnClose.clicked += OnBtnCloseClicked;
        }

        protected override void OnRefresh()
        {
            _lblTitle.text = "UITKModule Works!";
        }

        protected override void OnDestroy()
        {
            if (_btnClose != null)
            {
                _btnClose.clicked -= OnBtnCloseClicked;
            }
        }

        private void OnBtnCloseClicked()
        {
            UITKModule.Instance.CloseUI<TestUITKWindow>();
        }
    }
}
