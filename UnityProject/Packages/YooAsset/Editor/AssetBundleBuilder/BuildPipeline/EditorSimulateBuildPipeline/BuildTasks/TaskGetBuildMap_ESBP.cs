
using System;

namespace YooAsset.Editor
{
    public class TaskGetBuildMap_ESBP : TaskGetBuildMap, IBuildTask
    {
        void IBuildTask.Run(BuildContext context)
        {
            var buildParametersContext = context.GetContextObject<BuildParametersContext>();
            var buildMapContext = CreateBuildMap(true, buildParametersContext.Parameters);
            context.SetContextObject(buildMapContext);
        }
    }
}