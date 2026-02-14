namespace Fantasy.SourceGenerator
{
    /// <summary>
    /// 标记程序集禁用 Source Generator 生成注册代码
    /// 添加到 AssemblyInfo.cs 或任何文件：
    /// [assembly: Fantasy.SourceGenerator.Attributes.DisableSourceGenerator]
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Assembly)]
    public sealed class DisableSourceGeneratorAttribute : System.Attribute
    {

    }
}