using System;
using System.Runtime.CompilerServices;
using Fantasy.Pool;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Fantasy.EventAwaiter
{
    /// <summary>
    /// 事件等待器取消动作（池化对象）
    /// 用于在 FCancellationToken 触发时通知事件等待器取消等待
    /// Action 委托在构造时创建一次，对象池复用时无 GC 开销
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    internal sealed class EventAwaiterCancelAction<T> : IPool where T : struct
    {
        private bool _isPool;
        private EventAwaiterCallback<T> _callback;

        /// <summary>
        /// 取消委托，在构造时创建一次，整个对象生命周期内复用
        /// 对象池复用时不会重新分配委托，实现零 GC
        /// </summary>
        public Action Action { get; }

        /// <summary>
        /// 构造函数，创建可复用的 Action 委托
        /// </summary>
        public EventAwaiterCancelAction()
        {
            // 委托只在对象创建时分配一次，后续对象池复用时不再分配
            Action = Cancel;
        }

        /// <summary>
        /// 初始化取消动作（从对象池租用后调用）
        /// </summary>
        /// <param name="callback">事件回调对象</param>
        /// <returns>返回自身以支持链式调用</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EventAwaiterCancelAction<T> Initialize(EventAwaiterCallback<T> callback)
        {
            _callback = callback;
            return this;
        }

        /// <summary>
        /// 取消回调方法，由 FCancellationToken 在取消时自动调用
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Cancel()
        {
            _callback.SetCancel();
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

            // 归还时清理引用，避免内存泄漏
            _callback = null;
        }
    }
}