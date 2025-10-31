using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using YooAsset.Editor;

public class TextureTools
{
    /// <summary>
    /// POT尺寸检测
    /// </summary>
    public static bool IsPowerOfTwo(Texture tex)
    {
        if (Mathf.IsPowerOfTwo(tex.width) == false)
            return false;

        if (Mathf.IsPowerOfTwo(tex.height) == false)
            return false;

        return true;
    }

    /// <summary>
    /// 获取纹理运行时内存大小
    /// </summary>
    public static long GetStorageMemorySize(Texture tex)
    {
#if UNITY_2022_3_OR_NEWER
        var assembly = typeof(AssetDatabase).Assembly;
        var type = assembly.GetType("UnityEditor.TextureUtil");
        long size = (long)EditorTools.InvokePublicStaticMethod(type, "GetStorageMemorySizeLong", tex);
        return size;
#else
		var assembly = typeof(AssetDatabase).Assembly;
		var type = assembly.GetType("UnityEditor.TextureUtil");
		int size = (int)EditorTools.InvokePublicStaticMethod(type, "GetStorageMemorySize", tex);
		return size;
#endif
    }

    /// <summary>
    /// 获取当前平台纹理的格式
    /// </summary>
    public static TextureFormat GetCurrentPlatformTextureFormat(Texture tex)
    {
        var assembly = typeof(AssetDatabase).Assembly;
        var type = assembly.GetType("UnityEditor.TextureUtil");
        TextureFormat format = (TextureFormat)EditorTools.InvokePublicStaticMethod(type, "GetTextureFormat", tex);
        return format;
    }

    /// <summary>
    /// 获取纹理的导入器
    /// </summary>
    public static TextureImporter GetAssetImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            Debug.LogWarning($"Failed to load TextureImporter : {assetPath}");
        return importer;
    }

    public static TextureImporterPlatformSettings GetPlatformPCSettings(TextureImporter importer)
    {
        TextureImporterPlatformSettings platformSetting = importer.GetPlatformTextureSettings("Standalone");
        return platformSetting;
    }
    public static TextureImporterPlatformSettings GetPlatformIOSSettings(TextureImporter importer)
    {
        TextureImporterPlatformSettings platformSetting = importer.GetPlatformTextureSettings("iPhone");
        return platformSetting;
    }
    public static TextureImporterPlatformSettings GetPlatformAndroidSettings(TextureImporter importer)
    {
        TextureImporterPlatformSettings platformSetting = importer.GetPlatformTextureSettings("Android");
        return platformSetting;
    }

    public static TextureImporterFormat GetPlatformPCFormat(TextureImporter importer)
    {
        TextureImporterPlatformSettings platformSetting = GetPlatformPCSettings(importer);
        var format = platformSetting.format;
        if (format.ToString().StartsWith("Automatic"))
            format = importer.GetAutomaticFormat("Standalone");
        return format;
    }
    public static TextureImporterFormat GetPlatformIOSFormat(TextureImporter importer)
    {
        TextureImporterPlatformSettings platformSetting = GetPlatformIOSSettings(importer);
        var format = platformSetting.format;
        if (format.ToString().StartsWith("Automatic"))
            format = importer.GetAutomaticFormat("iPhone");
        return format;
    }
    public static TextureImporterFormat GetPlatformAndroidFormat(TextureImporter importer)
    {
        TextureImporterPlatformSettings platformSetting = GetPlatformAndroidSettings(importer);
        var format = platformSetting.format;
        if (format.ToString().StartsWith("Automatic"))
            format = importer.GetAutomaticFormat("Android");
        return format;
    }
}