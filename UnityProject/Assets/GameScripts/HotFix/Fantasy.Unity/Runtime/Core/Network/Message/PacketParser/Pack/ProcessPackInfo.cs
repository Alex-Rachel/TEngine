#if FANTASY_NET
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;
using Fantasy.Pool;
using Fantasy.Serialize;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
namespace Fantasy.PacketParser
{
    public sealed class ProcessPackInfo : APackInfo
    {
        private int _disposeCount;
        public Type MessageType { get; private set; }
        private static readonly ConcurrentQueue<ProcessPackInfo> Caches = new ConcurrentQueue<ProcessPackInfo>();

        public override void Dispose()
        {
            if (--_disposeCount > 0 || IsDisposed)
            {
                return;
            }

            _disposeCount = 0;
            MessageType = null;
            base.Dispose();

            if (Caches.Count > 2000)
            {
                return;
            }

            Caches.Enqueue(this);
        }

        public static ProcessPackInfo Create<T>(Scene scene, T message, int disposeCount, uint rpcId = 0, long address = 0) where T : IAddressMessage
        {
            if (!Caches.TryDequeue(out var packInfo))
            {
                packInfo = new ProcessPackInfo();
            }

            var type = typeof(T);
            var memoryStreamLength = 0;
            packInfo._disposeCount = disposeCount;
            packInfo.MessageType = type;
            packInfo.IsDisposed = false;
            packInfo.Scene = scene;
            var memoryStream = new MemoryStreamBuffer();
            memoryStream.MemoryStreamBufferSource = MemoryStreamBufferSource.Pack;
            var opCode = message.OpCode();
            OpCodeIdStruct opCodeIdStruct = opCode;
            memoryStream.Seek(Packet.InnerPacketHeadLength, SeekOrigin.Begin);

            if (SerializerManager.TrySerialize(opCodeIdStruct.OpCodeProtocolType, type, message, memoryStream,
                    out var error))
            {
                memoryStreamLength = (int)memoryStream.Position;
            }
            else
            {
                Log.Error(error);
            }
            
            var packetBodyCount = memoryStreamLength - Packet.InnerPacketHeadLength;

            if (packetBodyCount == 0)
            {
                // protoBuf做了一个优化、就是当序列化的对象里的属性和字段都为默认值的时候就不会序列化任何东西。
                // 为了TCP的分包和粘包、需要判定下是当前包数据不完整还是本应该如此、所以用-1代表。
                packetBodyCount = -1;
            }

            if (packetBodyCount > ProgramDefine.MaxMessageSize)
            {
                // 检查消息体长度是否超出限制
                throw new Exception($"Message content exceeds {ProgramDefine.MaxMessageSize} bytes");
            }

            var buffer = memoryStream.GetBuffer();
            var bufferSpan = buffer.AsSpan();
            
            MemoryMarshal.Write(bufferSpan, in packetBodyCount);
            MemoryMarshal.Write(bufferSpan[Packet.PacketLength..], in opCode);
            MemoryMarshal.Write(bufferSpan[Packet.InnerPacketRpcIdLocation..], in rpcId);
            MemoryMarshal.Write(bufferSpan[Packet.InnerPacketRouteAddressLocation..], in address);

            memoryStream.Seek(0, SeekOrigin.Begin);
            packInfo.MemoryStream = memoryStream;
            return packInfo;
        }

        public void Set(uint rpcId, long address)
        {
            var buffer = MemoryStream.GetBuffer();
            var bufferSpan = buffer.AsSpan();
            
            MemoryMarshal.Write(bufferSpan[Packet.InnerPacketRpcIdLocation..], in rpcId);
            MemoryMarshal.Write(bufferSpan[Packet.InnerPacketRouteAddressLocation..], in address);

            MemoryStream.Seek(0, SeekOrigin.Begin);
        }

        public override MemoryStreamBuffer RentMemoryStream(MemoryStreamBufferSource memoryStreamBufferSource, int size = 0)
        {
            throw new NotImplementedException();
        }

        public override object Deserialize(Type messageType)
        {
            if (MemoryStream == null)
            {
                Log.Debug("Deserialize MemoryStream is null");
                return null;
            }
            
            MemoryStream.Seek(Packet.InnerPacketHeadLength, SeekOrigin.Begin);
            
            if (MemoryStream.Length == 0)
            {
                return Network.Scene.PoolGeneratorComponent.Create(messageType);
            }

            if (SerializerManager.TryDeserialize(OpCodeIdStruct.OpCodeProtocolType, messageType, MemoryStream,
                    out var obj, out var error))
            {
                MemoryStream.Seek(0, SeekOrigin.Begin);
                return obj;
            }

            Log.Error(error);
            MemoryStream.Seek(0, SeekOrigin.Begin);
            return null;
        }
    }
}
#endif