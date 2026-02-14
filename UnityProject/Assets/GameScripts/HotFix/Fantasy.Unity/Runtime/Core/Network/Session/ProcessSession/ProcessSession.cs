#if FANTASY_NET
using System;
using System.Collections.Generic;
using System.IO;
using Fantasy.Async;
using Fantasy.Network.Interface;
using Fantasy.PacketParser;
using Fantasy.PacketParser.Interface;
using Fantasy.Pool;
using Fantasy.Scheduler;
using Fantasy.Serialize;
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
namespace Fantasy.Network;

/// <summary>
/// 网络服务器内部会话。
/// </summary>
public sealed class ProcessSession : Session
{
    private readonly MemoryStreamBufferPool _memoryStreamBufferPool = new MemoryStreamBufferPool();

    public override void Send(IMessage message, Type messageType, uint rpcId = 0, long address = 0)
    {
        if (IsDisposed)
        {
            return;
        }
        
        this.Scheduler(messageType, rpcId, address, message.OpCode(), message);
    }
    
    /// <summary>
    /// 发送消息到服务器内部。
    /// </summary>
    /// <param name="message">要发送的消息。</param>
    /// <param name="rpcId">RPC 标识符。</param>
    /// <param name="address">Address。</param>
    public override void Send<T>(T message, uint rpcId = 0, long address = 0)
    {
        if (IsDisposed)
        {
            return;
        }

        this.Scheduler(typeof(T), rpcId, address, message.OpCode(), message);
    }

    public override void Send(uint rpcId, long address, Type messageType, APackInfo packInfo)
    {
        if (IsDisposed)
        {
            return;
        }
        
        this.Scheduler(messageType, rpcId, address, packInfo);
    }

    public override void Send(ProcessPackInfo packInfo, uint rpcId = 0, long address = 0)
    {
        this.Scheduler(packInfo.MessageType, rpcId, address, packInfo);
    }

    public override void Send(MemoryStreamBuffer memoryStream, uint rpcId = 0, long address = 0)
    {
        throw new Exception("The use of this method is not supported");
    }

    public override FTask<IResponse> Call<T>(T request, long address = 0) 
    {
        throw new Exception("The use of this method is not supported");
    }
    
    public object Deserialize(Type messageType, object message, ref OpCodeIdStruct opCodeIdStruct)
    {
        var memoryStream = _memoryStreamBufferPool.RentMemoryStream(MemoryStreamBufferSource.None);

        try
        {
            if (!SerializerManager.TrySerialize(opCodeIdStruct.OpCodeProtocolType, messageType, message, memoryStream,
                    out var error))
            {
                throw new NotSupportedException(error);
            }

            if (memoryStream.Position == 0)
            {
                return Scene.PoolGeneratorComponent.Create(messageType);
            }

            memoryStream.SetLength(memoryStream.Position);
            memoryStream.Seek(0, SeekOrigin.Begin);

            return SerializerManager.TryDeserialize(opCodeIdStruct.OpCodeProtocolType, messageType, memoryStream,
                out var obj, out error) ? obj : throw new NotSupportedException(error);
        }
        finally
        {
            ((IMessage)message).Dispose();
            _memoryStreamBufferPool.ReturnMemoryStream(memoryStream);
        }
    }
}
#endif