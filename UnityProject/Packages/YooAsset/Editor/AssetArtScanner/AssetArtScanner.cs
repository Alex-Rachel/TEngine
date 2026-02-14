using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YooAsset.Editor
{
    [Serializable]
    public class AssetArtScanner
    {
        /// <summary>
        /// 扫描器GUID
        /// </summary>
        public string ScannerGUID = string.Empty;

        /// <summary>
        /// 扫描器名称
        /// </summary>
        public string ScannerName = string.Empty;

        /// <summary>
        /// 扫描器描述
        /// </summary>
        public string ScannerDesc = string.Empty;

        /// <summary>
        /// 扫描模式
        /// 注意：文件路径或文件GUID
        /// </summary>
        public string ScannerSchema = string.Empty;

        /// <summary>
        /// 存储目录
        /// </summary>
        public string SaveDirectory = string.Empty;

        /// <summary>
        /// 收集列表
        /// </summary>
        public List<AssetArtCollector> Collectors = new List<AssetArtCollector>();

        /// <summary>
        /// 白名单
        /// </summary>
        public List<string> WhiteList = new List<string>();


        /// <summary>
        /// 检测关键字匹配
        /// </summary>
        public bool CheckKeyword(string keyword)
        {
            if (ScannerName.Contains(keyword) || ScannerDesc.Contains(keyword))
                return true;
            else
                return false;
        }

        /// <summary>
        /// 是否在白名单里
        /// </summary>
        public bool CheckWhiteList(string guid)
        {
            return WhiteList.Contains(guid);
        }

        /// <summary>
        /// 检测配置错误
        /// </summary>
        public void CheckConfigError()
        {
            if (string.IsNullOrEmpty(ScannerName))
                throw new Exception($"Scanner name is null or empty !");

            if (string.IsNullOrEmpty(ScannerSchema))
                throw new Exception($"Scanner {ScannerName} schema is null !");

            if (string.IsNullOrEmpty(SaveDirectory) == false)
            {
                if (Directory.Exists(SaveDirectory) == false)
                    throw new Exception($"Scanner  {ScannerName} save directory is invalid : {SaveDirectory}");
            }
        }

        /// <summary>
        /// 加载扫描模式实例
        /// </summary>
        public ScannerSchema LoadSchema()
        {
            if (string.IsNullOrEmpty(ScannerSchema))
                return null;

            string filePath;
            if (ScannerSchema.StartsWith("Assets/"))
            {
                filePath = ScannerSchema;
            }
            else
            {
                string guid = ScannerSchema;
                filePath = AssetDatabase.GUIDToAssetPath(guid);
            }

            var schema = AssetDatabase.LoadMainAssetAtPath(filePath) as ScannerSchema;
            if (schema == null)
                Debug.LogWarning($"Failed load scanner schema : {filePath}");
            return schema;
        }

        /// <summary>
        /// 运行扫描器生成报告类
        /// </summary>
        public ScanReport RunScanner()
        {
            if (Collectors.Count == 0)
                Debug.LogWarning($"Scanner {ScannerName} collector is empty !");

            ScannerSchema schema = LoadSchema();
            if (schema == null)
                throw new Exception($"Failed to load schema : {ScannerSchema}");

            var report = schema.RunScanner(this);
            report.FileSign = ScannerDefine.ReportFileSign;
            report.FileVersion = ScannerDefine.ReportFileVersion;
            report.SchemaType = schema.GetType().FullName;
            report.ScannerGUID = ScannerGUID;
            return report;
        }
    }
}