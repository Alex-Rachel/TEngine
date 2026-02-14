
namespace YooAsset
{
    public sealed class UpdatePackageManifestOperation : AsyncOperationBase
    {
        private enum ESteps
        {
            None,
            CheckParams,
            CheckActiveManifest,
            LoadPackageManifest,
            Done,
        }

        private readonly PlayModeImpl _impl;
        private readonly string _packageVersion;
        private readonly int _timeout;
        private FSLoadPackageManifestOperation _loadPackageManifestOp;
        private ESteps _steps = ESteps.None;

        internal UpdatePackageManifestOperation(PlayModeImpl impl, string packageVersion, int timeout)
        {
            _impl = impl;
            _packageVersion = packageVersion;
            _timeout = timeout;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.CheckParams;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.CheckParams)
            {
                if (string.IsNullOrEmpty(_packageVersion))
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = "Package version is null or empty.";
                }
                else
                {
                    _steps = ESteps.CheckActiveManifest;
                }
            }

            if (_steps == ESteps.CheckActiveManifest)
            {
                // 检测当前激活的清单对象	
                if (_impl.ActiveManifest != null && _impl.ActiveManifest.PackageVersion == _packageVersion)
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Succeed;
                }
                else
                {
                    _steps = ESteps.LoadPackageManifest;
                }
            }

            if (_steps == ESteps.LoadPackageManifest)
            {
                if (_loadPackageManifestOp == null)
                {
                    var mainFileSystem = _impl.GetMainFileSystem();
                    _loadPackageManifestOp = mainFileSystem.LoadPackageManifestAsync(_packageVersion, _timeout);
                    _loadPackageManifestOp.StartOperation();
                    AddChildOperation(_loadPackageManifestOp);
                }

                _loadPackageManifestOp.UpdateOperation();
                if (_loadPackageManifestOp.IsDone == false)
                    return;

                if (_loadPackageManifestOp.Status == EOperationStatus.Succeed)
                {
                    _steps = ESteps.Done;
                    _impl.ActiveManifest = _loadPackageManifestOp.Manifest;
                    Status = EOperationStatus.Succeed;
                }
                else
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = _loadPackageManifestOp.Error;
                }
            }
        }
        internal override string InternalGetDesc()
        {
            return $"PackageVersion : {_packageVersion}";
        }
    }
}