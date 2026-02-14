namespace TEngine
{
    /// <summary>
    /// 加载资源回调函数集。
    /// </summary>
    public sealed class LoadAssetCallbacks
    {
        private readonly LoadAssetSuccessCallback _loadAssetSuccessCallback;
        private readonly LoadAssetFailureCallback _loadAssetFailureCallback;
        private readonly LoadAssetUpdateCallback _loadAssetUpdateCallback;

        /// <summary>
        /// 初始化加载资源回调函数集的新实例。
        /// </summary>
        /// <param name="loadAssetSuccessCallback">加载资源成功回调函数。</param>
        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback)
            : this(loadAssetSuccessCallback, null, null)
        {
        }

        /// <summary>
        /// 初始化加载资源回调函数集的新实例。
        /// </summary>
        /// <param name="loadAssetSuccessCallback">加载资源成功回调函数。</param>
        /// <param name="loadAssetFailureCallback">加载资源失败回调函数。</param>
        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetFailureCallback loadAssetFailureCallback)
            : this(loadAssetSuccessCallback, loadAssetFailureCallback, null)
        {
        }

        /// <summary>
        /// 初始化加载资源回调函数集的新实例。
        /// </summary>
        /// <param name="loadAssetSuccessCallback">加载资源成功回调函数。</param>
        /// <param name="loadAssetUpdateCallback">加载资源更新回调函数。</param>
        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetUpdateCallback loadAssetUpdateCallback)
            : this(loadAssetSuccessCallback, null, loadAssetUpdateCallback)
        {
        }

        /// <summary>
        /// 初始化加载资源回调函数集的新实例。
        /// </summary>
        /// <param name="loadAssetSuccessCallback">加载资源成功回调函数。</param>
        /// <param name="loadAssetFailureCallback">加载资源失败回调函数。</param>
        /// <param name="loadAssetUpdateCallback">加载资源更新回调函数。</param>
        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetFailureCallback loadAssetFailureCallback, LoadAssetUpdateCallback loadAssetUpdateCallback)
        {
            if (loadAssetSuccessCallback == null)
            {
                throw new GameFrameworkException("Load asset success callback is invalid.");
            }

            _loadAssetSuccessCallback = loadAssetSuccessCallback;
            _loadAssetFailureCallback = loadAssetFailureCallback;
            _loadAssetUpdateCallback = loadAssetUpdateCallback;
        }

        /// <summary>
        /// 获取加载资源成功回调函数。
        /// </summary>
        public LoadAssetSuccessCallback LoadAssetSuccessCallback
        {
            get
            {
                return _loadAssetSuccessCallback;
            }
        }

        /// <summary>
        /// 获取加载资源失败回调函数。
        /// </summary>
        public LoadAssetFailureCallback LoadAssetFailureCallback
        {
            get
            {
                return _loadAssetFailureCallback;
            }
        }

        /// <summary>
        /// 获取加载资源更新回调函数。
        /// </summary>
        public LoadAssetUpdateCallback LoadAssetUpdateCallback
        {
            get
            {
                return _loadAssetUpdateCallback;
            }
        }
    }
}
