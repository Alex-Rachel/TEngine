using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace YooAsset.Editor
{
    public class TaskPrepare_BBP : IBuildTask
    {
        void IBuildTask.Run(BuildContext context)
        {
            var buildParametersContext = context.GetContextObject<BuildParametersContext>();
            var buildParameters = buildParametersContext.Parameters;
            var builtinBuildParameters = buildParameters as BuiltinBuildParameters;

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

            // 检测Unity版本
#if UNITY_2021_3_OR_NEWER
            string warning = BuildLogger.GetErrorMessage(ErrorCode.RecommendScriptBuildPipeline, $"Starting with UnityEngine2021, recommend use script build pipeline (SBP) !");
            BuildLogger.Warning(warning);
#endif
        }
    }
}