using System;
using System.Collections;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 可绑定集合。增删改时通知 UI 刷新。
    /// </summary>
    public class BindableList<T> : IList<T>
    {
        private readonly List<T> _list = new();

        public event Action OnListChanged;
        public event Action<int, T> OnItemAdded;
        public event Action<int, T> OnItemRemoved;
        public event Action<int, T, T> OnItemChanged;

        internal IList InternalList => _list;

        public int Count => _list.Count;
        public bool IsReadOnly => false;

        public T this[int index]
        {
            get => _list[index];
            set
            {
                T old = _list[index];
                _list[index] = value;
                OnItemChanged?.Invoke(index, old, value);
                OnListChanged?.Invoke();
            }
        }

        public void Add(T item)
        {
            _list.Add(item);
            OnItemAdded?.Invoke(_list.Count - 1, item);
            OnListChanged?.Invoke();
        }

        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items) _list.Add(item);
            OnListChanged?.Invoke();
        }

        public void Insert(int index, T item)
        {
            _list.Insert(index, item);
            OnItemAdded?.Invoke(index, item);
            OnListChanged?.Invoke();
        }

        public bool Remove(T item)
        {
            int idx = _list.IndexOf(item);
            if (idx < 0) return false;
            RemoveAt(idx);
            return true;
        }

        public void RemoveAt(int index)
        {
            T item = _list[index];
            _list.RemoveAt(index);
            OnItemRemoved?.Invoke(index, item);
            OnListChanged?.Invoke();
        }

        public void Clear()
        {
            _list.Clear();
            OnListChanged?.Invoke();
        }

        public bool Contains(T item) => _list.Contains(item);
        public int IndexOf(T item) => _list.IndexOf(item);
        public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
