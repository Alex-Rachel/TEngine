using System;
using System.Collections.Generic;
using Fantasy.Serialize;
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.Network
{
    /// <summary>
    /// MemoryStreamBuffer对象池类
    /// </summary>
    public sealed class MemoryStreamBufferPool : IDisposable
    {
        private readonly int _poolSize;
        private readonly int _maxMemoryStreamSize;
        private readonly Queue<MemoryStreamBuffer> _memoryStreamPool = new Queue<MemoryStreamBuffer>();

        /// <summary>
        /// 构造方法
        /// </summary>
        /// <param name="maxMemoryStreamSize"></param>
        /// <param name="poolSize"></param>
        public MemoryStreamBufferPool(int maxMemoryStreamSize = 2048, int poolSize = 512)
        {
            _poolSize = poolSize;
            _maxMemoryStreamSize = maxMemoryStreamSize;
        }

        /// <summary>
        /// 租借MemoryStream
        /// </summary>
        /// <param name="memoryStreamBufferSource"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public MemoryStreamBuffer RentMemoryStream(MemoryStreamBufferSource memoryStreamBufferSource, int size = 0)
        {
            if (size > _maxMemoryStreamSize)
            {
                return new MemoryStreamBuffer(memoryStreamBufferSource, size);
            }

            if (size < _maxMemoryStreamSize)
            {
                size = _maxMemoryStreamSize;
            }

            if (_memoryStreamPool.Count == 0)
            {
                return new MemoryStreamBuffer(memoryStreamBufferSource, size);
            }

            if (_memoryStreamPool.TryDequeue(out var memoryStream))
            {
                memoryStream.MemoryStreamBufferSource = memoryStreamBufferSource;
                return memoryStream;
            }

            return new MemoryStreamBuffer(memoryStreamBufferSource, size);
        }
    
        /// <summary>
        /// 归还ReturnMemoryStream
        /// </summary>
        /// <param name="memoryStreamBuffer"></param>
        public void ReturnMemoryStream(MemoryStreamBuffer memoryStreamBuffer)
        {
            if (memoryStreamBuffer.Capacity > _maxMemoryStreamSize)
            {
                return;
            }
            
            if (_memoryStreamPool.Count > _poolSize)
            {
                // 设置该值只能是内网或服务器转发的时候可能在连接之前发送的数据过多的情况下可以修改。
                // 设置过大会导致内存占用过大，所以要谨慎设置。
                return;
            }
            
            memoryStreamBuffer.SetLength(0);
            memoryStreamBuffer.MemoryStreamBufferSource = MemoryStreamBufferSource.None;
            _memoryStreamPool.Enqueue(memoryStreamBuffer);
        }

        /// <summary>
        /// 销毁方法
        /// </summary>
        public void Dispose()
        {
            foreach (var memoryStreamBuffer in _memoryStreamPool)
            {
                memoryStreamBuffer.MemoryStreamBufferSource = MemoryStreamBufferSource.None;
                memoryStreamBuffer.Dispose();
            }
            _memoryStreamPool.Clear();
        }
    }
}