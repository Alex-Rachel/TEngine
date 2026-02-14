namespace TEngine
{
    /// <summary>
    /// 加载场景回调函数集。
    /// </summary>
    public sealed class LoadSceneCallbacks
    {
        private readonly LoadSceneSuccessCallback _loadSceneSuccessCallback;
        private readonly LoadSceneFailureCallback _loadSceneFailureCallback;
        private readonly LoadSceneUpdateCallback _loadSceneUpdateCallback;

        /// <summary>
        /// 初始化加载场景回调函数集的新实例。
        /// </summary>
        /// <param name="loadSceneSuccessCallback">加载场景成功回调函数。</param>
        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback)
            : this(loadSceneSuccessCallback, null, null)
        {
        }

        /// <summary>
        /// 初始化加载场景回调函数集的新实例。
        /// </summary>
        /// <param name="loadSceneSuccessCallback">加载场景成功回调函数。</param>
        /// <param name="loadSceneFailureCallback">加载场景失败回调函数。</param>
        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback, LoadSceneFailureCallback loadSceneFailureCallback)
            : this(loadSceneSuccessCallback, loadSceneFailureCallback, null)
        {
        }

        /// <summary>
        /// 初始化加载场景回调函数集的新实例。
        /// </summary>
        /// <param name="loadSceneSuccessCallback">加载场景成功回调函数。</param>
        /// <param name="loadSceneUpdateCallback">加载场景更新回调函数。</param>
        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback, LoadSceneUpdateCallback loadSceneUpdateCallback)
            : this(loadSceneSuccessCallback, null, loadSceneUpdateCallback)
        {
        }

        /// <summary>
        /// 初始化加载场景回调函数集的新实例。
        /// </summary>
        /// <param name="loadSceneSuccessCallback">加载场景成功回调函数。</param>
        /// <param name="loadSceneFailureCallback">加载场景失败回调函数。</param>
        /// <param name="loadSceneUpdateCallback">加载场景更新回调函数。</param>
        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback, LoadSceneFailureCallback loadSceneFailureCallback, LoadSceneUpdateCallback loadSceneUpdateCallback)
        {
            if (loadSceneSuccessCallback == null)
            {
                throw new GameFrameworkException("Load scene success callback is invalid.");
            }

            _loadSceneSuccessCallback = loadSceneSuccessCallback;
            _loadSceneFailureCallback = loadSceneFailureCallback;
            _loadSceneUpdateCallback = loadSceneUpdateCallback;
        }

        /// <summary>
        /// 获取加载场景成功回调函数。
        /// </summary>
        public LoadSceneSuccessCallback LoadSceneSuccessCallback
        {
            get
            {
                return _loadSceneSuccessCallback;
            }
        }

        /// <summary>
        /// 获取加载场景失败回调函数。
        /// </summary>
        public LoadSceneFailureCallback LoadSceneFailureCallback
        {
            get
            {
                return _loadSceneFailureCallback;
            }
        }

        /// <summary>
        /// 获取加载场景更新回调函数。
        /// </summary>
        public LoadSceneUpdateCallback LoadSceneUpdateCallback
        {
            get
            {
                return _loadSceneUpdateCallback;
            }
        }
    }
}
