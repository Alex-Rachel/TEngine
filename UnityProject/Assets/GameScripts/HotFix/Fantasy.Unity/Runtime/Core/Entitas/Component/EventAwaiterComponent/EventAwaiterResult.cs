using System.Runtime.CompilerServices;

namespace Fantasy.EventAwaiter
{
    /// <summary>
    /// 事件等待器结果类型枚举
    /// </summary>
    public enum EventAwaiterResultType : byte
    {
        /// <summary>
        /// 成功：事件正常触发并返回数据
        /// </summary>
        Success = 0,

        /// <summary>
        /// 取消：通过 FCancellationToken 主动取消等待
        /// </summary>
        Cancel = 1,

        /// <summary>
        /// 超时：等待时间超过指定的超时时间
        /// </summary>
        Timeout = 2,

        /// <summary>
        /// 销毁：EventAwaiterComponent 被销毁，等待被强制中断
        /// </summary>
        Destroy = 3,
    }

    /// <summary>
    /// 事件等待器返回结果，包含状态和返回值
    /// 避免使用异常来表示超时/取消，提供零开销的结果返回
    /// </summary>
    /// <typeparam name="T">事件类型，必须为值类型</typeparam>
    public readonly struct EventAwaiterResult<T> where T : struct
    {
        /// <summary>
        /// 结果状态（Success/Cancel/Timeout）
        /// </summary>
        public EventAwaiterResultType ResultType { get; }

        /// <summary>
        /// 事件数据（仅在 Success 时有效，其他情况为 default(T)）
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// 创建成功结果（正常触发）
        /// </summary>
        /// <param name="value">事件数据</param>
        /// <returns>成功结果</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventAwaiterResult<T> CreateSuccessResult(T value)
        {
            return new EventAwaiterResult<T>(EventAwaiterResultType.Success, value);
        }

        /// <summary>
        /// 创建超时结果
        /// </summary>
        /// <returns>超时结果，Value 为 default(T)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventAwaiterResult<T> CreateTimeoutResult()
        {
            return new EventAwaiterResult<T>(EventAwaiterResultType.Timeout, default);
        }

        /// <summary>
        /// 创建取消结果
        /// </summary>
        /// <returns>取消结果，Value 为 default(T)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventAwaiterResult<T> CreateCancelResult()
        {
            return new EventAwaiterResult<T>(EventAwaiterResultType.Cancel, default);
        }
        
        /// <summary>
        /// 创建销毁结果
        /// </summary>
        /// <returns>销毁结果，Value 为 default(T)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventAwaiterResult<T> CreateDestroyResult()
        {
            return new EventAwaiterResult<T>(EventAwaiterResultType.Destroy, default);
        }

        /// <summary>
        /// 私有构造函数，通过静态工厂方法创建
        /// </summary>
        private EventAwaiterResult(EventAwaiterResultType resultType, T value)
        {
            ResultType = resultType;
            Value = value;
        }
    }
}