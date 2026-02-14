#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;
using Fantasy.Scheduler;
using Fantasy.Timer;

#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network.Route
{
    public class AddressableRouteComponentAwakeSystem : AwakeSystem<AddressableRouteComponent>
    {
        protected override void Awake(AddressableRouteComponent self)
        {
            ((Session)self.Parent).AddressableRouteComponent = self;
            
            var selfScene = self.Scene;
            self.TimerComponent = selfScene.TimerComponent;
            self.NetworkMessagingComponent = selfScene.NetworkMessagingComponent;
            self.MessageDispatcherComponent = selfScene.MessageDispatcherComponent;
            self.AddressableRouteLock =
                selfScene.CoroutineLockComponent.Create(self.GetType().TypeHandle.Value.ToInt64());
        }
    }

    public class AddressableRouteComponentDestroySystem : DestroySystem<AddressableRouteComponent>
    {
        protected override void Destroy(AddressableRouteComponent self)
        {
            self.AddressableRouteLock.Dispose();

            self.AddressableAddress = 0;
            self.AddressableId = 0;
            self.TimerComponent = null;
            self.AddressableRouteLock = null;
            self.NetworkMessagingComponent = null;
            self.MessageDispatcherComponent = null;
        }
    }

    /// <summary>
    /// 可寻址路由消息组件，挂载了这个组件可以接收和发送 Addressable 消息。
    /// </summary>
    public sealed class AddressableRouteComponent : Entity
    {
        public long AddressableId;
        public long AddressableAddress;
        public CoroutineLock AddressableRouteLock;
        public TimerComponent TimerComponent;
        public NetworkMessagingComponent NetworkMessagingComponent;
        public MessageDispatcherComponent MessageDispatcherComponent;

        internal void Send<T>(T message)  where T : IAddressableMessage
        {
            Call<T>(message).Coroutine();
        }

        internal async FTask Send(Type requestType,APackInfo packInfo)
        {
            await Call(requestType, packInfo);
        }

        internal async FTask<IResponse> Call(Type requestType, APackInfo packInfo)
        {
            if (IsDisposed)
            {
                return MessageDispatcherComponent.CreateResponse(packInfo.ProtocolCode, InnerErrorCode.ErrNotFoundRoute);
            }

            packInfo.IsDisposed = true;
            var failCount = 0;
            var runtimeId = RuntimeId;
            IResponse iRouteResponse = null;

            try
            {
                using (await AddressableRouteLock.Wait(AddressableId, "AddressableRouteComponent Call MemoryStream"))
                {
                    while (!IsDisposed)
                    {
                        if (AddressableAddress == 0)
                        {
                            AddressableAddress = await AddressableHelper.GetAddressableAddress(Scene, AddressableId);
                        }

                        if (AddressableAddress == 0)
                        {
                            return MessageDispatcherComponent.CreateResponse(packInfo.ProtocolCode, InnerErrorCode.ErrNotFoundRoute);
                        }

                        iRouteResponse = await NetworkMessagingComponent.Call(AddressableAddress, requestType, packInfo);
                        
                        if (runtimeId != RuntimeId)
                        {
                            iRouteResponse.ErrorCode = InnerErrorCode.ErrRouteTimeout;
                        }

                        switch (iRouteResponse.ErrorCode)
                        {
                            case InnerErrorCode.ErrRouteTimeout:
                            {
                                return iRouteResponse;
                            }
                            case InnerErrorCode.ErrNotFoundRoute:
                            {
                                if (++failCount > 20)
                                {
                                    Log.Error($"AddressableComponent.Call failCount > 20 route send message fail, Address: {Address} AddressableRouteComponent:{Id}");
                                    return iRouteResponse;
                                }

                                await TimerComponent.Net.WaitAsync(100);

                                if (runtimeId != RuntimeId)
                                {
                                    iRouteResponse.ErrorCode = InnerErrorCode.ErrRouteTimeout;
                                }

                                AddressableAddress = 0;
                                continue;
                            }
                            default:
                            {
                                return iRouteResponse; // 对于其他情况，直接返回响应，无需额外处理
                            }
                        }
                    }
                }
            }
            finally
            {
                packInfo.Dispose();
            }


            return iRouteResponse;
        }

        /// <summary>
        /// 调用可寻址路由消息并等待响应。
        /// </summary>
        /// <param name="request">可寻址路由请求。</param>
        private async FTask<IResponse> Call<T>(T request) where T : IAddressableMessage
        {
            if (IsDisposed)
            {
                return MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoute);
            }

            var failCount = 0;
            var runtimeId = RuntimeId;

            using (await AddressableRouteLock.Wait(AddressableId, "AddressableRouteComponent Call"))
            {
                while (true)
                {
                    if (AddressableAddress == 0)
                    {
                        AddressableAddress = await AddressableHelper.GetAddressableAddress(Scene, AddressableId);
                    }

                    if (AddressableAddress == 0)
                    {
                        return MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoute);
                    }

                    var iRouteResponse = await NetworkMessagingComponent.Call(AddressableAddress, request);

                    if (runtimeId != RuntimeId)
                    {
                        iRouteResponse.ErrorCode = InnerErrorCode.ErrRouteTimeout;
                    }

                    switch (iRouteResponse.ErrorCode)
                    {
                        case InnerErrorCode.ErrNotFoundRoute:
                        {
                            if (++failCount > 20)
                            {
                                Log.Error($"AddressableRouteComponent.Call failCount > 20 route send message fail, Address: {Address} AddressableRouteComponent:{Id}");
                                return iRouteResponse;
                            }

                            await TimerComponent.Net.WaitAsync(500);

                            if (runtimeId != RuntimeId)
                            {
                                iRouteResponse.ErrorCode = InnerErrorCode.ErrRouteTimeout;
                            }

                            AddressableAddress = 0;
                            continue;
                        }
                        case InnerErrorCode.ErrRouteTimeout:
                        {
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
    }
}
#endif
