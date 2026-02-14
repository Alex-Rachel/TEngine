using System;
using System.Buffers;
using System.IO;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Serialize
{
    public enum MemoryStreamBufferSource
    {
        None = 0,
        Pack = 1,
        UnPack = 2,
    }
    
    public sealed class MemoryStreamBuffer : MemoryStream, IBufferWriter<byte>
    {
        public MemoryStreamBufferSource MemoryStreamBufferSource;

        public MemoryStreamBuffer() : base(256)
        {
            // 使用 capacity 参数的构造函数，这样创建的 MemoryStream 支持 GetBuffer()
        }

        public MemoryStreamBuffer(MemoryStreamBufferSource memoryStreamBufferSource, int capacity) : base(capacity)
        {
            MemoryStreamBufferSource = memoryStreamBufferSource;
        }
        public MemoryStreamBuffer(byte[] buffer): base(buffer) { } 
        
        public void Advance(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "The value of 'count' cannot be negative.");
            }
            
            var newLength = Position + count;

            if (newLength != Length)
            {
                SetLength(newLength);
            }
            
            Position = newLength;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint), sizeHint, "The value of 'count' cannot be negative.");
            }

            var availableSpace = Length - Position;

            // 当没有足够可用空间时，需要扩展
            if (availableSpace <= sizeHint)
            {
                // 当 sizeHint=0 时，至少扩展到 Capacity 或者增加一些空间
                long newLength;
                if (sizeHint == 0)
                {
                    // sizeHint=0 时，扩展到整个 Capacity，确保有足够空间
                    newLength = Math.Max(Capacity, Position + 256);
                }
                else
                {
                    newLength = Position + sizeHint;
                }

                SetLength(newLength);
            }

            return new Memory<byte>(GetBuffer(), (int)Position, (int)(Length - Position));
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint), sizeHint, "The value of 'count' cannot be negative.");
            }

            var availableSpace = Length - Position;

            // 当没有足够可用空间时，需要扩展
            if (availableSpace <= sizeHint)
            {
                // 当 sizeHint=0 时，至少扩展到 Capacity 或者增加一些空间
                long newLength;
                if (sizeHint == 0)
                {
                    // sizeHint=0 时，扩展到整个 Capacity，确保有足够空间
                    newLength = Math.Max(Capacity, Position + 256);
                }
                else
                {
                    newLength = Position + sizeHint;
                }

                SetLength(newLength);
            }

            return new Span<byte>(GetBuffer(), (int)Position, (int)(Length - Position));
        }
    }
}