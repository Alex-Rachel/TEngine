#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Fantasy.Async
{
    /// <summary>
    /// 一个异步任务
    /// </summary>
    public partial class FTask
    {
        /// <summary>
        /// 对象池中的最大缓存数量
        /// </summary>
        private const int MaxPoolSize = 2000;

        private bool _isPool;

        /// <summary>
        /// 线程本地的对象池栈
        /// </summary>
        [ThreadStatic]
        private static Stack<FTask>? _caches;
        /// <summary>
        /// 创建一个空的任务
        /// </summary>
        public static FTaskCompleted CompletedTask => new FTaskCompleted();
        
        private FTask() { }

        /// <summary>
        /// 创建一个任务
        /// </summary>
        /// <param name="isPool">是否从对象池中创建</param>
        /// <returns></returns>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask Create(bool isPool = true)
        {
            if (!isPool)
            {
                return new FTask();
            }

            var caches = _caches;
            FTask fTask;

            if (caches != null && caches.Count > 0)
            {
                fTask = caches.Pop();
            }
            else
            {
                fTask = new FTask();
            }

            fTask._isPool = true;
            return fTask;
        }

        private void Return()
        {
            if (!_isPool)
            {
                return;
            }

            var caches = _caches;
            if (caches == null)
            {
                _caches = caches = new Stack<FTask>(16);
            }

            if (caches.Count >= MaxPoolSize)
            {
                return;
            }

            _callBack = null;
            _status = STaskStatus.Pending;
            caches.Push(this);
        }
    }

    /// <summary>
    /// 一个异步任务
    /// </summary>
    /// <typeparam name="T">任务的泛型类型</typeparam>
    public partial class FTask<T>
    {
        /// <summary>
        /// 对象池中的最大缓存数量
        /// </summary>
        private const int MaxPoolSize = 2000;

        private bool _isPool;

        /// <summary>
        /// 线程本地的对象池栈
        /// </summary>
        [ThreadStatic]
        private static Stack<FTask<T>>? _caches;
        /// <summary>
        /// 创建一个任务
        /// </summary>
        /// <param name="isPool">是否从对象池中创建</param>
        /// <returns></returns>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask<T> Create(bool isPool = true)
        {
            if (!isPool)
            {
                return new FTask<T>();
            }

            var caches = _caches;
            FTask<T> fTask;

            if (caches != null && caches.Count > 0)
            {
                fTask = caches.Pop();
            }
            else
            {
                fTask = new FTask<T>();
            }

            fTask._isPool = true;
            return fTask;
        }

        /// <summary>
        /// 创建一个已完成的任务，并设置结果值
        /// </summary>
        /// <param name="result">任务结果</param>
        /// <returns>已完成的 FTask</returns>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FTask<T> FromResult(T result)
        {
            var fTask = Create();
            fTask.SetResult(result);
            return fTask;
        }

        private FTask() { }

        private void Return()
        {
            if (!_isPool)
            {
                return;
            }

            var caches = _caches;
            if (caches == null)
            {
                _caches = caches = new Stack<FTask<T>>(16);
            }

            if (caches.Count >= MaxPoolSize)
            {
                return;
            }

            _callBack = null;
            _status = STaskStatus.Pending;
            caches.Push(this);
        }
    }
}