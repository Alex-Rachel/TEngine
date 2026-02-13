// ReSharper disable SwapViaDeconstruction
// ReSharper disable UseIndexFromEndExpression
// ReSharper disable ConvertToPrimaryConstructor
using System;
using System.Collections.Generic;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8601 // Possible null reference assignment.
namespace Fantasy.DataStructure.PriorityQueue
{
    public sealed class PriorityQueue<T> where T : IComparable<T>
    {
        private readonly List<T> _heap;
        
        public PriorityQueue(int initialCapacity = 16)
        {
            _heap = new List<T>(initialCapacity);
        }
        
        public int Count => _heap.Count;

        public void Enqueue(T item)
        {
            _heap.Add(item);
            HeapifyUp(_heap.Count - 1);
        }

        public T Dequeue()
        {
            if (_heap.Count == 0)
            {
                throw new InvalidOperationException("The queue is empty.");
            }
            
            var item = _heap[0];
            var heapCount = _heap.Count - 1;
            _heap[0] = _heap[heapCount];
            _heap.RemoveAt(heapCount);
            HeapifyDown(0);
            return item;
        }

        public bool TryDequeue(out T item)
        {
            if (_heap.Count == 0)
            {
                item = default(T);
                return false;
            }

            item = Dequeue();
            return true;
        }

        public T Peek()
        {
            if (_heap.Count == 0)
            {
                throw new InvalidOperationException("The queue is empty.");
            }
            return _heap[0];
        }

        // ReSharper disable once IdentifierTypo
        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                var parentIndex = (index - 1) / 2;
                if (_heap[index].CompareTo(_heap[parentIndex]) >= 0)
                {
                    break;
                }
                Swap(index, parentIndex);
                index = parentIndex;
            }
        }

        // ReSharper disable once IdentifierTypo
        private void HeapifyDown(int index)
        {
            var lastIndex = _heap.Count - 1;
            while (true)
            {
                var smallestIndex = index;
                var leftChildIndex = 2 * index + 1;
                var rightChildIndex = 2 * index + 2;
                
                if (leftChildIndex <= lastIndex && _heap[leftChildIndex].CompareTo(_heap[smallestIndex]) < 0)
                {
                    smallestIndex = leftChildIndex;
                }
                
                if (rightChildIndex <= lastIndex && _heap[rightChildIndex].CompareTo(_heap[smallestIndex]) < 0)
                {
                    smallestIndex = rightChildIndex;
                }
                
                if (smallestIndex == index)
                {
                    break;
                }
                
                Swap(index, smallestIndex);
                index = smallestIndex;
            }
        }
        
        private void Swap(int index1, int index2)
        {
            var temp = _heap[index1];
            _heap[index1] = _heap[index2];
            _heap[index2] = temp;
        }
    }
}

