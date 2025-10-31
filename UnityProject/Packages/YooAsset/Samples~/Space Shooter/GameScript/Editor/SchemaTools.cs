using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using YooAsset.Editor;

public class SchemaTools
{
    /// <summary>
    /// 通用扫描快捷方法
    /// </summary>
    public static List<ReportElement> ScanAssets(string[] scanAssetList, System.Func<string, ReportElement> scanFun, int unloadAssetLimit = int.MaxValue)
    {
        int scanNumber = 0;
        int progressCount = 0;
        int totalCount = scanAssetList.Length;
        List<ReportElement> results = new List<ReportElement>(totalCount);

        EditorTools.ClearProgressBar();
        foreach (string assetPath in scanAssetList)
        {
            scanNumber++;
            progressCount++;
            EditorTools.DisplayProgressBar("扫描中...", progressCount, totalCount);
            var scanResult = scanFun.Invoke(assetPath);
            if (scanResult != null)
                results.Add(scanResult);

            // 释放编辑器未使用的资源
            if (scanNumber >= unloadAssetLimit)
            {
                scanNumber = 0;
                EditorUtility.UnloadUnusedAssetsImmediate(true);
            }
        }
        EditorTools.ClearProgressBar();

        return results;
    }

    /// <summary>
    /// 通用修复快捷方法
    /// </summary>
    public static void FixAssets(List<ReportElement> fixAssetList, System.Action<ReportElement> fixFun, int unloadAssetLimit = int.MaxValue)
    {
        int scanNumber = 0;
        int totalCount = fixAssetList.Count;
        int progressCount = 0;
        EditorTools.ClearProgressBar();
        foreach (var scanResult in fixAssetList)
        {
            scanNumber++;
            progressCount++;
            EditorTools.DisplayProgressBar("修复中...", progressCount, totalCount);
            fixFun.Invoke(scanResult);

            // 释放编辑器未使用的资源
            if (scanNumber >= unloadAssetLimit)
            {
                scanNumber = 0;
                EditorUtility.UnloadUnusedAssetsImmediate(true);
            }
        }
        EditorTools.ClearProgressBar();
    }
}