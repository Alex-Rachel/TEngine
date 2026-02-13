#if FANTASY_NET
using System;
using System.Collections.Generic;
using System.Linq;
using Fantasy.Entitas;
using System.Runtime.CompilerServices;
using Fantasy.Async;
using Fantasy.Entitas.Interface;
using Fantasy.Helper;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Network.Route;
using Fantasy.PacketParser;
using Fantasy.PacketParser.Interface;
using Fantasy.Timer;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy.Scheduler
{
    public struct NetworkMessageUpdate
    {
        public NetworkMessagingComponent NetworkMessagingComponent;
    }
    
    public class NetworkMessagingComponentAwakeSystem : AwakeSystem<NetworkMessagingComponent>
    {
        protected override void Awake(NetworkMessagingComponent self)
        {
            var selfScene = self.Scene;
            self.TimerComponent = selfScene.TimerComponent;
            self.MessageDispatcherComponent = selfScene.MessageDispatcherComponent;
            self.AddressableRouteMessageLock = selfScene.CoroutineLockComponent.Create(self.GetType().TypeHandle.Value.ToInt64());

            self.TimerId = self.TimerComponent.Net.RepeatedTimer(10000, new NetworkMessageUpdate()
            {
                NetworkMessagingComponent = self
            });
        }
    }
    
    public class NetworkMessagingComponentDestroySystem : DestroySystem<NetworkMessagingComponent>
    {
        protected override void Destroy(NetworkMessagingComponent self)
        {
            if (self.TimerId != 0)
            {
                self.TimerComponent.Net.Remove(ref self.TimerId);
            }
            
            foreach (var (rpcId, messageSender) in self.RequestCallback.ToDictionary())
            {
                self.ReturnMessageSender(rpcId, messageSender);
            }

            self.AddressableRouteMessageLock.Dispose();
            
            self.RequestCallback.Clear();
            self.TimeoutRouteMessageSenders.Clear();
            self.TimerComponent = null;
            self.MessageDispatcherComponent = null;
            self.AddressableRouteMessageLock = null;
        }
    }
    public sealed class NetworkMessagingComponent : Entity
    {
        public long TimerId;
        private uint _rpcId;
        public CoroutineLock AddressableRouteMessageLock;
        public TimerComponent TimerComponent;
        public MessageDispatcherComponent MessageDispatcherComponent;
        public readonly SortedDictionary<uint, MessageSender> RequestCallback = new();
        public readonly Dictionary<uint, MessageSender> TimeoutRouteMessageSenders = new();
        
        public void Send<T>(long address, T message) where T : IAddressMessage
        {
            if (address == 0)
            {
                Log.Error($"Send appId == 0");
                return;
            }

            Scene.GetSession(address).Send(message, 0, address);
        }

        internal void Send(long address, Type messageType, APackInfo packInfo)
        {
            if (address == 0)
            {
                Log.Error($"Send address == 0");
                return;
            }

            Scene.GetSession(address).Send(0, address, messageType, packInfo);
        }
        
        public void Send<T>(ICollection<long> addressCollection, T message) where T : IAddressMessage
        {
            if (addressCollection.Count <= 0)
            {
                Log.Error("Send addressCollection.Count <= 0");
                return;
            }
            
            using var processPackInfo = ProcessPackInfo.Create(Scene, message, addressCollection.Count);
            foreach (var address in addressCollection)
            {
                processPackInfo.Set(0, address);
                Scene.GetSession(address).Send(processPackInfo, 0, address);
            }
        }

        internal async FTask<IResponse> Call(long address, Type requestType, APackInfo packInfo)
        {
            if (address == 0)
            {
                Log.Error($"Call address == 0");
                return null;
            }

            var rpcId = ++_rpcId;
            var session = Scene.GetSession(address);
            var requestCallback = FTask<IResponse>.Create(false);
            RequestCallback.Add(rpcId, MessageSender.Create(rpcId, requestType, requestCallback));
            session.Send(rpcId, address, requestType, packInfo);
            return await requestCallback;
        }

        public async FTask<IResponse> Call<T>(Session session, long address, T request) where T : IAddressMessage
        {
            var rpcId = ++_rpcId;
            var requestCallback = FTask<IResponse>.Create(false);
            RequestCallback.Add(rpcId, MessageSender.Create(rpcId, request, requestCallback));
            session.Send(request, rpcId, address);
            return await requestCallback;
        }

        public async FTask<IResponse> Call<T>(long address, T request) where T : IAddressMessage
        {
            if (address == 0)
            {
                Log.Error($"Call address == 0");
                return null;
            }
            
            var rpcId = ++_rpcId;
            var session = Scene.GetSession(address);
            var requestCallback = FTask<IResponse>.Create(false);
            RequestCallback.Add(rpcId, MessageSender.Create(rpcId, request, requestCallback));
            session.Send<T>(request, rpcId, address);
            return await requestCallback;
        }
        
        public async FTask SendAddressable<T>(long addressableId, T message) where T : IAddressMessage
        {
            await CallAddressable(addressableId, message);
        }
        
        public async FTask<IResponse> CallAddressable<T>(long addressableId, T request) where T : IAddressMessage
        {
            var failCount = 0;
            
            using (await AddressableRouteMessageLock.Wait(addressableId, "CallAddressable"))
            {
                var addressableAddress = await AddressableHelper.GetAddressableAddress(Scene, addressableId);

                while (true)
                {
                    if (addressableAddress == 0)
                    {
                        addressableAddress = await AddressableHelper.GetAddressableAddress(Scene, addressableId);
                    }
                    
                    if (addressableAddress == 0)
                    {
                        return MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoute);
                    }
                    
                    var iRouteResponse = await Call(addressableAddress, request);
                    
                    switch (iRouteResponse.ErrorCode)
                    {
                        case InnerErrorCode.ErrNotFoundRoute:
                        {
                            if (++failCount > 20)
                            {
                                Log.Error($"AddressableComponent.Call failCount > 20 route send message fail, address: {addressableAddress} AddressableMessageComponent:{addressableId}");
                                return iRouteResponse;
                            }
                            
                            await TimerComponent.Net.WaitAsync(500);
                            addressableAddress = 0;
                            continue;
                        }
                        case InnerErrorCode.ErrRouteTimeout:
                        {
                            Log.Error($"CallAddressableRoute ErrorCode.ErrRouteTimeout Error:{iRouteResponse.ErrorCode} Message:{request}");
                            return iRouteResponse;
                        }
                        default:
                        {
                            return iRouteResponse;
                        }
                    }
                }
            }
        }
        
        public void ResponseHandler(uint rpcId, IResponse response)
        {
            if (!RequestCallback.Remove(rpcId, out var routeMessageSender))
            {
                throw new Exception($"not found rpc, response.RpcId:{rpcId} response message: {response.GetType().Name} Process:{Scene.Process.Id} Scene:{Scene.SceneConfigId}");
            }

            ResponseHandler(routeMessageSender, response);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ResponseHandler(MessageSender messageSender, IResponse response)
        {
            if (response.ErrorCode == InnerErrorCode.ErrRouteTimeout)
            {
#if FANTASY_DEVELOP
                messageSender.Tcs.SetException(new Exception($"Rpc error: request, 注意Address消息超时，请注意查看是否死锁或者没有reply: Address: {messageSender.Address} {messageSender.Request.ToJson()}, response: {response}"));
#else
                messageSender.Tcs.SetException(new Exception($"Rpc error: request, 注意Address消息超时，请注意查看是否死锁或者没有reply: Address: {messageSender.Address} {messageSender.Request}, response: {response}"));
#endif
                messageSender.Dispose();
                return;
            }

            messageSender.Tcs.SetResult(response);
            messageSender.Dispose();
        }

        public void ReturnMessageSender(uint rpcId, MessageSender messageSender)
        {
            try
            {
                switch (messageSender.Request)
                {
                    case IAddressMessage:
                    {
                        // IAddressMessage是个特殊的RPC协议、这里不处理就可以了。
                        break;
                    }
                    case IRequest iRequest:
                    {
                        var responseRpcId = messageSender.RpcId;
                        var response = MessageDispatcherComponent.CreateResponse(iRequest.OpCode(), InnerErrorCode.ErrRpcFail);
                        ResponseHandler(responseRpcId, response);
                        Log.Warning($"timeout rpcId:{rpcId} responseRpcId:{responseRpcId} {iRequest.ToJson()}");
                        break;
                    }
                    default:
                    {
                        Log.Error(messageSender.Request != null
                            ? $"Unsupported protocol type {messageSender.Request.GetType()} rpcId:{rpcId} messageSender.Request != null"
                            : $"Unsupported protocol type:{messageSender.MessageType} rpcId:{rpcId}");
                        RequestCallback.Remove(rpcId);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
#endif