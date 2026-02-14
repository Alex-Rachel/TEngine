
namespace YooAsset
{
    internal class RawBundleLoadSubAssetsOperation : FSLoadSubAssetsOperation
    {
        internal override void InternalStart()
        {
            Error = $"{nameof(RawBundleLoadSubAssetsOperation)} not support load sub assets !";
            Status = EOperationStatus.Failed;
        }
        internal override void InternalUpdate()
        {
        }
    }
}