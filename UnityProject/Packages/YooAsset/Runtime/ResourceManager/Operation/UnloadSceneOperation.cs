using UnityEngine;
using UnityEngine.SceneManagement;

namespace YooAsset
{
    /// <summary>
    /// 场景卸载异步操作类
    /// </summary>
    public sealed class UnloadSceneOperation : AsyncOperationBase
    {
        private enum ESteps
        {
            None,
            CheckError,
            WaitDone,
            UnLoadScene,
            Done,
        }

        private ESteps _steps = ESteps.None;
        private readonly string _error;
        private readonly ProviderOperation _provider;
        private AsyncOperation _asyncOp = null;

        internal UnloadSceneOperation(string error)
        {
            _error = error;
        }
        internal UnloadSceneOperation(ProviderOperation provider)
        {
            _error = null;
            _provider = provider;

            // 注意：卸载场景前必须先解除挂起操作
            if (provider is SceneProvider)
            {
                var temp = provider as SceneProvider;
                temp.UnSuspendLoad();
            }
            else
            {
                throw new System.NotImplementedException();
            }
        }
        internal override void InternalStart()
        {
            _steps = ESteps.CheckError;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.CheckError)
            {
                if (string.IsNullOrEmpty(_error) == false)
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = _error;
                    return;
                }

                _steps = ESteps.WaitDone;
            }

            if (_steps == ESteps.WaitDone)
            {
                if (_provider.IsDone == false)
                    return;

                if (_provider.SceneObject.IsValid() == false)
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = "Scene is invalid !";
                    return;
                }

                if (_provider.SceneObject.isLoaded == false)
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = "Scene is not loaded !";
                    return;
                }

                _steps = ESteps.UnLoadScene;
            }

            if (_steps == ESteps.UnLoadScene)
            {
                if (_asyncOp == null)
                {
                    _asyncOp = SceneManager.UnloadSceneAsync(_provider.SceneObject);
                    if (_asyncOp == null)
                    {
                        _steps = ESteps.Done;
                        Status = EOperationStatus.Failed;
                        Error = "Unload scene failed, see the console logs !";
                        return;
                    }
                }

                Progress = _asyncOp.progress;
                if (_asyncOp.isDone == false)
                    return;

                _steps = ESteps.Done;
                Status = EOperationStatus.Succeed;
            }
        }
        internal override string InternalGetDesc()
        {
            return $"SceneName : {_provider.SceneName}";
        }
    }
}