namespace GameLogic
{
    /// <summary>
    /// 全功能测试 ViewModel。
    /// </summary>
    public class TestFullFeatureViewModel : ViewModelBase
    {
        // 基础绑定
        public BindableProperty<int> Counter { get; } = new(0);

        // 双向绑定
        public BindableProperty<string> PlayerName { get; } = new("");
        public BindableProperty<float> SliderValue { get; } = new(0f);

        // 命令
        public BindableCommand IncrementCommand { get; }
        public BindableCommand DecrementCommand { get; }
        public BindableCommand ResetCommand { get; }
        public BindableCommand BuyCommand { get; }

        // Buy 结果
        public BindableProperty<string> BuyResult { get; } = new("");

        // Widget 数据
        public BindableList<string> Items { get; } = new();
        private int _itemIndex;

        public TestFullFeatureViewModel()
        {
            IncrementCommand = new BindableCommand(() =>
            {
                Counter.Value++;
                BuyCommand.RaiseCanExecuteChanged();
            });

            DecrementCommand = new BindableCommand(() =>
            {
                if (Counter.Value > 0) Counter.Value--;
                BuyCommand.RaiseCanExecuteChanged();
            });

            ResetCommand = new BindableCommand(() =>
            {
                Counter.Value = 0;
                BuyCommand.RaiseCanExecuteChanged();
            });

            BuyCommand = new BindableCommand(
                execute: () => BuyResult.Value = $"购买成功! 剩余: {Counter.Value - 5}",
                canExecute: () => Counter.Value >= 5
            );
        }

        public void AddItem()
        {
            _itemIndex++;
            Items.Add($"动态 Widget #{_itemIndex}");
        }

        public void RemoveItem(int index)
        {
            if (index >= 0 && index < Items.Count)
            {
                Items.RemoveAt(index);
            }
        }
    }
}
