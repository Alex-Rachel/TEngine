namespace GameLogic
{
    /// <summary>
    /// MVVM 自动绑定测试窗口的 ViewModel。
    /// 覆盖：int/string/float 属性、命令、CanExecute 控制。
    /// </summary>
    public class TestMvvmAutoBindViewModel : ViewModelBase
    {
        public BindableProperty<int> Counter { get; } = new(0);
        public BindableProperty<string> PlayerName { get; } = new("Player");
        public BindableProperty<float> Volume { get; } = new(50f);

        public BindableCommand IncrementCommand { get; }
        public BindableCommand CloseCommand { get; }

        public TestMvvmAutoBindViewModel()
        {
            IncrementCommand = new BindableCommand(() => Counter.Value++);
            CloseCommand = new BindableCommand(() => UITKModule.Instance.CloseUI<TestMvvmAutoBindWindow>());
        }
    }
}
