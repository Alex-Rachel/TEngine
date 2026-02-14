
namespace YooAsset
{
    internal interface IPlayMode
    {
        /// <summary>
        /// 当前激活的清单
        /// </summary>
        PackageManifest ActiveManifest { set; get; }

        /// <summary>
        /// 销毁文件系统
        /// </summary>
        void DestroyFileSystem();

        /// <summary>
        /// 向网络端请求最新的资源版本
        /// </summary>
        RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks, int timeout);

        /// <summary>
        /// 向网络端请求并更新清单
        /// </summary>
        UpdatePackageManifestOperation UpdatePackageManifestAsync(string packageVersion, int timeout);

        /// <summary>
        /// 预下载指定版本的包裹内容
        /// </summary>
        PreDownloadContentOperation PreDownloadContentAsync(string packageVersion, int timeout);

        /// <summary>
        /// 清理缓存文件
        /// </summary>
        ClearCacheFilesOperation ClearCacheFilesAsync(ClearCacheFilesOptions options);
        
        // 下载相关
        ResourceDownloaderOperation CreateResourceDownloaderByAll(int downloadingMaxNumber, int failedTryAgain);
        ResourceDownloaderOperation CreateResourceDownloaderByTags(string[] tags, int downloadingMaxNumber, int failedTryAgain);
        ResourceDownloaderOperation CreateResourceDownloaderByPaths(AssetInfo[] assetInfos, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain);

        // 解压相关
        ResourceUnpackerOperation CreateResourceUnpackerByAll(int upackingMaxNumber, int failedTryAgain);
        ResourceUnpackerOperation CreateResourceUnpackerByTags(string[] tags, int upackingMaxNumber, int failedTryAgain);
        
        // 导入相关
        ResourceImporterOperation CreateResourceImporterByFilePaths(string[] filePaths, int importingMaxNumber, int failedTryAgain);
        ResourceImporterOperation CreateResourceImporterByFileInfos(ImportFileInfo[] fileInfos, int importingMaxNumber, int failedTryAgain);
    }
}