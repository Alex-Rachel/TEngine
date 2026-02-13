namespace Fantasy.Pool
{
    /// <summary>
    /// 实现了这个接口代表支持对象池
    /// </summary>
    public interface IPool
    {
        /// <summary>
        /// 是否从池里创建的
        /// </summary>
        bool IsPool();
        /// <summary>
        /// 设置是否从池里创建的
        /// </summary>
        /// <param name="isPool"></param>
        void SetIsPool(bool isPool);
    }
}