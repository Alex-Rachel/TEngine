
namespace YooAsset
{
    internal class DownloadFileOptions
    {
        /// <summary>
        /// 失败后重试次数
        /// </summary>
        public readonly int FailedTryAgain;

        /// <summary>
        /// 主资源地址
        /// </summary>
        public string MainURL { private set; get; }

        /// <summary>
        /// 备用资源地址
        /// </summary>
        public string FallbackURL { private set; get; }

        /// <summary>
        /// 拷贝的本地文件路径
        /// </summary>
        public string ImportFilePath { set; get; }

        public DownloadFileOptions(int failedTryAgain)
        {
            FailedTryAgain = failedTryAgain;
        }

        /// <summary>
        /// 设置下载地址
        /// </summary>
        public void SetURL(string mainURL, string fallbackURL)
        {
            MainURL = mainURL;
            FallbackURL = fallbackURL;
        }

        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid()
        {
            if (string.IsNullOrEmpty(MainURL) || string.IsNullOrEmpty(FallbackURL))
                return false;

            return true;
        }
    }

    internal abstract class FSDownloadFileOperation : AsyncOperationBase
    {
        public PackageBundle Bundle { private set; get; }

        /// <summary>
        /// 当前下载的字节数
        /// </summary>
        public long DownloadedBytes { protected set; get; }

        /// <summary>
        /// 当前下载进度（0f - 1f）
        /// </summary>
        public float DownloadProgress { protected set; get; }


        public FSDownloadFileOperation(PackageBundle bundle)
        {
            Bundle = bundle;
            DownloadedBytes = 0;
            DownloadProgress = 0;
        }
    }
}