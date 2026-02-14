
namespace YooAsset
{
    public abstract class RequestPackageVersionOperation : AsyncOperationBase
    {
        /// <summary>
        /// 当前最新的包裹版本
        /// </summary>
        public string PackageVersion { protected set; get; }
    }
    internal sealed class RequestPackageVersionImplOperation : RequestPackageVersionOperation
    {
        private enum ESteps
        {
            None,
            RequestPackageVersion,
            Done,
        }

        private readonly PlayModeImpl _impl;
        private readonly bool _appendTimeTicks;
        private readonly int _timeout;
        private FSRequestPackageVersionOperation _requestPackageVersionOp;
        private ESteps _steps = ESteps.None;

        internal RequestPackageVersionImplOperation(PlayModeImpl impl, bool appendTimeTicks, int timeout)
        {
            _impl = impl;
            _appendTimeTicks = appendTimeTicks;
            _timeout = timeout;
        }
        internal override void InternalStart()
        {
            _steps = ESteps.RequestPackageVersion;
        }
        internal override void InternalUpdate()
        {
            if (_steps == ESteps.None || _steps == ESteps.Done)
                return;

            if (_steps == ESteps.RequestPackageVersion)
            {
                if (_requestPackageVersionOp == null)
                {
                    var mainFileSystem = _impl.GetMainFileSystem();
                    _requestPackageVersionOp = mainFileSystem.RequestPackageVersionAsync(_appendTimeTicks, _timeout);
                    _requestPackageVersionOp.StartOperation();
                    AddChildOperation(_requestPackageVersionOp);
                }

                _requestPackageVersionOp.UpdateOperation();
                if (_requestPackageVersionOp.IsDone == false)
                    return;

                if (_requestPackageVersionOp.Status == EOperationStatus.Succeed)
                {
                    _steps = ESteps.Done;
                    PackageVersion = _requestPackageVersionOp.PackageVersion;
                    Status = EOperationStatus.Succeed;
                }
                else
                {
                    _steps = ESteps.Done;
                    Status = EOperationStatus.Failed;
                    Error = _requestPackageVersionOp.Error;
                }
            }
        }
    }
}