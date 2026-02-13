namespace Fantasy.SourceGenerator
{
    /// <summary>
    /// 目标平台类型
    /// </summary>
    public enum PlatformType
    {
        /// <summary>
        /// 自动检测（根据预编译符号 FANTASY_NET/FANTASY_UNITY）
        /// </summary>
        Auto = 0,

        /// <summary>
        /// .NET Server 平台
        /// </summary>
        Server = 1,

        /// <summary>
        /// Unity 客户端平台
        /// </summary>
        Unity = 2
    }

    /// <summary>
    /// 标记程序集的目标平台类型（Server/Unity）
    /// Source Generator 根据此属性或预编译符号（FANTASY_NET/FANTASY_UNITY）生成对应平台的代码
    ///
    /// 使用示例：
    /// [assembly: Fantasy.SourceGenerator.TargetPlatform(PlatformType.Server)]
    /// [assembly: Fantasy.SourceGenerator.TargetPlatform(PlatformType.Unity)]
    /// [assembly: Fantasy.SourceGenerator.TargetPlatform(PlatformType.Auto)] // 或不传参数
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Assembly)]
    public sealed class TargetPlatformAttribute : System.Attribute
    {
        /// <summary>
        /// 目标平台类型
        /// </summary>
        public PlatformType Platform { get; }

        /// <summary>
        /// 默认构造函数，自动检测平台类型（根据预编译符号）
        /// </summary>
        public TargetPlatformAttribute() : this(PlatformType.Auto)
        {
        }

        /// <summary>
        /// 指定目标平台类型
        /// </summary>
        /// <param name="platform">目标平台类型</param>
        public TargetPlatformAttribute(PlatformType platform)
        {
            Platform = platform;
        }
    }
}