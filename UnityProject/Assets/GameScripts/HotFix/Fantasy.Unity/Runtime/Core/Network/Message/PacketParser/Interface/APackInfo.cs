using System;
using System.IO;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Serialize;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.PacketParser.Interface
{
    public abstract class APackInfo : IDisposable
    {
        internal ANetwork Network;
        
        public uint RpcId;
        public long Address;
        public long PackInfoId;
        public bool IsDisposed;
        private uint _protocolCode;
        
        public uint ProtocolCode
        {
            get => _protocolCode;
            set
            {
                _protocolCode = value;
                OpCodeIdStruct = value;
            }
        }
        public Scene Scene { get; protected set; }
        public OpCodeIdStruct OpCodeIdStruct { get; private set; }
        public MemoryStreamBuffer MemoryStream { get; protected set; }
        public abstract object Deserialize(Type messageType);
        public abstract MemoryStreamBuffer RentMemoryStream(MemoryStreamBufferSource memoryStreamBufferSource, int size = 0);
        public virtual void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            
            RpcId = 0;
            Address = 0;
            PackInfoId = 0;
            ProtocolCode = 0;
            _protocolCode = 0;
            Scene = null;
            OpCodeIdStruct = default;
            
            if (MemoryStream != null)
            {
                Network.MemoryStreamBufferPool.ReturnMemoryStream(MemoryStream);
                MemoryStream = null;
            }
            
            IsDisposed = true;
            Network = null;
        }
    }
}