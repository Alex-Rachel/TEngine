namespace Fantasy.PacketParser
{
    /// <summary>
    /// 提供关于消息包的常量定义。
    /// </summary>
    public struct Packet
    {
        /// <summary>
        /// 消息体长度在消息头占用的长度
        /// </summary>
        public const int PacketLength = sizeof(int);
        /// <summary>
        /// 协议编号在消息头占用的长度
        /// </summary>
        public const int ProtocolCodeLength = sizeof(uint);
        /// <summary>
        /// Address长度
        /// </summary>
        public const int PacketAddressLength = sizeof(long);
        /// <summary>
        /// RpcId在消息头占用的长度
        /// </summary>
        public const int RpcIdLength = sizeof(uint);
        /// <summary>
        /// OuterRPCId所在的位置
        /// </summary>
        public const int OuterPacketRpcIdLocation = PacketLength + ProtocolCodeLength;
        /// <summary>
        /// InnerRPCId所在的位置
        /// </summary>
        public const int InnerPacketRpcIdLocation = PacketLength + ProtocolCodeLength;
        /// <summary>
        /// Address所在的位置
        /// </summary>
        public const int InnerPacketRouteAddressLocation = PacketLength + ProtocolCodeLength + RpcIdLength;
        /// <summary>
        /// 外网消息头长度（消息体长度在消息头占用的长度 + 协议编号在消息头占用的长度 + RPCId长度 + Address长度）
        /// </summary>
        public const int OuterPacketHeadLength = PacketLength + ProtocolCodeLength + RpcIdLength + PacketAddressLength;
        /// <summary>
        /// 内网消息头长度（消息体长度在消息头占用的长度 + 协议编号在消息头占用的长度 + RPCId长度 + Address长度）
        /// </summary>
        public const int InnerPacketHeadLength = PacketLength + ProtocolCodeLength + RpcIdLength + PacketAddressLength;
    }
}