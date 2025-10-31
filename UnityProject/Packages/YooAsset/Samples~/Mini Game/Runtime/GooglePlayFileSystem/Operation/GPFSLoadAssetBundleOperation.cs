#if UNITY_ANDROID && GOOGLE_PLAY
using System.IO;
using UnityEngine;
using YooAsset;
using Google.Play.AssetDelivery;

internal class GPFSLoadAssetBundleOperation : FSLoadBundleOperation
{
    private enum ESteps
    {
        None,
        LoadAssetBundle,
        CheckResult,
        Done,
    }

    private readonly GooglePlayFileSystem _fileSystem;
    private readonly PackageBundle _bundle;
    private PlayAssetBundleRequest _bundleRequest;
    private ESteps _steps = ESteps.None;


    internal GPFSLoadAssetBundleOperation(GooglePlayFileSystem fileSystem, PackageBundle bundle)
    {
        _fileSystem = fileSystem;
        _bundle = bundle;
    }
    internal override void InternalStart()
    {
        DownloadProgress = 1f;
        DownloadedBytes = _bundle.FileSize;
        _steps = ESteps.LoadAssetBundle;
    }
    internal override void InternalUpdate()
    {
        if (_steps == ESteps.None || _steps == ESteps.Done)
            return;

        if (_steps == ESteps.LoadAssetBundle)
        {
            if (_bundle.Encrypted)
            {
                _steps = ESteps.Done;
                Status = EOperationStatus.Failed;
                Error = $"The {nameof(GooglePlayFileSystem)} not support bundle encrypted !";
                YooLogger.Error(Error);
                return;
            }

            _bundleRequest = PlayAssetDelivery.RetrieveAssetBundleAsync(_bundle.FileName);
            _steps = ESteps.CheckResult;
        }

        if (_steps == ESteps.CheckResult)
        {
            if (_bundleRequest.IsDone == false)
                return;

            if (_bundleRequest.Error != AssetDeliveryErrorCode.NoError)
            {
                _steps = ESteps.Done;
                Status = EOperationStatus.Failed;
                Error = $"Failed to load delivery asset bundle file : {_bundle.BundleName} Error : {_bundleRequest.Error}";
                YooLogger.Error(Error);
            }
            else
            {
                _steps = ESteps.Done;
                Status = EOperationStatus.Succeed;
                Result = new AssetBundleResult(_fileSystem, _bundle, _bundleRequest.AssetBundle, null);
            }
        }
    }
    internal override void InternalWaitForAsyncComplete()
    {
        if (_steps != ESteps.Done)
        {
            _steps = ESteps.Done;
            Status = EOperationStatus.Failed;
            Error = $"{nameof(GooglePlayFileSystem)} not support sync load method !";
            UnityEngine.Debug.LogError(Error);
        }
    }
}
#endif