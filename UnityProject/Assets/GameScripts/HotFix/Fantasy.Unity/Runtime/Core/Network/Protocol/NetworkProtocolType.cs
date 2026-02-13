namespace Fantasy.Network
{
    /// <summary>
    /// 网络服务器类型
    /// </summary>
    public enum NetworkType
    {
        /// <summary>
        /// 默认
        /// </summary>
        None = 0,
        /// <summary>
        /// 客户端网络
        /// </summary>
        Client = 1,
#if FANTASY_NET
        /// <summary>
        /// 服务器网络
        /// </summary>
        Server = 2
#endif
    }
    /// <summary>
    /// 网络服务的目标
    /// </summary>
    public enum NetworkTarget
    {
        /// <summary>
        /// 默认
        /// </summary>
        None = 0,
        /// <summary>
        /// 对外
        /// </summary>
        Outer = 1,
#if FANTASY_NET
        /// <summary>
        /// 对内
        /// </summary>
        Inner = 2
#endif
    }
    /// <summary>
    /// 支持的网络协议
    /// </summary>
    public enum NetworkProtocolType
    {
        /// <summary>
        /// 默认
        /// </summary>
        None = 0,
        /// <summary>
        /// KCP
        /// </summary>
        KCP = 1,
        /// <summary>
        /// TCP
        /// </summary>
        TCP = 2,
        /// <summary>
        /// WebSocket
        /// </summary>
        WebSocket = 3,
        /// <summary>
        /// HTTP
        /// </summary>
        HTTP = 4,
    }
}