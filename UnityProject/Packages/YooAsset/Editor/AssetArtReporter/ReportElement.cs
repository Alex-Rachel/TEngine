using System;
using System.Collections;
using System.Collections.Generic;

namespace YooAsset.Editor
{
    [Serializable]
    public class ReportElement
    {
        /// <summary>
        /// GUID（白名单存储对象）
        /// </summary>
        public string GUID;

        /// <summary>
        /// 扫描是否通过
        /// </summary>
        public bool Passes = true;

        /// <summary>
        /// 反馈的信息列表
        /// </summary>
        public List<ReportScanInfo> ScanInfos = new List<ReportScanInfo>();


        public ReportElement(string guid)
        {
            GUID = guid;
        }

        /// <summary>
        /// 添加扫描信息
        /// </summary>
        public void AddScanInfo(string headerTitle, string value)
        {
            var reportScanInfo = new ReportScanInfo(headerTitle, value);
            ScanInfos.Add(reportScanInfo);
        }

        /// <summary>
        /// 添加扫描信息
        /// </summary>
        public void AddScanInfo(string headerTitle, long value)
        {
            AddScanInfo(headerTitle, value.ToString());
        }

        /// <summary>
        /// 添加扫描信息
        /// </summary>
        public void AddScanInfo(string headerTitle, double value)
        {
            AddScanInfo(headerTitle, value.ToString());
        }

        /// <summary>
        /// 获取扫描信息
        /// </summary>
        public ReportScanInfo GetScanInfo(string headerTitle)
        {
            foreach (var scanInfo in ScanInfos)
            {
                if (scanInfo.HeaderTitle == headerTitle)
                    return scanInfo;
            }

            UnityEngine.Debug.LogWarning($"Not found {nameof(ReportScanInfo)} : {headerTitle}");
            return null;
        }

        #region 临时字段
        /// <summary>
        /// 是否在列表里选中
        /// </summary>
        public bool IsSelected { set; get; }

        /// <summary>
        /// 是否在白名单里
        /// </summary>
        public bool IsWhiteList { set; get; }

        /// <summary>
        /// 是否隐藏元素
        /// </summary>
        public bool Hidden { set; get; }
        #endregion
    }
}