using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;

namespace YooAsset.Editor
{
    public class ScanReportConfig
    {
        /// <summary>
        /// 导入JSON报告文件
        /// </summary>
        public static ScanReport ImportJsonConfig(string filePath)
        {
            if (File.Exists(filePath) == false)
                throw new FileNotFoundException(filePath);

            string jsonData = FileUtility.ReadAllText(filePath);
            ScanReport report = JsonUtility.FromJson<ScanReport>(jsonData);

            // 检测配置文件的签名
            if (report.FileSign != ScannerDefine.ReportFileSign)
                throw new Exception($"导入的报告文件无法识别 : {filePath}");

            // 检测报告文件的版本
            if (report.FileVersion != ScannerDefine.ReportFileVersion)
                throw new Exception($"报告文件的版本不匹配 : {report.FileVersion} != {ScannerDefine.ReportFileVersion}");

            // 检测标题数和内容是否匹配
            foreach (var element in report.ReportElements)
            {
                if (element.ScanInfos.Count != report.ReportHeaders.Count)
                {
                    throw new Exception($"报告的标题数和内容不匹配！");
                }
            }

            return report;
        }

        /// <summary>
        /// 导出JSON报告文件
        /// </summary>
        public static void ExportJsonConfig(string savePath, ScanReport scanReport)
        {
            if (File.Exists(savePath))
                File.Delete(savePath);

            string json = JsonUtility.ToJson(scanReport, true);
            FileUtility.WriteAllText(savePath, json);
        }
    }
}