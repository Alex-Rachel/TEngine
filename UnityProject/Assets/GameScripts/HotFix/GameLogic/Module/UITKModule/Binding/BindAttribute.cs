using System;

namespace GameLogic
{
    /// <summary>
    /// MVVM 数据绑定标记。将 UI 元素绑定到 ViewModel 属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class BindAttribute : Attribute
    {
        public string Path { get; }
        public BindingMode Mode { get; }
        public string Format { get; }
        public Type Converter { get; }

        public BindAttribute(string path, BindingMode mode = BindingMode.OneWay, string format = null, Type converter = null)
        {
            Path = path;
            Mode = mode;
            Format = format;
            Converter = converter;
        }
    }
}
