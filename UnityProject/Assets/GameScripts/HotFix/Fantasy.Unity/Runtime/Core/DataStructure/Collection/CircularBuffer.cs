using System;
using System.Collections.Generic;
using System.IO;
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.DataStructure.Collection
{
    /// 环形缓存（自增式缓存，自动扩充、不会收缩缓存、所以不要用这个操作过大的IO流）
    /// 1、环大小8192，溢出的会自动增加环的大小。
    /// 2、每个块都是一个环形缓存，当溢出的时候会自动添加到下一个环中。
    /// 3、当读取完成后用过的环会放在缓存中，不会销毁掉。
    /// <summary>
    /// 自增式缓存类，继承自 Stream 和 IDisposable 接口。
    /// 环形缓存具有自动扩充的特性，但不会收缩，适用于操作不过大的 IO 流。
    /// </summary>
    public sealed class CircularBuffer : Stream, IDisposable
    {
        private byte[] _lastBuffer;
        /// <summary>
        /// 环形缓存块的默认大小
        /// </summary>
        public const int ChunkSize = 8192;
        private readonly Queue<byte[]> _bufferCache = new Queue<byte[]>();
        private readonly Queue<byte[]> _bufferQueue = new Queue<byte[]>();
        /// <summary>
        /// 获取或设置环形缓存的第一个索引位置
        /// </summary>
        public int FirstIndex { get; set; }
        /// <summary>
        /// 获取或设置环形缓存的最后一个索引位置
        /// </summary>
        public int LastIndex { get; set; }
        /// <summary>
        /// 获取环形缓存的总长度
        /// </summary>
        public override long Length
        {
            get
            {
                if (_bufferQueue.Count == 0)
                {
                    return 0;
                }

                return (_bufferQueue.Count - 1) * ChunkSize + LastIndex - FirstIndex;
            }
        }

        /// <summary>
        /// 获取环形缓存的第一个块
        /// </summary>
        public byte[] First
        {
            get
            {
                if (_bufferQueue.Count == 0)
                {
                    AddLast();
                }

                return _bufferQueue.Peek();
            }
        }

        /// <summary>
        /// 获取环形缓存的最后一个块
        /// </summary>
        public byte[] Last
        {
            get
            {
                if (_bufferQueue.Count == 0)
                {
                    AddLast();
                }

                return _lastBuffer;
            }
        }
        /// <summary>
        /// 向环形缓存中添加一个新的块
        /// </summary>
        public void AddLast()
        {
            var buffer = _bufferCache.Count > 0 ? _bufferCache.Dequeue() : new byte[ChunkSize];
            _bufferQueue.Enqueue(buffer);
            _lastBuffer = buffer;
        }
        /// <summary>
        /// 从环形缓存中移除第一个块
        /// </summary>
        public void RemoveFirst()
        {
            _bufferCache.Enqueue(_bufferQueue.Dequeue());
        }

        /// <summary>
        /// 从流中读取指定数量的数据到缓存。
        /// </summary>
        /// <param name="stream">源数据流。</param>
        /// <param name="count">要读取的字节数。</param>
        public void Read(Stream stream, int count)
        {
            if (count > Length)
            {
                throw new Exception($"bufferList length < count, {Length} {count}");
            }

            var copyCount = 0;
            while (copyCount < count)
            {
                var n = count - copyCount;
                if (ChunkSize - FirstIndex > n)
                {
                    stream.Write(First, FirstIndex, n);
                    FirstIndex += n;
                    copyCount += n;
                }
                else
                {
                    stream.Write(First, FirstIndex, ChunkSize - FirstIndex);
                    copyCount += ChunkSize - FirstIndex;
                    FirstIndex = 0;
                    RemoveFirst();
                }
            }
        }

        /// <summary>
        /// 从缓存中读取指定数量的数据到内存。
        /// </summary>
        /// <param name="memory">目标内存。</param>
        /// <param name="count">要读取的字节数。</param>
        public void Read(Memory<byte> memory, int count)
        {
            if (count > Length)
            {
                throw new Exception($"bufferList length < count, {Length} {count}");
            }

            var copyCount = 0;
            while (copyCount < count)
            {
                var n = count - copyCount;
                var asMemory = First.AsMemory();
                
                if (ChunkSize - FirstIndex > n)
                {
                    var slice = asMemory.Slice(FirstIndex, n);
                    slice.CopyTo(memory.Slice(copyCount, n));
                    FirstIndex += n;
                    copyCount += n;
                }
                else
                {
                    var length = ChunkSize - FirstIndex;
                    var slice = asMemory.Slice(FirstIndex, length);
                    slice.CopyTo(memory.Slice(copyCount, length));
                    copyCount += ChunkSize - FirstIndex;
                    FirstIndex = 0;
                    RemoveFirst();
                }
            }
        }

        /// <summary>
        /// 从自定义流中读取数据到指定的缓冲区。
        /// </summary>
        /// <param name="buffer">目标缓冲区，用于存储读取的数据。</param>
        /// <param name="offset">目标缓冲区中的起始偏移量。</param>
        /// <param name="count">要读取的字节数。</param>
        /// <returns>实际读取的字节数。</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer.Length < offset + count)
            {
                throw new Exception($"buffer length < count, buffer length: {buffer.Length} {offset} {count}");
            }

            var length = Length;
            if (length < count)
            {
                count = (int) length;
            }

            var copyCount = 0;

            // 循环直到成功读取所需的字节数
            while (copyCount < count)
            {
                var copyLength = count - copyCount;

                if (ChunkSize - FirstIndex > copyLength)
                {
                    // 将数据从当前块的缓冲区复制到目标缓冲区
                    Array.Copy(First, FirstIndex, buffer, copyCount + offset, copyLength);

                    FirstIndex += copyLength;
                    copyCount += copyLength;
                    continue;
                }

                // 复制当前块中剩余的数据，并切换到下一个块
                Array.Copy(First, FirstIndex, buffer, copyCount + offset, ChunkSize - FirstIndex);
                copyCount += ChunkSize - FirstIndex;
                FirstIndex = 0;

                RemoveFirst();
            }

            return count;
        }

        /// <summary>
        /// 将数据从给定的字节数组写入流中。
        /// </summary>
        /// <param name="buffer">包含要写入的数据的字节数组。</param>
        public void Write(byte[] buffer)
        {
            Write(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 将数据从给定的流写入流中。
        /// </summary>
        /// <param name="stream">包含要写入的数据的流。</param>
        public void Write(Stream stream)
        {
            var copyCount = 0;
            var count = (int) (stream.Length - stream.Position);
            
            while (copyCount < count)
            {
                if (LastIndex == ChunkSize)
                {
                    AddLast();
                    LastIndex = 0;
                }

                var n = count - copyCount;
                
                if (ChunkSize - LastIndex > n)
                {
                    _ = stream.Read(Last, LastIndex, n);
                    LastIndex += count - copyCount;
                    copyCount += n;
                }
                else
                {
                    _ = stream.Read(Last, LastIndex, ChunkSize - LastIndex);
                    copyCount += ChunkSize - LastIndex;
                    LastIndex = ChunkSize;
                }
            }
        }

        /// <summary>
        /// 将数据从给定的字节数组写入流中。
        /// </summary>
        /// <param name="buffer">包含要写入的数据的字节数组。</param>
        /// <param name="offset">开始写入的缓冲区中的索引。</param>
        /// <param name="count">要写入的字节数。</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            var copyCount = 0;

            while (copyCount < count)
            {
                if (ChunkSize == LastIndex)
                {
                    AddLast();
                    LastIndex = 0;
                }

                var byteLength = count - copyCount;

                if (ChunkSize - LastIndex > byteLength)
                {
                    Array.Copy(buffer, copyCount + offset, Last, LastIndex, byteLength);
                    LastIndex += byteLength;
                    copyCount += byteLength;
                }
                else
                {
                    Array.Copy(buffer, copyCount + offset, Last, LastIndex, ChunkSize - LastIndex);
                    copyCount += ChunkSize - LastIndex;
                    LastIndex = ChunkSize;
                }
            }
        }

        /// <summary>
        /// 获取一个值，指示流是否支持读取操作。
        /// </summary>
        public override bool CanRead { get; } = true;
        /// <summary>
        /// 获取一个值，指示流是否支持寻找操作。
        /// </summary>
        public override bool CanSeek { get; } = false;
        /// <summary>
        /// 获取一个值，指示流是否支持写入操作。
        /// </summary>
        public override bool CanWrite { get; } = true;
        /// <summary>
        /// 获取或设置流中的位置。
        /// </summary>
        public override long Position { get; set; }

        /// <summary>
        /// 刷新流（在此实现中引发未实现异常）。
        /// </summary>
        public override void Flush()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 在流中寻找特定位置（在此实现中引发未实现异常）。
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 设置流的长度（在此实现中引发未实现异常）。
        /// </summary>
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 释放 CustomStream 使用的所有资源。
        /// </summary>
        public new void Dispose()
        {
            _bufferQueue.Clear();
            _lastBuffer = null;
            FirstIndex = 0;
            LastIndex = 0;
            base.Dispose();
        }
    }
}