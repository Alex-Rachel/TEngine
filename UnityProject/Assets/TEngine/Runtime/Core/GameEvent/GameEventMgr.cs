using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 游戏事件管理器。
    /// </summary>
    public class GameEventMgr : IMemory
    {
        private readonly List<int> _listEventTypes;
        private readonly List<Delegate> _listHandles;
        private readonly bool _isInit = false;

        /// <summary>
        /// 游戏事件管理器构造函数。
        /// </summary>
        public GameEventMgr()
        {
            if (_isInit)
            {
                return;
            }

            _isInit = true;
            _listEventTypes = new List<int>();
            _listHandles = new List<Delegate>();
        }

        /// <summary>
        /// 清理内存对象回收入池。
        /// </summary>
        public void Clear()
        {
            if (!_isInit)
            {
                return;
            }

            for (int i = 0; i < _listEventTypes.Count; ++i)
            {
                var eventType = _listEventTypes[i];
                var handle = _listHandles[i];
                GameEvent.RemoveEventListener(eventType, handle);
            }

            _listEventTypes.Clear();
            _listHandles.Clear();
        }

        private void AddEventImp(int eventType, Delegate handler)
        {
            _listEventTypes.Add(eventType);
            _listHandles.Add(handler);
        }

        #region UIEvent

        public void AddEvent(int eventType, Action handler)
        {
            if (GameEvent.AddEventListener(eventType, handler))
            {
                AddEventImp(eventType, handler);
            }
        }

        public void AddEvent<T>(int eventType, Action<T> handler)
        {
            if (GameEvent.AddEventListener(eventType, handler))
            {
                AddEventImp(eventType, handler);
            }
        }

        public void AddEvent<T1, T2>(int eventType, Action<T1, T2> handler)
        {
            if (GameEvent.AddEventListener(eventType, handler))
            {
                AddEventImp(eventType, handler);
            }
        }

        public void AddEvent<T1, T2, T3>(int eventType, Action<T1, T2, T3> handler)
        {
            if (GameEvent.AddEventListener(eventType, handler))
            {
                AddEventImp(eventType, handler);
            }
        }

        public void AddEvent<T1, T2, T3, T4>(int eventType, Action<T1, T2, T3, T4> handler)
        {
            if (GameEvent.AddEventListener(eventType, handler))
            {
                AddEventImp(eventType, handler);
            }
        }

        public void AddEvent<T1, T2, T3, T4, T5>(int eventType, Action<T1, T2, T3, T4, T5> handler)
        {
            if (GameEvent.AddEventListener(eventType, handler))
            {
                AddEventImp(eventType, handler);
            }
        }

        #endregion
    }
}