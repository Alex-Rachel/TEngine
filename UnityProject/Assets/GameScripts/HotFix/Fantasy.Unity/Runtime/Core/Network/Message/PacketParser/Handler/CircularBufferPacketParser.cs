// using System.Runtime.CompilerServices;
// // ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// #pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
//
// namespace Fantasy
// {
//     // 这个对处理分包和粘包逻辑不完整、考虑现在没有任何地方使用了、就先不修改了。
//     // 后面用到了再修改、现在这个只是留做备份、万一以后用到了呢。
//     public abstract class CircularBufferPacketParser : APacketParser
//     {
//         protected uint RpcId;
//         protected long RouteId;
//         protected uint ProtocolCode;
//         protected int MessagePacketLength;
//         protected bool IsUnPackHead = true;
//         protected readonly byte[] MessageHead = new byte[Packet.InnerPacketHeadLength];
//         public abstract bool UnPack(CircularBuffer buffer, out APackInfo packInfo);
//     }
//
// #if FANTASY_NET
//     public sealed class InnerCircularBufferPacketParser : CircularBufferPacketParser, IInnerPacketParser
//     {
//         public override bool UnPack(CircularBuffer buffer, out APackInfo packInfo)
//         {
//             packInfo = null;
//
//             // 在对象没有被释放的情况下循环解析数据
//             while (!IsDisposed)
//             {
//                 if (IsUnPackHead)
//                 {
//                     // 如果缓冲区中的数据长度小于内部消息头的长度，无法解析
//                     if (buffer.Length < Packet.InnerPacketHeadLength)
//                     {
//                         return false;
//                     }
//
//                     // 从缓冲区中读取内部消息头的数据
//                     _ = buffer.Read(MessageHead, 0, Packet.InnerPacketHeadLength);
//                     MessagePacketLength = BitConverter.ToInt32(MessageHead, 0);
//
//                     // 检查消息体长度是否超出限制
//                     if (MessagePacketLength > Packet.PacketBodyMaxLength)
//                     {
//                         throw new ScanException(
//                             $"The received information exceeds the maximum limit = {MessagePacketLength}");
//                     }
//
//                     // 解析协议编号、RPC ID 和 Route ID
//                     ProtocolCode = BitConverter.ToUInt32(MessageHead, Packet.PacketLength);
//                     RpcId = BitConverter.ToUInt32(MessageHead, Packet.InnerPacketRpcIdLocation);
//                     RouteId = BitConverter.ToInt64(MessageHead, Packet.InnerPacketRouteRouteIdLocation);
//                     IsUnPackHead = false;
//                 }
//
//                 try
//                 {
//                     // 如果缓冲区中的数据长度小于消息体的长度，无法解析
//                     if (MessagePacketLength < 0 || buffer.Length < MessagePacketLength)
//                     {
//                         return false;
//                     }
//
//                     IsUnPackHead = true;
//                     packInfo = InnerPackInfo.Create(Network);
//                     var memoryStream = packInfo.RentMemoryStream(MessagePacketLength);
//                     memoryStream.SetLength(MessagePacketLength);
//                     buffer.Read(memoryStream, MessagePacketLength);
//                     packInfo.RpcId = RpcId;
//                     packInfo.RouteId = RouteId;
//                     packInfo.ProtocolCode = ProtocolCode;
//                     packInfo.MessagePacketLength = MessagePacketLength;
//                     return true;
//                 }
//                 catch (Exception e)
//                 {
//                     // 在发生异常时，释放 packInfo 并记录日志
//                     packInfo?.Dispose();
//                     Log.Error(e);
//                     return false;
//                 }
//             }
//
//             return false;
//         }
//
//         public override MemoryStream Pack(ref uint rpcId, ref long routeTypeOpCode, ref long routeId,
//             MemoryStream memoryStream, object message)
//         {
//             return memoryStream == null
//                 ? Pack(ref rpcId, ref routeId, message)
//                 : Pack(ref rpcId, ref routeId, memoryStream);
//         }
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         private unsafe MemoryStream Pack(ref uint rpcId, ref long routeId, MemoryStream memoryStream)
//         {
//             var buffer = memoryStream.GetBuffer();
//
//             fixed (byte* bufferPtr = buffer)
//             {
//                 var rpcIdPtr = bufferPtr + Packet.InnerPacketRpcIdLocation;
//                 var routeIdPtr = bufferPtr + Packet.InnerPacketRouteRouteIdLocation;
//                 *(uint*)rpcIdPtr = rpcId;
//                 *(long*)routeIdPtr = routeId;
//             }
//
//             memoryStream.Seek(0, SeekOrigin.Begin);
//             return memoryStream;
//         }
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         private unsafe MemoryStream Pack(ref uint rpcId, ref long routeId, object message)
//         {
//             var memoryStream = Network.RentMemoryStream();
//             memoryStream.Seek(Packet.InnerPacketHeadLength, SeekOrigin.Begin);
//
//             switch (message)
//             {
//                 case IBsonMessage:
//                 {
//                     MongoHelper.SerializeTo(message, memoryStream);
//                     break;
//                 }
//                 default:
//                 {
//                     ProtoBuffHelper.ToStream(message, memoryStream);
//                     break;
//                 }
//             }
//
//             var opCode = Scene.MessageDispatcherComponent.GetOpCode(message.GetType());
//             var packetBodyCount = (int)(memoryStream.Position - Packet.InnerPacketHeadLength);
//
//             if (packetBodyCount == 0)
//             {
//                 // protoBuf做了一个优化、就是当序列化的对象里的属性和字段都为默认值的时候就不会序列化任何东西。
//                 // 为了TCP的分包和粘包、需要判定下是当前包数据不完整还是本应该如此、所以用-1代表。
//                 packetBodyCount = -1;
//             }
//
//             // 检查消息体长度是否超出限制
//             if (packetBodyCount > Packet.PacketBodyMaxLength)
//             {
//                 throw new Exception($"Message content exceeds {Packet.PacketBodyMaxLength} bytes");
//             }
//
//             var buffer = memoryStream.GetBuffer();
//
//             fixed (byte* bufferPtr = buffer)
//             {
//                 var opCodePtr = bufferPtr + Packet.PacketLength;
//                 var rpcIdPtr = bufferPtr + Packet.InnerPacketRpcIdLocation;
//                 var routeIdPtr = bufferPtr + Packet.InnerPacketRouteRouteIdLocation;
//                 *(int*)bufferPtr = packetBodyCount;
//                 *(uint*)opCodePtr = opCode;
//                 *(uint*)rpcIdPtr = rpcId;
//                 *(long*)routeIdPtr = routeId;
//             }
//
//             memoryStream.Seek(0, SeekOrigin.Begin);
//             return memoryStream;
//         }
//     }
// #endif
// }
//
//
//
//
