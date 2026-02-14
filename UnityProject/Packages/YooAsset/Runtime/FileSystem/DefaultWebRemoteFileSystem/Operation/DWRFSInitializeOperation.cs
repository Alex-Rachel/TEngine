
namespace YooAsset
{
    internal class DWRFSInitializeOperation : FSInitializeFileSystemOperation
    {
        private readonly DefaultWebRemoteFileSystem _fileSystem;

        public DWRFSInitializeOperation(DefaultWebRemoteFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }
        internal override void InternalStart()
        {
            Status = EOperationStatus.Succeed;
        }
        internal override void InternalUpdate()
        {
        }
    }
}