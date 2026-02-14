namespace TEngine
{
    /// <summary>
    /// 卸载场景回调函数集。
    /// </summary>
    public sealed class UnloadSceneCallbacks
    {
        private readonly UnloadSceneSuccessCallback _unloadSceneSuccessCallback;
        private readonly UnloadSceneFailureCallback _unloadSceneFailureCallback;

        /// <summary>
        /// 初始化卸载场景回调函数集的新实例。
        /// </summary>
        /// <param name="unloadSceneSuccessCallback">卸载场景成功回调函数。</param>
        public UnloadSceneCallbacks(UnloadSceneSuccessCallback unloadSceneSuccessCallback)
            : this(unloadSceneSuccessCallback, null)
        {
        }

        /// <summary>
        /// 初始化卸载场景回调函数集的新实例。
        /// </summary>
        /// <param name="unloadSceneSuccessCallback">卸载场景成功回调函数。</param>
        /// <param name="unloadSceneFailureCallback">卸载场景失败回调函数。</param>
        public UnloadSceneCallbacks(UnloadSceneSuccessCallback unloadSceneSuccessCallback, UnloadSceneFailureCallback unloadSceneFailureCallback)
        {
            if (unloadSceneSuccessCallback == null)
            {
                throw new GameFrameworkException("Unload scene success callback is invalid.");
            }

            _unloadSceneSuccessCallback = unloadSceneSuccessCallback;
            _unloadSceneFailureCallback = unloadSceneFailureCallback;
        }

        /// <summary>
        /// 获取卸载场景成功回调函数。
        /// </summary>
        public UnloadSceneSuccessCallback UnloadSceneSuccessCallback
        {
            get
            {
                return _unloadSceneSuccessCallback;
            }
        }

        /// <summary>
        /// 获取卸载场景失败回调函数。
        /// </summary>
        public UnloadSceneFailureCallback UnloadSceneFailureCallback
        {
            get
            {
                return _unloadSceneFailureCallback;
            }
        }
    }
}
