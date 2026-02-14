using System;
using System.Runtime.InteropServices;

namespace TEngine
{
    /// <summary>
    /// 内存池信息。
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct MemoryPoolInfo
    {
        private readonly Type _type;
        private readonly int _unusedMemoryCount;
        private readonly int _usingMemoryCount;
        private readonly int _acquireMemoryCount;
        private readonly int _releaseMemoryCount;
        private readonly int _addMemoryCount;
        private readonly int _removeMemoryCount;

        /// <summary>
        /// 初始化内存池信息的新实例。
        /// </summary>
        /// <param name="type">内存池类型。</param>
        /// <param name="unusedMemoryCount">未使用内存对象数量。</param>
        /// <param name="usingMemoryCount">正在使用内存对象数量。</param>
        /// <param name="acquireMemoryCount">获取内存对象数量。</param>
        /// <param name="releaseMemoryCount">归还内存对象数量。</param>
        /// <param name="addMemoryCount">增加内存对象数量。</param>
        /// <param name="removeMemoryCount">移除内存对象数量。</param>
        public MemoryPoolInfo(Type type, int unusedMemoryCount, int usingMemoryCount, int acquireMemoryCount, int releaseMemoryCount, int addMemoryCount, int removeMemoryCount)
        {
            _type = type;
            _unusedMemoryCount = unusedMemoryCount;
            _usingMemoryCount = usingMemoryCount;
            _acquireMemoryCount = acquireMemoryCount;
            _releaseMemoryCount = releaseMemoryCount;
            _addMemoryCount = addMemoryCount;
            _removeMemoryCount = removeMemoryCount;
        }

        /// <summary>
        /// 获取内存池类型。
        /// </summary>
        public Type Type
        {
            get
            {
                return _type;
            }
        }

        /// <summary>
        /// 获取未使用内存对象数量。
        /// </summary>
        public int UnusedMemoryCount
        {
            get
            {
                return _unusedMemoryCount;
            }
        }

        /// <summary>
        /// 获取正在使用内存对象数量。
        /// </summary>
        public int UsingMemoryCount
        {
            get
            {
                return _usingMemoryCount;
            }
        }

        /// <summary>
        /// 获取获取内存对象数量。
        /// </summary>
        public int AcquireMemoryCount
        {
            get
            {
                return _acquireMemoryCount;
            }
        }

        /// <summary>
        /// 获取归还内存对象数量。
        /// </summary>
        public int ReleaseMemoryCount
        {
            get
            {
                return _releaseMemoryCount;
            }
        }

        /// <summary>
        /// 获取增加内存对象数量。
        /// </summary>
        public int AddMemoryCount
        {
            get
            {
                return _addMemoryCount;
            }
        }

        /// <summary>
        /// 获取移除内存对象数量。
        /// </summary>
        public int RemoveMemoryCount
        {
            get
            {
                return _removeMemoryCount;
            }
        }
    }
}
