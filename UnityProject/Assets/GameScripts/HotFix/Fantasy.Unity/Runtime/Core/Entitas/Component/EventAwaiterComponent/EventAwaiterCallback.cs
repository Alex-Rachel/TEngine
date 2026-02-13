using System;
using Fantasy.Async;
using System.Runtime.CompilerServices;
using Fantasy.Pool;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

// ReSharper disable CheckNamespace
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Fantasy.EventAwaiter
{
    /// <summary>
    /// 事件等待器回调接口
    /// 用于在 EventAwaiterComponent 销毁时通知所有等待中的回调
    /// </summary>
    internal interface IEventAwaiterCallback
    {
        /// <summary>
        /// 设置销毁结果，当 EventAwaiterComponent 被销毁时调用
        /// 通知所有等待者组件已销毁，避免无限等待
        /// </summary>
        void SetDestroyResult();
    }
     
    /// <summary>
    /// EventAwaiter 专用对象池
    /// 用于管理 EventAwaiterCallback 和 EventAwaiterCancelAction 的对象池化
    /// </summary>
    internal sealed class EventAwaiterPool : PoolCore
    {
        /// <summary>
        /// 构造函数，初始化对象池
        /// </summary>
        /// <param name="maxCapacity">最大容量，默认 1024</param>
        public EventAwaiterPool(int maxCapacity = 1024) : base(maxCapacity) { }
    }

    /// <summary>
    /// 超时回调的可复用 Action 封装（池化对象）
    /// Action 委托在构造时创建一次，对象池复用时无 GC 开销
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    internal sealed class EventAwaiterTimeoutAction<T> : IPool where T : struct
    {
        private bool _isPool;
        private long _timerId;

        private Scene _scene;
        private EventAwaiterCallback<T> _callback;

        /// <summary>
        /// Action 委托，在构造时创建一次，整个对象生命周期内复用
        /// 对象池复用时不会重新分配委托，实现零 GC
        /// </summary>
        private Action Action { get; }

        /// <summary>
        /// 构造函数，创建可复用的 Action 委托
        /// </summary>
        public EventAwaiterTimeoutAction()
        {
            // 委托只在对象创建时分配一次，后续对象池复用时不再分配
            Action = OnTimeout;
        }

        /// <summary>
        /// 初始化超时回调（从对象池租用后调用）
        /// 创建定时器并在超时后自动调用 Action 委托
        /// </summary>
        /// <param name="scene">Scene 实例</param>
        /// <param name="callback">事件回调对象</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>返回自身以支持链式调用</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EventAwaiterTimeoutAction<T> Initialize(Scene scene, EventAwaiterCallback<T> callback, int timeoutMs)
        {
            _scene  = scene;
            _callback = callback;
            // 创建一次性定时器，超时后自动触发 Action 委托
#if FANTASY_NET
            _timerId = _scene.TimerComponent.Net.OnceTimer(timeoutMs, Action);
#elif FANTASY_UNITY
            _timerId = _scene.TimerComponent.Unity.OnceTimer(timeoutMs, Action);
#endif
            return this;
        }

        /// <summary>
        /// 超时回调方法，由定时器在超时后自动调用
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnTimeout()
        {
            _callback.SetTimeout();
        }

        /// <summary>
        /// 获取当前对象是否在对象池中
        /// </summary>
        /// <returns>true 表示在池中，false 表示已租出</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsPool() => _isPool;

        /// <summary>
        /// 设置对象池状态，对象池框架自动调用
        /// isPool = true 表示租出，isPool = false 表示归还
        /// </summary>
        /// <param name="isPool">true=租出, false=归还</param>
        public void SetIsPool(bool isPool)
        {
            _isPool = isPool;

            // 租出时不做任何操作
            if (isPool)
            {
                return;
            }

            // 归还时取消未执行的定时器并清理引用
            if (_timerId != 0)
            {
#if FANTASY_NET
                _scene.TimerComponent.Net.Remove(ref _timerId);
#elif FANTASY_UNITY
                _scene.TimerComponent.Unity.Remove(ref _timerId);
#endif
            }

            _scene = null;
            _callback = null;
        }
    }

    /// <summary>
    /// 事件等待回调包装器（池化对象）
    /// 封装 FTask 用于异步等待事件通知，通过 RuntimeId 解决对象池复用时的竞态条件问题
    /// </summary>
    /// <typeparam name="T">事件类型，必须为值类型</typeparam>
    internal sealed class EventAwaiterCallback<T> : IEventAwaiterCallback, IPool where T : struct
    {
        private bool _isPool;

        /// <summary>
        /// 封装的异步任务，等待事件结果
        /// 当事件通知到达时通过 SetResult 完成任务
        /// </summary>
        public FTask<EventAwaiterResult<T>> Task { get; private set; }

        /// <summary>
        /// 运行时唯一 ID，用于验证超时处理器的有效性
        /// 每次 Initialize 时生成新 ID，防止对象池复用时超时处理器访问错误的实例
        /// </summary>
        public long RuntimeId { get; private set; }

        /// <summary>
        /// 初始化回调包装器，创建新的 FTask 和唯一 RuntimeId
        /// 从对象池租借后必须调用此方法进行初始化
        /// </summary>
        /// <param name="scene">Scene 实例，用于生成 RuntimeId</param>
        /// <returns>返回自身以支持链式调用</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EventAwaiterCallback<T> Initialize(Scene scene)
        {
            // 生成全局唯一的 RuntimeId，用于超时处理器验证
            // 即使对象从池中复用，每次 Initialize 都会生成新的 ID
            RuntimeId = scene.RuntimeIdFactory.Create(true);
            // 创建新的 FTask 用于异步等待
            Task = FTask<EventAwaiterResult<T>>.Create();
            return this;
        }

        /// <summary>
        /// 设置任务销毁结果
        /// 当 EventAwaiterComponent 被销毁时调用，通知等待者组件已不可用
        /// </summary>
        public void SetDestroyResult()
        {
            // 早期返回：如果 Task 已经为 null，说明已经处理过或未初始化
            if (Task == null)
            {
                return;
            }

            // 先保存 Task 引用，再清空 Task 字段
            var task = Task;
            Task = null;

            // 设置销毁结果，通知等待者
            task.SetResult(EventAwaiterResult<T>.CreateDestroyResult());
        }
        
        /// <summary>
        /// 设置任务结果并完成等待（正常返回）
        /// 由 EventAwaiterComponent.Notify() 调用
        /// </summary>
        /// <param name="result">事件结果</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResult(T result)
        {
            // 早期返回：如果 Task 已经为 null，说明已经处理过或未初始化
            if (Task == null)
            {
                return;
            }

            // 先保存 Task 引用，再清空 Task 字段
            // 防止多次调用 SetResult（幂等性）
            var task = Task;
            Task = null;

            // 设置任务结果，唤醒等待的协程
            task.SetResult(EventAwaiterResult<T>.CreateSuccessResult(result));
        }

        /// <summary>
        /// 设置任务超时结果
        /// 由超时处理器调用
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTimeout()
        {
            // 早期返回：如果 Task 已经为 null，说明已经处理过或未初始化
            if (Task == null)
            {
                return;
            }
            
            // 先保存 Task 引用，再清空 Task 字段
            var task = Task;
            Task = null;
            
            // 设置超时结果
            task.SetResult(EventAwaiterResult<T>.CreateTimeoutResult());
        }

        /// <summary>
        /// 设置任务取消结果
        /// 由取消令牌触发时调用
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCancel()
        {
            // 早期返回：如果 Task 已经为 null，说明已经处理过或未初始化
            if (Task == null)
            {
                return;
            }

            // 先保存 Task 引用，再清空 Task 字段
            var task = Task;
            Task = null;

            // 设置取消结果
            task.SetResult(EventAwaiterResult<T>.CreateCancelResult());
        }
        
        /// <summary>
        /// 获取当前对象是否在对象池中
        /// </summary>
        /// <returns>true 表示在池中，false 表示已租出</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsPool() => _isPool;

        /// <summary>
        /// 设置对象池状态，对象池框架自动调用
        /// isPool = true 表示租出，isPool = false 表示归还
        /// </summary>
        /// <param name="isPool">true=租出, false=归还</param>
        public void SetIsPool(bool isPool)
        {
            _isPool = isPool;

            // 租出时不做任何操作
            if (isPool)
            {
                return;
            }

            // 归还时清理 Task 引用，避免内存泄漏
            // 即使 SetResult 未调用（异常情况），也能确保 FTask 被释放
            Task = null;
            RuntimeId = 0;
        }
    }
}
