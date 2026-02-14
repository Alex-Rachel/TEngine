using System;
using System.Collections;
using System.Collections.Generic;

namespace YooAsset.Editor
{
    [Serializable]
    public class ScanReport
    {
        /// <summary>
        /// 文件签名（自动填写）
        /// </summary>
        public string FileSign;

        /// <summary>
        /// 文件版本（自动填写）
        /// </summary>
        public string FileVersion;

        /// <summary>
        /// 模式类型（自动填写）
        /// </summary>
        public string SchemaType;

        /// <summary>
        /// 扫描器GUID（自动填写）
        /// </summary>
        public string ScannerGUID;


        /// <summary>
        /// 报告名称
        /// </summary>
        public string ReportName;

        /// <summary>
        /// 报告介绍
        /// </summary>
        public string ReportDesc;

        /// <summary>
        /// 报告的标题列表
        /// </summary>
        public List<ReportHeader> ReportHeaders = new List<ReportHeader>();

        /// <summary>
        /// 扫描的元素列表
        /// </summary>
        public List<ReportElement> ReportElements = new List<ReportElement>();


        public ScanReport(string reportName, string reportDesc)
        {
            ReportName = reportName;
            ReportDesc = reportDesc;
        }

        /// <summary>
        /// 添加标题
        /// </summary>
        public ReportHeader AddHeader(string headerTitle, int width)
        {
            var reportHeader = new ReportHeader(headerTitle, width);
            ReportHeaders.Add(reportHeader);
            return reportHeader;
        }

        /// <summary>
        /// 添加标题
        /// </summary>
        public ReportHeader AddHeader(string headerTitle, int width, int minWidth, int maxWidth)
        {
            var reportHeader = new ReportHeader(headerTitle, width, minWidth, maxWidth);
            ReportHeaders.Add(reportHeader);
            return reportHeader;
        }

        /// <summary>
        /// 检测错误
        /// </summary>
        public void CheckError()
        {
            // 检测标题
            Dictionary<string, ReportHeader> headerMap = new Dictionary<string, ReportHeader>();
            foreach (var header in ReportHeaders)
            {
                string headerTitle = header.HeaderTitle;
                if (headerMap.ContainsKey(headerTitle))
                    throw new Exception($"The header title {headerTitle} already exists !");
                else
                    headerMap.Add(headerTitle, header);
            }

            // 检测扫描元素
            HashSet<string> elementMap = new HashSet<string>();
            foreach (var element in ReportElements)
            {
                if (string.IsNullOrEmpty(element.GUID))
                    throw new Exception($"The report element GUID is null or empty !");

                if (elementMap.Contains(element.GUID))
                    throw new Exception($"The report element GUID already exists ! {element.GUID}");
                else
                    elementMap.Add(element.GUID);

                foreach (var scanInfo in element.ScanInfos)
                {
                    if (headerMap.ContainsKey(scanInfo.HeaderTitle) == false)
                        throw new Exception($"The report element header {scanInfo.HeaderTitle} is missing !");

                    // 检测数值有效性
                    var header = headerMap[scanInfo.HeaderTitle];
                    header.CheckValueValid(scanInfo.ScanInfo);
                }
            }
        }
    }
}