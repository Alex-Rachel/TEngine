using UnityEngine.SceneManagement;

namespace YooAsset
{
    internal abstract class BundleResult
    {
        /// <summary>
        /// 卸载资源包文件
        /// </summary>
        public abstract void UnloadBundleFile();

        /// <summary>
        /// 获取资源包文件的路径
        /// </summary>
        public abstract string GetBundleFilePath();

        /// <summary>
        /// 读取资源包文件的二进制数据
        /// </summary>
        public abstract byte[] ReadBundleFileData();

        /// <summary>
        /// 读取资源包文件的文本数据
        /// </summary>
        public abstract string ReadBundleFileText();


        /// <summary>
        /// 加载资源包内的资源对象
        /// </summary>
        public abstract FSLoadAssetOperation LoadAssetAsync(AssetInfo assetInfo);

        /// <summary>
        /// 加载资源包内的所有资源对象
        /// </summary>
        public abstract FSLoadAllAssetsOperation LoadAllAssetsAsync(AssetInfo assetInfo);

        /// <summary>
        /// 加载资源包内的资源对象及所有子资源对象
        /// </summary>
        public abstract FSLoadSubAssetsOperation LoadSubAssetsAsync(AssetInfo assetInfo);

        /// <summary>
        /// 加载资源包内的场景对象
        /// </summary>
        public abstract FSLoadSceneOperation LoadSceneOperation(AssetInfo assetInfo, LoadSceneParameters loadParams, bool suspendLoad);
    }
}