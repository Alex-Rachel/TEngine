
namespace YooAsset
{
    internal class DEFSInitializeOperation : FSInitializeFileSystemOperation
    {
        private readonly DefaultEditorFileSystem _fileSytem;

        internal DEFSInitializeOperation(DefaultEditorFileSystem fileSystem)
        {
            _fileSytem = fileSystem;
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