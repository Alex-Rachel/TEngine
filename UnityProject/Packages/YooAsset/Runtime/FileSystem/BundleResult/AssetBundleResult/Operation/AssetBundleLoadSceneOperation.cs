using UnityEngine;
using UnityEngine.SceneManagement;

namespace YooAsset
{
    internal class AssetBundleLoadSceneOperation : FSLoadSceneOperation
    {
        protected enum ESteps
        {
            None,
            LoadScene,
            CheckResult,
            Done,
        }

        private readonly AssetInfo _assetInfo;
        private readonly LoadSceneParameters _loadParams;
        private bool _suspendLoad;
        private AsyncOperation _asyncOperation;
        private ESteps _steps = ESteps.None;

        public AssetBundleLoadSceneOperation(AssetInfo assetInfo, LoadSceneParameters loadParams, bool suspendLoad)
        {
            _assetInfo = assetInfo;
            _loadParams = loadParams;
            _suspendLoad = suspendLoad;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.LoadScene;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.LoadScene)
            {
                if (IsWaitForAsyncComplete)
                {
                    // 注意：场景同步加载方法不会立即加载场景，而是在下一帧加载。
                    Result = SceneManager.LoadScene(_assetInfo.AssetPath, _loadParams);
                    _steps = ESteps.CheckResult;
                }
                else
                {
                    // 注意：如果场景不存在异步加载方法返回NULL
                    // 注意：即使是异步加载也要在当帧获取到场景对象
                    _asyncOperation = SceneManager.LoadSceneAsync(_assetInfo.AssetPath, _loadParams);
                    if (_asyncOperation != null)
                    {
                        _asyncOperation.allowSceneActivation = !_suspendLoad;
                        _asyncOperation.priority = 100;
                        Result = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
                        _steps = ESteps.CheckResult;
                    }
                    else
                    {
                        string error = $"Failed to load scene : {_assetInfo.AssetPath}";
                        YooLogger.Error(error);
                        _steps = ESteps.Done;
                        Error = error;
                        Status = EOperationStatus.Failed;
                    }
                }
            }

            if (_steps == ESteps.CheckResult)
            {
                if (_asyncOperation != null)
                {
                    if (IsWaitForAsyncComplete)
                    {
                        //注意：场景加载无法强制异步转同步
                        YooLogger.Error("The scene is loading asyn !");
                    }
                    else
                    {
                        // 注意：在业务层中途可以取消挂起
                        if (_asyncOperation.allowSceneActivation == false)
                        {
                            if (_suspendLoad == false)
                                _asyncOperation.allowSceneActivation = true;
                        }

                        Progress = _asyncOperation.progress;
                        if (_asyncOperation.isDone == false)
                            return;
                    }
                }

                if (Result.IsValid())
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Succeed;
                }
                else
                {
                    string error = $"The loaded scene is invalid : {_assetInfo.AssetPath}";
                    YooLogger.Error(error);
                    _steps = ESteps.Done;
                    Error = error;
                    Status = EOperationStatus.Failed;
                }
            }
        }
        internal override void InternalWaitForAsyncComplete()
        {
            //注意：场景加载不支持异步转同步，为了支持同步加载方法需要实现该方法！
            InternalUpdate();
        }
        public override void UnSuspendLoad()
        {
            _suspendLoad = false;
        }
    }
}