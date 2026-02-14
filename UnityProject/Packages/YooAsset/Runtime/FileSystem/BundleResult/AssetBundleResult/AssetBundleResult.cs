using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YooAsset
{
    internal class AssetBundleResult : BundleResult
    {
        private readonly IFileSystem _fileSystem;
        private readonly PackageBundle _packageBundle;
        private readonly AssetBundle _assetBundle;
        private readonly Stream _managedStream;

        public AssetBundleResult(IFileSystem fileSystem, PackageBundle packageBundle, AssetBundle assetBundle, Stream managedStream)
        {
            _fileSystem = fileSystem;
            _packageBundle = packageBundle;
            _assetBundle = assetBundle;
            _managedStream = managedStream;
        }

        public override void UnloadBundleFile()
        {
            if (_assetBundle != null)
            {
                _assetBundle.Unload(true);
            }

            if (_managedStream != null)
            {
                _managedStream.Close();
                _managedStream.Dispose();
            }
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
            var operation = new AssetBundleLoadAssetOperation(_packageBundle, _assetBundle, assetInfo);
            return operation;
        }
        public override FSLoadAllAssetsOperation LoadAllAssetsAsync(AssetInfo assetInfo)
        {
            var operation = new AssetBundleLoadAllAssetsOperation(_packageBundle, _assetBundle, assetInfo);
            return operation;
        }
        public override FSLoadSubAssetsOperation LoadSubAssetsAsync(AssetInfo assetInfo)
        {
            var operation = new AssetBundleLoadSubAssetsOperation(_packageBundle, _assetBundle, assetInfo);
            return operation;
        }
        public override FSLoadSceneOperation LoadSceneOperation(AssetInfo assetInfo, LoadSceneParameters loadParams, bool suspendLoad)
        {
            var operation = new AssetBundleLoadSceneOperation(assetInfo, loadParams, suspendLoad);
            return operation;
        }
    }
}