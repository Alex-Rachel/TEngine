
namespace YooAsset.Editor
{
    public class SchemaInspector
    {
        /// <summary>
        /// 检视界面的UI元素容器（UIElements元素）
        /// </summary>
        public object Containner { private set; get; }

        /// <summary>
        /// 检视界面宽度
        /// </summary>
        public int Width = 250;

        /// <summary>
        /// 检视界面最小宽度
        /// </summary>
        public int MinWidth = 250;

        /// <summary>
        /// 检视界面最大宽度
        /// </summary>
        public int MaxWidth = 250;

        public SchemaInspector(object containner)
        {
            Containner = containner;
        }
        public SchemaInspector(object containner, int width)
        {
            Containner = containner;
            Width = width;
            MinWidth = width;
            MaxWidth = width;
        }
        public SchemaInspector(object containner, int width, int minWidth, int maxWidth)
        {
            Containner = containner;
            Width = width;
            MinWidth = minWidth;
            MaxWidth = maxWidth;
        }
    }
}