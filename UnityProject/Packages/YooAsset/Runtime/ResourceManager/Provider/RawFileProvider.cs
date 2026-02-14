
namespace YooAsset
{
    internal class RawFileProvider : ProviderOperation
    {
        public RawFileProvider(ResourceManager manager, string providerGUID, AssetInfo assetInfo) : base(manager, providerGUID, assetInfo)
        {
        }
        protected override void ProcessBundleResult()
        {
            InvokeCompletion(string.Empty, EOperationStatus.Succeed);
        }
    }
}