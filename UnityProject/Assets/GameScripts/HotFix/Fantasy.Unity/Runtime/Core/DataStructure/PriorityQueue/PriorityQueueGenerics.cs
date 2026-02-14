// ReSharper disable SwapViaDeconstruction
// ReSharper disable UseIndexFromEndExpression
// ReSharper disable ConvertToPrimaryConstructor
using System;
using System.Collections.Generic;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

#pragma warning disable CS8601 // Possible null reference assignment.
namespace Fantasy.DataStructure.PriorityQueue
{
    /// <summary>
    /// 优先队列
    /// </summary>
    /// <typeparam name="TElement">节点数据</typeparam>
    /// <typeparam name="TPriority">排序的类型、</typeparam>
    public sealed class PriorityQueue<TElement, TPriority> where TPriority : IComparable<TPriority>
    {
        private readonly List<PriorityQueueItem<TElement, TPriority>> _heap;
        
        public PriorityQueue(int initialCapacity = 16)
        {
            _heap = new List<PriorityQueueItem<TElement, TPriority>>(initialCapacity);
        }
        
        public int Count => _heap.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            _heap.Add(new PriorityQueueItem<TElement, TPriority>(element, priority));
            HeapifyUp(_heap.Count - 1);
        }

        public TElement Dequeue()
        {
            if (_heap.Count == 0)
            {
                throw new InvalidOperationException("The queue is empty.");
            }
            
            var item = _heap[0]; 
            _heap[0] = _heap[_heap.Count - 1];
            _heap.RemoveAt(_heap.Count - 1);
            HeapifyDown(0);
            return item.Element;
        }

        public bool TryDequeue(out TElement element)
        {
            if (_heap.Count == 0)
            {
                element = default(TElement);
                return false;
            }

            element = Dequeue();
            return true;
        }

        public TElement Peek()
        {
            if (_heap.Count == 0)
            {
                throw new InvalidOperationException("The queue is empty.");
            }
            return _heap[0].Element;
        }

        // ReSharper disable once IdentifierTypo
        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                var parentIndex = (index - 1) / 2;
                if (_heap[index].Priority.CompareTo(_heap[parentIndex].Priority) >= 0)
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
                
                if (leftChildIndex <= lastIndex && _heap[leftChildIndex].Priority.CompareTo(_heap[smallestIndex].Priority) < 0)
                {
                    smallestIndex = leftChildIndex;
                }
                
                if (rightChildIndex <= lastIndex && _heap[rightChildIndex].Priority.CompareTo(_heap[smallestIndex].Priority) < 0)
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

