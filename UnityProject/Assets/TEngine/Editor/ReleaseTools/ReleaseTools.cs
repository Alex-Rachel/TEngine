using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;
using BuildResult = UnityEditor.Build.Reporting.BuildResult;

namespace TEngine
{
    /// <summary>
    /// 打包工具类。
    /// <remarks>通过CommandLineReader可以不前台开启Unity实现静默打包以及CLI工作流，详见CommandLineReader.cs example1</remarks>
    /// </summary>
    public static class ReleaseTools
    {
        public static void BuildDll()
        {
            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
            {
                Debug.LogError($"Build Asset Bundle Error！platform is null");
                return;
            }

            BuildTarget target = GetBuildTarget(platform);
            
            // BuildDLLCommand.BuildAndCopyDlls(target);
        }

        public static void BuildAssetBundle()
        {
            string outputRoot = CommandLineReader.GetCustomArgument("outputRoot");
            if (string.IsNullOrEmpty(outputRoot))
            {
                Debug.LogError($"Build Asset Bundle Error！outputRoot is null");
                return;
            }

            string packageVersion = CommandLineReader.GetCustomArgument("packageVersion");
            if (string.IsNullOrEmpty(packageVersion))
            {
                Debug.LogError($"Build Asset Bundle Error！packageVersion is null");
                return;
            }

            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
            {
                Debug.LogError($"Build Asset Bundle Error！platform is null");
                return;
            }

            BuildTarget target = GetBuildTarget(platform);
            BuildInternal(target, outputRoot);
            Debug.LogWarning($"Start BuildPackage BuildTarget:{target} outputPath:{outputRoot}");
        }
        
        [MenuItem("TEngine/Build/一键打包AssetBundle _F8")]
        public static void BuildCurrentPlatformAB()
        {      
            BuildDLLCommand.BuildAndCopyDlls();
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildInternal(target, Application.dataPath + "/../Builds/", packageVersion: GetBuildPackageVersion());
            AssetDatabase.Refresh();
            //复制到打包后的StreamingAssets
            CopyStreamingAssetsFiles();
        }
        /// <summary>
        /// 复制StreamingAssets文件去打包目录
        /// </summary>
        public static void CopyStreamingAssetsFiles()
        {
            if (!Settings.UpdateSetting.IsAutoAssetCopeToBuildAddress())
            {
                Debug.Log("UpdateSetting.IsAutoAssetCopeToBuildAddress关闭,并不会生产到打包目录中");
                return;
            }
            // 获取StreamingAssets路径
            string streamingAssetsPath = Application.streamingAssetsPath;

            // 目标路径，可以是任何你想要的目录
            string targetPath =  Settings.UpdateSetting.GetBuildAddress();

            // 判断目标路径是相对路径还是绝对路径
            if (!System.IO.Path.IsPathRooted(targetPath))
            {
                // 如果是相对路径，结合 StreamingAssets 的路径进行合并
                targetPath = System.IO.Path.Combine(streamingAssetsPath, targetPath);
            }
            
            // 如果目标目录不存在，创建它
            if (!System.IO.Directory.Exists(targetPath))
            {
                Debug.LogError("打包目录不存在,检查UpdateSetting BuildAddress:"+targetPath);
                return;
            }
            // 删除目标路径下的所有文件
            string[] Deletefiles = System.IO.Directory.GetFiles(targetPath);
            foreach (var file in Deletefiles)
            {
                System.IO.File.Delete(file);
                Debug.Log($"删除文件: {file}");
            }

            // 删除目标路径下的所有子目录
            string[] directories = System.IO.Directory.GetDirectories(targetPath);
            foreach (var directory in directories)
            {
                System.IO.Directory.Delete(directory, true); // true 表示递归删除子目录及其中内容
                Debug.Log($"删除目录: {directory}");
            }
            
            // 获取StreamingAssets中的所有文件，排除.meta文件
            string[] files = System.IO.Directory.GetFiles(streamingAssetsPath, "*", System.IO.SearchOption.AllDirectories);

            // 遍历并复制文件到目标目录
            foreach (var file in files)
            {
                // 排除.meta文件
                if (file.EndsWith(".meta"))
                    continue;

                // 获取相对路径，用于在目标目录中创建相同的文件结构
                string relativePath = file.Substring(streamingAssetsPath.Length + 1);
                string destinationFilePath = System.IO.Path.Combine(targetPath, relativePath);

                // 确保目标文件夹存在
                string destinationDir = System.IO.Path.GetDirectoryName(destinationFilePath);
                if (!System.IO.Directory.Exists(destinationDir))
                {
                    System.IO.Directory.CreateDirectory(destinationDir);
                }

                // 复制文件
                System.IO.File.Copy(file, destinationFilePath, true); // true 表示覆盖已存在的文件

                
            }
            Debug.Log($"复制文件完成：{targetPath}");
        }
        private static BuildTarget GetBuildTarget(string platform)
        {
            BuildTarget target = BuildTarget.NoTarget;
            switch (platform)
            {
                case "Android":
                    target = BuildTarget.Android;
                    break;
                case "IOS":
                    target = BuildTarget.iOS;
                    break;
                case "Windows":
                    target = BuildTarget.StandaloneWindows64;
                    break;
                case "MacOS":
                    target = BuildTarget.StandaloneOSX;
                    break;
                case "Linux":
                    target = BuildTarget.StandaloneLinux64;
                    break;
                case "WebGL":
                    target = BuildTarget.WebGL;
                    break;
                case "Switch":
                    target = BuildTarget.Switch;
                    break;
                case "PS4":
                    target = BuildTarget.PS4;
                    break;
                case "PS5":
                    target = BuildTarget.PS5;
                    break;
            }

            return target;
        }

        private static void BuildInternal(BuildTarget buildTarget, string outputRoot, string packageVersion = "1.0",
            EBuildPipeline buildPipeline = EBuildPipeline.ScriptableBuildPipeline)
        {
            Debug.Log($"开始构建 : {buildTarget}");

            IBuildPipeline pipeline = null;
            BuildParameters buildParameters = null;
            
            if (buildPipeline == EBuildPipeline.BuiltinBuildPipeline)
            {
                // 构建参数
                BuiltinBuildParameters builtinBuildParameters = new BuiltinBuildParameters();
                
                // 执行构建
                pipeline = new BuiltinBuildPipeline();
                buildParameters = builtinBuildParameters;
                
                builtinBuildParameters.CompressOption = ECompressOption.LZ4;
            }
            else
            {
                ScriptableBuildParameters scriptableBuildParameters = new ScriptableBuildParameters();
                
                // 执行构建
                pipeline = new ScriptableBuildPipeline();
                buildParameters = scriptableBuildParameters;
                
                scriptableBuildParameters.CompressOption = ECompressOption.LZ4;
                
                scriptableBuildParameters.BuiltinShadersBundleName = GetBuiltinShaderBundleName("DefaultPackage");
                scriptableBuildParameters.ReplaceAssetPathWithAddress = Settings.UpdateSetting.GetReplaceAssetPathWithAddress();
            }

            buildParameters.BuildOutputRoot = outputRoot;//AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
            buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = buildPipeline.ToString();
            buildParameters.BuildTarget = buildTarget;
            buildParameters.BuildBundleType = (int)EBuildBundleType.AssetBundle;
            buildParameters.PackageName = "DefaultPackage";
            buildParameters.PackageVersion = packageVersion;
            buildParameters.VerifyBuildingResult = true;
            // 启用共享资源打包
            buildParameters.EnableSharePackRule = true;
            buildParameters.FileNameStyle =  EFileNameStyle.BundleName_HashName;
            buildParameters.BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll;
            buildParameters.BuildinFileCopyParams = string.Empty;
            buildParameters.EncryptionServices = GetEncryptionFromResourceModuleDriver();
            buildParameters.ClearBuildCacheFiles = false; //不清理构建缓存，启用增量构建，可以提高打包速度！
            buildParameters.UseAssetDependencyDB = true; //使用资源依赖关系数据库，可以提高打包速度！

            var buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
            {
                Debug.Log($"构建成功 : {buildResult.OutputPackageDirectory}");
            }
            else
            {
                Debug.LogError($"构建失败 : {buildResult.ErrorInfo}");
            }
        }
        
        /// <summary>
        /// 内置着色器资源包名称
        /// 注意：和自动收集的着色器资源包名保持一致！
        /// </summary>
        private static string GetBuiltinShaderBundleName(string packageName)
        {
            var uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            var packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        /// <summary>
        /// 根据 ResourceModuleDriver 的 encryptionType 获取对应的加密服务
        /// </summary>
        private static IEncryptionServices GetEncryptionFromResourceModuleDriver()
        {
            // 通过名字查找 GameEntry 预制体
            var guids = AssetDatabase.FindAssets("t:Prefab GameEntry");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[BuildInternal] Failed to find GameEntry.prefab");
                return null;
            }

            var gameEntryPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var gameEntryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gameEntryPath);
            if (gameEntryPrefab == null)
            {
                Debug.LogWarning("[BuildInternal] Failed to load GameEntry.prefab");
                return null;
            }

            var resourceModuleDriver = gameEntryPrefab.GetComponentInChildren<ResourceModuleDriver>();
            if (resourceModuleDriver == null)
            {
                Debug.LogWarning("[BuildInternal] ResourceModuleDriver not found in GameEntry.prefab");
                return null;
            }

            var encryptionType = resourceModuleDriver.EncryptionType;
            Debug.Log($"[BuildInternal] Use EncryptionType from ResourceModuleDriver: {encryptionType}");

            return encryptionType switch
            {
                EncryptionType.FileOffSet => new FileOffsetEncryption(),
                EncryptionType.FileStream => new FileStreamEncryption(),
                _ => null // EncryptionType.None
            };
        }
        
        /// <summary>
        /// 创建加密类实例
        /// </summary>
        private static IEncryptionServices CreateEncryptionInstance(string packageName, EBuildPipeline buildPipeline)
        {
            var encryptionClassName = AssetBundleBuilderSetting.GetPackageEncyptionServicesClassName(packageName, buildPipeline.ToString());
            var encryptionClassTypes = EditorTools.GetAssignableTypes(typeof(IEncryptionServices));
            var classType = encryptionClassTypes.Find(x => x.FullName != null && x.FullName.Equals(encryptionClassName));
            if (classType != null)
            {
                Debug.Log($"Use Encryption {classType}");
                return (IEncryptionServices)Activator.CreateInstance(classType);
            }
            else
            {
                return null;
            }
        }

        [MenuItem("TEngine/Build/一键打包Window", false, 30)]
        public static void AutomationBuild()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            // BuildDLLCommand.BuildAndCopyDlls(target);
            AssetDatabase.Refresh();
            BuildInternal(target, Application.dataPath + "/../Builds/Windows", packageVersion: GetBuildPackageVersion());
            AssetDatabase.Refresh();
            BuildImp(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64, $"{Application.dataPath}/../Build/Windows/Release_Windows.exe");
        }

        // 构建版本相关
        private static string GetBuildPackageVersion()
        {
            int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        [MenuItem("TEngine/Build/一键打包Android", false, 30)]
        public static void AutomationBuildAndroid()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            // BuildDLLCommand.BuildAndCopyDlls(target);
            AssetDatabase.Refresh();
            BuildInternal(target, outputRoot: Application.dataPath + "/../Bundles", packageVersion: GetBuildPackageVersion());
            AssetDatabase.Refresh();
            BuildImp(BuildTargetGroup.Android, BuildTarget.Android, $"{Application.dataPath}/../Build/Android/{GetBuildPackageVersion()}Android.apk");
            // BuildImp(BuildTargetGroup.Android, BuildTarget.Android, $"{Application.dataPath}/../Build/Android/Android.apk");
        }

        [MenuItem("TEngine/Build/一键打包IOS", false, 30)]
        public static void AutomationBuildIOS()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            // BuildDLLCommand.BuildAndCopyDlls(target);
            AssetDatabase.Refresh();
            BuildInternal(target, outputRoot: Application.dataPath + "/../Bundles", packageVersion: GetBuildPackageVersion());
            AssetDatabase.Refresh();
            BuildImp(BuildTargetGroup.iOS, BuildTarget.iOS, $"{Application.dataPath}/../Build/IOS/XCode_Project");
        }

        public static void BuildImp(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget, string locationPathName)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
            AssetDatabase.Refresh();

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Select(scene => scene.path).ToArray(),
                locationPathName = locationPathName,
                targetGroup = buildTargetGroup,
                target = buildTarget,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Build success: {summary.totalSize / 1024 / 1024} MB");
            }
            else
            {
                Debug.Log($"Build Failed" + summary.result);
            }
        }
    }
}