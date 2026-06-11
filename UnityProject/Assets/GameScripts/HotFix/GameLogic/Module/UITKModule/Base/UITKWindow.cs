using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 窗口基类。
    /// </summary>
    public abstract class UITKWindow : UITKBase
    {
        public override UIType Type => UIType.Window;
        public string WindowName { get; private set; }
        public int WindowLayer { get; private set; }
        public string AssetName { get; private set; }
        public virtual bool FullScreen { get; private set; }
        public bool FromResources { get; private set; }
        public int HideTimeToClose { get; set; }
        public string Package { get; private set; }
        public int HideTimerId { get; set; }
        public bool IsLoadDone { get; private set; }
        public bool IsHide { get; set; }

        private bool _isCreate;
        private bool _isDestroyed;
        private VisualTreeAsset _visualTreeAsset;
        private Action<UITKWindow> _prepareCallback;

        // ━━━ 动画配置 ━━━

        protected virtual UITKAnimationType ShowAnimation => UITKAnimationType.FadeIn;
        protected virtual UITKAnimationType HideAnimation => UITKAnimationType.FadeOut;
        protected virtual int AnimationDuration => 200;

        internal virtual UniTask OnShowAnimation()
        {
            return UITKModule.Instance.AnimationDriver.Play(RootElement, ShowAnimation, true, AnimationDuration);
        }

        internal virtual UniTask OnHideAnimation()
        {
            return UITKModule.Instance.AnimationDriver.Play(RootElement, HideAnimation, false, AnimationDuration);
        }

        // ━━━ 可见性 ━━━

        private bool _visible;
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (RootElement != null)
                {
                    RootElement.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                    RootElement.pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
                }
                if (_isCreate)
                {
                    OnSetVisible(value);
                }
            }
        }

        // ━━━ 初始化 ━━━

        internal void Init(string windowName, int windowLayer, bool fullScreen, string assetName, bool fromResources, int hideTimeToClose, string package)
        {
            WindowName = windowName;
            WindowLayer = windowLayer;
            FullScreen = fullScreen;
            AssetName = assetName;
            FromResources = fromResources;
            HideTimeToClose = hideTimeToClose;
            Package = package;
        }

        // ━━━ 加载流程 ━━━

        internal async UniTaskVoid InternalLoad(string assetName, Action<UITKWindow> prepareCallback, bool isAsync, params object[] userDatas)
        {
            _prepareCallback = prepareCallback;
            _userDatas = userDatas;

            if (FromResources)
            {
                _visualTreeAsset = Resources.Load<VisualTreeAsset>(assetName);
            }
            else if (isAsync)
            {
                _visualTreeAsset = await UITKModule.Resource.LoadVisualTreeAssetAsync(assetName, default, Package);
            }
            else
            {
                _visualTreeAsset = UITKModule.Resource.LoadVisualTreeAsset(assetName, Package);
            }

            HandleLoadCompleted();
        }

        private void HandleLoadCompleted()
        {
            IsLoadDone = true;

            if (_isDestroyed)
            {
                ReleaseAsset();
                return;
            }

            RootElement = _visualTreeAsset.CloneTree();
            RootElement.name = WindowName;
            RootElement.style.flexGrow = 1;
            RootElement.style.position = Position.Absolute;
            RootElement.style.left = 0;
            RootElement.style.right = 0;
            RootElement.style.top = 0;
            RootElement.style.bottom = 0;

            UITKModule.Instance.AttachToLayer(this);

            IsPrepare = true;
            _prepareCallback?.Invoke(this);
        }

        // ━━━ 生命周期内部方法 ━━━

        internal void InternalCreate()
        {
            if (_isCreate) return;
            _isCreate = true;
            Inject();
            OnCreate();
            RegisterEvent();
        }

        internal void InternalRefresh()
        {
            OnRefresh();
        }

        internal void InternalUpdate()
        {
            if (!IsPrepare || !_visible) return;

            OnUpdate();

            if (ListChild.Count > 0)
            {
                if (!_updateListValid)
                {
                    _listUpdateChild ??= new();
                    _listUpdateChild.Clear();
                    for (int i = 0; i < ListChild.Count; i++)
                    {
                        if (ListChild[i]._hasOverrideUpdate)
                        {
                            _listUpdateChild.Add(ListChild[i]);
                        }
                    }
                    _updateListValid = true;
                }

                if (_listUpdateChild != null)
                {
                    for (int i = 0; i < _listUpdateChild.Count; i++)
                    {
                        _listUpdateChild[i].InternalUpdate();
                    }
                }
            }
        }

        internal void InternalDestroy(bool isShutDown = false)
        {
            _isDestroyed = true;

            if (!isShutDown)
            {
                CancelHideToCloseTimer();
            }

            OnDestroy();
            RemoveAllUIEvent();

            for (int i = ListChild.Count - 1; i >= 0; i--)
            {
                ListChild[i].Destroy();
            }
            ListChild.Clear();

            RootElement?.RemoveFromHierarchy();
            ReleaseAsset();
        }

        internal void CancelHideToCloseTimer()
        {
            if (HideTimerId > 0)
            {
                GameModule.Timer.RemoveTimer(HideTimerId);
                HideTimerId = 0;
            }
        }

        private void ReleaseAsset()
        {
            if (_visualTreeAsset != null && !FromResources)
            {
                UITKModule.Resource.Unload(_visualTreeAsset);
                _visualTreeAsset = null;
            }
        }

        // ━━━ TryInvoke (重新 Show 已存在窗口) ━━━

        internal void TryInvoke(Action<UITKWindow> prepareCallback, params object[] userDatas)
        {
            CancelHideToCloseTimer();
            IsHide = false;
            _userDatas = userDatas;

            if (IsPrepare)
            {
                prepareCallback?.Invoke(this);
            }
            else
            {
                _prepareCallback = prepareCallback;
            }
        }
    }
}
