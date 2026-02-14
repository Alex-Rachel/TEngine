using System;
using System.Collections;
using System.Collections.Generic;

namespace YooAsset
{
    [Serializable]
    internal struct DebugBundleInfo : IComparer<DebugBundleInfo>, IComparable<DebugBundleInfo>
    {
        /// <summary>
        /// 资源包名称
        /// </summary>
        public string BundleName;

        /// <summary>
        /// 引用计数
        /// </summary>
        public int RefCount;

        /// <summary>
        /// 加载状态
        /// </summary>
        public string Status;

        /// <summary>
        /// 谁引用了该资源包
        /// </summary>
        public List<string> ReferenceBundles;

        public int CompareTo(DebugBundleInfo other)
        {
            return Compare(this, other);
        }
        public int Compare(DebugBundleInfo a, DebugBundleInfo b)
        {
            return string.CompareOrdinal(a.BundleName, b.BundleName);
        }
    }
}