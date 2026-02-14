using UnityEngine;

namespace YooAsset
{
    [CreateAssetMenu(fileName = "YooAssetSettings", menuName = "YooAsset/Create YooAsset Settings")]
    internal class YooAssetSettings : ScriptableObject
    {
        /// <summary>
        /// YooAsset文件夹名称
        /// </summary>
        public string DefaultYooFolderName = "yoo";

        /// <summary>
        /// 资源清单前缀名称（默认为空)
        /// </summary>
        public string PackageManifestPrefix = string.Empty;
        

        /// <summary>
        /// 构建输出文件夹名称
        /// </summary>
        public const string OutputFolderName = "OutputCache";
    }
}