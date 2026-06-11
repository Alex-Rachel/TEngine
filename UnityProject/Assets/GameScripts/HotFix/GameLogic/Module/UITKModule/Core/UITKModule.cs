using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit 管理模块。
    /// </summary>
    public sealed partial class UITKModule : Singleton<UITKModule>, IUpdate
    {
        public static IUITKResourceLoader Resource { get; private set; }
        public IUITKAnimationDriver AnimationDriver { get; set; }

        private readonly List<UITKWindow> _windowStack = new List<UITKWindow>(64);

        protected override void OnInit()
        {
            Resource = new UITKResourceLoader();
            AnimationDriver = new USSAnimationDriver();
            InitPanels();
        }

        protected override void OnRelease()
        {
            CloseAll(isShutDown: true);
            DestroyPanels();
        }

        public void OnUpdate()
        {
            int count = _windowStack.Count;
            for (int i = 0; i < _windowStack.Count; i++)
            {
                if (_windowStack.Count != count) break;
                _windowStack[i].InternalUpdate();
            }
        }

        // ━━━ Show ━━━

        public void ShowUI<T>(params object[] userDatas) where T : UITKWindow, new()
        {
            ShowUIImp<T>(false, userDatas);
        }

        public void ShowUIAsync<T>(params object[] userDatas) where T : UITKWindow, new()
        {
            ShowUIImp<T>(true, userDatas);
        }

        public async UniTask<T> ShowUIAsyncAwait<T>(params object[] userDatas) where T : UITKWindow, new()
        {
            return await ShowUIAwaitImp<T>(true, userDatas) as T;
        }

        public void ShowUI(Type type, params object[] userDatas)
        {
            ShowUIImp(type, false, userDatas);
        }

        public void ShowUIAsync(Type type, params object[] userDatas)
        {
            ShowUIImp(type, true, userDatas);
        }

        private void ShowUIImp<T>(bool isAsync, params object[] userDatas) where T : UITKWindow, new()
        {
            Type type = typeof(T);
            string windowName = type.FullName;

            if (!TryGetWindow(windowName, out UITKWindow window, userDatas))
            {
                window = CreateInstance<T>();
                Push(window);
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();
            }
        }

        private void ShowUIImp(Type type, bool isAsync, params object[] userDatas)
        {
            string windowName = type.FullName;

            if (!TryGetWindow(windowName, out UITKWindow window, userDatas))
            {
                window = CreateInstance(type);
                Push(window);
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();
            }
        }

        private async UniTask<UITKWindow> ShowUIAwaitImp<T>(bool isAsync, params object[] userDatas) where T : UITKWindow, new()
        {
            Type type = typeof(T);
            string windowName = type.FullName;

            if (TryGetWindow(windowName, out UITKWindow window, userDatas))
            {
                return window;
            }

            window = CreateInstance<T>();
            Push(window);
            window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();

            float time = 0f;
            while (!window.IsLoadDone)
            {
                time += Time.deltaTime;
                if (time > 60f) break;
                await UniTask.Yield();
            }
            return window;
        }

        private bool TryGetWindow(string windowName, out UITKWindow window, params object[] userDatas)
        {
            window = GetWindow(windowName);
            if (window != null)
            {
                Pop(window);
                Push(window);
                window.TryInvoke(OnWindowPrepare, userDatas);
                return true;
            }
            return false;
        }

        // ━━━ Close / Hide ━━━

        public void CloseUI<T>() where T : UITKWindow
        {
            CloseUI(typeof(T));
        }

        public void CloseUI(Type type)
        {
            string windowName = type.FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            window.InternalDestroy();
            Pop(window);
            OnSetWindowVisible();
        }

        public void HideUI<T>() where T : UITKWindow
        {
            HideUI(typeof(T));
        }

        public void HideUI(Type type)
        {
            string windowName = type.FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            if (window.HideTimeToClose <= 0)
            {
                CloseUI(type);
                return;
            }

            window.CancelHideToCloseTimer();
            window.Visible = false;
            window.IsHide = true;
            window.HideTimerId = GameModule.Timer.AddTimer((arg) =>
            {
                CloseUI(type);
            }, window.HideTimeToClose);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }

        public void CloseAll(bool isShutDown = false)
        {
            for (int i = 0; i < _windowStack.Count; i++)
            {
                _windowStack[i].InternalDestroy(isShutDown);
            }
            _windowStack.Clear();
        }

        public void CloseAllWithOut<T>() where T : UITKWindow
        {
            for (int i = _windowStack.Count - 1; i >= 0; i--)
            {
                UITKWindow window = _windowStack[i];
                if (window.GetType() == typeof(T)) continue;
                window.InternalDestroy();
                _windowStack.RemoveAt(i);
            }
        }

        // ━━━ Query ━━━

        public bool HasWindow<T>() => GetWindow(typeof(T).FullName) != null;

        public T GetUI<T>() where T : UITKWindow
        {
            return GetWindow(typeof(T).FullName) as T;
        }

        // ━━━ Instance Factory ━━━

        private UITKWindow CreateInstance<T>() where T : UITKWindow, new()
        {
            return CreateInstance(typeof(T));
        }

        private UITKWindow CreateInstance(Type type)
        {
            UITKWindow window = Activator.CreateInstance(type) as UITKWindow;
            UIWindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(UIWindowAttribute)) as UIWindowAttribute;

            if (window == null)
                throw new GameFrameworkException($"UITKWindow {type.FullName} create instance failed.");

            if (attribute != null)
            {
                string assetName = string.IsNullOrEmpty(attribute.Location) ? type.Name : attribute.Location;
                window.Init(type.FullName, (int)attribute.Layer, attribute.FullScreen, assetName, attribute.FromResources, attribute.HideTimeToClose, attribute.Package);
            }
            else
            {
                window.Init(type.FullName, (int)UILayer.UI, false, type.Name, false, 10, "");
            }

            return window;
        }
    }
}
