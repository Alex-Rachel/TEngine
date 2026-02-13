#if FANTASY_NET
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
using System;
using System.Collections.Generic;
using System.IO;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;
using Fantasy.Pool;
using Fantasy.Serialize;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
namespace Fantasy.PacketParser
{
    public sealed class InnerPackInfo : APackInfo
    {
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            var network = Network;
            base.Dispose();
            network.ReturnInnerPackInfo(this);
        }

        public static InnerPackInfo Create(ANetwork network)
        {
            var innerPackInfo = network.RentInnerPackInfo();
            innerPackInfo.Network = network;
            innerPackInfo.IsDisposed = false;
            innerPackInfo.Scene = network.Scene;
            return innerPackInfo;
        }

        public override MemoryStreamBuffer RentMemoryStream(MemoryStreamBufferSource memoryStreamBufferSource, int size = 0)
        {
            return MemoryStream ??= Network.MemoryStreamBufferPool.RentMemoryStream(memoryStreamBufferSource, size);
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
                return Scene.PoolGeneratorComponent.Create(messageType);
            }

            if (SerializerManager.TryDeserialize( OpCodeIdStruct.OpCodeProtocolType, messageType, MemoryStream,
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