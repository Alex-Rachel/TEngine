
namespace YooAsset
{
    internal interface IFileSystem
    {
        /// <summary>
        /// 包裹名称
        /// </summary>
        string PackageName { get; }

        /// <summary>
        /// 文件根目录
        /// </summary>
        string FileRoot { get; }

        /// <summary>
        /// 文件数量
        /// </summary>
        int FileCount { get; }


        /// <summary>
        /// 初始化文件系统
        /// </summary>
        FSInitializeFileSystemOperation InitializeFileSystemAsync();

        /// <summary>
        /// 加载包裹清单
        /// </summary>
        FSLoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout);

        /// <summary>
        /// 查询包裹版本
        /// </summary>
        FSRequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks, int timeout);

        /// <summary>
        /// 清理缓存文件
        /// </summary>
        FSClearCacheFilesOperation ClearCacheFilesAsync(PackageManifest manifest, ClearCacheFilesOptions options);

        /// <summary>
        /// 下载Bundle文件
        /// </summary>
        FSDownloadFileOperation DownloadFileAsync(PackageBundle bundle, DownloadFileOptions options);
        
        /// <summary>
        /// 加载Bundle文件
        /// </summary>
        FSLoadBundleOperation LoadBundleFile(PackageBundle bundle);


        /// <summary>
        /// 设置自定义参数
        /// </summary>
        void SetParameter(string name, object value);

        /// <summary>
        /// 创建文件系统
        /// </summary>
        void OnCreate(string packageName, string packageRoot);

        /// <summary>
        /// 销毁文件系统
        /// </summary>
        void OnDestroy();


        /// <summary>
        /// 查询文件归属
        /// </summary>
        bool Belong(PackageBundle bundle);

        /// <summary>
        /// 查询文件是否存在
        /// </summary>
        bool Exists(PackageBundle bundle);

        /// <summary>
        /// 是否需要下载
        /// </summary>
        bool NeedDownload(PackageBundle bundle);

        /// <summary>
        /// 是否需要解压
        /// </summary>
        bool NeedUnpack(PackageBundle bundle);

        /// <summary>
        /// 是否需要导入
        /// </summary>
        bool NeedImport(PackageBundle bundle);


        /// <summary>
        /// 获取Bundle文件路径
        /// </summary>
        string GetBundleFilePath(PackageBundle bundle);

        /// <summary>
        /// 读取Bundle文件的二进制数据
        /// </summary>
        byte[] ReadBundleFileData(PackageBundle bundle);

        /// <summary>
        /// 读取Bundle文件的文本数据
        /// </summary>
        string ReadBundleFileText(PackageBundle bundle);
    }
}