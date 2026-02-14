using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace YooAsset.Editor
{
    public class TaskPrepare_RFBP : IBuildTask
    {
        void IBuildTask.Run(BuildContext context)
        {
            var buildParametersContext = context.GetContextObject<BuildParametersContext>();
            var buildParameters = buildParametersContext.Parameters;

            // 检测基础构建参数
            buildParametersContext.CheckBuildParameters();

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
        }
    }
}