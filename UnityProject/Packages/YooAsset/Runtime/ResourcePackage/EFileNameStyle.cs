
namespace YooAsset
{
    public enum EFileNameStyle
    {
        /// <summary>
        /// 哈希值名称
        /// </summary>
        HashName = 0,

        /// <summary>
        /// 资源包名称（不推荐）
        /// </summary>
        BundleName = 1,

        /// <summary>
        /// 资源包名称 + 哈希值名称
        /// </summary>
        BundleName_HashName = 2,
    }
}