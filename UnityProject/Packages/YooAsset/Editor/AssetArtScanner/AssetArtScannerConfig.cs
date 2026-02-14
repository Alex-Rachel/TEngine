using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YooAsset.Editor
{
    public class AssetArtScannerConfig
    {
        public class ConfigWrapper
        {
            /// <summary>
            /// 文件签名
            /// </summary>
            public string FileSign;

            /// <summary>
            /// 文件版本
            /// </summary>
            public string FileVersion;

            /// <summary>
            /// 扫描器列表
            /// </summary>
            public List<AssetArtScanner> Scanners = new List<AssetArtScanner>();
        }

        /// <summary>
        /// 导入JSON配置文件
        /// </summary>
        public static void ImportJsonConfig(string filePath)
        {
            if (File.Exists(filePath) == false)
                throw new FileNotFoundException(filePath);

            string json = FileUtility.ReadAllText(filePath);
            ConfigWrapper setting = JsonUtility.FromJson<ConfigWrapper>(json);

            // 检测配置文件的签名
            if (setting.FileSign != ScannerDefine.SettingFileSign)
                throw new Exception($"导入的配置文件无法识别 : {filePath}");

            // 检测配置文件的版本
            if (setting.FileVersion != ScannerDefine.SettingFileVersion)
                throw new Exception($"配置文件的版本不匹配 : {setting.FileVersion} != {ScannerDefine.SettingFileVersion}");

            // 检测配置合法性
            HashSet<string> scanGUIDs = new HashSet<string>();
            foreach (var sacnner in setting.Scanners)
            {
                if (scanGUIDs.Contains(sacnner.ScannerGUID))
                {
                    throw new Exception($"Scanner {sacnner.ScannerName} GUID is existed : {sacnner.ScannerGUID} ");
                }
                else
                {
                    scanGUIDs.Add(sacnner.ScannerGUID);
                }
            }

            AssetArtScannerSettingData.Setting.Scanners = setting.Scanners;
            AssetArtScannerSettingData.SaveFile();
        }

        /// <summary>
        /// 导出JSON配置文件
        /// </summary>
        public static void ExportJsonConfig(string savePath)
        {
            if (File.Exists(savePath))
                File.Delete(savePath);

            ConfigWrapper wrapper = new ConfigWrapper();
            wrapper.FileSign = ScannerDefine.SettingFileSign;
            wrapper.FileVersion = ScannerDefine.SettingFileVersion;
            wrapper.Scanners = AssetArtScannerSettingData.Setting.Scanners;

            string json = JsonUtility.ToJson(wrapper, true);
            FileUtility.WriteAllText(savePath, json);
        }
    }
}