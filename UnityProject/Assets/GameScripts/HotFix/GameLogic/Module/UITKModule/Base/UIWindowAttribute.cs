using System;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 窗口声明特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class UIWindowAttribute : Attribute
    {
        /// <summary>
        /// 窗口层级。
        /// </summary>
        public UILayer Layer { get; }

        /// <summary>
        /// 资源定位地址。空则使用类名。
        /// </summary>
        public string Location { get; set; } = "";

        /// <summary>
        /// 是否全屏窗口（用于遮挡下层）。
        /// </summary>
        public bool FullScreen { get; set; } = false;

        /// <summary>
        /// 是否从 Resources 加载（不走 AB）。
        /// </summary>
        public bool FromResources { get; set; } = false;

        /// <summary>
        /// 隐藏后自动关闭时间（秒）。0 表示立即关闭。
        /// </summary>
        public int HideTimeToClose { get; set; } = 10;

        /// <summary>
        /// YooAsset 资源包名。空则使用默认包。
        /// </summary>
        public string Package { get; set; } = "";

        public UIWindowAttribute(UILayer layer)
        {
            Layer = layer;
        }

        public UIWindowAttribute(UILayer layer, bool fullScreen)
        {
            Layer = layer;
            FullScreen = fullScreen;
        }
    }
}
