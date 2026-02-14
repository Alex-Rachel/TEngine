namespace Fantasy.IdFactory
{
    /// <summary>
    /// EntityId生成器接口类
    /// </summary>
    public interface IEntityIdFactory
    {
        /// <summary>
        /// 创建一个新的Id
        /// </summary>
        public long Create { get; }
    }
    
    /// <summary>
    /// RuntimeId生成器接口类
    /// </summary>
    public interface IRuntimeIdFactory
    {
        /// <summary>
        /// 创建一个新的Id
        /// </summary>
        public long Create(bool isPool);
    }
}