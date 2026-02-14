
namespace YooAsset.Editor
{
    public class TaskEncryption_SBP : TaskEncryption, IBuildTask
    {
        void IBuildTask.Run(BuildContext context)
        {
            var buildParameters = context.GetContextObject<BuildParametersContext>();
            var buildMapContext = context.GetContextObject<BuildMapContext>();
            EncryptingBundleFiles(buildParameters, buildMapContext);
        }
    }
}