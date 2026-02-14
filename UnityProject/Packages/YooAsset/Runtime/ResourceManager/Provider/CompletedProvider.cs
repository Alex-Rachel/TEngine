
namespace YooAsset
{
    internal sealed class CompletedProvider : ProviderOperation
    {
        public CompletedProvider(ResourceManager manager, AssetInfo assetInfo) : base(manager, string.Empty, assetInfo)
        {
        }
        protected override void ProcessBundleResult()
        {
        }

        public void SetCompletedWithError(string error)
        {
            InvokeCompletion(error, EOperationStatus.Failed);
        }
    }
}