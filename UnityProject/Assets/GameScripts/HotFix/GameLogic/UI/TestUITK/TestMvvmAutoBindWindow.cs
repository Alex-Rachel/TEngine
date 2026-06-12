using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// MVVM 自动绑定测试窗口。
    /// 演示 [Bind]/[BindCommand] 全自动绑定（OneWay+format、TwoWay 同类型、TwoWay+converter、命令）。
    /// 修改特性后需经菜单 TEngine/UITK/Generate All Bindings 重新生成 .bindgen.cs。
    /// </summary>
    [UIWindow(UILayer.UI)]
    public partial class TestMvvmAutoBindWindow : UITKWindow
    {
        // 纯展示（手动 [Q]）
        [Q] private Label lblTitle;

        // ━━━ MVVM 自动绑定字段（也会被自动查询并赋值）━━━

        /// <summary>OneWay + format：Counter(int) → "Count: N"</summary>
        [Bind("Counter", format: "Count: {0}")] private Label lblCounter;

        /// <summary>TwoWay 同类型：PlayerName(string) ↔ TextField</summary>
        [Bind("PlayerName", BindingMode.TwoWay)] private TextField inputName;

        /// <summary>TwoWay 同类型：Volume(float) ↔ Slider</summary>
        [Bind("Volume", BindingMode.TwoWay)] private Slider sliderVolume;

        /// <summary>TwoWay + converter：Counter(int) ↔ TextField(string)，经 IntToStringConverter</summary>
        [Bind("Counter", BindingMode.TwoWay, converter: typeof(IntToStringConverter))] private TextField inputCounter;

        /// <summary>命令绑定</summary>
        [BindCommand("IncrementCommand")] private Button btnIncrement;
        [BindCommand("CloseCommand")] private Button btnClose;

        // 生成器据此解析 ViewModel 类型与字段名
        private TestMvvmAutoBindViewModel _vm;

        protected override void OnCreate()
        {
            _vm = new TestMvvmAutoBindViewModel();
            lblTitle.text = "MVVM 自动绑定测试";
            // 注意：无需任何手动 += 订阅，__UITKAutoBindMVVM 在此之后自动执行绑定。
        }

        protected override void OnDestroy()
        {
            _vm.Dispose();
            // 无需手动解绑：__UITKAutoUnbindMVVM 在 OnDestroy 前已自动解绑。
        }
    }
}
