
using System.Collections.Generic;
using System;

namespace YooAsset.Editor
{
    public class EditorSimulateBuildPipeline : IBuildPipeline
    {
        public BuildResult Run(BuildParameters buildParameters, bool enableLog)
        {
            if (buildParameters is EditorSimulateBuildParameters)
            {
                AssetBundleBuilder builder = new AssetBundleBuilder();
                return builder.Run(buildParameters, GetDefaultBuildPipeline(), enableLog);
            }
            else
            {
                throw new Exception($"Invalid build parameter type : {buildParameters.GetType().Name}");
            }
        }
        
        /// <summary>
        /// 获取默认的构建流程
        /// </summary>
        private List<IBuildTask> GetDefaultBuildPipeline()
        {
            List<IBuildTask> pipeline = new List<IBuildTask>
                {
                    new TaskPrepare_ESBP(),
                    new TaskGetBuildMap_ESBP(),
                    new TaskUpdateBundleInfo_ESBP(),
                    new TaskCreateManifest_ESBP()
                };
            return pipeline;
        }
    }
}