using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using UnityEditor;

namespace YooAsset.Editor
{
    /// <summary>
    /// 资源扫描报告合并器
    /// 说明：相同类型的报告可以合并查看
    /// </summary>
    public class ScanReportCombiner
    {
        /// <summary>
        /// 模式类型
        /// </summary>
        public string SchemaType { private set; get; }

        /// <summary>
        /// 报告标题
        /// </summary>
        public string ReportTitle { private set; get; }

        /// <summary>
        /// 报告介绍
        /// </summary>
        public string ReportDesc { private set; get; }

        /// <summary>
        /// 标题列表
        /// </summary>
        public List<ReportHeader> Headers = new List<ReportHeader>();

        /// <summary>
        /// 扫描结果
        /// </summary>
        public readonly List<ReportElement> Elements = new List<ReportElement>(10000);
        private readonly Dictionary<string, ScanReport> _combines = new Dictionary<string, ScanReport>(100);


        /// <summary>
        /// 合并报告文件
        /// 注意：模式不同的报告文件会合并失败！
        /// </summary>
        public bool Combine(ScanReport scanReport)
        {
            if (string.IsNullOrEmpty(scanReport.SchemaType))
            {
                Debug.LogError("Scan report schema type is null or empty !");
                return false;
            }

            if (string.IsNullOrEmpty(SchemaType))
            {
                SchemaType = scanReport.SchemaType;
                ReportTitle = scanReport.ReportName;
                ReportDesc = scanReport.ReportDesc;
                Headers = scanReport.ReportHeaders;
            }

            if (SchemaType != scanReport.SchemaType)
            {
                Debug.LogWarning($"Scan report has different schema type！{scanReport.SchemaType} != {SchemaType}");
                return false;
            }

            if (_combines.ContainsKey(scanReport.ScannerGUID))
            {
                Debug.LogWarning($"Scan report has already existed : {scanReport.ScannerGUID}");
                return false;
            }

            _combines.Add(scanReport.ScannerGUID, scanReport);
            CombineInternal(scanReport);
            return true;
        }
        private void CombineInternal(ScanReport scanReport)
        {
            string scannerGUID = scanReport.ScannerGUID;
            List<ReportElement> elements = scanReport.ReportElements;

            // 设置白名单
            var scanner = AssetArtScannerSettingData.Setting.GetScanner(scannerGUID);
            if (scanner != null)
            {
                foreach (var element in elements)
                {
                    if (scanner.CheckWhiteList(element.GUID))
                        element.IsWhiteList = true;
                }
            }

            // 添加到集合
            Elements.AddRange(elements);
        }

        /// <summary>
        /// 获取指定的标题类
        /// </summary>
        public ReportHeader GetHeader(string headerTitle)
        {
            foreach (var header in Headers)
            {
                if (header.HeaderTitle == headerTitle)
                    return header;
            }

            UnityEngine.Debug.LogWarning($"Not found {nameof(ReportHeader)} : {headerTitle}");
            return null;
        }

        /// <summary>
        /// 导出选中文件
        /// </summary>
        public void ExportFiles(string exportFolderPath)
        {
            if (string.IsNullOrEmpty(exportFolderPath))
                return;

            foreach (var element in Elements)
            {
                if (element.IsSelected)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(element.GUID);
                    if (string.IsNullOrEmpty(assetPath) == false)
                    {
                        string destPath = Path.Combine(exportFolderPath, assetPath);
                        EditorTools.CopyFile(assetPath, destPath, true);
                    }
                }
            }
        }

        /// <summary>
        /// 保存改变数据
        /// </summary>
        public void SaveChange()
        {
            // 存储白名单
            foreach (var scanReport in _combines.Values)
            {
                string scannerGUID = scanReport.ScannerGUID;
                var elements = scanReport.ReportElements;

                var scanner = AssetArtScannerSettingData.Setting.GetScanner(scannerGUID);
                if (scanner != null)
                {
                    List<string> whiteList = new List<string>(elements.Count);
                    foreach (var element in elements)
                    {
                        if (element.IsWhiteList)
                            whiteList.Add(element.GUID);
                    }
                    whiteList.Sort();
                    scanner.WhiteList = whiteList;
                    AssetArtScannerSettingData.SaveFile();
                }
            }
        }

        /// <summary>
        /// 修复所有元素
        /// 注意：排除白名单和隐藏元素
        /// </summary>
        public void FixAll()
        {
            foreach (var scanReport in _combines.Values)
            {
                string scannerGUID = scanReport.ScannerGUID;
                var elements = scanReport.ReportElements;

                List<ReportElement> fixList = new List<ReportElement>(elements.Count);
                foreach (var element in elements)
                {
                    if (element.Passes || element.IsWhiteList || element.Hidden)
                        continue;
                    fixList.Add(element);
                }
                FixInternal(scannerGUID, fixList);
            }
        }

        /// <summary>
        /// 修复选定元素
        /// 注意：包含白名单和隐藏元素
        /// </summary>
        public void FixSelect()
        {
            foreach (var scanReport in _combines.Values)
            {
                string scannerGUID = scanReport.ScannerGUID;
                var elements = scanReport.ReportElements;

                List<ReportElement> fixList = new List<ReportElement>(elements.Count);
                foreach (var element in elements)
                {
                    if (element.Passes)
                        continue;
                    if (element.IsSelected)
                        fixList.Add(element);
                }
                FixInternal(scannerGUID, fixList);
            }
        }

        private void FixInternal(string scannerGUID, List<ReportElement> fixList)
        {
            AssetArtScanner scanner = AssetArtScannerSettingData.Setting.GetScanner(scannerGUID);
            if (scanner != null)
            {
                var schema = scanner.LoadSchema();
                if (schema != null)
                {
                    schema.FixResult(fixList);
                }
            }
        }
    }
}