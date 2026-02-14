#if FANTASY_NET
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
using System;
using Fantasy.IdFactory;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;
using Fantasy.Platform.Net;

namespace Fantasy.Scheduler;

internal static class ProcessScheduler
{
    public static void Scheduler(this ProcessSession session, Type messageType, uint rpcId, long address, APackInfo packInfo)
    {
        switch (packInfo.OpCodeIdStruct.Protocol)
        {
            case OpCodeType.InnerResponse:
            case OpCodeType.InnerAddressResponse:
            case OpCodeType.InnerAddressableResponse:
            case OpCodeType.InnerRoamingResponse:
            case OpCodeType.OuterAddressableResponse:
            case OpCodeType.OuterCustomRouteResponse:
            case OpCodeType.OuterRoamingResponse:
            {
                using (packInfo)
                {
                    var sessionScene = session.Scene;
                    var message = packInfo.Deserialize(messageType);
                    sessionScene.ThreadSynchronizationContext.Post(() =>
                    {
                        // 因为有可能是其他Scene线程下发送过来的、所以必须放到当前Scene进程下运行。
                        sessionScene.NetworkMessagingComponent.ResponseHandler(rpcId, (IResponse)message);
                    });
                }
                
                return;
            }
            case OpCodeType.InnerAddressMessage:
            {
                using (packInfo)
                {
                    var sceneId = IdFactoryHelper.RuntimeIdTool.GetSceneId(ref address);
                
                    if (!Process.TryGetScene(sceneId, out var scene))
                    {
                        throw new Exception($"not found scene address:{address}");
                    }
                    
                    var protocolCode = packInfo.ProtocolCode;
                    var message = packInfo.Deserialize(messageType);
        
                    scene.ThreadSynchronizationContext.Post(() =>
                    {
                        var entity = scene.GetEntity(address);
                        var sceneMessageDispatcherComponent = scene.MessageDispatcherComponent;
            
                        if (entity == null || entity.IsDisposed)
                        {
                            return;
                        }

                        sceneMessageDispatcherComponent.AddressMessageHandler(session, messageType, entity, message, rpcId, protocolCode).Coroutine();
                    });
                }
                
                return;
            }
            case OpCodeType.InnerAddressRequest:
            {
                using (packInfo)
                {
                    var sceneId = IdFactoryHelper.RuntimeIdTool.GetSceneId(ref address);
                
                    if (!Process.TryGetScene(sceneId, out var scene))
                    {
                        throw new Exception($"not found scene address:{address}");
                    }
                    
                    var protocolCode = packInfo.ProtocolCode;
                    var message = packInfo.Deserialize(messageType);
        
                    scene.ThreadSynchronizationContext.Post(() =>
                    {
                        var entity = scene.GetEntity(address);
                        var sceneMessageDispatcherComponent = scene.MessageDispatcherComponent;
            
                        if (entity == null || entity.IsDisposed)
                        {
                            sceneMessageDispatcherComponent.FailRouteResponse(session, protocolCode, InnerErrorCode.ErrNotFoundRoute, rpcId);
                            return;
                        }

                        sceneMessageDispatcherComponent.AddressMessageHandler(session, messageType, entity, message, rpcId, protocolCode).Coroutine();
                    });
                }
                
                return;
            }
            case OpCodeType.OuterAddressableMessage:
            case OpCodeType.OuterCustomRouteMessage:
            case OpCodeType.OuterAddressableRequest:
            case OpCodeType.OuterCustomRouteRequest:
            case OpCodeType.OuterRoamingMessage:
            case OpCodeType.OuterRoamingRequest:
            {
                using (packInfo)
                {
                    var sceneId = IdFactoryHelper.RuntimeIdTool.GetSceneId(ref address);

                    if (!Process.TryGetScene(sceneId, out var scene))
                    {
                        throw new NotSupportedException($"not found scene address = {address}");
                    }

                    var protocolCode = packInfo.ProtocolCode;
                    var message = packInfo.Deserialize(messageType);
                    
                    scene.ThreadSynchronizationContext.Post(() =>
                    {
                        var entity = scene.GetEntity(address);

                        if (entity == null || entity.IsDisposed)
                        {
                            scene.MessageDispatcherComponent.FailRouteResponse(session, protocolCode, InnerErrorCode.ErrNotFoundRoute, rpcId);
                            return;
                        }

                        scene.MessageDispatcherComponent.AddressMessageHandler(session, messageType, entity, message, rpcId, protocolCode).Coroutine();
                    });
                }
                return;
            }
            default:
            {
                var packInfoProtocolCode = packInfo.ProtocolCode;
                packInfo.Dispose();
                throw new NotSupportedException($"SessionInnerScheduler Received unsupported message protocolCode:{packInfoProtocolCode} messageType:{messageType}");
            }
        }
    }
    
    public static void Scheduler(this ProcessSession session, Type messageType, uint rpcId, long address, uint protocolCode, object message)
    {
        OpCodeIdStruct opCodeIdStruct = protocolCode;
   
        switch (opCodeIdStruct.Protocol)
        {
            case OpCodeType.InnerResponse:
            case OpCodeType.InnerAddressResponse:
            case OpCodeType.InnerAddressableResponse:
            case OpCodeType.InnerRoamingResponse:
            case OpCodeType.OuterAddressableResponse:
            case OpCodeType.OuterCustomRouteResponse:
            case OpCodeType.OuterRoamingResponse:
            {
                var sessionScene = session.Scene;
                sessionScene.ThreadSynchronizationContext.Post(() =>
                {
                    var iResponse = (IResponse)session.Deserialize(messageType, message, ref opCodeIdStruct);
                    // 因为有可能是其他Scene线程下发送过来的、所以必须放到当前Scene进程下运行。
                    sessionScene.NetworkMessagingComponent.ResponseHandler(rpcId, iResponse);
                });
                
                return;
            }
            case OpCodeType.InnerRoamingMessage:
            case OpCodeType.InnerAddressableMessage:
            case OpCodeType.InnerAddressMessage:
            {
                var sceneId = IdFactoryHelper.RuntimeIdTool.GetSceneId(ref address);
                
                if (!Process.TryGetScene(sceneId, out var scene))
                {
                    throw new Exception($"not found scene address:{address}");
                }
        
                var messageObject = session.Deserialize(messageType, message, ref opCodeIdStruct);
                
                scene.ThreadSynchronizationContext.Post(() =>
                {
                    var entity = scene.GetEntity(address);
                    var sceneMessageDispatcherComponent = scene.MessageDispatcherComponent;
            
                    if (entity == null || entity.IsDisposed)
                    {
                        return;
                    }

                    sceneMessageDispatcherComponent
                        .AddressMessageHandler(session, messageType, entity, messageObject, rpcId, protocolCode)
                        .Coroutine();
                });
                
                return;
            }
            case OpCodeType.InnerAddressableRequest:
            case OpCodeType.InnerRoamingRequest:
            case OpCodeType.InnerAddressRequest:
            {
                var sceneId = IdFactoryHelper.RuntimeIdTool.GetSceneId(ref address);
                
                if (!Process.TryGetScene(sceneId, out var scene))
                {
                    throw new Exception($"not found scene address:{address}");
                }
                
                var messageObject = session.Deserialize(messageType, message, ref opCodeIdStruct);
        
                scene.ThreadSynchronizationContext.Post(() =>
                {
                    var entity = scene.GetEntity(address);
                    var sceneMessageDispatcherComponent = scene.MessageDispatcherComponent;
            
                    if (entity == null || entity.IsDisposed)
                    {
                        sceneMessageDispatcherComponent.FailRouteResponse(session, protocolCode, InnerErrorCode.ErrNotFoundRoute, rpcId);
                        return;
                    }

                    sceneMessageDispatcherComponent
                        .AddressMessageHandler(session, messageType, entity, messageObject, rpcId, protocolCode)
                        .Coroutine();
                });
                
                return;
            }
            case OpCodeType.OuterAddressableMessage:
            case OpCodeType.OuterCustomRouteMessage:
            case OpCodeType.OuterRoamingMessage:
            {
                var sceneId = IdFactoryHelper.RuntimeIdTool.GetSceneId(ref address);
                
                if (!Process.TryGetScene(sceneId, out var scene))
                {
                    Log.Error($"not found scene address:{address}");
                    return;
                }
                
                var messageObject = session.Deserialize(messageType, message, ref opCodeIdStruct);
                
                scene.ThreadSynchronizationContext.Post(() =>
                {
                    var entity = scene.GetEntity(address);
                    
                    switch (entity)
                    {
                        case null:
                        {
                            // 执行到这里是说明Session已经断开了
                            // 因为这里是其他服务器Send到外网的数据、所以不需要给发送端返回就可以
                            return;
                        }
                        case Session gateSession:
                        {
                            // 这里如果是Session只可能是Gate的Session、如果是的话、肯定是转发Address消息
                            gateSession.Send((IMessage)messageObject, messageType, rpcId);
                            return;
                        }
                        default:
                        {
                            scene.MessageDispatcherComponent.AddressMessageHandler(session, messageType, entity,
                                messageObject, rpcId, protocolCode).Coroutine();
                            return;
                        }
                    }
                });
                
                return;
            }
            default:
            {
                throw new NotSupportedException($"SessionInnerScheduler Received unsupported message protocolCode:{protocolCode} messageType:{messageType}");
            }
        }
    }
}
#endif