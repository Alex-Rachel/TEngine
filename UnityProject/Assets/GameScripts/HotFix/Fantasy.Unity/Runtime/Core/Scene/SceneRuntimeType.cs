namespace Fantasy
{
    /// <summary>
    /// 代表一个Scene的类型
    /// </summary>
    public enum SceneRuntimeType
    {
        /// <summary>
        /// 默认
        /// </summary>
        None = 0,
        /// <summary>
        /// 代表一个普通的Scene，一个普通的Scene肯定是是Root的
        /// </summary>
        Root = 1,       
        /// <summary>
        /// 代表一个子场景，子场景肯定是有父场景的
        /// </summary>
        SubScene = 2,
    }
}