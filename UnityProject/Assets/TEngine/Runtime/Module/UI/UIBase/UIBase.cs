using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TEngine
{
    public abstract partial class UIBase : IDisposable
    {
        protected UIBase()
        {
            _state = UIState.CreatedUI;
        }

        ~UIBase() => Dispose(false);
        private bool _disposed;

        internal Canvas _canvas;

        internal GraphicRaycaster _raycaster;

        internal UIState _state = UIState.Uninitialized;
        internal UIState State => _state;


        private System.Object[] _userDatas;
        protected System.Object UserData => _userDatas != null && _userDatas.Length >= 1 ? _userDatas[0] : null;
        protected System.Object[] UserDatas => _userDatas;

        private RuntimeTypeHandle _runtimeTypeHandle;

        internal RuntimeTypeHandle RuntimeTypeHandler
        {
            get
            {
                if (_runtimeTypeHandle.Value == IntPtr.Zero)
                {
                    _runtimeTypeHandle = GetType().TypeHandle;
                }

                return _runtimeTypeHandle;
            }
        }

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnDestroy()
        {
        }

        protected virtual void OnOpen()
        {
        }

        protected virtual void OnClose()
        {
        }

        protected virtual void OnUpdate()
        {
        }

        /// <summary>
        /// 如果重写当前方法 则同步OnInitialize不会调用
        /// </summary>
        protected virtual async UniTask OnInitializeAsync()
        {
            await UniTask.CompletedTask;
            OnInitialize();
        }

        /// <summary>
        /// 如果重写当前方法 则同步OnOpen不会调用
        /// </summary>
        protected virtual async UniTask OnOpenAsync()
        {
            await UniTask.CompletedTask;
            OnOpen();
        }

        /// <summary>
        /// 如果重写当前方法 则同步OnClose不会调用
        /// </summary>
        protected virtual async UniTask OnCloseAsync()
        {
            await UniTask.CompletedTask;
            OnClose();
        }

        /// <summary>
        /// 事件在窗口销毁后会自动移除
        /// </summary>
        /// <param name="proxy"></param>
        protected virtual void OnRegisterEvent(GameEventMgr proxy)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 托管资源释放
                _canvas = null;
                _raycaster = null;
            }

            _userDatas = null;

            // 非托管资源释放
            if (Holder != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(Holder.gameObject);
                else
                    Object.DestroyImmediate(Holder.gameObject);
            }

            _disposed = true;
        }

        internal bool Visible
        {
            get => _canvas != null && _canvas.gameObject.layer == UIComponent.UIShowLayer;

            set
            {
                if (_canvas != null)
                {
                    int setLayer = value ? UIComponent.UIShowLayer : UIComponent.UIHideLayer;
                    if (_canvas.gameObject.layer == setLayer)
                        return;

                    _canvas.gameObject.layer = setLayer;
                    ChildVisible(value);
                    Interactable = value;
                }
            }
        }

        private bool Interactable
        {
            get => _raycaster.enabled && _raycaster != null;

            set
            {
                if (_raycaster != null && _raycaster.enabled != value)
                {
                    _raycaster.enabled = value;
                }
            }
        }

        /// <summary>
        /// 窗口深度值。
        /// </summary>
        internal int Depth
        {
            get => _canvas != null ? _canvas.sortingOrder : 0;

            set
            {
                if (_canvas != null && _canvas.sortingOrder != value)
                {
                    // 设置父类
                    _canvas.sortingOrder = value;
                }
            }
        }

        #region Event

        private GameEventMgr _eventListenerProxy;

        private GameEventMgr EventListenerProxy => _eventListenerProxy ??= MemoryPool.Acquire<GameEventMgr>();

        private void ReleaseEventListenerProxy()
        {
            if (_eventListenerProxy!=null)
            {
                MemoryPool.Release(_eventListenerProxy);
            }
        }

        #endregion

        #region 管理器内部调用

        internal UIHolderObjectBase Holder;
        internal abstract Type UIHolderType { get; }

        internal abstract void BindUIHolder(UIHolderObjectBase holder, UIBase owner);

        internal async UniTask InternalInitlized()
        {
            _state = UIState.Initialized;
            Holder.OnWindowInitEvent?.Invoke();
            await OnInitializeAsync();
            OnRegisterEvent(EventListenerProxy);
        }

        internal async UniTask InternalOpen()
        {
            if (_state != UIState.Opened)
            {
                _state = UIState.Opened;
                Visible = true;
                Holder.OnWindowShowEvent?.Invoke();
                await OnOpenAsync();
            }
        }

        internal async UniTask InternalClose()
        {
            if (_state == UIState.Opened)
            {
                Holder.OnWindowClosedEvent?.Invoke();
                await OnCloseAsync();
                _state = UIState.Closed;
                Visible = false;
            }
        }

        internal void InternalUpdate()
        {
            if (_state != UIState.Opened) return;
            OnUpdate();
            UpdateChildren();
        }

        internal async UniTask InternalDestroy()
        {
            _state = UIState.Destroying;
            Holder.OnWindowDestroyEvent?.Invoke();
            await DestroyAllChildren();
            OnDestroy();
            ReleaseEventListenerProxy();
            Dispose();
            _state = UIState.Destroyed;
        }

        internal void RefreshParams(params System.Object[] userDatas)
        {
            this._userDatas = userDatas;
        }

        #endregion
    }
}
