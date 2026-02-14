
namespace YooAsset
{
    internal class VirtualBundleLoadAssetOperation : FSLoadAssetOperation
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
        private readonly AssetInfo _assetInfo;
        private ESteps _steps = ESteps.None;

        public VirtualBundleLoadAssetOperation(PackageBundle packageBundle, AssetInfo assetInfo)
        {
            _packageBundle = packageBundle;
            _assetInfo = assetInfo;
        }
        internal override void InternalStart()
        {
#if UNITY_EDITOR
            _steps = ESteps.CheckBundle;
#else
            _steps = ESteps.Done;
            Error = $"{nameof(VirtualBundleLoadAssetOperation)} only support unity editor platform !";
            Status = EOperationStatus.Failed;            
#endif
        }
        internal override void InternalUpdate()
        {
#if UNITY_EDITOR
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.CheckBundle)
            {
                // 检测资源文件是否存在
                string guid = UnityEditor.AssetDatabase.AssetPathToGUID(_assetInfo.AssetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    string error = $"Not found asset : {_assetInfo.AssetPath}";
                    YooLogger.Error(error);
                    _steps = ESteps.Done;
                    Error = error;
                    Status = EOperationStatus.Failed;
                    return;
                }

                _steps = ESteps.LoadAsset;
            }

            if (_steps == ESteps.LoadAsset)
            {
                if (_assetInfo.AssetType == null)
                    Result = UnityEditor.AssetDatabase.LoadMainAssetAtPath(_assetInfo.AssetPath);
                else
                    Result = UnityEditor.AssetDatabase.LoadAssetAtPath(_assetInfo.AssetPath, _assetInfo.AssetType);
                _steps = ESteps.CheckResult;
            }

            if (_steps == ESteps.CheckResult)
            {
                if (Result == null)
                {
                    string error;
                    if (_assetInfo.AssetType == null)
                        error = $"Failed to load asset object : {_assetInfo.AssetPath} AssetType : null";
                    else
                        error = $"Failed to load asset object : {_assetInfo.AssetPath} AssetType : {_assetInfo.AssetType}";
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
#endif
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