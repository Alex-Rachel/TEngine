using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 按钮点击音效处理器接口。
    /// 业务层实现此接口，决定每个按钮播放什么音效。
    /// </summary>
    public interface IUITKClickSoundHandler
    {
        /// <summary>
        /// 当按钮被点击时调用。
        /// </summary>
        /// <param name="button">被点击的按钮。</param>
        /// <returns>true=已处理音效，false=跳过（静音按钮）。</returns>
        bool OnButtonClick(Button button);
    }
}
