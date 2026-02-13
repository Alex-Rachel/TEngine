using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// UI管理模块。
    /// </summary>
    public sealed partial class UIModule : Singleton<UIModule>, IUpdate
    {
        // 核心字段
        private static Transform _instanceRoot = null;          // UI根节点变换组件
        private bool _enableErrorLog = true;                    // 是否启用错误日志
        private Camera _uiCamera = null;                        // UI专用摄像机
        private readonly List<UIWindow> _uiStack = new List<UIWindow>(128); // 窗口堆栈
        private ErrorLogger _errorLogger;                       // 错误日志记录器

        // 常量定义
        public const int LAYER_DEEP = 2000; 
        public const int WINDOW_DEEP = 100;
        public const int WINDOW_HIDE_LAYER = 2; // Ignore Raycast
        public const int WINDOW_SHOW_LAYER = 5; // UI

        // 资源加载接口
        public static IUIResourceLoader Resource;
        
        /// <summary>
        /// UI根节点访问属性
        /// </summary>
        public static Transform UIRoot => _instanceRoot;

        /// <summary>
        /// UI摄像机访问属性
        /// </summary>
        public Camera UICamera => _uiCamera;

        #region UIController

        private List<IUIController> m_uiControllers = new List<IUIController>();

        private void RegisterAllController()
        {
            AddUIController<CommonUIController>();
        }

        private void AddUIController<T>() where T : IUIController, new()
        {
            for (int i = 0; i < m_uiControllers.Count; i++)
            {
                var type = m_uiControllers[i].GetType();
                if (type == typeof(T))
                {
                    Log.Fatal("repeat controller type: {0}", typeof(T).Name);
                    return;
                }
            }

            var controller = new T();
            m_uiControllers.Add(controller);

            controller.RegUIMessage();
        }

        #endregion
        
        /// <summary>
        /// 模块初始化（自动调用）。
        /// 1. 查找场景中的UIRoot
        /// 2. 初始化资源加载器
        /// 3. 配置错误日志系统
        /// </summary>
        protected override void OnInit()
        {
            var uiRoot = GameObject.Find("UIRoot");
            if (uiRoot != null)
            {
                _instanceRoot = uiRoot.GetComponentInChildren<Canvas>()?.transform;
                _uiCamera = uiRoot.GetComponentInChildren<Camera>();
            }
            else
            {
                Log.Fatal("UIRoot not found !");
                return;
            }
            
            Resource = new UIResourceLoader();

            UnityEngine.Object.DontDestroyOnLoad(_instanceRoot.parent != null ? _instanceRoot.parent : _instanceRoot);

            _instanceRoot.gameObject.layer = LayerMask.NameToLayer("UI");
            RegisterAllController();

            if (Debugger.Instance != null)
            {
                switch (Debugger.Instance.ActiveWindowType)
                {
                    case DebuggerActiveWindowType.AlwaysOpen:
                        _enableErrorLog = true;
                        break;

                    case DebuggerActiveWindowType.OnlyOpenWhenDevelopment:
                        _enableErrorLog = Debug.isDebugBuild;
                        break;

                    case DebuggerActiveWindowType.OnlyOpenInEditor:
                        _enableErrorLog = Application.isEditor;
                        break;

                    default:
                        _enableErrorLog = false;
                        break;
                }
                if (_enableErrorLog)
                {
                    _errorLogger = new ErrorLogger(this);
                }   
            }
        }

        /// <summary>
        /// 模块释放（自动调用）。
        /// 1. 清理错误日志系统
        /// 2. 关闭所有窗口
        /// 3. 销毁UI根节点
        /// </summary>
        protected override void OnRelease()
        {
            if (_errorLogger != null)
            {
                _errorLogger.Dispose();
                _errorLogger = null;
            }
            CloseAll(isShutDown:true);
            if (_instanceRoot != null && _instanceRoot.parent != null)
            {
                UnityEngine.Object.Destroy(_instanceRoot.parent.gameObject);
            }
        }

        #region 设置安全区域

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域矩形（基于屏幕像素坐标）。</param>
        public static void ApplyScreenSafeRect(Rect safeRect)
        {
            CanvasScaler scaler = UIRoot.GetComponentInParent<CanvasScaler>();
            if (scaler == null)
            {
                Log.Error($"Not found {nameof(CanvasScaler)} !");
                return;
            }

            // Convert safe area rectangle from absolute pixels to UGUI coordinates
            float rateX = scaler.referenceResolution.x / Screen.width;
            float rateY = scaler.referenceResolution.y / Screen.height;
            float posX = (int)(safeRect.position.x * rateX);
            float posY = (int)(safeRect.position.y * rateY);
            float width = (int)(safeRect.size.x * rateX);
            float height = (int)(safeRect.size.y * rateY);

            float offsetMaxX = scaler.referenceResolution.x - width - posX;
            float offsetMaxY = scaler.referenceResolution.y - height - posY;

            // 注意：安全区坐标系的原点为左下角	
            var rectTrans = UIRoot.transform as RectTransform;
            if (rectTrans != null)
            {
                rectTrans.offsetMin = new Vector2(posX, posY); //锚框状态下的屏幕左下角偏移向量
                rectTrans.offsetMax = new Vector2(-offsetMaxX, -offsetMaxY); //锚框状态下的屏幕右上角偏移向量
            }
        }

        /// <summary>
        /// 模拟IPhoneX异形屏
        /// </summary>
        public static void SimulateIPhoneXNotchScreen()
        {
            Rect rect;
            if (Screen.height > Screen.width)
            {
                // 竖屏Portrait
                float deviceWidth = 1125;
                float deviceHeight = 2436;
                rect = new Rect(0f / deviceWidth, 102f / deviceHeight, 1125f / deviceWidth, 2202f / deviceHeight);
            }
            else
            {
                // 横屏Landscape
                float deviceWidth = 2436;
                float deviceHeight = 1125;
                rect = new Rect(132f / deviceWidth, 63f / deviceHeight, 2172f / deviceWidth, 1062f / deviceHeight);
            }

            Rect safeArea = new Rect(Screen.width * rect.x, Screen.height * rect.y, Screen.width * rect.width, Screen.height * rect.height);
            ApplyScreenSafeRect(safeArea);
        }

        #endregion

        /// <summary>
        /// 获取所有层级下顶部的窗口名称。
        /// </summary>
        public string GetTopWindow()
        {
            if (_uiStack.Count == 0)
            {
                return string.Empty;
            }

            UIWindow topWindow = _uiStack[^1];
            return topWindow.WindowName;
        }

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        public string GetTopWindow(int layer)
        {
            UIWindow lastOne = null;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack[i].WindowLayer == layer)
                    lastOne = _uiStack[i];
            }

            if (lastOne == null)
                return string.Empty;

            return lastOne.WindowName;
        }

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        public bool IsAnyLoading()
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                var window = _uiStack[i];
                if (window.IsLoadDone == false)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <returns>是否存在。</returns>
        public bool HasWindow<T>()
        {
            return HasWindow(typeof(T));
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <returns>是否存在。</returns>
        public bool HasWindow(Type type)
        {
            return IsContains(type.FullName);
        }

       /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUIAsync<T>(params System.Object[] userDatas) where T : UIWindow , new()
        {
            ShowUIImp<T>(true, userDatas);
        }

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUIAsync(Type type, params System.Object[] userDatas)
        {
            ShowUIImp(type, true, userDatas);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUI<T>(params System.Object[] userDatas) where T : UIWindow , new()
        {
            ShowUIImp<T>(false, userDatas);
        }
        
        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public async UniTask<T> ShowUIAsyncAwait<T>(params System.Object[] userDatas) where T : UIWindow , new()
        {
            return await ShowUIAwaitImp<T>(true, userDatas) as T;
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <param name="type"></param>
        /// <param name="userDatas"></param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUI(Type type, params System.Object[] userDatas)
        {
            ShowUIImp(type, false, userDatas);
        }

        private void ShowUIImp(Type type, bool isAsync, params System.Object[] userDatas)
        {
            string windowName = type.FullName;

            if (!TryGetWindow(windowName, out UIWindow window, userDatas))
            {
                window = CreateInstance(type);
                Push(window); //首次压入
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();
            }
        }
        
        private void ShowUIImp<T>(bool isAsync, params System.Object[] userDatas) where T : UIWindow , new()
        {
            Type type = typeof(T);
            string windowName = type.FullName;

            if (!TryGetWindow(windowName, out UIWindow window, userDatas))
            {
                window = CreateInstance<T>();
                Push(window); //首次压入
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();
            }
        }

        private bool TryGetWindow(string windowName,out UIWindow window, params System.Object[] userDatas)
        {
            window = null;
            if (IsContains(windowName))
            {
                window = GetWindow(windowName);
                Pop(window); //弹出窗口
                Push(window); //重新压入
                window.TryInvoke(OnWindowPrepare, userDatas);
                
                return true;
            }
            return false;
        }
        
        private async UniTask<T> ShowUIAwaitImp<T>(bool isAsync, params System.Object[] userDatas) where T : UIWindow , new()
        {
            Type type = typeof(T);
            string windowName = type.FullName;

            if (TryGetWindow(windowName, out UIWindow window, userDatas))
            {
                return window as T;
            }
            else
            {
                window = CreateInstance<T>();
                Push(window); //首次压入
                window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();
                float time = 0f;
                while (!window.IsLoadDone)
                {
                    time += Time.deltaTime;
                    if (time > 60f)
                    {
                        break;
                    }
                    await UniTask.Yield();
                }
                return window as T;
            }
        }

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型</typeparam>
        public void CloseUI<T>() where T : UIWindow
        {
            CloseUI(typeof(T));
        }

        public void CloseUI(Type type)
        {
            string windowName = type.FullName;
            UIWindow window = GetWindow(windowName);
            if (window == null)
                return;

            window.InternalDestroy();
            Pop(window);
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();
        }
        
        public void HideUI<T>() where T : UIWindow
        {
            HideUI(typeof(T));
        }

        public void HideUI(Type type)
        {
            string windowName = type.FullName;
            UIWindow window = GetWindow(windowName);
            if (window == null)
            {
                return;
            }

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
            },window.HideTimeToClose);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public void CloseAll(bool isShutDown = false)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                window.InternalDestroy(isShutDown);
            }

            _uiStack.Clear();
        }

        /// <summary>
        /// 关闭所有窗口除了。
        /// </summary>
        public void CloseAllWithOut(UIWindow withOut)
        {
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (window == withOut)
                {
                    continue;
                }

                window.InternalDestroy();
                _uiStack.RemoveAt(i);
            }
        }

        /// <summary>
        /// 关闭所有窗口除了。
        /// </summary>
        public void CloseAllWithOut<T>() where T : UIWindow
        {
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (window.GetType() == typeof(T))
                {
                    continue;
                }

                window.InternalDestroy();
                _uiStack.RemoveAt(i);
            }
        }

        private void OnWindowPrepare(UIWindow window)
        {
            window.InternalCreate();
            window.InternalRefresh();
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();
        }

        private void OnSortWindowDepth(int layer)
        {
            int depth = layer * LAYER_DEEP;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack[i].WindowLayer == layer)
                {
                    _uiStack[i].Depth = depth;
                    depth += WINDOW_DEEP;
                }
            }
        }

        private void OnSetWindowVisible()
        {
            bool isHideNext = false;
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (isHideNext == false)
                {
                    if (window.IsHide)
                    {
                        continue;
                    }
                    window.Visible = true;
                    if (window.IsPrepare && window.FullScreen)
                    {
                        isHideNext = true;
                    }
                }
                else
                {
                    window.Visible = false;
                }
            }
        }
        
        private UIWindow CreateInstance<T>() where T : UIWindow , new()
        {
            Type type = typeof(T);
            UIWindow window = new T();
            WindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(WindowAttribute)) as WindowAttribute;

            if (window == null)
                throw new GameFrameworkException($"Window {type.FullName} create instance failed.");

            if (attribute != null)
            {
                string assetName = string.IsNullOrEmpty(attribute.Location) ? type.Name : attribute.Location;
                window.Init(type.FullName, attribute.WindowLayer, attribute.FullScreen, assetName, attribute.FromResources, attribute.HideTimeToClose);
            }
            else
            {
                window.Init(type.FullName, (int)UILayer.UI, fullScreen: window.FullScreen, assetName: type.Name, fromResources: false, hideTimeToClose: 10);
            }

            return window;
        }

        private UIWindow CreateInstance(Type type)
        {
            UIWindow window = Activator.CreateInstance(type) as UIWindow;
            WindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(WindowAttribute)) as WindowAttribute;

            if (window == null)
                throw new GameFrameworkException($"Window {type.FullName} create instance failed.");

            if (attribute != null)
            {
                string assetName = string.IsNullOrEmpty(attribute.Location) ? type.Name : attribute.Location;
                window.Init(type.FullName, attribute.WindowLayer, attribute.FullScreen, assetName, attribute.FromResources, attribute.HideTimeToClose);
            }
            else
            {
                window.Init(type.FullName, (int)UILayer.UI, fullScreen: window.FullScreen, assetName: type.Name, fromResources: false, hideTimeToClose: 10);
            }

            return window;
        }
        
        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <returns>打开窗口操作句柄。</returns>
        public async UniTask<T> GetUIAsyncAwait<T>(CancellationToken cancellationToken = default) where T : UIWindow
        {
            string windowName = typeof(T).FullName;
            var window = GetWindow(windowName);
            if (window == null)
            {
                return null;
            }
            
            var ret = window as T;

            if (ret == null)
            {
                return null;
            }

            if (ret.IsLoadDone)
            {
                return ret;
            }

            float time = 0f;
            while (!ret.IsLoadDone)
            {
                time += Time.deltaTime;
                if (time > 60f)
                {
                    break;
                }
                await UniTask.Yield(cancellationToken: cancellationToken);
            }
            return ret;
        }

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <param name="callback">回调。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void GetUIAsync<T>(Action<T> callback) where T : UIWindow
        {
            string windowName = typeof(T).FullName;
            var window = GetWindow(windowName);
            if (window == null)
            {
                return;
            }

            var ret = window as T;
            
            if (ret == null)
            {
                return;
            }

            GetUIAsyncImp(callback).Forget();

            async UniTaskVoid GetUIAsyncImp(Action<T> ctx)
            {
                float time = 0f;
                while (!ret.IsLoadDone)
                {
                    time += Time.deltaTime;
                    if (time > 60f)
                    {
                        break;
                    }
                    await UniTask.Yield();
                }
                ctx?.Invoke(ret);
            }
        }

        private UIWindow GetWindow(string windowName)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (window.WindowName == windowName)
                {
                    return window;
                }
            }

            return null;
        }

        private bool IsContains(string windowName)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (window.WindowName == windowName)
                {
                    return true;
                }
            }

            return false;
        }

        private void Push(UIWindow window)
        {
            // 如果已经存在
            if (IsContains(window.WindowName))
            {
                throw new GameFrameworkException($"Window {window.WindowName} is exist.");
            }

            // 获取插入到所属层级的位置
            int insertIndex = -1;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (window.WindowLayer == _uiStack[i].WindowLayer)
                {
                    insertIndex = i + 1;
                }
            }

            // 如果没有所属层级，找到相邻层级
            if (insertIndex == -1)
            {
                for (int i = 0; i < _uiStack.Count; i++)
                {
                    if (window.WindowLayer > _uiStack[i].WindowLayer)
                    {
                        insertIndex = i + 1;
                    }
                }
            }

            // 如果是空栈或没有找到插入位置
            if (insertIndex == -1)
            {
                insertIndex = 0;
            }

            // 最后插入到堆栈
            _uiStack.Insert(insertIndex, window);
        }

        private void Pop(UIWindow window)
        {
            // 从堆栈里移除
            _uiStack.Remove(window);
        }

        public void OnUpdate()
        {
            if (_uiStack == null)
            {
                return;
            }

            int count = _uiStack.Count;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack.Count != count)
                {
                    break;
                }

                var window = _uiStack[i];
                window.InternalUpdate();
            }
        }
    }
}