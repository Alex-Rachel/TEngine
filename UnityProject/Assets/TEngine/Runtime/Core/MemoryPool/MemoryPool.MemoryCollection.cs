using System;
using System.Collections.Generic;

namespace TEngine
{
    public static partial class MemoryPool
    {
        /// <summary>
        /// 内存池收集器。
        /// </summary>
        private sealed class MemoryCollection
        {
            private readonly Queue<IMemory> _memories;
            private readonly Type _memoryType;
            private int _usingMemoryCount;
            private int _acquireMemoryCount;
            private int _releaseMemoryCount;
            private int _addMemoryCount;
            private int _removeMemoryCount;

            public MemoryCollection(Type memoryType)
            {
                _memories = new Queue<IMemory>();
                _memoryType = memoryType;
                _usingMemoryCount = 0;
                _acquireMemoryCount = 0;
                _releaseMemoryCount = 0;
                _addMemoryCount = 0;
                _removeMemoryCount = 0;
            }

            public Type MemoryType => _memoryType;

            public int UnusedMemoryCount => _memories.Count;

            public int UsingMemoryCount => _usingMemoryCount;

            public int AcquireMemoryCount => _acquireMemoryCount;

            public int ReleaseMemoryCount => _releaseMemoryCount;

            public int AddMemoryCount => _addMemoryCount;

            public int RemoveMemoryCount => _removeMemoryCount;

            public T Acquire<T>() where T : class, IMemory, new()
            {
                if (typeof(T) != _memoryType)
                {
                    throw new Exception("Type is invalid.");
                }

                _usingMemoryCount++;
                _acquireMemoryCount++;
                lock (_memories)
                {
                    if (_memories.Count > 0)
                    {
                        return (T)_memories.Dequeue();
                    }
                }

                _addMemoryCount++;
                return new T();
            }

            public IMemory Acquire()
            {
                _usingMemoryCount++;
                _acquireMemoryCount++;
                lock (_memories)
                {
                    if (_memories.Count > 0)
                    {
                        return _memories.Dequeue();
                    }
                }

                _addMemoryCount++;
                return (IMemory)Activator.CreateInstance(_memoryType);
            }

            public void Release(IMemory memory)
            {
                memory.Clear();
                lock (_memories)
                {
                    if (_enableStrictCheck && _memories.Contains(memory))
                    {
                        throw new Exception("The memory has been released.");
                    }

                    _memories.Enqueue(memory);
                }

                _releaseMemoryCount++;
                _usingMemoryCount--;
            }

            public void Add<T>(int count) where T : class, IMemory, new()
            {
                if (typeof(T) != _memoryType)
                {
                    throw new Exception("Type is invalid.");
                }

                lock (_memories)
                {
                    _addMemoryCount += count;
                    while (count-- > 0)
                    {
                        _memories.Enqueue(new T());
                    }
                }
            }

            public void Add(int count)
            {
                lock (_memories)
                {
                    _addMemoryCount += count;
                    while (count-- > 0)
                    {
                        _memories.Enqueue((IMemory)Activator.CreateInstance(_memoryType));
                    }
                }
            }

            public void Remove(int count)
            {
                lock (_memories)
                {
                    if (count > _memories.Count)
                    {
                        count = _memories.Count;
                    }

                    _removeMemoryCount += count;
                    while (count-- > 0)
                    {
                        _memories.Dequeue();
                    }
                }
            }

            public void RemoveAll()
            {
                lock (_memories)
                {
                    _removeMemoryCount += _memories.Count;
                    _memories.Clear();
                }
            }
        }
    }
}
