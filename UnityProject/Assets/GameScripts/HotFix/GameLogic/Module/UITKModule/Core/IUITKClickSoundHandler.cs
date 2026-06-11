using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 按钮点击音效处理器接口。
    /// 业务层实现此接口，根据 button.name 或 class 决定播放什么音效。
    /// </summary>
    public interface IUITKClickSoundHandler
    {
        /// <summary>
        /// 当按钮被点击时调用。业务层自行决定音效策略。
        /// </summary>
        /// <param name="button">被点击的按钮。</param>
        void OnButtonClick(Button button);
    }
}
