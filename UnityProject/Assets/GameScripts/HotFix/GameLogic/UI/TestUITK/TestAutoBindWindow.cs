using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// Source Generator 自动绑定测试窗口。
    /// 使用 [Q] + [OnClick] + [OnChange] 验证编译期代码生成。
    /// </summary>
    [UIWindow(UILayer.UI)]
    public partial class TestAutoBindWindow : UITKWindow
    {
        // ━━━ [Q] 自动查询绑定 ━━━
        // 字段名 camelCase → UXML name kebab-case
        [Q] Label lblTitle;           // → lbl-title
        [Q] Label lblCount;           // → lbl-count
        [Q] Button btnIncrement;      // → btn-increment
        [Q] Button btnDecrement;      // → btn-decrement
        [Q] TextField inputName;      // → input-name
        [Q] Label lblNameEcho;        // → lbl-name-echo
        [Q] Button btnClose;          // → btn-close

        private int _count;

        protected override void OnCreate()
        {
            // Source Generator 生成 __UITKAutoBind(RootElement) 填充上面的字段
            // Source Generator 生成 __UITKAutoBindEvents() 注册下面的事件
            __UITKAutoBind(RootElement);
            __UITKAutoBindEvents();
        }

        protected override void OnRefresh()
        {
            _count = 0;
            lblCount.text = "0";
            lblTitle.text = "Auto-Bind Works!";
        }

        // ━━━ [OnClick] 事件自动绑定 ━━━
        // 方法名 OnBtnIncrement → 绑定到 btn-increment.clicked
        [OnClick]
        void OnBtnIncrement()
        {
            _count++;
            lblCount.text = _count.ToString();
        }

        [OnClick]
        void OnBtnDecrement()
        {
            if (_count > 0) _count--;
            lblCount.text = _count.ToString();
        }

        [OnClick]
        void OnBtnClose()
        {
            UITKModule.Instance.CloseUI<TestAutoBindWindow>();
        }

        // ━━━ [OnChange] 值变化自动绑定 ━━━
        [OnChange("input-name")]
        void OnNameChanged(ChangeEvent<string> evt)
        {
            lblNameEcho.text = $"Echo: {evt.newValue}";
        }

        protected override void OnDestroy()
        {
            __UITKAutoUnbindEvents();
        }
    }
}
