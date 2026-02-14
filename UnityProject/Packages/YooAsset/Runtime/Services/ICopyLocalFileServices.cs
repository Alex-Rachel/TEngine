
namespace YooAsset
{
    public struct LocalFileInfo
    {
        /// <summary>
        /// 包裹名称
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 资源包名称
        /// </summary>
        public string BundleName;

        /// <summary>
        /// 源文件请求地址
        /// </summary>
        public string SourceFileURL;
    }

    /// <summary>
    /// 本地文件拷贝服务类
    /// 备注：包体内文件拷贝，沙盒内文件导入都会触发该服务！
    /// </summary>
    public interface ICopyLocalFileServices
    {
        void CopyFile(LocalFileInfo sourceFileInfo, string destFilePath);
    }
}