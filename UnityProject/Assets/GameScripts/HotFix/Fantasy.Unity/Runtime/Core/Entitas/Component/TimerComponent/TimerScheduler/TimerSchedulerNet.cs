using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.DataStructure.Collection;
using Fantasy.Helper;
// ReSharper disable UnusedParameter.Global

#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Fantasy.Timer
{
    /// <summary>
    /// 基于系统事件的任务调度系统
    /// </summary>
    public sealed class TimerSchedulerNet
    {
        private readonly Scene _scene;
        private long _idGenerator;
        private long _minTime; // 最小时间
        private readonly Queue<long> _timeOutTime = new Queue<long>();
        private readonly Queue<long> _timeOutTimerIds = new Queue<long>();
        private readonly Dictionary<long, TimerAction> _timerActions = new Dictionary<long, TimerAction>();
        private readonly SortedOneToManyList<long, long> _timeId = new(); // 时间与计时器ID的有序一对多列表
        private long GetId => ++_idGenerator;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="scene">当前的Scene</param>
        public TimerSchedulerNet(Scene scene)
        {
            _scene = scene;
        }
        
        private long Now()
        {
            return TimeHelper.Now;
        }
        
        /// <summary>
        /// 驱动方法，只有调用这个方法任务系统才会正常运转。
        /// </summary>
        public void Update()
        {
            if (_timeId.Count == 0)
            { 
                return;
            }
            
            var currentTime = Now(); 
            
            if (currentTime < _minTime)
            { 
                return;
            }
            
            // 遍历时间ID列表，查找超时的计时器任务
            foreach (var (key, _) in _timeId)
            {
                if (key > currentTime)
                {
                    _minTime = key;
                    break;
                }

                _timeOutTime.Enqueue(key);
            }
            
            // 处理超时的计时器任务
            while (_timeOutTime.TryDequeue(out var time))
            {
                var timerIds = _timeId[time];
                for (var i = 0; i < timerIds.Count; ++i)
                {
                    _timeOutTimerIds.Enqueue(timerIds[i]);
                }

                _timeId.Remove(time);
                // _timeId.RemoveKey(time);
            }
            
            if (_timeId.Count == 0)
            {
                _minTime = long.MaxValue;
            }

            // 执行超时的计时器任务的回调操作
            while (_timeOutTimerIds.TryDequeue(out var timerId))
            {
                if (!_timerActions.Remove(timerId, out var timerAction))
                {
                    continue;
                }

                // 根据计时器类型执行不同的操作
                switch (timerAction.TimerType)
                {
                    case TimerType.OnceWaitTimer:
                    {
                        var tcs = (FTask<bool>)timerAction.Callback;
                        tcs.SetResult(true);
                        break;
                    }
                    case TimerType.OnceTimer:
                    {
                        if (timerAction.Callback is not Action action)
                        {
                            Log.Error($"timerAction {timerAction.ToJson()}");
                            break;
                        }

                        action();
                        break;
                    }
                    case TimerType.RepeatedTimer:
                    {
                        if (timerAction.Callback is not Action action)
                        {
                            Log.Error($"timerAction {timerAction.ToJson()}");
                            break;
                        }
                        
                        timerAction.StartTime = Now();
                        AddTimer(ref timerAction);
                        action();
                        break;
                    }
                }
            }
        }
        
        private void AddTimer(ref TimerAction timer)
        {
            var tillTime = timer.StartTime + timer.TriggerTime;
            _timeId.Add(tillTime, timer.TimerId);
            _timerActions.Add(timer.TimerId, timer);

            if (tillTime < _minTime)
            {
                _minTime = tillTime;
            }
        }

        /// <summary>
        /// 异步等待指定时间。
        /// </summary>
        /// <param name="time">等待的时间长度。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>等待是否成功。</returns>
        public async FTask<bool> WaitAsync(long time, FCancellationToken cancellationToken = null)
        {
            if (time <= 0)
            {
                return true;
            }

            var now = Now();
            var timerId = GetId;
            var tcs = FTask<bool>.Create();
            var timerAction = new TimerAction(timerId, TimerType.OnceWaitTimer, now, time, tcs);

            void CancelActionVoid()
            {
                if (Remove(timerId))
                {
                    tcs.SetResult(false);
                }
            }

            bool result;

            try
            {
                cancellationToken?.Add(CancelActionVoid);
                AddTimer(ref timerAction);
                result = await tcs;
            }
            catch (Exception ex)
            {
                Log.Error($"TimerSchedulerNet.WaitAsync failed: timerId={timerId}, time={time}, exception={ex}");
                throw;
            }
            finally
            {
                cancellationToken?.Remove(CancelActionVoid);
            }

            return result;
        }

        /// <summary>
        /// 异步等待直到指定时间。
        /// </summary>
        /// <param name="tillTime">等待的目标时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>等待是否成功。</returns>
        public async FTask<bool> WaitTillAsync(long tillTime, FCancellationToken cancellationToken = null)
        {
            var now = Now();

            if (now >= tillTime)
            {
                return true;
            }

            var timerId = GetId;
            var tcs = FTask<bool>.Create();
            var timerAction = new TimerAction(timerId, TimerType.OnceWaitTimer, now, tillTime - now, tcs);

            void CancelActionVoid()
            {
                if (Remove(timerId))
                {
                    tcs.SetResult(false);
                }
            }

            bool result;

            try
            {
                cancellationToken?.Add(CancelActionVoid);
                AddTimer(ref timerAction);
                result = await tcs;
            }
            catch (Exception ex)
            {
                Log.Error($"TimerSchedulerNet.WaitTillAsync failed: timerId={timerId}, tillTime={tillTime}, now={now}, waitTime={tillTime - now}, exception={ex}");
                throw;
            }
            finally
            {
                cancellationToken?.Remove(CancelActionVoid);
            }

            return result;
        }

        /// <summary>
        /// 异步等待一帧时间。
        /// </summary>
        /// <returns>等待是否成功。</returns>
        public async FTask WaitFrameAsync()
        {
#if FANTASY_NET
            await WaitAsync(100);
#else
            await WaitAsync(1);
#endif
        }

        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间
        /// </summary>
        /// <param name="time">计时器执行的目标时间。</param>
        /// <param name="action">计时器回调方法。</param>
        /// <returns></returns>
        public long OnceTimer(long time, Action action)
        {
            var now = Now();
            var timerId = GetId;
            var timerAction = new TimerAction(timerId, TimerType.OnceTimer, now, time, action);
            AddTimer(ref timerAction);
            return timerId;
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间。
        /// </summary>
        /// <param name="tillTime">计时器执行的目标时间。</param>
        /// <param name="action">计时器回调方法。</param>
        /// <returns>计时器的 ID。</returns>
        public long OnceTillTimer(long tillTime, Action action)
        {
            var now = Now();
            
            if (tillTime < now)
            {
                Log.Error($"new once time too small tillTime:{tillTime} Now:{now}");
            }

            var timerId = GetId;
            var timerAction = new TimerAction(timerId, TimerType.OnceTimer, now, tillTime - now, action);
            AddTimer(ref timerAction);
            return timerId;
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，用于发布指定类型的事件。
        /// </summary>
        /// <typeparam name="T">事件类型。</typeparam>
        /// <param name="time">计时器执行的延迟时间。</param>
        /// <param name="timerHandlerType">事件处理器类型。</param>
        /// <returns>计时器的 ID。</returns>
        public long OnceTimer<T>(long time, T timerHandlerType) where T : struct
        {
            void OnceTimerVoid()
            {
                _scene.EventComponent.Publish(timerHandlerType);
            }

            return OnceTimer(time, OnceTimerVoid);
        }
        
        /// <summary>
        /// 创建一个只执行一次的计时器，直到指定时间，用于发布指定类型的事件。
        /// </summary>
        /// <typeparam name="T">事件类型。</typeparam>
        /// <param name="tillTime">计时器执行的目标时间。</param>
        /// <param name="timerHandlerType">事件处理器类型。</param>
        /// <returns>计时器的 ID。</returns>
        public long OnceTillTimer<T>(long tillTime, T timerHandlerType) where T : struct
        {
            void OnceTillTimerVoid()
            {
                _scene.EventComponent.Publish(timerHandlerType);
            }

            return OnceTillTimer(tillTime, OnceTillTimerVoid);
        }

        /// <summary>
        /// 创建一个帧任务
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public long FrameTimer(Action action)
        {
#if FANTASY_NET
            return RepeatedTimerInner(100, action);
#else
            return RepeatedTimerInner(0, action);
#endif
        }

        /// <summary>
        /// 创建一个重复执行的计时器。
        /// </summary>
        /// <param name="time">计时器重复间隔的时间。</param>
        /// <param name="action">计时器回调方法。</param>
        /// <returns>计时器的 ID。</returns>
        public long RepeatedTimer(long time, Action action)
        {
            if (time < 0)
            {
                Log.Error($"time too small: {time}");
                return 0;
            }

            return RepeatedTimerInner(time, action);
        }
        
        /// <summary>
        /// 创建一个重复执行的计时器，用于发布指定类型的事件。
        /// </summary>
        /// <typeparam name="T">事件类型。</typeparam>
        /// <param name="time">计时器重复间隔的时间。</param>
        /// <param name="timerHandlerType">事件处理器类型。</param>
        /// <returns>计时器的 ID。</returns>
        public long RepeatedTimer<T>(long time, T timerHandlerType) where T : struct
        {
            void RepeatedTimerVoid()
            {
                _scene.EventComponent.Publish(timerHandlerType);
            }

            return RepeatedTimer(time, RepeatedTimerVoid);
        }
        
        private long RepeatedTimerInner(long time, Action action)
        {
            var now = Now();
            var timerId = GetId;
            var timerAction = new TimerAction(timerId, TimerType.RepeatedTimer, now, time, action);
            AddTimer(ref timerAction);
            return timerId;
        }
        
        /// <summary>
        /// 移除指定 ID 的计时器。
        /// </summary>
        /// <param name="timerId"></param>
        /// <returns></returns>
        public bool Remove(ref long timerId)
        {
            var id = timerId;
            timerId = 0;
            return Remove(id);
        }
        
        /// <summary>
        /// 移除指定 ID 的计时器。
        /// </summary>
        /// <param name="timerId">计时器的 ID。</param>
        public bool Remove(long timerId)
        {
            return timerId != 0 && _timerActions.Remove(timerId, out _);
        }
    }
}