#if FANTASY_NET
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Serialize;

namespace Fantasy.PacketParser
{
    /// <summary>
    /// 打包Outer消息的帮助类
    /// </summary>
    public static class OuterBufferPacketParserHelper
    {
        /// <summary>
        /// 打包一个网络消息
        /// </summary>
        /// <param name="scene">scene</param>
        /// <param name="rpcId">如果是RPC消息需要传递一个rpcId</param>
        /// <param name="message">打包的网络消息</param>
        /// <param name="messageType">打包的网络消息类型</param>
        /// <param name="memoryStreamLength">序列化后流的长度</param>
        /// <returns>打包完成会返回一个MemoryStreamBuffer</returns>
        /// <exception cref="Exception"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryStreamBuffer Pack(Scene scene, uint rpcId, IMessage message, Type messageType, out int memoryStreamLength)
        {
            memoryStreamLength = 0;
            var memoryStream = new MemoryStreamBuffer();
            memoryStream.MemoryStreamBufferSource = MemoryStreamBufferSource.Pack;
            var opCode = message.OpCode();
            OpCodeIdStruct opCodeIdStruct = opCode;
            memoryStream.Seek(Packet.OuterPacketHeadLength, SeekOrigin.Begin);

            if (SerializerManager.TrySerialize(opCodeIdStruct.OpCodeProtocolType, messageType, message, memoryStream, out var error))
            {
                memoryStreamLength = (int)memoryStream.Position;
            }
            else
            {
                Log.Error($"type:{messageType} {error}");
            }
            
            var packetBodyCount = memoryStreamLength - Packet.OuterPacketHeadLength;

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
            ref var bufferRef = ref MemoryMarshal.GetArrayDataReference(buffer);
            
            Unsafe.WriteUnaligned(ref bufferRef, packetBodyCount);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref bufferRef, Packet.PacketLength), opCode);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref bufferRef, Packet.OuterPacketRpcIdLocation), rpcId);
            
            message.Dispose();
            return memoryStream;
        }
    }
}

#endif