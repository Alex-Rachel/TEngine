using System;

namespace GameLogic
{
    /// <summary>
    /// 可绑定命令。支持 CanExecute 控制按钮可用状态。
    /// </summary>
    public class BindableCommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;
        public event Action CanExecuteChanged;

        public BindableCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute() => _canExecute?.Invoke() ?? true;

        public void Execute()
        {
            if (CanExecute()) _execute();
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke();
    }

    /// <summary>
    /// 泛型可绑定命令。
    /// </summary>
    public class BindableCommand<T>
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;
        public event Action CanExecuteChanged;

        public BindableCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(T param) => _canExecute?.Invoke(param) ?? true;

        public void Execute(T param)
        {
            if (CanExecute(param)) _execute(param);
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke();
    }
}
