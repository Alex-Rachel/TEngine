// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
using System;
using System.IO;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;
using Fantasy.Serialize;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
namespace Fantasy.PacketParser
{
    public sealed class OuterPackInfo : APackInfo
    {
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            var network = Network;
            base.Dispose();
            network.ReturnOuterPackInfo(this);
        }

        public static OuterPackInfo Create(ANetwork network)
        {
            var outerPackInfo = network.RentOuterPackInfo();
            outerPackInfo.Network = network;
            outerPackInfo.IsDisposed = false;
            outerPackInfo.Scene = network.Scene;
            return outerPackInfo;
        }

        public override MemoryStreamBuffer RentMemoryStream(MemoryStreamBufferSource memoryStreamBufferSource, int size = 0)
        {
            if (MemoryStream == null)
            {
                MemoryStream = Network.MemoryStreamBufferPool.RentMemoryStream(memoryStreamBufferSource, size);
            }

            return MemoryStream;
        }

        /// <summary>
        /// 将消息数据从内存反序列化为指定的消息类型实例。
        /// </summary>
        /// <param name="messageType">目标消息类型。</param>
        /// <returns>反序列化后的消息类型实例。</returns>
        public override object Deserialize(Type messageType)
        {
            if (MemoryStream == null)
            {
                Log.Debug("Deserialize MemoryStream is null");
                return null;
            }

            MemoryStream.Seek(Packet.OuterPacketHeadLength, SeekOrigin.Begin);

            if (SerializerManager.TryDeserialize(
                    OpCodeIdStruct.OpCodeProtocolType, 
                    messageType, MemoryStream,
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