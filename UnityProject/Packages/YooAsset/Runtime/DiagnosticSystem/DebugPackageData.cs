using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace YooAsset
{
    [Serializable]
    internal class DebugPackageData
    {
        /// <summary>
        /// 包裹名称
        /// </summary>
        public string PackageName;

        public List<DebugProviderInfo> ProviderInfos = new List<DebugProviderInfo>(1000);
        public List<DebugBundleInfo> BundleInfos = new List<DebugBundleInfo>(1000);
        public List<DebugOperationInfo> OperationInfos = new List<DebugOperationInfo>(1000);


        [NonSerialized]
        public Dictionary<string, DebugBundleInfo> BundleInfoDic = new Dictionary<string, DebugBundleInfo>();
        private bool _isParse = false;

        /// <summary>
        /// 获取调试资源包信息类
        /// </summary>
        public DebugBundleInfo GetBundleInfo(string bundleName)
        {
            // 解析数据
            if (_isParse == false)
            {
                _isParse = true;
                foreach (var bundleInfo in BundleInfos)
                {
                    if (BundleInfoDic.ContainsKey(bundleInfo.BundleName) == false)
                    {
                        BundleInfoDic.Add(bundleInfo.BundleName, bundleInfo);
                    }
                }
            }

            if (BundleInfoDic.TryGetValue(bundleName, out DebugBundleInfo value))
            {
                return value;
            }
            else
            {
                UnityEngine.Debug.LogError($"Can not found {nameof(DebugBundleInfo)} : {bundleName}");
                return default;
            }
        }
    }
}