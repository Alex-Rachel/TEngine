namespace GameLogic
{
    public class TestBindingViewModel : ViewModelBase
    {
        public BindableProperty<int> Counter { get; } = new(0);
        public BindableProperty<string> PlayerName { get; } = new("");
        public BindableCommand IncrementCommand { get; }
        public BindableCommand CloseCommand { get; }

        public TestBindingViewModel()
        {
            IncrementCommand = new BindableCommand(() => Counter.Value++);
            CloseCommand = new BindableCommand(() => UITKModule.Instance.CloseUI<TestBindingWindow>());
        }
    }
}
