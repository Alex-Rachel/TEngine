// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace Fantasy.Async
{
    public partial class FTask
    {
        #region NetTimer

        /// <summary>
        /// 异步等待指定时间
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask<bool> Wait(Scene scene, long time, FCancellationToken cancellationToken = null)
        {
            return scene.TimerComponent.Net.WaitAsync(time, cancellationToken);
        }

        /// <summary>
        /// 异步等待直到指定时间
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask<bool> WaitTill(Scene scene, long time, FCancellationToken cancellationToken = null)
        {
            return scene.TimerComponent.Net.WaitTillAsync(time, cancellationToken);
        }
        
        /// <summary>
        /// 异步等待一帧时间
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask WaitFrame(Scene scene)
        {
            return scene.TimerComponent.Net.WaitFrameAsync();
        }

        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long OnceTimer(Scene scene, long time, Action action)
        {
            return scene.TimerComponent.Net.OnceTimer(time, action);
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long OnceTillTimer(Scene scene, long time, Action action)
        {
            return scene.TimerComponent.Net.OnceTillTimer(time, action);
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，用于发布指定类型的事件。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="timerHandlerType"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long OnceTimer<T>(Scene scene, long time, T timerHandlerType) where T : struct
        {
            return scene.TimerComponent.Net.OnceTimer<T>(time, timerHandlerType);
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间，用于发布指定类型的事件。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="tillTime"></param>
        /// <param name="timerHandlerType"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long OnceTillTimer<T>(Scene scene, long tillTime, T timerHandlerType) where T : struct
        {
            return scene.TimerComponent.Net.OnceTillTimer<T>(tillTime, timerHandlerType);
        }
        
        /// <summary>
        /// 创建一个重复执行的计时器。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long RepeatedTimer(Scene scene, long time, Action action)
        {
            return scene.TimerComponent.Net.RepeatedTimer(time, action);
        }
        
        /// <summary>
        /// 创建一个重复执行的计时器，用于发布指定类型的事件。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="timerHandlerType"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long RepeatedTimer<T>(Scene scene, long time, T timerHandlerType) where T : struct
        {
            return scene.TimerComponent.Net.RepeatedTimer<T>(time, timerHandlerType);
        }
        
        /// <summary>
        /// 移除指定 ID 的计时器。
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="timerId"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RemoveTimer(Scene scene, ref long timerId)
        {
            return scene.TimerComponent.Net.Remove(ref timerId);
        }
        
        #endregion

        #region Unity

#if FANTASY_UNITY
        /// <summary>
        /// 异步等待指定时间。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask<bool> UnityWait(Scene scene, long time, FCancellationToken cancellationToken = null)
        {
            return scene.TimerComponent.Unity.WaitAsync(time, cancellationToken);
        }

        /// <summary>
        /// 异步等待直到指定时间。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask<bool> UnityWaitTill(Scene scene, long time, FCancellationToken cancellationToken = null)
        {
            return scene.TimerComponent.Unity.WaitTillAsync(time, cancellationToken);
        }
        
        /// <summary>
        /// 异步等待一帧时间。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask UnityWaitFrame(Scene scene)
        {
            return scene.TimerComponent.Unity.WaitFrameAsync();
        }

        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long UnityOnceTimer(Scene scene, long time, Action action)
        {
            return scene.TimerComponent.Unity.OnceTimer(time, action);
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long UnityOnceTillTimer(Scene scene, long time, Action action)
        {
            return scene.TimerComponent.Unity.OnceTillTimer(time, action);
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，用于发布指定类型的事件。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="timerHandlerType"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long UnityOnceTimer<T>(Scene scene, long time, T timerHandlerType) where T : struct
        {
            return scene.TimerComponent.Unity.OnceTimer<T>(time, timerHandlerType);
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间，用于发布指定类型的事件。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="tillTime"></param>
        /// <param name="timerHandlerType"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long UnityOnceTillTimer<T>(Scene scene, long tillTime, T timerHandlerType) where T : struct
        {
            return scene.TimerComponent.Unity.OnceTillTimer<T>(tillTime, timerHandlerType);
        }
        
        /// <summary>
        /// 创建一个重复执行的计时器。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long UnityRepeatedTimer(Scene scene, long time, Action action)
        {
            return scene.TimerComponent.Unity.RepeatedTimer(time, action);
        }
        
        /// <summary>
        /// 创建一个重复执行的计时器，用于发布指定类型的事件。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="time"></param>
        /// <param name="timerHandlerType"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long UnityRepeatedTimer<T>(Scene scene, long time, T timerHandlerType) where T : struct
        {
            return scene.TimerComponent.Unity.RepeatedTimer<T>(time, timerHandlerType);
        }
        
        /// <summary>
        /// 移除指定 ID 的计时器。（使用Unity的Time时间）
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="timerId"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UnityRemoveTimer(Scene scene, ref long timerId)
        {
            return scene.TimerComponent.Unity.Remove(ref timerId);
        }
#endif

        #endregion

        /// <summary>
        /// 创建并运行一个异步任务
        /// </summary>
        /// <param name="factory"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask Run(Func<FTask> factory)
        {
            return factory();
        }

        /// <summary>
        /// 创建并运行一个带有结果的异步任务
        /// </summary>
        /// <param name="factory"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask<T> Run<T>(Func<FTask<T>> factory)
        {
            return factory();
        }

        /// <summary>
        /// 等待所有任务完成
        /// </summary>
        /// <param name="tasks"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async FTask WaitAll(List<FTask> tasks)
        {
            if (tasks.Count <= 0)
            {
                return;
            }
            
            var count = tasks.Count;
            var sTaskCompletionSource = Create();
            
            foreach (var task in tasks)
            {
                RunSTask(task).Coroutine();
            }
            
            await sTaskCompletionSource;
            
            async FVoid RunSTask(FTask task)
            {
                await task;
                count--;
                if (count <= 0)
                {
                    sTaskCompletionSource.SetResult();
                }
            }
        }
        /// <summary>
        /// 等待其中一个任务完成
        /// </summary>
        /// <param name="tasks"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async FTask WaitAny(List<FTask> tasks)
        {
            if (tasks.Count <= 0)
            {
                return;
            }
            
            var count = 1;
            var sTaskCompletionSource = Create();
            
            foreach (var task in tasks)
            {
                RunSTask(task).Coroutine();
            }
            
            await sTaskCompletionSource;
            
            async FVoid RunSTask(FTask task)
            {
                await task;
                count--;
                if (count == 0)
                {
                    sTaskCompletionSource.SetResult();
                }
            }
        }
    }
}