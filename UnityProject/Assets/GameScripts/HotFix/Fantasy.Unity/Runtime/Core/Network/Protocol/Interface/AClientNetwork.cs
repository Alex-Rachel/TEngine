using System;
using System.IO;
using Fantasy.Serialize;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network.Interface
{
    /// <summary>
    /// 抽象客户端网络基类。
    /// </summary>
    public abstract class AClientNetwork : ANetwork, INetworkChannel
    {
        protected bool IsInit;
        public Session Session { get; protected set; }
        public abstract Session Connect(string remoteAddress, Action onConnectComplete, Action onConnectFail, Action onConnectDisconnect, bool isHttps, int connectTimeout = 5000);
        public abstract void Send(uint rpcId, long address, MemoryStreamBuffer memoryStream, IMessage message, Type messageType);
        public override void Dispose()
        {
            IsInit = false;
            
            if (Session != null)
            {
                if (!Session.IsDisposed)
                {
                    Session.Dispose();
                }
                
                Session = null;
            }
            
            base.Dispose();
        }
    }
}