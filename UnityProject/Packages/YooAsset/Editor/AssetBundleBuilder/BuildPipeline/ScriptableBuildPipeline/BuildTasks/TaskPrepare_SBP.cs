using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace YooAsset.Editor
{
    public class TaskPrepare_SBP : IBuildTask
    {
        void IBuildTask.Run(BuildContext context)
        {
            var buildParametersContext = context.GetContextObject<BuildParametersContext>();
            var buildParameters = buildParametersContext.Parameters as ScriptableBuildParameters;

            // 检测基础构建参数
            buildParametersContext.CheckBuildParameters();

            // 检测是否有未保存场景
            if (EditorTools.HasDirtyScenes())
            {
                string message = BuildLogger.GetErrorMessage(ErrorCode.FoundUnsavedScene, "Found unsaved scene !");
                throw new Exception(message);
            }

            // 删除包裹目录
            if (buildParameters.ClearBuildCacheFiles)
            {
                // Deletes the build cache directory.
                UnityEditor.Build.Pipeline.Utilities.BuildCache.PurgeCache(false);

                string packageRootDirectory = buildParameters.GetPackageRootDirectory();
                if (EditorTools.DeleteDirectory(packageRootDirectory))
                {
                    BuildLogger.Log($"Delete package root directory: {packageRootDirectory}");
                }
            }

            // 检测包裹输出目录是否存在
            string packageOutputDirectory = buildParameters.GetPackageOutputDirectory();
            if (Directory.Exists(packageOutputDirectory))
            {
                string message = BuildLogger.GetErrorMessage(ErrorCode.PackageOutputDirectoryExists, $"Package outout directory exists: {packageOutputDirectory}");
                throw new Exception(message);
            }

            // 如果输出目录不存在
            string pipelineOutputDirectory = buildParameters.GetPipelineOutputDirectory();
            if (EditorTools.CreateDirectory(pipelineOutputDirectory))
            {
                BuildLogger.Log($"Create pipeline output directory: {pipelineOutputDirectory}");
            }

            // 检测内置着色器资源包名称
            if (string.IsNullOrEmpty(buildParameters.BuiltinShadersBundleName))
            {
                string warning = BuildLogger.GetErrorMessage(ErrorCode.BuiltinShadersBundleNameIsNull, $"Builtin shaders bundle name is null. It will cause resource redundancy !");
                BuildLogger.Warning(warning);
            }
        }
    }
}