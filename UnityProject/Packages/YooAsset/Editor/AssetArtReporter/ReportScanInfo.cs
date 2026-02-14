using System;
using System.Collections;
using System.Collections.Generic;

namespace YooAsset.Editor
{
    [Serializable]
    public class ReportScanInfo
    {
        /// <summary>
        /// 标题
        /// </summary>
        public string HeaderTitle;

        /// <summary>
        /// 扫描反馈的信息
        /// </summary>
        public string ScanInfo;

        public ReportScanInfo(string headerTitle, string scanInfo)
        {
            HeaderTitle = headerTitle;
            ScanInfo = scanInfo;
        }
    }
}