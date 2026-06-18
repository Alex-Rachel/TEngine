using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 可绑定属性。值变化时自动通知所有订阅者。
    /// </summary>
    public class BindableProperty<T>
    {
        private T _value;
        public event Action<T> OnValueChanged;

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                OnValueChanged?.Invoke(_value);
            }
        }

        public BindableProperty() { }
        public BindableProperty(T initial) => _value = initial;

        public static implicit operator T(BindableProperty<T> prop) => prop is null ? default : prop.Value;

        public override string ToString() => _value?.ToString() ?? "null";
    }
}
