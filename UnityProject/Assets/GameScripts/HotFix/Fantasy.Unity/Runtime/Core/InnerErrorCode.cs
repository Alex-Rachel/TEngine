namespace Fantasy.Network
{
    /// <summary>
    /// 定义 Fantasy 框架中的内部错误代码。
    /// </summary>
    public class InnerErrorCode
    {
        private InnerErrorCode() { }
        /// <summary>
        /// 表示成功的错误码
        /// </summary>
        public const uint Success = 0;
        /// <summary>
        /// 表示 Rpc 消息发送失败的错误代码。
        /// </summary>
        public const uint ErrRpcFail = 100000002; 
        /// <summary>
        /// 表示未找到 Route 消息的错误代码。
        /// </summary>
        public const uint ErrNotFoundRoute = 100000003; 
        /// <summary>
        /// 表示发送 Route 消息超时的错误代码。
        /// </summary>
        public const uint ErrRouteTimeout = 100000004; 
        /// <summary>
        /// 表示未找到实体的错误代码。
        /// </summary>
        public const uint ErrEntityNotFound = 100000008; 
        /// <summary>
        /// 表示传送过程中发生错误的错误代码。
        /// </summary>
        public const uint ErrTransfer = 100000009;
        /// <summary>
        /// 表示连接Roaming时候已经存在同RoamingType的Roaming了。
        /// </summary>
        public const uint ErrLinkRoamingAlreadyExists = 100000009;
        /// <summary>
        /// 表示连接Roaming时候在漫游终端已经存在同Id的终端。
        /// </summary>
        public const uint ErrAddRoamingTerminalAlreadyExists = 100000010;
        /// <summary>
        /// 表示未找到 Roaming 消息的错误代码。
        /// </summary>
        public const uint ErrNotFoundRoaming = 100000011; 
        /// <summary>
        /// 表示发送 Roaming 消息超时的错误代码。
        /// </summary>
        public const uint ErrRoamingTimeout = 100000012;
        /// <summary>
        /// 表示再锁定 Roaming 消息的时候没有找到对应的Session错误代码。
        /// </summary>
        public const uint ErrLockTerminusIdNotFoundSession = 100000013; 
        /// <summary>
        /// 表示再锁定 Roaming 消息的时候没有找到对应的RoamingType错误代码。
        /// </summary>
        public const uint ErrLockTerminusIdNotFoundRoamingType = 100000014; 
        /// <summary>
        /// 表示再解除锁定 Roaming 消息的时候没有找到对应的Session错误代码。
        /// </summary>
        public const uint ErrUnLockTerminusIdNotFoundSession = 100000015; 
        /// <summary>
        /// 表示再解除锁定 Roaming 消息的时候没有找到对应的RoamingType错误代码。
        /// </summary>
        public const uint ErrUnLockTerminusIdNotFoundRoamingType = 100000016; 
        /// <summary>
        /// 表示再传送 Terminus 时对应的错误代码。
        /// </summary>
        public const uint ErrTerminusStartTransfer = 100000017;
        /// <summary>
        /// 表示订阅领域事件时 Address 无效的错误代码。
        /// </summary>
        public const uint ErrSubscribeSphereEventInvalidAddress = 100000018;
        /// <summary>
        /// 表示订阅领域事件时 TypeHashCode 无效的错误代码。
        /// </summary>
        public const uint ErrSubscribeSphereEventInvalidTypeHashCode = 100000019;
        /// <summary>
        /// 表示取消订阅领域事件时 Address 无效的错误代码。
        /// </summary>
        public const uint ErrUnsubscribeSphereEventInvalidAddress = 100000020;
        /// <summary>
        /// 表示取消订阅领域事件时 TypeHashCode 无效的错误代码。
        /// </summary>
        public const uint ErrUnsubscribeSphereEventInvalidTypeHashCode = 100000021;
        /// <summary>
        /// 表示撤销远程订阅者时 Address 无效的错误代码。
        /// </summary>
        public const uint ErrRevokeRemoteSubscriberInvalidAddress = 100000022;
        /// <summary>
        /// 表示撤销远程订阅者时 TypeHashCode 无效的错误代码。
        /// </summary>
        public const uint ErrRevokeRemoteSubscriberInvalidTypeHashCode = 100000023;
        /// <summary>
        /// 表示发布领域事件时 Address 无效的错误代码。
        /// </summary>
        public const uint ErrPublishSphereEventInvalidAddress = 100000024;
        /// <summary>
        /// 表示发布领域事件时 SphereEventArgs 为空的错误代码。
        /// </summary>
        public const uint ErrPublishSphereEventNullEventArgs = 100000025;
        /// <summary>
        /// 表示处理远程发布的领域事件时未找到订阅关系的错误代码。
        /// </summary>
        public const uint ErrHandleRemotePublicationNotSubscribed = 100000026;
        /// <summary>
        /// 表示设置转发会话地址时未找到对应的漫游终端的错误代码。
        /// </summary>
        public const uint ErrSetForwardSessionAddressNotFoundTerminus = 100000027;
        /// <summary>
        /// 表示创建漫游终端时 RoamingId 无效的错误代码。
        /// </summary>
        public const uint ErrCreateTerminusInvalidRoamingId = 100000028;
    }
}