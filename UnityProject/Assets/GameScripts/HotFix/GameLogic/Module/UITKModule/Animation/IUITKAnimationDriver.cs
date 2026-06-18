using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 动画驱动器接口。默认使用 USS Transition，可替换为 DOTween 等实现。
    /// </summary>
    public interface IUITKAnimationDriver
    {
        /// <summary>
        /// 播放动画。
        /// </summary>
        /// <param name="target">目标 VisualElement。</param>
        /// <param name="type">动画类型。</param>
        /// <param name="isShow">true=显示动画，false=隐藏动画。</param>
        /// <param name="durationMs">动画时长（毫秒）。</param>
        UniTask Play(VisualElement target, UITKAnimationType type, bool isShow, int durationMs);
    }
}
