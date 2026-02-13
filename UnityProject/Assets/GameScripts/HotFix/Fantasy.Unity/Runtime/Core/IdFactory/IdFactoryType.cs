namespace Fantasy.IdFactory
{
    /// <summary>
    /// ID生成器规则
    /// </summary>
    public enum IdFactoryType
    {
        /// <summary>
        /// 空。
        /// </summary>
        None = 0,
        /// <summary>
        /// 默认生成器
        /// Scene最大为65535个。
        /// </summary>
        Default = 1,
        /// <summary>
        /// ID中包含World,使用这种方式可以不用管理合区的ID重复的问题。
        /// 但Scene的数量也会限制到255个。
        /// </summary>
        World = 2
    }
}