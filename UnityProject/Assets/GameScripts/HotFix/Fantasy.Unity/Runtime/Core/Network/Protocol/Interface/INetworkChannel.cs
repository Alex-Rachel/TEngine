using System;
using System.IO;
using Fantasy.Serialize;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy.Network.Interface
{
    public interface INetworkChannel : IDisposable
    {
        public Session Session { get;}
        public bool IsDisposed { get;}
        public void Send(uint rpcId, long address, MemoryStreamBuffer memoryStream, IMessage message, Type messageType);
    }
}