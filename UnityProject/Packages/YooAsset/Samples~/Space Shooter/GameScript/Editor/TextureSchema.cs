#if UNITY_2019_4_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using YooAsset.Editor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CreateAssetMenu(fileName = "TextureSchema", menuName = "YooAssetArt/Create TextureSchema")]
public class TextureSchema : ScannerSchema
{
    /// <summary>
    /// 图片最大宽度
    /// </summary>
    public int MaxWidth = 1024;

    /// <summary>
    /// 图片最大高度
    /// </summary>
    public int MaxHeight = 1024;

    /// <summary>
    /// 测试列表
    /// </summary>
    public List<string> TestStringValues = new List<string>();
    

    /// <summary>
    /// 获取用户指南信息
    /// </summary>
    public override string GetUserGuide()
    {
        return "规则介绍：检测图片的格式，尺寸";
    }

    /// <summary>
    /// 运行生成扫描报告
    /// </summary>
    public override ScanReport RunScanner(AssetArtScanner scanner)
    {
        // 创建扫描报告
        string name = "扫描所有纹理资产";
        string desc = GetUserGuide();
        var report = new ScanReport(name, desc);
        report.AddHeader("资源路径", 600, 500, 1000).SetStretchable().SetSearchable().SetSortable().SetCounter().SetHeaderType(EHeaderType.AssetPath);
        report.AddHeader("图片宽度", 100).SetSortable().SetHeaderType(EHeaderType.LongValue);
        report.AddHeader("图片高度", 100).SetSortable().SetHeaderType(EHeaderType.LongValue);
        report.AddHeader("内存大小", 120).SetSortable().SetUnits("bytes").SetHeaderType(EHeaderType.LongValue);
        report.AddHeader("苹果格式", 100);
        report.AddHeader("安卓格式", 100);
        report.AddHeader("错误信息", 500).SetStretchable();

        // 获取扫描资源集合
        var searchDirectorys = scanner.Collectors.Select(c => { return c.CollectPath; });
        string[] findAssets = EditorTools.FindAssets(EAssetSearchType.Texture, searchDirectorys.ToArray());

        // 开始扫描资源集合
        var results = SchemaTools.ScanAssets(findAssets, ScanAssetInternal);
        report.ReportElements.AddRange(results);
        return report;
    }
    private ReportElement ScanAssetInternal(string assetPath)
    {
        var importer = TextureTools.GetAssetImporter(assetPath);
        if (importer == null)
            return null;

        // 加载纹理对象
        var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
        var assetGUID = AssetDatabase.AssetPathToGUID(assetPath);
        var iosFormat = TextureTools.GetPlatformIOSFormat(importer);
        var androidFormat = TextureTools.GetPlatformAndroidFormat(importer);
        var memorySize = TextureTools.GetStorageMemorySize(texture);

        // 获取错误信息
        string errorInfo = string.Empty;
        {
            // 苹果格式
            if (iosFormat != TextureImporterFormat.ASTC_4x4)
            {
                errorInfo += " | ";
                errorInfo += "苹果格式不对";
            }

            // 安卓格式
            if (androidFormat != TextureImporterFormat.ASTC_4x4)
            {
                errorInfo += " | ";
                errorInfo += "安卓格式不对";
            }

            // 多级纹理
            if (importer.isReadable)
            {
                errorInfo += " | ";
                errorInfo += "开启了可读写";
            }

            // 超大纹理
            if (texture.width > MaxWidth || texture.height > MaxHeight)
            {
                errorInfo += " | ";
                errorInfo += "超大纹理";
            }
        }

        // 添加扫描信息
        ReportElement result = new ReportElement(assetGUID);
        result.AddScanInfo("资源路径", assetPath);
        result.AddScanInfo("图片宽度", texture.width);
        result.AddScanInfo("图片高度", texture.height);
        result.AddScanInfo("内存大小", memorySize);
        result.AddScanInfo("苹果格式", iosFormat.ToString());
        result.AddScanInfo("安卓格式", androidFormat.ToString());
        result.AddScanInfo("错误信息", errorInfo);

        // 判断是否通过
        result.Passes = string.IsNullOrEmpty(errorInfo);
        return result;
    }

    /// <summary>
    /// 修复扫描结果
    /// </summary>
    public override void FixResult(List<ReportElement> fixList)
    {
        SchemaTools.FixAssets(fixList, FixAssetInternal);
    }
    private void FixAssetInternal(ReportElement result)
    {
        var scanInfo = result.GetScanInfo("资源路径");
        var assetPath = scanInfo.ScanInfo;
        var importer = TextureTools.GetAssetImporter(assetPath);
        if (importer == null)
            return;

        // 苹果格式
        var iosPlatformSetting = TextureTools.GetPlatformIOSSettings(importer);
        iosPlatformSetting.format = TextureImporterFormat.ASTC_4x4;
        iosPlatformSetting.overridden = true;

        // 安卓格式
        var androidPlatformSetting = TextureTools.GetPlatformAndroidSettings(importer);
        androidPlatformSetting.format = TextureImporterFormat.ASTC_4x4;
        androidPlatformSetting.overridden = true;

        // 可读写
        importer.isReadable = false;

        // 保存配置
        importer.SetPlatformTextureSettings(iosPlatformSetting);
        importer.SetPlatformTextureSettings(androidPlatformSetting);
        importer.SaveAndReimport();
        Debug.Log($"修复了 : {assetPath}");
    }

    /// <summary>
    /// 创建检视面板对
    /// </summary>
    public override SchemaInspector CreateInspector()
    {
        var container = new VisualElement();

        // 图片最大宽度
        var maxWidthField = new IntegerField();
        maxWidthField.label = "图片最大宽度";
        maxWidthField.SetValueWithoutNotify(MaxWidth);
        maxWidthField.RegisterValueChangedCallback((evt) =>
        {
            MaxWidth = evt.newValue;
        });
        container.Add(maxWidthField);

        // 图片最大高度
        var maxHeightField = new IntegerField();
        maxHeightField.label = "图片最大高度";
        maxHeightField.SetValueWithoutNotify(MaxHeight);
        maxHeightField.RegisterValueChangedCallback((evt) =>
        {
            MaxHeight = evt.newValue;
        });
        container.Add(maxHeightField);

        // 创建测试列表
#if UNITY_2021_3_OR_NEWER
        ReorderableListView reorderableListView = new ReorderableListView();
        reorderableListView.SourceData = TestStringValues;
        reorderableListView.HeaderName = "测试列表";
        container.Add(reorderableListView);
#endif

        SchemaInspector inspector = new SchemaInspector(container);
        return inspector;
    }
}
#endif