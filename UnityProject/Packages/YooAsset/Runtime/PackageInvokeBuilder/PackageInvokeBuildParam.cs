
namespace YooAsset
{
    public class PackageInvokeBuildParam
    {
        /// <summary>
        /// 包裹名称
        /// </summary>
        public readonly string PackageName;

        /// <summary>
        /// 构建管线名称
        /// </summary>
        public string BuildPipelineName;

        /// <summary>
        /// 用户数据
        /// </summary>
        public object BuildUserData;

        /// <summary>
        /// 构建类所属程序集名称
        /// </summary>
        public string InvokeAssmeblyName;

        /// <summary>
        /// 构建执行的类名全称
        /// 注意：类名必须包含命名空间！
        /// </summary>
        public string InvokeClassFullName;

        /// <summary>
        /// 构建执行的方法名称
        /// 注意：执行方法必须满足 BindingFlags.Public | BindingFlags.Static
        /// </summary>
        public string InvokeMethodName;

        public PackageInvokeBuildParam(string packageName)
        {
            PackageName = packageName;
        }
    }
}