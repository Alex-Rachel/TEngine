using System;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 循环模式。
    /// </summary>
    public enum CycleMode
    {
        /// <summary>
        /// Restarts the tween from the beginning.
        /// </summary>
        Restart,

        /// <summary>
        /// Animates forth and back, like a yoyo. Easing is the same on the backward cycle.
        /// </summary>
        Yoyo,

        /// <summary>
        /// At the end of a cycle increments the `endValue` by the difference between `startValue` and `endValue`.\n\nFor example, if a tween moves position.x from 0 to 1, then after the first cycle, the tween will move the position.x from 1 to 2, and so on.
        /// </summary>
        Incremental,

        /// <summary>
        /// Rewinds the tween as if time was reversed. Easing is reversed on the backward cycle.
        /// </summary>
        Rewind,
    }

    /// <summary>
    /// 动画曲线类型。
    /// </summary>
    public enum Ease
    {
        Custom = -1,
        Default = 0,
        Linear = 1,
        InSine = 2,
        OutSine = 3,
        InOutSine = 4,
        InQuad = 5,
        OutQuad = 6,
        InOutQuad = 7,
        InCubic = 8,
        OutCubic = 9,
        InOutCubic = 10,
        InQuart = 11,
        OutQuart = 12,
        InOutQuart = 13,
        InQuint = 14,
        OutQuint = 15,
        InOutQuint = 16,
        InExpo = 17,
        OutExpo = 18,
        InOutExpo = 19,
        InCirc = 20,
        OutCirc = 21,
        InOutCirc = 22,
        InElastic = 23,
        OutElastic = 24,
        InOutElastic = 25,
        InBack = 26,
        OutBack = 27,
        InOutBack = 28,
        InBounce = 29,
        OutBounce = 30,
        InOutBounce = 31,
    }

    public static partial class Utility
    {
        public static partial class Tween
        {
            /// <summary>
            /// 动画辅助器接口。
            /// </summary>
            public interface ITweenHelper
            {
                public bool IsTweening(object onTarget);

                public int GetTweenCount(object onTarget);

                public bool IsAlive(long tweenId);

                public void Stop(long tweenId);

                public void Complete(long tweenId);

                public int StopAll(object onTarget = null);

                public int CompleteAll(object onTarget = null);

                public void OnComplete(long tweenId, Action onComplete);

                public long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true);
                
                public long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true);

                public long LocalRotation(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalRotation(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Scale(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Scale(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Rotation(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Rotation(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Position(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Position(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long PositionX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long PositionX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long PositionY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long PositionY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);
                
                public long PositionZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long PositionZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPosition(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPosition(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPositionX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPositionX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPositionY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPositionY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPositionZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalPositionZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Rotation(UnityEngine.Transform target, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Rotation(UnityEngine.Transform target, UnityEngine.Quaternion startValue, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalRotation(UnityEngine.Transform target, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long LocalRotation(UnityEngine.Transform target, UnityEngine.Quaternion startValue, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Scale(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Scale(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long ScaleX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long ScaleX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long ScaleY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long ScaleY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long ScaleZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long ScaleZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Color(UnityEngine.SpriteRenderer target, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Color(UnityEngine.SpriteRenderer target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);
                public long MaterialColor(UnityEngine.Material target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);
                public long Alpha(UnityEngine.SpriteRenderer target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Alpha(UnityEngine.SpriteRenderer target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UISliderValue(UnityEngine.UI.Slider target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UISliderValue(UnityEngine.UI.Slider target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UINormalizedPosition(UnityEngine.UI.ScrollRect target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UINormalizedPosition(UnityEngine.UI.ScrollRect target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIHorizontalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIHorizontalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);
                
                public long UIAnchoredPosition(UnityEngine.RectTransform target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIAnchoredPosition(UnityEngine.RectTransform target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIAnchoredPositionX(UnityEngine.RectTransform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIAnchoredPositionX(UnityEngine.RectTransform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIAnchoredPositionY(UnityEngine.RectTransform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIAnchoredPositionY(UnityEngine.RectTransform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIVerticalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIVerticalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIAnchoredPosition3D(UnityEngine.RectTransform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIAnchoredPosition3D(UnityEngine.RectTransform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UISizeDelta(UnityEngine.RectTransform target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UISizeDelta(UnityEngine.RectTransform target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
                    int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);
                
                public long Color(UnityEngine.UI.Graphic target, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Color(UnityEngine.UI.Graphic target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);
                
                public long Alpha(UnityEngine.CanvasGroup target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Alpha(UnityEngine.CanvasGroup target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Alpha(UnityEngine.UI.Graphic target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long Alpha(UnityEngine.UI.Graphic target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIFillAmount(UnityEngine.UI.Image target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                    float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long UIFillAmount(UnityEngine.UI.Image target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);

                public long MoveBezierPath(UnityEngine.Transform target, UnityEngine.Vector3[] path, float duration, Ease ease = Ease.Default, int cycles = 1,
                    CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false);
                
                public long Custom<T>(T target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Action<T, UnityEngine.Vector3> onValueChange,
                    Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
                    where T : class;

                public long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange,
                    Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
                    where T : class;

                public long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange,
                    Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
                    where T : class;
            }
        }
    }
}