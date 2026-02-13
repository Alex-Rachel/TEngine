using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Fantasy.Pool;

namespace Fantasy.Async
{
    /// <summary>
    /// 用于FTask取消的CancellationToken
    /// </summary>
    public sealed class FCancellationToken : IDisposable
    {
        private bool _isDispose;
        private bool _isCancel;
        private readonly HashSet<Action> _actions = new HashSet<Action>();
        /// <summary>
        /// 当前CancellationToken是否已经取消过了
        /// </summary>
        public bool IsCancel => _isDispose || _isCancel;
        /// <summary>
        /// 添加一个取消要执行的Action
        /// </summary>
        /// <param name="action"></param>
        public void Add(Action action)
        {
            if (_isDispose)
            {
                return;
            }

            if (_isCancel)
            {
                return;
            }
            
            _actions.Add(action);
        }
        /// <summary>
        /// 移除一个取消要执行的Action
        /// </summary>
        /// <param name="action"></param>
        public void Remove(Action action)
        {
            if (_isDispose)
            {
                return;
            }
            
            if (_isCancel)
            {
                return;
            }
            
            _actions.Remove(action);
        }
        /// <summary>
        /// 取消CancellationToken
        /// </summary>
        public void Cancel()
        {
            if (IsCancel)
            {
                return;
            }
            
            _isCancel = true;
            
            foreach (var action in _actions)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
            
            _actions.Clear();
        }
        /// <summary>
        /// 销毁掉CancellationToken，会执行Cancel方法。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            if (!IsCancel)
            {
                Cancel();
                _isCancel = true;
            }
            
            _isDispose = true;

            if (Caches.Count > 2000)
            {
                return;
            }

            Caches.Enqueue(this);
        }
        
        #region Static

        private static readonly ConcurrentQueue<FCancellationToken> Caches = new ConcurrentQueue<FCancellationToken>();

        /// <summary>
        /// 获取一个新的CancellationToken
        /// </summary>
        public static FCancellationToken ToKen
        {
            get
            {
                if (!Caches.TryDequeue(out var fCancellationToken))
                {
                    fCancellationToken = new FCancellationToken();
                }

                fCancellationToken._isCancel = false;
                fCancellationToken._isDispose = false;
                return fCancellationToken;
            }
        }

        #endregion
    }
}