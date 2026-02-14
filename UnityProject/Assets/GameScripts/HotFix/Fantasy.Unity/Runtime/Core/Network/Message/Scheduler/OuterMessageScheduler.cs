using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.PacketParser.Interface;
#if FANTASY_NET
using System.Text;
using Fantasy.Network.Interface;
using Fantasy.Network.Route;
using Fantasy.PacketParser;
using Fantasy.Helper;
using Fantasy.InnerMessage;
using Fantasy.Roaming;
#pragma warning disable CS8604 // Possible null reference argument.
#endif

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy.Scheduler
{
    /// <summary>
    /// 提供了一个机制来调度和处理外部网络消息。
    /// </summary>
#if FANTASY_UNITY
    public sealed class OuterMessageScheduler : ANetworkMessageScheduler
    {
        public OuterMessageScheduler(Scene scene) : base(scene) { }
        
        /// <summary>
        /// 在Unity环境下，处理外部消息的方法。
        /// </summary>
        /// <param name="session">网络会话。</param>
        /// <param name="packInfo">消息封包信息。</param>
        public override FTask Scheduler(Session session, APackInfo packInfo)
        {
            throw new NotSupportedException($"Received unsupported message protocolCode:{packInfo.ProtocolCode}");
        }
    }
#endif
#if FANTASY_NET
    internal sealed class OuterMessageScheduler(Scene scene) : ANetworkMessageScheduler(scene)
    {
        private readonly PingResponse _pingResponse = new PingResponse();
        public override async FTask Scheduler(Session session, APackInfo packInfo)
        {
            if (session.IsDisposed)
            {
                return;
            }
            
            switch (packInfo.OpCodeIdStruct.Protocol)
            {
                case OpCodeType.OuterPingRequest:
                {
                    // 注意心跳目前只有外网才才会有、内网之间不需要心跳。

                    session.LastReceiveTime = TimeHelper.Now;
                    _pingResponse.Now = session.LastReceiveTime;
                
                    using (packInfo)
                    {
                        session.Send(_pingResponse, packInfo.RpcId);
                    }
                
                    return;
                }
                case OpCodeType.OuterMessage:
                case OpCodeType.OuterRequest:
                {
                    var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);
                    
                    try
                    {
                        if (messageType == null)
                        {
                            throw new Exception($"可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                        
                        var message = packInfo.Deserialize(messageType);
                        MessageDispatcherComponent.MessageHandler(session, messageType, message, packInfo.RpcId, packInfo.ProtocolCode);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"ANetworkMessageScheduler OuterResponse error messageProtocolCode:{packInfo.ProtocolCode} messageType:{messageType} SessionId {session.Id} IsDispose {session.IsDisposed} {e}");
                    }
                    finally
                    {
                        packInfo.Dispose();
                    }
                    
                    return;
                }
                case OpCodeType.OuterResponse:
                {
                    using (packInfo)
                    {
                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);
                    
                        if (messageType == null)
                        {
                            throw new Exception($"可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                    
                        NetworkMessagingComponent.ResponseHandler(packInfo.RpcId, (IResponse)packInfo.Deserialize(messageType));
                    }
                    
                    return;
                }
                case OpCodeType.OuterAddressableMessage:
                {
                    var packInfoPackInfoId = packInfo.PackInfoId;
                   
                    try
                    {
                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);

                        if (messageType == null)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }

                        var addressableRouteComponent = session.AddressableRouteComponent;

                        if (addressableRouteComponent == null)
                        {
                            throw new Exception("OuterMessageScheduler error session does not have an AddressableRouteComponent component");
                        }

                        await addressableRouteComponent.Send(messageType, packInfo);
                    }
                    finally
                    {
                        if (packInfo.PackInfoId == packInfoPackInfoId)
                        {
                            packInfo.Dispose();
                        }
                    }

                    return;
                }
                case OpCodeType.OuterAddressableRequest:
                {
                    var packInfoPackInfoId = packInfo.PackInfoId;
                    
                    try
                    {
                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);

                        if (messageType == null)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }

                        var addressableRouteComponent = session.AddressableRouteComponent;

                        if (addressableRouteComponent == null)
                        {
                            throw new Exception("OuterMessageScheduler error session does not have an AddressableRouteComponent component");
                        }
                    
                        var rpcId = packInfo.RpcId;
                        var runtimeId = session.RuntimeId;
                        var response = await addressableRouteComponent.Call(messageType, packInfo);
                        // session可能已经断开了，所以这里需要判断
                        if (session.RuntimeId == runtimeId)
                        {
                            var responseType = MessageDispatcherComponent.GetOpCodeType(response.OpCode());
                            session.Send(response, responseType, rpcId);
                        }
                    }
                    finally
                    {
                        if (packInfo.PackInfoId == packInfoPackInfoId)
                        {
                            packInfo.Dispose();
                        }
                    }
                    
                    return;
                }
                case OpCodeType.OuterCustomRouteMessage:
                {
                    var packInfoProtocolCode = packInfo.ProtocolCode;
                    var packInfoPackInfoId = packInfo.PackInfoId;

                    try
                    {
                        var routeType = MessageDispatcherComponent.GetCustomRouteType(packInfoProtocolCode);
                        
                        if (!routeType.HasValue)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }

                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);

                        if (messageType == null)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                    
                        var routeComponent = session.RouteComponent;

                        if (routeComponent == null)
                        {
                            throw new Exception($"OuterMessageScheduler CustomRouteType session does not have an routeComponent component messageType:{messageType.FullName} ProtocolCode：{packInfo.ProtocolCode}");
                        }

                        if (!routeComponent.TryGetAddress(routeType.Value, out var address))
                        {
                            throw new Exception($"OuterMessageScheduler RouteComponent cannot find Address with RouteType {routeType}");
                        }
                    
                        NetworkMessagingComponent.Send(address, messageType, packInfo);
                    }
                    finally
                    {
                        if (packInfo.PackInfoId == packInfoPackInfoId)
                        {
                            packInfo.Dispose();
                        }
                    }
                    
                    return;
                }
                case OpCodeType.OuterCustomRouteRequest:
                {
                    var packInfoProtocolCode = packInfo.ProtocolCode;
                    var packInfoPackInfoId = packInfo.PackInfoId;

                    try
                    {
                        var routeType = MessageDispatcherComponent.GetCustomRouteType(packInfoProtocolCode);
                        
                        if (!routeType.HasValue)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                        
                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);

                        if (messageType == null)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                    
                        var routeComponent = session.RouteComponent;

                        if (routeComponent == null)
                        {
                            throw new Exception("OuterMessageScheduler CustomRouteType session does not have an routeComponent component");
                        }

                        if (!routeComponent.TryGetAddress(routeType.Value, out var address))
                        {
                            throw new Exception($"OuterMessageScheduler RouteComponent cannot find Address with RouteType {routeType}");
                        }
                    
                        var rpcId = packInfo.RpcId;
                        var runtimeId = session.RuntimeId;
                        var response = await NetworkMessagingComponent.Call(address, messageType, packInfo);
                        // session可能已经断开了，所以这里需要判断
                        if (session.RuntimeId == runtimeId)
                        {
                            var responseType = MessageDispatcherComponent.GetOpCodeType(response.OpCode());
                            session.Send(response, responseType, rpcId);
                        }
                    }
                    finally
                    {
                        if (packInfo.PackInfoId == packInfoPackInfoId)
                        {
                            packInfo.Dispose();
                        }
                    }

                    return;
                }
                case OpCodeType.OuterRoamingMessage:
                {
                    var packInfoProtocolCode = packInfo.ProtocolCode;
                    var packInfoPackInfoId = packInfo.PackInfoId;

                    try
                    {
                        var routeType = MessageDispatcherComponent.GetCustomRouteType(packInfoProtocolCode);
                        
                        if (!routeType.HasValue)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }

                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);

                        if (messageType == null)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                    
                        var sessionRoamingComponent = session.SessionRoamingComponent;

                        if (sessionRoamingComponent == null)
                        {
                            throw new Exception($"OuterMessageScheduler Roaming session does not have an sessionRoamingComponent component messageType:{messageType.FullName} ProtocolCode：{packInfo.ProtocolCode}");
                        }

                        await sessionRoamingComponent.Send(routeType.Value, messageType, packInfo);
                    }
                    finally
                    {
                        if (packInfo.PackInfoId == packInfoPackInfoId)
                        {
                            packInfo.Dispose();
                        }
                    }
                    
                    return;
                }
                case OpCodeType.OuterRoamingRequest:
                {
                    var packInfoProtocolCode = packInfo.ProtocolCode;
                    var packInfoPackInfoId = packInfo.PackInfoId;

                    try
                    {
                        var routeType = MessageDispatcherComponent.GetCustomRouteType(packInfoProtocolCode);
                        
                        if (!routeType.HasValue)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                        
                        var messageType = MessageDispatcherComponent.GetOpCodeType(packInfo.ProtocolCode);

                        if (messageType == null)
                        {
                            throw new Exception($"OuterMessageScheduler error 可能遭受到恶意发包或没有协议定义ProtocolCode ProtocolCode：{packInfo.ProtocolCode}");
                        }
                    
                        var sessionRoamingComponent = session.SessionRoamingComponent;

                        if (sessionRoamingComponent == null)
                        {
                            throw new Exception("OuterMessageScheduler Roaming session does not have an sessionRoamingComponent component");
                        }
                    
                        var rpcId = packInfo.RpcId;
                        var runtimeId = session.RuntimeId;
                        var response = await sessionRoamingComponent.Call(routeType.Value, messageType, packInfo);
                        // session可能已经断开了，所以这里需要判断
                        if (session.RuntimeId == runtimeId)
                        {
                            var responseType = MessageDispatcherComponent.GetOpCodeType(response.OpCode());
                            session.Send(response, responseType, rpcId);
                        }
                    }
                    finally
                    {
                        if (packInfo.PackInfoId == packInfoPackInfoId)
                        {
                            packInfo.Dispose();
                        }
                    }

                    return;
                }
                default:
                {
                    var ipAddress = session.IsDisposed ? "null" : session.RemoteEndPoint.ToString();
                    packInfo.Dispose();
                    throw new NotSupportedException($"OuterMessageScheduler Received unsupported message protocolCode:{packInfo.ProtocolCode}\n1、请检查该协议所在的程序集是否在框架初始化的时候添加到框架中。\n2、如果看到这个消息表示你有可能用的老版本的导出工具，请更换为最新的导出工具。\n IP地址:{ipAddress}");
                }
            }
        }
    }
#endif
}