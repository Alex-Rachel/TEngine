using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 游戏事件数据类。
    /// </summary>
    internal class EventDelegateData
    {
        private readonly int _eventType = 0;
        private readonly List<Delegate> _listExist = new List<Delegate>();
        private readonly List<Delegate> _addList = new List<Delegate>();
        private readonly List<Delegate> _deleteList = new List<Delegate>();
        private bool _isExecute = false;
        private bool _dirty = false;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="eventType">事件类型。</param>
        internal EventDelegateData(int eventType)
        {
            _eventType = eventType;
        }

        /// <summary>
        /// 添加注册委托。
        /// </summary>
        /// <param name="handler">事件处理回调。</param>
        /// <returns>是否添加回调成功。</returns>
        internal bool AddHandler(Delegate handler)
        {
            if (_listExist.Contains(handler))
            {
                Log.Fatal("Repeated Add Handler");
                return false;
            }

            if (_isExecute)
            {
                _dirty = true;
                _addList.Add(handler);
            }
            else
            {
                _listExist.Add(handler);
            }

            return true;
        }

        /// <summary>
        /// 移除反注册委托。
        /// </summary>
        /// <param name="handler">事件处理回调。</param>
        internal void RmvHandler(Delegate handler)
        {
            if (_isExecute)
            {
                _dirty = true;
                _deleteList.Add(handler);
            }
            else
            {
                if (!_listExist.Remove(handler))
                {
                    Log.Fatal("Delete handle failed, not exist, EventId: {0}", RuntimeId.ToString(_eventType));
                }
            }
        }

        /// <summary>
        /// 检测脏数据修正。
        /// </summary>
        private void CheckModify()
        {
            _isExecute = false;
            if (_dirty)
            {
                for (int i = 0; i < _addList.Count; i++)
                {
                    _listExist.Add(_addList[i]);
                }

                _addList.Clear();

                for (int i = 0; i < _deleteList.Count; i++)
                {
                    _listExist.Remove(_deleteList[i]);
                }

                _deleteList.Clear();
                _dirty = false;
            }
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        public void Callback()
        {
            _isExecute = true;
            for (var i = 0; i < _listExist.Count; i++)
            {
                var d = _listExist[i];
                if (d is Action action)
                {
                    action();
                }
            }

            CheckModify();
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        public void Callback<TArg1>(TArg1 arg1)
        {
            _isExecute = true;
            for (var i = 0; i < _listExist.Count; i++)
            {
                var d = _listExist[i];
                if (d is Action<TArg1> action)
                {
                    action(arg1);
                }
            }

            CheckModify();
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        public void Callback<TArg1, TArg2>(TArg1 arg1, TArg2 arg2)
        {
            _isExecute = true;
            for (var i = 0; i < _listExist.Count; i++)
            {
                var d = _listExist[i];
                if (d is Action<TArg1, TArg2> action)
                {
                    action(arg1, arg2);
                }
            }

            CheckModify();
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3>(TArg1 arg1, TArg2 arg2, TArg3 arg3)
        {
            _isExecute = true;
            for (var i = 0; i < _listExist.Count; i++)
            {
                var d = _listExist[i];
                if (d is Action<TArg1, TArg2, TArg3> action)
                {
                    action(arg1, arg2, arg3);
                }
            }

            CheckModify();
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <param name="arg4">事件参数4。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        /// <typeparam name="TArg4">事件参数4类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3, TArg4>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
        {
            _isExecute = true;
            for (var i = 0; i < _listExist.Count; i++)
            {
                var d = _listExist[i];
                if (d is Action<TArg1, TArg2, TArg3, TArg4> action)
                {
                    action(arg1, arg2, arg3, arg4);
                }
            }

            CheckModify();
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <param name="arg4">事件参数4。</param>
        /// <param name="arg5">事件参数5。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        /// <typeparam name="TArg4">事件参数4类型。</typeparam>
        /// <typeparam name="TArg5">事件参数5类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3, TArg4, TArg5>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5)
        {
            _isExecute = true;
            for (var i = 0; i < _listExist.Count; i++)
            {
                var d = _listExist[i];
                if (d is Action<TArg1, TArg2, TArg3, TArg4, TArg5> action)
                {
                    action(arg1, arg2, arg3, arg4, arg5);
                }
            }

            CheckModify();
        }

        /// <summary>
        /// 回调调用。
        /// </summary>
        /// <param name="arg1">事件参数1。</param>
        /// <param name="arg2">事件参数2。</param>
        /// <param name="arg3">事件参数3。</param>
        /// <param name="arg4">事件参数4。</param>
        /// <param name="arg5">事件参数5。</param>
        /// <param name="arg6">事件参数6。</param>
        /// <typeparam name="TArg1">事件参数1类型。</typeparam>
        /// <typeparam name="TArg2">事件参数2类型。</typeparam>
        /// <typeparam name="TArg3">事件参数3类型。</typeparam>
        /// <typeparam name="TArg4">事件参数4类型。</typeparam>
        /// <typeparam name="TArg5">事件参数5类型。</typeparam>
        /// <typeparam name="TArg6">事件参数6类型。</typeparam>
        public void Callback<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6)
        {
            _isExecute = true;
            for (var i = 0; i < _listExist.Count; i++)
            {
                var d = _listExist[i];
                if (d is Action<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> action)
                {
                    action(arg1, arg2, arg3, arg4, arg5, arg6);
                }
            }

            CheckModify();
        }
    }
}