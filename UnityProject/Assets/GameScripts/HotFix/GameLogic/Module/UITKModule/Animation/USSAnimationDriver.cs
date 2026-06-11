using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 默认动画驱动器，使用 USS Transition 实现。
    /// </summary>
    public class USSAnimationDriver : IUITKAnimationDriver
    {
        public async UniTask Play(VisualElement target, UITKAnimationType type, bool isShow, int durationMs)
        {
            if (type == UITKAnimationType.None || target == null)
                return;

            // 1. 设置初始状态（无过渡）
            SetTransitionDuration(target, 0);
            ApplyState(target, type, isStart: true, isShow);

            await UniTask.Yield();

            // 2. 设置过渡 + 目标状态
            SetTransitionDuration(target, durationMs);
            ApplyState(target, type, isStart: false, isShow);

            // 3. 等待过渡完成
            await UniTask.Delay(durationMs);
        }

        private void SetTransitionDuration(VisualElement target, int ms)
        {
            target.style.transitionDuration = new List<TimeValue> { new TimeValue(ms, TimeUnit.Millisecond) };
            target.style.transitionProperty = new List<StylePropertyName>
            {
                new StylePropertyName("opacity"),
                new StylePropertyName("translate"),
                new StylePropertyName("scale"),
            };
            target.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };
        }

        private void ApplyState(VisualElement target, UITKAnimationType type, bool isStart, bool isShow)
        {
            bool atOrigin = isShow ? isStart : !isStart;

            switch (type)
            {
                case UITKAnimationType.FadeIn:
                case UITKAnimationType.FadeOut:
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;

                case UITKAnimationType.SlideFromBottom:
                case UITKAnimationType.SlideToBottom:
                    target.style.translate = atOrigin
                        ? new Translate(0, Length.Percent(100))
                        : new Translate(0, 0);
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;

                case UITKAnimationType.SlideFromRight:
                case UITKAnimationType.SlideToRight:
                    target.style.translate = atOrigin
                        ? new Translate(Length.Percent(100), 0)
                        : new Translate(0, 0);
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;

                case UITKAnimationType.ScaleIn:
                case UITKAnimationType.ScaleOut:
                    target.style.scale = atOrigin
                        ? new Scale(new Vector3(0.8f, 0.8f, 1f))
                        : new Scale(Vector3.one);
                    target.style.opacity = atOrigin ? 0f : 1f;
                    break;
            }
        }
    }
}
