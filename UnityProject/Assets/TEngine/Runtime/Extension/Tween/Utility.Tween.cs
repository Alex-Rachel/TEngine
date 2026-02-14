using System;

namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>
        /// 动画相关的实用函数。
        /// </summary>
        public static partial class Tween
        {
            private static ITweenHelper _tweenHelper = null;

            /// <summary>
            /// 设置动画辅助器。
            /// </summary>
            /// <param name="textHelper">要设置的动画辅助器。</param>
            public static void SetTweenHelper(ITweenHelper textHelper)
            {
                _tweenHelper = textHelper;
            }

            public static bool IsTweening(object onTarget)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.IsTweening(onTarget);
            }

            public static int GetTweenCount(object onTarget)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.GetTweenCount(onTarget);
            }

            public static bool IsAlive(long tweenId)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.IsAlive(tweenId);
            }

            public static void Stop(long tweenId)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                _tweenHelper.Stop(tweenId);
            }

            public static void Complete(long tweenId)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                _tweenHelper.Complete(tweenId);
            }

            public static int StopAll(object onTarget = null)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.StopAll(onTarget);
            }

            public static int CompleteAll(object onTarget = null)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.CompleteAll(onTarget);
            }

            public static void OnComplete(long tweenId, Action onComplete)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                _tweenHelper.OnComplete(tweenId, onComplete);
            }

            public static long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Delay(duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
            }

            public static long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Delay(target, duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
            }

            public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalRotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalRotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Scale(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Scale(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Scale(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Scale(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Rotation(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Rotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Rotation(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Rotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Position(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Position(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Position(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Position(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long PositionX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.PositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long PositionX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.PositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long PositionY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.PositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long PositionY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.PositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long PositionZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.PositionZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long PositionZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.PositionZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long LocalPosition(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long LocalPosition(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long LocalPositionX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long LocalPositionX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long LocalPositionY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long LocalPositionY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long LocalPositionZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPositionZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long LocalPositionZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalPositionZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Rotation(UnityEngine.Transform target, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Rotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Rotation(UnityEngine.Transform target, UnityEngine.Quaternion startValue, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Rotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalRotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Quaternion startValue, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.LocalRotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Scale(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Scale(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Scale(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Scale(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long ScaleX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.ScaleX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long ScaleX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.ScaleX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long ScaleY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.ScaleY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long ScaleY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.ScaleY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long ScaleZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.ScaleZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long ScaleZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.ScaleZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Color(UnityEngine.SpriteRenderer target, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Color(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Color(UnityEngine.SpriteRenderer target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Color(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long MaterialColor(UnityEngine.Material target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int 
                    cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.MaterialColor(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }
            public static long Alpha(UnityEngine.SpriteRenderer target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Alpha(UnityEngine.SpriteRenderer target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UISliderValue(UnityEngine.UI.Slider target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UISliderValue(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UISliderValue(UnityEngine.UI.Slider target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UISliderValue(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UINormalizedPosition(UnityEngine.UI.ScrollRect target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UINormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UINormalizedPosition(UnityEngine.UI.ScrollRect target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UINormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UIHorizontalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIHorizontalNormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIHorizontalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIHorizontalNormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UIAnchoredPosition(UnityEngine.RectTransform target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIAnchoredPosition(UnityEngine.RectTransform target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIAnchoredPositionX(UnityEngine.RectTransform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIAnchoredPositionX(UnityEngine.RectTransform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UIAnchoredPositionY(UnityEngine.RectTransform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIAnchoredPositionY(UnityEngine.RectTransform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UIVerticalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIVerticalNormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIVerticalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIVerticalNormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UIAnchoredPosition3D(UnityEngine.RectTransform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPosition3D(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIAnchoredPosition3D(UnityEngine.RectTransform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIAnchoredPosition3D(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UISizeDelta(UnityEngine.RectTransform target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UISizeDelta(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UISizeDelta(UnityEngine.RectTransform target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
                int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UISizeDelta(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Color(UnityEngine.UI.Graphic target, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Color(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Color(UnityEngine.UI.Graphic target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Color(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Alpha(UnityEngine.CanvasGroup target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Alpha(UnityEngine.CanvasGroup target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long Alpha(UnityEngine.UI.Graphic target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Alpha(UnityEngine.UI.Graphic target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }


            public static long UIFillAmount(UnityEngine.UI.Image target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
                float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIFillAmount(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long UIFillAmount(UnityEngine.UI.Image target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.UIFillAmount(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long MoveBezierPath(UnityEngine.Transform target, UnityEngine.Vector3[] path, float duration, Ease ease = Ease.Default, int cycles = 1,
                CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.MoveBezierPath(target, path, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Custom<T>(T target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Action<T, UnityEngine.Vector3> onValueChange,
                Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
                where T : class
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange,
                Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
                where T : class
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }

            public static long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange,
                Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
                where T : class
            {
                if (_tweenHelper == null)
                {
                    throw new GameFrameworkException("ITweenHelper is invalid.");
                }
                return _tweenHelper.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
            }
        }
    }
    
    public static class TweenExtensions
    {
        public static long OnComplete(this long tweenId, Action onComplete)
        {
            Utility.Tween.OnComplete(tweenId, onComplete);
            return tweenId;
        }
    }
}

