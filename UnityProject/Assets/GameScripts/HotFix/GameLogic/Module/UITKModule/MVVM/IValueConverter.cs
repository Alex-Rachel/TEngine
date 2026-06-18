using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 类型转换器接口。
    /// </summary>
    public interface IValueConverter<TSource, TTarget>
    {
        TTarget Convert(TSource value);
        TSource ConvertBack(TTarget value);
    }

    /// <summary>int → string</summary>
    public class IntToStringConverter : IValueConverter<int, string>
    {
        public string Convert(int value) => value.ToString();
        public int ConvertBack(string value) => int.TryParse(value, out int r) ? r : 0;
    }

    /// <summary>float → string</summary>
    public class FloatToStringConverter : IValueConverter<float, string>
    {
        public string Convert(float value) => value.ToString("F1");
        public float ConvertBack(string value) => float.TryParse(value, out float r) ? r : 0f;
    }

    /// <summary>bool → DisplayStyle</summary>
    public class BoolToDisplayConverter : IValueConverter<bool, DisplayStyle>
    {
        public DisplayStyle Convert(bool value) => value ? DisplayStyle.Flex : DisplayStyle.None;
        public bool ConvertBack(DisplayStyle value) => value == DisplayStyle.Flex;
    }

    /// <summary>bool → opacity (1/0)</summary>
    public class BoolToOpacityConverter : IValueConverter<bool, float>
    {
        public float Convert(bool value) => value ? 1f : 0f;
        public bool ConvertBack(float value) => value > 0.5f;
    }
}
