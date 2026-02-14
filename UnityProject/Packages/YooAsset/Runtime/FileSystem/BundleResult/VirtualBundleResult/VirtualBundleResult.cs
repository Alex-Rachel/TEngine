using UnityEngine.SceneManagement;

namespace YooAsset
{
    internal class VirtualBundleResult : BundleResult
    {
        private readonly IFileSystem _fileSystem;
        private readonly PackageBundle _packageBundle;

        public VirtualBundleResult(IFileSystem fileSystem, PackageBundle bundle)
        {
            _fileSystem = fileSystem;
            _packageBundle = bundle;
        }

        public override void UnloadBundleFile()
        {
        }
        public override string GetBundleFilePath()
        {
            return _fileSystem.GetBundleFilePath(_packageBundle);
        }
        public override byte[] ReadBundleFileData()
        {
            return _fileSystem.ReadBundleFileData(_packageBundle);
        }
        public override string ReadBundleFileText()
        {
            return _fileSystem.ReadBundleFileText(_packageBundle);
        }

        public override FSLoadAssetOperation LoadAssetAsync(AssetInfo assetInfo)
        {
            var operation = new VirtualBundleLoadAssetOperation(_packageBundle, assetInfo);
            return operation;
        }
        public override FSLoadAllAssetsOperation LoadAllAssetsAsync(AssetInfo assetInfo)
        {
            var operation = new VirtualBundleLoadAllAssetsOperation(_packageBundle, assetInfo);
            return operation;
        }
        public override FSLoadSubAssetsOperation LoadSubAssetsAsync(AssetInfo assetInfo)
        {
            var operation = new VirtualBundleLoadSubAssetsOperation(_packageBundle, assetInfo);
            return operation;
        }
        public override FSLoadSceneOperation LoadSceneOperation(AssetInfo assetInfo, LoadSceneParameters loadParams, bool suspendLoad)
        {
            var operation = new VirtualBundleLoadSceneOperation(assetInfo, loadParams, suspendLoad);
            return operation;
        }
    }
}