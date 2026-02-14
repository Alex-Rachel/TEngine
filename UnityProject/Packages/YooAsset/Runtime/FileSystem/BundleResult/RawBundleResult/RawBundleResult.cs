using UnityEngine.SceneManagement;

namespace YooAsset
{
    internal class RawBundleResult : BundleResult
    {
        private readonly IFileSystem _fileSystem;
        private readonly PackageBundle _packageBundle;

        public RawBundleResult(IFileSystem fileSystem, PackageBundle packageBundle)
        {
            _fileSystem = fileSystem;
            _packageBundle = packageBundle;
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
            var operation = new RawBundleLoadAssetOperation();
            return operation;
        }
        public override FSLoadAllAssetsOperation LoadAllAssetsAsync(AssetInfo assetInfo)
        {
            var operation = new RawBundleLoadAllAssetsOperation();
            return operation;
        }
        public override FSLoadSubAssetsOperation LoadSubAssetsAsync(AssetInfo assetInfo)
        {
            var operation = new RawBundleLoadSubAssetsOperation();
            return operation;
        }
        public override FSLoadSceneOperation LoadSceneOperation(AssetInfo assetInfo, LoadSceneParameters loadParams, bool suspendLoad)
        {
            var operation = new RawBundleLoadSceneOperation();
            return operation;
        }
    }
}