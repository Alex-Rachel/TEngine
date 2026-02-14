#if FANTASY_NET
using System;
using System.Net;
using Fantasy.Serialize;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network.Interface
{
    public abstract class ANetworkServerChannel : INetworkChannel
    {
        /// <summary>
        /// 获取通道的唯一标识 ID。
        /// </summary>
        public readonly uint Id;
        /// <summary>
        /// 获取通道的远程终端点。
        /// </summary>
        public readonly EndPoint RemoteEndPoint;
        /// <summary>
        /// 获取或设置通道所属的场景。
        /// </summary>
        public Scene Scene { get; protected set; }
        /// <summary>
        /// 获取或设置通道所属的会话。
        /// </summary>
        public Session Session { get; protected set; }
        /// <summary>
        /// 获取通道是否已经被释放。
        /// </summary>
        public bool IsDisposed { get; protected set; }

        protected ANetworkServerChannel(ANetwork network, uint id, EndPoint remoteEndPoint)
        {
            Id = id;
            Scene = network.Scene;
            RemoteEndPoint = remoteEndPoint;
            Session = Session.Create(network.NetworkMessageScheduler, this, network.NetworkTarget);
        }

        public virtual void Dispose()
        {
            IsDisposed = true;
            
            if (!Session.IsDisposed)
            {
                Session.Dispose();
            }
        }
        
        public abstract void Send(uint rpcId, long address, MemoryStreamBuffer memoryStream, IMessage message, Type messageType);
    }
}
#endif