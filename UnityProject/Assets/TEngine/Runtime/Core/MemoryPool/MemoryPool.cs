using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 内存池。
    /// </summary>
    public static partial class MemoryPool
    {
        private static readonly Dictionary<Type, MemoryCollection> _memoryCollections = new Dictionary<Type, MemoryCollection>();
        private static bool _enableStrictCheck = false;

        /// <summary>
        /// 获取或设置是否开启强制检查。
        /// </summary>
        public static bool EnableStrictCheck
        {
            get => _enableStrictCheck;
            set => _enableStrictCheck = value;
        }

        /// <summary>
        /// 获取内存池的数量。
        /// </summary>
        // ReSharper disable once InconsistentlySynchronizedField
        public static int Count => _memoryCollections.Count;

        /// <summary>
        /// 获取所有内存池的信息。
        /// </summary>
        /// <returns>所有内存池的信息。</returns>
        public static MemoryPoolInfo[] GetAllMemoryPoolInfos()
        {
            int index = 0;
            MemoryPoolInfo[] results = null;

            lock (_memoryCollections)
            {
                results = new MemoryPoolInfo[_memoryCollections.Count];
                foreach (KeyValuePair<Type, MemoryCollection> memoryCollection in _memoryCollections)
                {
                    results[index++] = new MemoryPoolInfo(memoryCollection.Key, memoryCollection.Value.UnusedMemoryCount, memoryCollection.Value.UsingMemoryCount, memoryCollection.Value.AcquireMemoryCount, memoryCollection.Value.ReleaseMemoryCount, memoryCollection.Value.AddMemoryCount, memoryCollection.Value.RemoveMemoryCount);
                }
            }

            return results;
        }

        /// <summary>
        /// 清除所有内存池。
        /// </summary>
        public static void ClearAll()
        {
            lock (_memoryCollections)
            {
                foreach (KeyValuePair<Type, MemoryCollection> memoryCollection in _memoryCollections)
                {
                    memoryCollection.Value.RemoveAll();
                }

                _memoryCollections.Clear();
            }
        }

        /// <summary>
        /// 从内存池获取内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <returns>内存对象。</returns>
        public static T Acquire<T>() where T : class, IMemory, new()
        {
            return GetMemoryCollection(typeof(T)).Acquire<T>();
        }

        /// <summary>
        /// 从内存池获取内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <returns>内存对象。</returns>
        public static IMemory Acquire(Type memoryType)
        {
            InternalCheckMemoryType(memoryType);
            return GetMemoryCollection(memoryType).Acquire();
        }

        /// <summary>
        /// 将内存对象归还内存池。
        /// </summary>
        /// <param name="memory">内存对象。</param>
        public static void Release(IMemory memory)
        {
            if (memory == null)
            {
                throw new Exception("Memory is invalid.");
            }

            Type memoryType = memory.GetType();
            InternalCheckMemoryType(memoryType);
            GetMemoryCollection(memoryType).Release(memory);
        }

        /// <summary>
        /// 向内存池中追加指定数量的内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <param name="count">追加数量。</param>
        public static void Add<T>(int count) where T : class, IMemory, new()
        {
            GetMemoryCollection(typeof(T)).Add<T>(count);
        }

        /// <summary>
        /// 向内存池中追加指定数量的内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <param name="count">追加数量。</param>
        public static void Add(Type memoryType, int count)
        {
            InternalCheckMemoryType(memoryType);
            GetMemoryCollection(memoryType).Add(count);
        }

        /// <summary>
        /// 从内存池中移除指定数量的内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        /// <param name="count">移除数量。</param>
        public static void Remove<T>(int count) where T : class, IMemory
        {
            GetMemoryCollection(typeof(T)).Remove(count);
        }

        /// <summary>
        /// 从内存池中移除指定数量的内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        /// <param name="count">移除数量。</param>
        public static void Remove(Type memoryType, int count)
        {
            InternalCheckMemoryType(memoryType);
            GetMemoryCollection(memoryType).Remove(count);
        }

        /// <summary>
        /// 从内存池中移除所有的内存对象。
        /// </summary>
        /// <typeparam name="T">内存对象类型。</typeparam>
        public static void RemoveAll<T>() where T : class, IMemory
        {
            GetMemoryCollection(typeof(T)).RemoveAll();
        }

        /// <summary>
        /// 从内存池中移除所有的内存对象。
        /// </summary>
        /// <param name="memoryType">内存对象类型。</param>
        public static void RemoveAll(Type memoryType)
        {
            InternalCheckMemoryType(memoryType);
            GetMemoryCollection(memoryType).RemoveAll();
        }

        private static void InternalCheckMemoryType(Type memoryType)
        {
            if (!_enableStrictCheck)
            {
                return;
            }

            if (memoryType == null)
            {
                throw new Exception("Memory type is invalid.");
            }

            if (!memoryType.IsClass || memoryType.IsAbstract)
            {
                throw new Exception("Memory type is not a non-abstract class type.");
            }

            if (!typeof(IMemory).IsAssignableFrom(memoryType))
            {
                throw new Exception(string.Format("Memory type '{0}' is invalid.", memoryType.FullName));
            }
        }

        private static MemoryCollection GetMemoryCollection(Type memoryType)
        {
            if (memoryType == null)
            {
                throw new Exception("MemoryType is invalid.");
            }

            MemoryCollection memoryCollection = null;
            lock (_memoryCollections)
            {
                if (!_memoryCollections.TryGetValue(memoryType, out memoryCollection))
                {
                    memoryCollection = new MemoryCollection(memoryType);
                    _memoryCollections.Add(memoryType, memoryCollection);
                }
            }

            return memoryCollection;
        }
    }
}
