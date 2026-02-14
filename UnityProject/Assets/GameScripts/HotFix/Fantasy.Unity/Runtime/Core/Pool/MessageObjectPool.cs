#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
// ReSharper disable CheckNamespace

namespace Fantasy.Pool
{
    /// <summary>
    /// 用于消息实例的简单线程本地对象池，用于减少GC压力。
    /// </summary>
    public static class MessageObjectPool<T> where T : IPool, new()
    {
        /// <summary>
        /// 每个类型的最大缓存实例数
        /// </summary>
        private const int MaxPoolSize = 1024;

        /// <summary>
        /// 线程本地的对象池栈
        /// </summary>
        [ThreadStatic]
        private static Stack<T>? _pool;

        /// <summary>
        /// 从对象池中租用一个实例，如果池为空则创建一个新实例。
        /// </summary>
        /// <returns>从池中获取或新创建的实例</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent()
        {
            var pool = _pool;
            var result = pool is { Count: > 0 } ? pool.Pop() : new T();
            result.SetIsPool(true);
            return result;
        }

        /// <summary>
        /// 将实例归还到对象池以供重用。
        /// </summary>
        /// <param name="instance">要归还的实例，如果为null则忽略</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T? instance)
        {
            if (instance == null)
            {
                return;
            }

            if (!instance.IsPool())
            {
                return;
            }

            var pool = _pool;
            if (pool == null)
            {
                _pool = pool = new Stack<T>(16);
            }

            if (pool.Count < MaxPoolSize)
            {
                pool.Push(instance);
            }
            
            instance.SetIsPool(false);
        }

        /// <summary>
        /// 清空当前线程的对象池（主要用于测试）。
        /// </summary>
        public static void Clear()
        {
            var pool = _pool;
            if (pool != null)
            {
                pool.Clear();
            }
        }
    }
}
