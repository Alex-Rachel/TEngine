using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// MVVM 集成测试窗口。
    /// 本窗口用「手动绑定」验证 MVVM 核心功能；也可改为 partial class + [Q]/[Bind]，
    /// 经菜单 TEngine/UITK/Generate All Bindings 自动生成绑定代码。
    /// </summary>
    [UIWindow(UILayer.UI)]
    public class TestBindingWindow : UITKWindow
    {
        private Label _lblTitle;
        private Label _lblCounter;
        private Button _btnIncrement;
        private Button _btnClose;
        private TextField _inputName;
        private Label _lblNameDisplay;

        private TestBindingViewModel _vm;

        protected override void OnCreate()
        {
            // 手动 Q 绑定（可改用 [Q] 经绑定生成器自动生成）
            _lblTitle = RootElement.Q<Label>("lbl-title");
            _lblCounter = RootElement.Q<Label>("lbl-counter");
            _btnIncrement = RootElement.Q<Button>("btn-increment");
            _btnClose = RootElement.Q<Button>("btn-close");
            _inputName = RootElement.Q<TextField>("input-name");
            _lblNameDisplay = RootElement.Q<Label>("lbl-name-display");

            // 创建 ViewModel
            _vm = new TestBindingViewModel();

            // 手动 MVVM 绑定（可改用 [Bind]/[BindCommand] 经绑定生成器自动生成）
            _vm.Counter.OnValueChanged += v => _lblCounter.text = v.ToString();
            _vm.PlayerName.OnValueChanged += v => _lblNameDisplay.text = v;

            // Command 绑定
            _btnIncrement.clicked += _vm.IncrementCommand.Execute;
            _btnClose.clicked += _vm.CloseCommand.Execute;

            // TwoWay: TextField → ViewModel
            _inputName.RegisterValueChangedCallback(evt => _vm.PlayerName.Value = evt.newValue);
        }

        protected override void OnRefresh()
        {
            _lblTitle.text = "MVVM Binding Test";
            _lblCounter.text = _vm.Counter.Value.ToString();
        }

        protected override void OnDestroy()
        {
            _btnIncrement.clicked -= _vm.IncrementCommand.Execute;
            _btnClose.clicked -= _vm.CloseCommand.Execute;
            _vm.Dispose();
        }
    }
}
