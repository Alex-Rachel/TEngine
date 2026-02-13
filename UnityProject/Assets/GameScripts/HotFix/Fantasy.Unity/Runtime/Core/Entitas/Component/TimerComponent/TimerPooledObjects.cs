// using System;
// using System.Runtime.CompilerServices;
// using Fantasy.Async;
// using Fantasy.Pool;
//
// #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
// #pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
//
// namespace Fantasy.Timer
// {
//     /// <summary>
//     /// 定时器取消动作（池化对象）
//     /// 用于 TimerSchedulerNet.WaitAsync/WaitTillAsync 的取消处理
//     /// 通过对象池复用，避免每次等待都创建新的委托对象
//     /// </summary>
//     internal sealed class TimerCancelAction : IPool
//     {
//         private bool _isPool;
//         private TimerSchedulerNet _scheduler;
//         private long _timerId;
//         private FTask<bool> _tcs;
//
//         /// <summary>
//         /// 缓存的取消委托，避免每次创建新委托对象
//         /// </summary>
//         public Action Action { get; }
//
//         /// <summary>
//         /// 构造函数，创建并缓存 Action 委托
//         /// </summary>
//         public TimerCancelAction()
//         {
//             Action = Cancel;  // 只在构造时创建一次委托，后续复用
//         }
//
//         /// <summary>
//         /// 初始化取消动作，设置定时器调度器、定时器 ID 和任务
//         /// </summary>
//         /// <param name="scheduler">定时器调度器</param>
//         /// <param name="timerId">定时器 ID</param>
//         /// <param name="tcs">异步任务</param>
//         /// <returns>返回自身以支持链式调用</returns>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public TimerCancelAction Initialize(TimerSchedulerNet scheduler, long timerId, FTask<bool> tcs)
//         {
//             _scheduler = scheduler;
//             _timerId = timerId;
//             _tcs = tcs;
//             return this;
//         }
//
//         /// <summary>
//         /// 取消回调方法，移除定时器并设置任务结果为 false
//         /// 由 FCancellationToken.Cancel() 触发
//         /// </summary>
//         private void Cancel()
//         {
//             if (_scheduler?.Remove(_timerId) == true)
//             {
//                 _tcs?.SetResult(false);
//             }
//         }
//
//         /// <summary>
//         /// 获取当前对象是否在对象池中
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public bool IsPool() => _isPool;
//
//         /// <summary>
//         /// 设置对象池状态，归还到池时清理引用
//         /// </summary>
//         /// <param name="isPool">是否在池中</param>
//         public void SetIsPool(bool isPool)
//         {
//             _isPool = isPool;
//
//             if (!isPool)  // 归还到池时清理引用，避免内存泄漏
//             {
//                 _scheduler = null;
//                 _timerId = 0;
//                 _tcs = null;
//             }
//         }
//     }
//
//     /// <summary>
//     /// 定时器事件发布器（池化对象）
//     /// 用于定时发布事件到 EventComponent
//     /// 通过对象池复用，避免每次定时器都创建新的委托对象
//     /// </summary>
//     /// <typeparam name="T">事件类型</typeparam>
//     internal sealed class TimerEventPublisher<T> : IPool where T : struct
//     {
//         private bool _isPool;
//         private Scene _scene;
//         private T _eventData;
//
//         /// <summary>
//         /// 缓存的发布委托，避免每次创建新委托对象
//         /// </summary>
//         public Action Action { get; }
//
//         /// <summary>
//         /// 构造函数，创建并缓存 Action 委托
//         /// </summary>
//         public TimerEventPublisher()
//         {
//             Action = Publish;  // 只在构造时创建一次委托，后续复用
//         }
//
//         /// <summary>
//         /// 初始化事件发布器，设置 Scene 和事件数据
//         /// </summary>
//         /// <param name="scene">Scene 实例</param>
//         /// <param name="eventData">事件数据</param>
//         /// <returns>返回自身以支持链式调用</returns>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public TimerEventPublisher<T> Initialize(Scene scene, T eventData)
//         {
//             _scene = scene;
//             _eventData = eventData;
//             return this;
//         }
//
//         /// <summary>
//         /// 发布事件到 EventComponent
//         /// 由定时器系统在触发时调用
//         /// </summary>
//         private void Publish()
//         {
//             _scene?.EventComponent.Publish(_eventData);
//         }
//
//         /// <summary>
//         /// 获取当前对象是否在对象池中
//         /// </summary>
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public bool IsPool() => _isPool;
//
//         /// <summary>
//         /// 设置对象池状态，归还到池时清理引用
//         /// </summary>
//         /// <param name="isPool">是否在池中</param>
//         public void SetIsPool(bool isPool)
//         {
//             _isPool = isPool;
//
//             if (!isPool)  // 归还到池时清理引用，避免内存泄漏
//             {
//                 _scene = null;
//                 _eventData = default;
//             }
//         }
//     }
//
//     /// <summary>
//     /// 定时器对象池，用于管理定时器相关的池化对象
//     /// </summary>
//     internal sealed class TimerObjectPool : PoolCore
//     {
//         /// <summary>
//         /// 构造函数，初始化对象池
//         /// </summary>
//         /// <param name="maxCapacity">最大容量，默认 512</param>
//         public TimerObjectPool(int maxCapacity = 512) : base(maxCapacity) { }
//     }
// }
