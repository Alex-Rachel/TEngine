#if ENABLE_HYBRIDCLR
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
#endif
using System.IO;
#if ENABLE_OBFUZ
using Obfuz.Settings;
using Obfuz4HybridCLR;
#endif
using System.Collections.Generic;
using HybridCLR.Editor.Installer;
using TEngine.Editor;
using UnityEditor;
using UnityEngine;

public static class BuildDLLCommand
{
    private const string EnableHybridClrScriptingDefineSymbol = "ENABLE_HYBRIDCLR";
    private const string EnableObfuzScriptingDefineSymbol = "ENABLE_OBFUZ";

    #region HybridCLR/Define Symbols
    /// <summary>
    /// 禁用HybridCLR宏定义。
    /// </summary>
    [MenuItem("HybridCLR/Define Symbols/Disable HybridCLR", false, 30)]
    public static void DisableHybridCLR()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        HybridCLR.Editor.SettingsUtil.Enable = false;
#if ENABLE_HYBRIDCLR
        UpdateSettingEditor.ForceUpdateAssemblies();
#endif
    }

    /// <summary>
    /// 开启HybridCLR宏定义。
    /// </summary>
    [MenuItem("HybridCLR/Define Symbols/Enable HybridCLR", false, 31)]
    public static void EnableHybridCLR()
    {
        // 先去判断安装了没
        var controller = new InstallerController();
        if (!controller.HasInstalledHybridCLR())
        {
            controller.InstallDefaultHybridCLR();
        }

        if (!HybridCLR.Editor.SettingsUtil.Enable)
        {
            HybridCLR.Editor.SettingsUtil.Enable = true;
#if ENABLE_HYBRIDCLR
            UpdateSettingEditor.ForceUpdateAssemblies();
#endif
        }
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        UpdateSettingEditor.ForceUpdateAssemblies();
    }
    #endregion
    
#if ENABLE_OBFUZ
    #region Obfuz/Define Symbols
    /// <summary>
    /// 禁用Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Disable Obfuz", false, 30)]
    public static void DisableObfuz()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ObfuzSettings.Instance.buildPipelineSettings.enable = false;
    }

    /// <summary>
    /// 开启Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Enable Obfuz", false, 31)]
    public static void EnableObfuz()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ObfuzSettings.Instance.buildPipelineSettings.enable = true;
    }
    #endregion
#endif

    [MenuItem("HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath")]
    public static void BuildAndCopyDlls()
    {
#if ENABLE_HYBRIDCLR
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        CompileDllCommand.CompileDll(target);
        CopyAOTHotUpdateDlls(target);
#endif
    }

    public static void BuildAndCopyDlls(BuildTarget target)
    {
#if ENABLE_HYBRIDCLR
        CompileDllCommand.CompileDll(target);
        CopyAOTHotUpdateDlls(target);
#endif
    }

    public static void CopyAOTHotUpdateDlls(BuildTarget target)
    {
        CopyAOTAssembliesToAssetPath();
        CopyHotUpdateAssembliesToAssetPath();
        
#if ENABLE_HYBRIDCLR && ENABLE_OBFUZ
        CompileDllCommand.CompileDll(target);

        string obfuscatedHotUpdateDllPath = PrebuildCommandExt.GetObfuscatedHotUpdateAssemblyOutputPath(target);
        ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);

        Directory.CreateDirectory(Application.streamingAssetsPath);

        string hotUpdateDllPath = $"{SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)}";
        List<string> obfuscationRelativeAssemblyNames = ObfuzSettings.Instance.assemblySettings.GetObfuscationRelativeAssemblyNames();

        foreach (string assName in SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
        {
            string srcDir = obfuscationRelativeAssemblyNames.Contains(assName) ? obfuscatedHotUpdateDllPath : hotUpdateDllPath;
            string srcFile = $"{srcDir}/{assName}.dll";
            string dstFile = Application.dataPath +"/"+ TEngine.Settings.UpdateSetting.AssemblyTextAssetPath  + $"/{assName}.dll.bytes";
            if (File.Exists(srcFile))
            {
                File.Copy(srcFile, dstFile, true);
                Debug.Log($"[CompileAndObfuscate] Copy {srcFile} to {dstFile}");
            }
        }
#endif
        
        AssetDatabase.Refresh();
    }

    public static void CopyAOTAssembliesToAssetPath()
    {
#if ENABLE_HYBRIDCLR
        var target = EditorUserBuildSettings.activeBuildTarget;
        string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
        string aotAssembliesDstDir = Application.dataPath +"/"+ TEngine.Settings.UpdateSetting.AssemblyTextAssetPath;

        foreach (var dll in TEngine.Settings.UpdateSetting.AOTMetaAssemblies)
        {
            string srcDllPath = $"{aotAssembliesSrcDir}/{dll}";
            if (!System.IO.File.Exists(srcDllPath))
            {
                Debug.LogError($"ab中添加AOT补充元数据dll:{srcDllPath} 时发生错误,文件不存在。裁剪后的AOT dll在BuildPlayer时才能生成，因此需要你先构建一次游戏App后再打包。");
                continue;
            }
            string dllBytesPath = $"{aotAssembliesDstDir}/{dll}.bytes";
            System.IO.File.Copy(srcDllPath, dllBytesPath, true);
            Debug.Log($"[CopyAOTAssembliesToStreamingAssets] copy AOT dll {srcDllPath} -> {dllBytesPath}");
        }
#endif
    }

    public static void CopyHotUpdateAssembliesToAssetPath()
    {
#if ENABLE_HYBRIDCLR
        var target = EditorUserBuildSettings.activeBuildTarget;

        string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
        string hotfixAssembliesDstDir = Application.dataPath +"/"+ TEngine.Settings.UpdateSetting.AssemblyTextAssetPath;
        foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
        {
            string dllPath = $"{hotfixDllSrcDir}/{dll}";
            string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
            System.IO.File.Copy(dllPath, dllBytesPath, true);
            Debug.Log($"[CopyHotUpdateAssembliesToStreamingAssets] copy hotfix dll {dllPath} -> {dllBytesPath}");
        }
#endif
    }
}