using System;
using System.Runtime.CompilerServices;
using Fantasy.Async;
using Fantasy.DataStructure.Collection;
using Fantasy.Entitas;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace Fantasy.EventAwaiter
{
    /// <summary>
    /// 事件等待器组件，用于实现类型化的异步等待和通知机制
    /// </summary>
    public sealed class EventAwaiterComponent : Entity
    {
        /// <summary>
        /// 存储不同类型事件的等待回调队列
        /// </summary>
        private OneToManyList<RuntimeTypeHandle, IEventAwaiterCallback> WaitCallbacks { get; } = new();

        /// <summary>
        /// 等待指定类型的事件
        /// </summary>
        /// <param name="cancellationToken">取消令牌，用于提前取消等待</param>
        /// <typeparam name="T">事件类型，必须为值类型</typeparam>
        /// <returns>返回包含事件数据和状态的结果（Success/Cancel）</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async FTask<EventAwaiterResult<T>> Wait<T>(FCancellationToken? cancellationToken = null) where T : struct
        {
            EventAwaiterResult<T> result = default;
            var typeHandle = typeof(T).TypeHandle;
            var sceneEventAwaiterPool = Scene.EventAwaiterPool;
            var eventAwaiterCallback = sceneEventAwaiterPool.Rent<EventAwaiterCallback<T>>().Initialize(Scene);

            try
            {
                WaitCallbacks.Add(typeHandle, eventAwaiterCallback);

                if (cancellationToken != null)
                {
                    var cancelAction = sceneEventAwaiterPool.Rent<EventAwaiterCancelAction<T>>().Initialize(eventAwaiterCallback);

                    try
                    {
                        cancellationToken.Add(cancelAction.Action);
                        result = await eventAwaiterCallback.Task;
                    }
                    finally
                    {
                        cancellationToken.Remove(cancelAction.Action);
                        sceneEventAwaiterPool.Return(typeof(EventAwaiterCancelAction<T>), cancelAction);
                    }
                }
                else
                {
                    result = await eventAwaiterCallback.Task;
                }
            }
            finally
            {
                // 归还回调对象到对象池
                sceneEventAwaiterPool.Return(typeof(EventAwaiterCallback<T>), eventAwaiterCallback);
                // 如果不是正常完成（取消），需要手动清理等待队列
                // 正常完成时由 Notify 方法统一清理整个类型的等待队列
                if (result.ResultType != EventAwaiterResultType.Success)
                {
                    WaitCallbacks.RemoveValue(typeHandle, eventAwaiterCallback);
                }
            }
            
            return result;
        }

        /// <summary>
        /// 等待指定类型的事件，带超时控制
        /// </summary>
        /// <param name="timeout">超时时间（毫秒），必须大于 0</param>
        /// <param name="cancellationToken">取消令牌，用于提前取消等待</param>
        /// <typeparam name="T">事件类型，必须为值类型</typeparam>
        /// <returns>返回包含事件数据和状态的结果（Success/Timeout/Cancel）</returns>
        /// <exception cref="ArgumentException">timeout 小于或等于 0 时抛出</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async FTask<EventAwaiterResult<T>> Wait<T>(int timeout, FCancellationToken? cancellationToken = null) where T : struct
        {
            // 校验超时参数
            if (timeout <= 0)
            {
                throw new ArgumentException($"Timeout must be greater than 0, but got {timeout}", nameof(timeout));
            }

            EventAwaiterResult<T> result = default;
            var typeHandle = typeof(T).TypeHandle;
            var sceneEventAwaiterPool = Scene.EventAwaiterPool;
            var eventAwaiterCallback = sceneEventAwaiterPool.Rent<EventAwaiterCallback<T>>().Initialize(Scene);
            var timeoutAction = sceneEventAwaiterPool.Rent<EventAwaiterTimeoutAction<T>>().Initialize(Scene, eventAwaiterCallback, timeout);

            try
            {
                WaitCallbacks.Add(typeHandle, eventAwaiterCallback);

                if (cancellationToken != null)
                {
                    var cancelAction = sceneEventAwaiterPool.Rent<EventAwaiterCancelAction<T>>().Initialize(eventAwaiterCallback);

                    try
                    {
                        cancellationToken.Add(cancelAction.Action);
                        result = await eventAwaiterCallback.Task;
                    }
                    finally
                    {
                        cancellationToken.Remove(cancelAction.Action);
                        sceneEventAwaiterPool.Return(typeof(EventAwaiterCancelAction<T>), cancelAction);
                    }
                }
                else
                {
                    result = await eventAwaiterCallback.Task;
                }
            }
            finally
            {
                // 归还超时处理器和回调对象到对象池
                // 归还时会自动取消未执行的 Timer（在 SetIsPool 中处理）
                sceneEventAwaiterPool.Return(typeof(EventAwaiterTimeoutAction<T>), timeoutAction);
                sceneEventAwaiterPool.Return(typeof(EventAwaiterCallback<T>), eventAwaiterCallback);
                // 如果不是正常完成（超时或取消），需要手动清理等待队列
                // 正常完成时由 Notify 方法统一清理整个类型的等待队列
                if (result.ResultType != EventAwaiterResultType.Success)
                {
                    WaitCallbacks.RemoveValue(typeHandle, eventAwaiterCallback);
                }
            }

            return result;
        }

        /// <summary>
        /// 通知所有等待指定类型事件的回调，完成异步等待
        /// </summary>
        /// <param name="obj">事件数据</param>
        /// <typeparam name="T">事件类型，必须为值类型</typeparam>
        public void Notify<T>(T obj) where T : struct
        {
            var typeHandle = typeof(T).TypeHandle;

            // 查找该类型的所有等待回调
            if (!WaitCallbacks.TryGetValue(typeHandle, out var wailtCallbackList))
            {
                return;
            }

            // 通知所有等待该类型事件的回调
            foreach (var wailtCallback in wailtCallbackList)
            {
                ((EventAwaiterCallback<T>)wailtCallback).SetResult(obj);
            }

            // 清理整个类型的等待队列（正常完成时统一清理）
            WaitCallbacks.RemoveKey(typeHandle);
        }

        /// <summary>
        /// 销毁事件等待器组件，通知所有等待中的回调
        /// </summary>
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            // 通知所有等待中的回调，组件已被销毁
            // 避免等待者无限等待，返回 Destroy 状态
            foreach (var (_, wailtCallbackList) in WaitCallbacks)
            {
                foreach (var wailtCallback in wailtCallbackList)
                {
                    wailtCallback.SetDestroyResult();
                }
            }

            // 清空所有等待队列
            WaitCallbacks.Clear();
            base.Dispose();
        }
    }
}