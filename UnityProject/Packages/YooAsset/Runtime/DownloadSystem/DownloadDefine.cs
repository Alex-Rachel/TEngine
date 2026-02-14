
namespace YooAsset
{
    /// <summary>
    /// 下载器结束
    /// </summary>
    public struct DownloaderFinishData
    {
        /// <summary>
        /// 所属包裹名称
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Succeed;
    }

    /// <summary>
    /// 下载器相关的更新数据
    /// </summary>
    public struct DownloadUpdateData
    {
        /// <summary>
        /// 所属包裹名称
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 下载进度 (0-1f)
        /// </summary>
        public float Progress;

        /// <summary>
        /// 下载文件总数
        /// </summary>
        public int TotalDownloadCount;

        /// <summary>
        /// 当前完成的下载文件数量
        /// </summary>
        public int CurrentDownloadCount;

        /// <summary>
        /// 下载数据总大小（单位：字节）
        /// </summary>
        public long TotalDownloadBytes;

        /// <summary>
        /// 当前完成的下载数据大小（单位：字节）
        /// </summary>
        public long CurrentDownloadBytes;
    }

    /// <summary>
    /// 下载器相关的错误数据
    /// </summary>
    public struct DownloadErrorData
    {
        /// <summary>
        /// 所属包裹名称
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 下载失败的文件名称
        /// </summary>
        public string FileName;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorInfo;
    }

    /// <summary>
    /// 下载器相关的文件数据
    /// </summary>
    public struct DownloadFileData
    {
        /// <summary>
        /// 所属包裹名称
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 下载的文件名称
        /// </summary>
        public string FileName;

        /// <summary>
        /// 下载的文件大小
        /// </summary>
        public long FileSize;
    }

    /// <summary>
    /// 导入文件的信息
    /// </summary>
    public struct ImportFileInfo
    {
        /// <summary>
        /// 本地文件路径
        /// </summary>
        public string FilePath;

        /// <summary>
        /// 资源包名称
        /// </summary>
        public string BundleName;

        /// <summary>
        /// 资源包GUID
        /// </summary>
        public string BundleGUID;
    }
}