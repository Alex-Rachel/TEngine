using UnityEngine;

namespace YooAsset
{
    internal class AssetBundleLoadAllAssetsOperation : FSLoadAllAssetsOperation
    {
        protected enum ESteps
        {
            None,
            CheckBundle,
            LoadAsset,
            CheckResult,
            Done,
        }

        private readonly PackageBundle _packageBundle;
        private readonly AssetBundle _assetBundle;
        private readonly AssetInfo _assetInfo;
        private AssetBundleRequest _request;
        private ESteps _steps = ESteps.None;

        public AssetBundleLoadAllAssetsOperation(PackageBundle packageBundle, AssetBundle assetBundle, AssetInfo assetInfo)
        {
            _packageBundle = packageBundle;
            _assetBundle = assetBundle;
            _assetInfo = assetInfo;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.CheckBundle;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.CheckBundle)
            {
                if (_assetBundle == null)
                {
                    _steps = ESteps.Done;
                    Error = $"The bundle {_packageBundle.BundleName} has been destroyed due to unity engine bugs !";
                    Status = EOperationStatus.Failed;
                    return;
                }

                _steps = ESteps.LoadAsset;
            }

            if (_steps == ESteps.LoadAsset)
            {
                if (IsWaitForAsyncComplete)
                {
                    if (_assetInfo.AssetType == null)
                        Result = _assetBundle.LoadAllAssets();
                    else
                        Result = _assetBundle.LoadAllAssets(_assetInfo.AssetType);
                }
                else
                {
                    if (_assetInfo.AssetType == null)
                        _request = _assetBundle.LoadAllAssetsAsync();
                    else
                        _request = _assetBundle.LoadAllAssetsAsync(_assetInfo.AssetType);
                }

                _steps = ESteps.CheckResult;
            }

            if (_steps == ESteps.CheckResult)
            {
                if (_request != null)
                {
                    if (IsWaitForAsyncComplete)
                    {
                        // 强制挂起主线程（注意：该操作会很耗时）
                        YooLogger.Warning("Suspend the main thread to load unity asset.");
                        Result = _request.allAssets;
                    }
                    else
                    {
                        Progress = _request.progress;
                        if (_request.isDone == false)
                            return;
                        Result = _request.allAssets;
                    }
                }

                if (Result == null)
                {
                    string error;
                    if (_assetInfo.AssetType == null)
                        error = $"Failed to load all assets : {_assetInfo.AssetPath} AssetType : null AssetBundle : {_packageBundle.BundleName}";
                    else
                        error = $"Failed to load all assets : {_assetInfo.AssetPath} AssetType : {_assetInfo.AssetType} AssetBundle : {_packageBundle.BundleName}";
                    YooLogger.Error(error);

                    _steps = ESteps.Done;
                    Error = error;
                    Status = EOperationStatus.Failed;
                }
                else
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Succeed;
                }
            }
        }
        internal override void InternalWaitForAsyncComplete()
        {
            while (true)
            {
                if (ExecuteWhileDone())
                {
                    _steps = ESteps.Done;
                    break;
                }
            }
        }
    }
}