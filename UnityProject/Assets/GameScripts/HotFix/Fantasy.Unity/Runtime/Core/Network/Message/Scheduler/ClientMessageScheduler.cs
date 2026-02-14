using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Scheduler
{
#if FANTASY_UNITY || FANTASY_CONSOLE
    /// <summary>
    /// 提供了一个用于客户端网络消息调度和处理的抽象基类。
    /// </summary>
    public sealed class ClientMessageScheduler : ANetworkMessageScheduler
    {
        public ClientMessageScheduler(Scene scene) : base(scene) { }

        public override async FTask Scheduler(Session session, APackInfo packInfo)
        {
            await FTask.CompletedTask;
            switch (packInfo.OpCodeIdStruct.Protocol)
            {
                case OpCodeType.OuterMessage:
                case OpCodeType.OuterRequest:
                case OpCodeType.OuterAddressableMessage:
                case OpCodeType.OuterAddressableRequest:
                case OpCodeType.OuterCustomRouteMessage:
                case OpCodeType.OuterCustomRouteRequest:
                case OpCodeType.OuterRoamingMessage:
                case OpCodeType.OuterRoamingRequest:
                {
                    using (packInfo)
                    {
                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);

                        if (messageType == null)
                        {
                            throw new Exception($"可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }

                        var message = packInfo.Deserialize(messageType);
                        MessageDispatcherComponent.MessageHandler(session, messageType, message, packInfo.RpcId, packInfo.ProtocolCode);
                    }
                    
                    return;
                }
                case OpCodeType.OuterResponse:
                case OpCodeType.OuterPingResponse:
                case OpCodeType.OuterAddressableResponse:
                case OpCodeType.OuterCustomRouteResponse:
                case OpCodeType.OuterRoamingResponse:
                {
                    using (packInfo)
                    {
                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);
                    
                        if (messageType == null)
                        {
                            throw new Exception($"可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                    
                        // 这个一般是客户端Session.Call发送时使用的、目前这个逻辑只有Unity客户端时使用
                    
                        var aResponse = (IResponse)packInfo.Deserialize(messageType);

                        if (!session.RequestCallback.Remove(packInfo.RpcId, out var action))
                        {
                            Log.Error($"not found rpc {packInfo.RpcId}, response message: {aResponse.GetType().Name}");
                            return;
                        }

                        action.SetResult(aResponse);
                    }
                    
                    return;
                }
                default:
                {
                    packInfo.Dispose();
                    throw new NotSupportedException($"Received unsupported message protocolCode:{packInfo.ProtocolCode}");
                }
            }
        }
    }
#endif
#if FANTASY_NET
    internal sealed class ClientMessageScheduler(Scene scene) : ANetworkMessageScheduler(scene)
    {
        public override FTask Scheduler(Session session, APackInfo packInfo)
        {
            throw new NotSupportedException($"ClientMessageScheduler Received unsupported message protocolCode:{packInfo.ProtocolCode}");
        }
    }
#endif
}