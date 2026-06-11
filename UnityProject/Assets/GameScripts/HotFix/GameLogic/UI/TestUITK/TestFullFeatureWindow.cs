using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UITKModule 全功能测试窗口。
    /// 覆盖：MVVM绑定、双向绑定、Command+CanExecute、Widget动态创建、动画。
    /// Source Generator 就绪后可改为 partial + [Q]/[Bind] 自动绑定。
    /// </summary>
    [UIWindow(UILayer.UI, FullScreen = true)]
    public class TestFullFeatureWindow : UITKWindow
    {
        // 动画配置
        protected override UITKAnimationType ShowAnimation => UITKAnimationType.ScaleIn;
        protected override UITKAnimationType HideAnimation => UITKAnimationType.FadeOut;
        protected override int AnimationDuration => 300;

        // UI 元素
        private Label _lblTitle;
        private Label _lblCounter;
        private Button _btnIncrement;
        private Button _btnDecrement;
        private Button _btnReset;
        private TextField _inputName;
        private Label _lblNameSync;
        private Slider _sliderValue;
        private Label _lblSliderValue;
        private Button _btnBuy;
        private Label _lblBuyResult;
        private VisualElement _widgetContainer;
        private Button _btnAddWidget;
        private Button _btnCloseFade;
        private Button _btnCloseSlide;

        // MVVM
        private TestFullFeatureViewModel _vm;
        private List<TestItemWidget> _itemWidgets = new();

        protected override void OnCreate()
        {
            // ━━━ 手动 Q 绑定（Source Generator 就绪后替换为 [Q]）━━━
            _lblTitle = RootElement.Q<Label>("lbl-title");
            _lblCounter = RootElement.Q<Label>("lbl-counter");
            _btnIncrement = RootElement.Q<Button>("btn-increment");
            _btnDecrement = RootElement.Q<Button>("btn-decrement");
            _btnReset = RootElement.Q<Button>("btn-reset");
            _inputName = RootElement.Q<TextField>("input-name");
            _lblNameSync = RootElement.Q<Label>("lbl-name-sync");
            _sliderValue = RootElement.Q<Slider>("slider-value");
            _lblSliderValue = RootElement.Q<Label>("lbl-slider-value");
            _btnBuy = RootElement.Q<Button>("btn-buy");
            _lblBuyResult = RootElement.Q<Label>("lbl-buy-result");
            _widgetContainer = RootElement.Q<VisualElement>("widget-container");
            _btnAddWidget = RootElement.Q<Button>("btn-add-widget");
            _btnCloseFade = RootElement.Q<Button>("btn-close-fade");
            _btnCloseSlide = RootElement.Q<Button>("btn-close-slide");

            // ━━━ 创建 ViewModel ━━━
            _vm = new TestFullFeatureViewModel();

            // ━━━ OneWay 绑定：ViewModel → View ━━━
            _vm.Counter.OnValueChanged += OnCounterChanged;
            _vm.BuyResult.OnValueChanged += v => _lblBuyResult.text = v;

            // ━━━ TwoWay 绑定：View ↔ ViewModel ━━━
            // Name
            _vm.PlayerName.OnValueChanged += v => _lblNameSync.text = $"实时同步: {v}";
            _inputName.RegisterValueChangedCallback(evt => _vm.PlayerName.Value = evt.newValue);

            // Slider
            _vm.SliderValue.OnValueChanged += v => _lblSliderValue.text = $"滑块值: {v:F0}";
            _sliderValue.RegisterValueChangedCallback(evt => _vm.SliderValue.Value = evt.newValue);

            // ━━━ Command 绑定 ━━━
            _btnIncrement.clicked += _vm.IncrementCommand.Execute;
            _btnDecrement.clicked += _vm.DecrementCommand.Execute;
            _btnReset.clicked += _vm.ResetCommand.Execute;
            _btnBuy.clicked += _vm.BuyCommand.Execute;
            _vm.BuyCommand.CanExecuteChanged += UpdateBuyButtonState;

            // ━━━ Widget 动态创建 ━━━
            _btnAddWidget.clicked += OnAddWidget;
            _vm.Items.OnListChanged += RefreshWidgetList;

            // ━━━ 关闭按钮 ━━━
            _btnCloseFade.clicked += OnCloseFade;
            _btnCloseSlide.clicked += OnCloseSlide;

            // 初始状态
            UpdateBuyButtonState();
        }

        protected override void OnRefresh()
        {
            _lblCounter.text = _vm.Counter.Value.ToString();
        }

        private void OnCounterChanged(int value)
        {
            _lblCounter.text = value.ToString();
        }

        private void UpdateBuyButtonState()
        {
            _btnBuy.SetEnabled(_vm.BuyCommand.CanExecute());
        }

        private void OnAddWidget()
        {
            _vm.AddItem();
        }

        private void RefreshWidgetList()
        {
            // 清除旧 Widget
            foreach (var w in _itemWidgets)
            {
                w.Destroy();
            }
            _itemWidgets.Clear();

            // 重建
            for (int i = 0; i < _vm.Items.Count; i++)
            {
                var widget = new TestItemWidget();
                var asset = UITKModule.Resource.LoadVisualTreeAsset("TestItemWidget");
                var root = asset.CloneTree();
                _widgetContainer.Add(root);
                widget.Create(this, root, true);
                widget.OnBindData(_vm.Items[i], i);
                widget.OnRemoveClicked = OnRemoveWidget;
                _itemWidgets.Add(widget);
            }
        }

        private void OnRemoveWidget(int index)
        {
            _vm.RemoveItem(index);
        }

        private void OnCloseFade()
        {
            UITKModule.Instance.CloseUIWithAnimation<TestFullFeatureWindow>().Forget();
        }

        private void OnCloseSlide()
        {
            // 临时覆盖动画后关闭
            UITKModule.Instance.CloseUIWithAnimation<TestFullFeatureWindow>().Forget();
        }

        protected override void OnDestroy()
        {
            // 解绑事件
            _vm.Counter.OnValueChanged -= OnCounterChanged;
            _vm.BuyCommand.CanExecuteChanged -= UpdateBuyButtonState;
            _btnIncrement.clicked -= _vm.IncrementCommand.Execute;
            _btnDecrement.clicked -= _vm.DecrementCommand.Execute;
            _btnReset.clicked -= _vm.ResetCommand.Execute;
            _btnBuy.clicked -= _vm.BuyCommand.Execute;
            _btnAddWidget.clicked -= OnAddWidget;
            _btnCloseFade.clicked -= OnCloseFade;
            _btnCloseSlide.clicked -= OnCloseSlide;
            _vm.Items.OnListChanged -= RefreshWidgetList;

            foreach (var w in _itemWidgets)
            {
                w.Destroy();
            }
            _itemWidgets.Clear();

            _vm.Dispose();
        }
    }
}
