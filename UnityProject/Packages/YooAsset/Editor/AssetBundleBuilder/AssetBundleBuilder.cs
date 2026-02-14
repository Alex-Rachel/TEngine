using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YooAsset.Editor
{
    public class AssetBundleBuilder
    {
        private readonly BuildContext _buildContext = new BuildContext();

        /// <summary>
        /// 构建资源包
        /// </summary>
        public BuildResult Run(BuildParameters buildParameters, List<IBuildTask> buildPipeline, bool enableLog)
        {
            // 检测构建参数是否为空
            if (buildParameters == null)
                throw new Exception($"{nameof(buildParameters)} is null !");

            // 检测构建参数是否为空
            if (buildPipeline.Count == 0)
                throw new Exception($"Build pipeline is empty !");

            // 清空旧数据
            _buildContext.ClearAllContext();

            // 构建参数
            var buildParametersContext = new BuildParametersContext(buildParameters);
            _buildContext.SetContextObject(buildParametersContext);

            // 初始化日志系统
            string logFilePath = $"{buildParametersContext.GetPipelineOutputDirectory()}/buildInfo.log";
            BuildLogger.InitLogger(enableLog, logFilePath);

            // 执行构建流程
            BuildLogger.Log($"Begin to build package : {buildParameters.PackageName} by {buildParameters.BuildPipeline}");
            var buildResult = BuildRunner.Run(buildPipeline, _buildContext);
            if (buildResult.Success)
            {
                buildResult.OutputPackageDirectory = buildParametersContext.GetPackageOutputDirectory();
                BuildLogger.Log("Resource pipeline build success");
            }
            else
            {
                BuildLogger.Error($"{buildParameters.BuildPipeline} build failed !");
                BuildLogger.Error($"An error occurred in build task {buildResult.FailedTask}");
                BuildLogger.Error(buildResult.ErrorInfo);
            }

            // 关闭日志系统
            BuildLogger.Shuntdown();

            return buildResult;
        }
    }
}