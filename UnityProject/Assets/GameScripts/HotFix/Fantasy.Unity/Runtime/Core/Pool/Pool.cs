#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable CheckNamespace

namespace Fantasy.Pool
{
    /// <summary>
    /// 静态的线程本地对象池系统。
    /// 每个线程拥有独立的对象池，无需同步，性能更高。
    /// </summary>
    /// <typeparam name="T">对象池中对象的类型</typeparam>
    public static class Pool<T> where T : IPool, new()
    {
        /// <summary>
        /// 对象池的最大容量，防止内存无限增长
        /// </summary>
        private const int MaxPoolSize = 1024;

        /// <summary>
        /// 线程本地的对象池队列
        /// </summary>
        [ThreadStatic]
        private static Queue<T>? _poolQueue;

        /// <summary>
        /// 获取当前线程池子里可用的数量
        /// </summary>
        public static int Count => _poolQueue?.Count ?? 0;
        
        /// <summary>
        /// 从对象池中租借一个对象，如果池为空则创建新对象。
        /// </summary>
        /// <returns>从池中获取或新创建的对象</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent()
        {
            var poolQueue = _poolQueue;
            if (poolQueue != null && poolQueue.Count > 0)
            {
                return poolQueue.Dequeue();
            }
            return new T();
        }
        
        /// <summary>
        /// 从对象池中租借一个对象，如果池为空则使用提供的生成器创建新对象。
        /// </summary>
        /// <param name="generator">对象生成器，当池为空时调用</param>
        /// <returns>从池中获取或通过生成器创建的对象</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent(Func<T> generator)
        {
            var poolQueue = _poolQueue;
            if (poolQueue != null && poolQueue.Count > 0)
            {
                return poolQueue.Dequeue();
            }
            return generator();
        }
        
        /// <summary>
        /// 将对象归还到对象池，如果池已满则丢弃对象。
        /// </summary>
        /// <param name="t">要归还的对象</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T t)
        {
            if (t == null)
            {
                return;
            }

            var poolQueue = _poolQueue;
            if (poolQueue == null)
            {
                _poolQueue = poolQueue = new Queue<T>(16);
            }

            if (poolQueue.Count >= MaxPoolSize)
            {
                return;
            }

            poolQueue.Enqueue(t);
        }
        
        /// <summary>
        /// 将对象归还到对象池，并在归还前执行重置操作。
        /// </summary>
        /// <param name="t">要归还的对象</param>
        /// <param name="reset">归还前执行的重置委托</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T t, Action<T> reset)
        {
            if (t == null)
            {
                return;
            }

            reset(t);

            var poolQueue = _poolQueue;
            if (poolQueue == null)
            {
                _poolQueue = poolQueue = new Queue<T>(16);
            }

            if (poolQueue.Count >= MaxPoolSize)
            {
                return;
            }

            poolQueue.Enqueue(t);
        }
        
        /// <summary>
        /// 清空当前线程的对象池。
        /// </summary>
        public static void Clear()
        {
            var poolQueue = _poolQueue;
            if (poolQueue != null)
            {
                poolQueue.Clear();
            }
        }
    }
}