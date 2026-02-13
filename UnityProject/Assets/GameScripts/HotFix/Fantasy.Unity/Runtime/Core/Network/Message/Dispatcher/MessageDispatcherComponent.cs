using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.DataStructure.Dictionary;
using Fantasy.Entitas;
using Fantasy.InnerMessage;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable InvertIf
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
// ReSharper disable CheckNamespace
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.Network.Interface
{
    public sealed class MessageDispatcherComponent : Entity, IAssemblyLifecycle
    {
        private CoroutineLock _receiveRouteMessageLock;
        private UInt32FrozenDictionary<Type> _opCodeDictionary;
        private UInt32FrozenDictionary<Type> _responseTypeDictionary;
        private UInt32FrozenDictionary<Func<Session, uint, object, FTask>> _messageHandlerDictionary;
        
        private readonly UInt32MergerFrozenDictionary<Type> _opCodeMerger = new();
        private readonly UInt32MergerFrozenDictionary<Type> _responseTypeMerger = new();
        private readonly UInt32MergerFrozenDictionary<Func<Session, uint, object, FTask>> _messageHandlerMerger = new();
#if FANTASY_NET
        private UInt32FrozenDictionary<int> _customRouteDictionary;
        private UInt32FrozenDictionary<Func<Session, Entity, uint, object, FTask>> _routeMessageHandlerDictionary;
        private readonly UInt32MergerFrozenDictionary<int> _customRouteMerger = new();
        private readonly UInt32MergerFrozenDictionary<Func<Session, Entity, uint, object, FTask>> _routeMessageHandlerMerger = new();
#endif

#if FANTASY_UNITY
        /*
         * 方便客户端通过 GameClient RegisterMsgHandler 注册的回调
         * 客户端可以通过 GameClient 和协议号直接监听服务器数据的下发
         */

        public readonly Dictionary<uint, List<Action<IMessage>>> MsgHandles = new Dictionary<uint, List<Action<IMessage>>>();

        public void RegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (!MsgHandles.ContainsKey(protocolCode))
            {
                MsgHandles[protocolCode] = new List<Action<IMessage>>();
            }

            MsgHandles[protocolCode].Add(ctx);
        }

        public void UnRegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (MsgHandles.TryGetValue(protocolCode, out var handle))
            {
                handle.Remove(ctx);
            }
        }
#endif

        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            
            _receiveRouteMessageLock.Dispose();
            _opCodeDictionary = null;
            _responseTypeDictionary = null;
            _messageHandlerDictionary = null;
#if FANTASY_NET
            _customRouteDictionary = null;
            _routeMessageHandlerDictionary = null;
#endif
            AssemblyLifecycle.Remove(this);
            base.Dispose();
        }

        #region AssemblyManifest

        internal async FTask<MessageDispatcherComponent> Initialize()
        {
            _receiveRouteMessageLock = Scene.CoroutineLockComponent.Create(GetType().TypeHandle.Value.ToInt64());
            await AssemblyLifecycle.Add(this);
            return this;
        }

        public async FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            var tcs = FTask.Create(false);
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;
            
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                // 注册Handler
                var messageHandlerResolver = assemblyManifest.MessageHandlerResolver;
                _messageHandlerMerger.Add(
                    assemblyManifestId,
                    messageHandlerResolver.MessageHandlerOpCodes(),
                    messageHandlerResolver.MessageHandlers());
                _messageHandlerDictionary = _messageHandlerMerger.GetFrozenDictionary();
                // 注册OpCode
                var opCodeResolver = assemblyManifest.OpCodeRegistrar;
                _opCodeMerger.Add(
                    assemblyManifestId,
                    opCodeResolver.TypeOpCodes(),
                    opCodeResolver.OpCodeTypes());
                _opCodeDictionary = _opCodeMerger.GetFrozenDictionary();
#if FANTASY_NET
                _customRouteMerger.Add(
                    assemblyManifestId,
                    opCodeResolver.CustomRouteTypeOpCodes(),
                    opCodeResolver.CustomRouteTypes());
                _customRouteDictionary = _customRouteMerger.GetFrozenDictionary();
                
                _routeMessageHandlerMerger.Add(
                    assemblyManifestId,
                    messageHandlerResolver.AddressMessageHandlerOpCodes(),
                    messageHandlerResolver.AddressMessageHandler());
                _routeMessageHandlerDictionary = _routeMessageHandlerMerger.GetFrozenDictionary();
#endif
                // 注册ResponseType
                var responseTypeRegistrar = assemblyManifest.ResponseTypeRegistrar;
                _responseTypeMerger.Add(
                    assemblyManifest.AssemblyManifestId,
                    responseTypeRegistrar.OpCodes(),
                    responseTypeRegistrar.Types());
                _responseTypeDictionary = _responseTypeMerger.GetFrozenDictionary();
                
                tcs.SetResult();
            });
            await tcs;
        }

        public async FTask OnUnload(AssemblyManifest assemblyManifest)
        {
            var tcs = FTask.Create(false);
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;
            
            Scene?.ThreadSynchronizationContext.Post(() =>
            {
                _opCodeMerger.Remove(assemblyManifestId);
                _responseTypeMerger.Remove(assemblyManifestId);
                _messageHandlerMerger.Remove(assemblyManifestId);

                _opCodeDictionary = _opCodeMerger.GetFrozenDictionary();
                _responseTypeDictionary = _responseTypeMerger.GetFrozenDictionary();
                _messageHandlerDictionary = _messageHandlerMerger.GetFrozenDictionary();
#if FANTASY_NET
                _customRouteMerger.Remove(assemblyManifestId);
                _routeMessageHandlerMerger.Remove(assemblyManifestId);
            
                _customRouteDictionary = _customRouteMerger.GetFrozenDictionary();
                _routeMessageHandlerDictionary = _routeMessageHandlerMerger.GetFrozenDictionary();
#endif
                tcs.SetResult();
            });
            await tcs;
        }

        #endregion

        internal void MessageHandler(Session session, Type type, object message, uint rpcId, uint protocolCode)
        {
#if FANTASY_UNITY
            // 先触发通过 GameClient RegisterMsgHandler 注册的回调
            // 方便客户端通过 GameClient 和协议号直接监听服务器数据的下发
            if (MsgHandles.TryGetValue(protocolCode, out var handlers))
            {
                for (int i = handlers.Count - 1; i >= 0; i--)
                {
                    var handler = handlers[i];
                    try
                    {
                        handler.Invoke((IMessage)message);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"MsgHandler error, protocolCode:{protocolCode}, error:{e}");
                    }
                }
                return;
            }
#endif
            if (!_messageHandlerDictionary.TryGetValue(protocolCode, out var messageHandler))
            {
                Log.Warning($"Scene:{session.Scene.Id} Found Unhandled Message: {type.FullName}");
                return;
            }

            messageHandler(session, rpcId, message).Coroutine();
        }
#if FANTASY_NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async FTask<bool> InnerAddressMessageHandler(Session session, Entity entity, uint rpcId, uint protocolCode, object message)
        {
            if (!_routeMessageHandlerDictionary.TryGetValue(protocolCode, out var messageHandler))
            {
                return false;
            }

            await messageHandler(session, entity, rpcId, message);
            return true;
        }

        internal async FTask AddressMessageHandler(Session session, Type type, Entity entity, object message, uint rpcId, uint protocolCode)
        {
            if (entity is Scene)
            {
                // 如果是Scene的话、就不要加锁了、如果加锁很一不小心就可能会造成死锁
                if (!await InnerAddressMessageHandler(session, entity, rpcId, protocolCode, message))
                {
                    Log.Warning($"Scene:{session.Scene.Id} Found Unhandled AddressMessage: {type}");
                }
                return;
            }
            
            // 使用协程锁来确保消息的顺序
            var runtimeId = entity.RuntimeId;
            var sessionRuntimeId = session.RuntimeId;
            using (await _receiveRouteMessageLock.Wait(runtimeId))
            {
                if (sessionRuntimeId != session.RuntimeId)
                {
                    return;
                }

                if (runtimeId != entity.RuntimeId)
                {
                    if (message is IAddressRequest request)
                    {
                        FailRouteResponse(session, request.OpCode(), InnerErrorCode.ErrEntityNotFound, rpcId);
                    }
                    
                    return;
                }

                if (!await InnerAddressMessageHandler(session, entity, rpcId, protocolCode, message))
                {
                    Log.Warning($"Scene:{session.Scene.Id} Found Unhandled AddressMessage: {message.GetType()}");
                }
            }
        }
#endif
        #region Response

        internal void FailRouteResponse(Session session, uint requestOpCode, uint error, uint rpcId)
        {
            session.Send(CreateRouteResponse(requestOpCode, error, out var responseType), responseType, rpcId);
        }

        internal IResponse CreateResponse(uint requestOpCode, uint error)
        {
            if (_responseTypeDictionary.TryGetValue(requestOpCode, out var responseType))
            {
                var findResponse = (IResponse)Activator.CreateInstance(responseType);
                findResponse.ErrorCode = error;
                return findResponse;
            }

            var response = new Response();
            response.ErrorCode = error;
            return response;
        }

        private IAddressResponse CreateRouteResponse(uint requestOpCode, uint error, out Type responseType)
        {
            if (_responseTypeDictionary.TryGetValue(requestOpCode, out responseType))
            {
                var findResponse = (IAddressResponse)Activator.CreateInstance(responseType);
                findResponse.ErrorCode = error;
                return findResponse;
            }
            
            responseType = typeof(AddressResponse);
            var response = new AddressResponse();
            response.ErrorCode = error;
            return response;
        }

        #endregion

        #region OpCode

        internal Type? GetOpCodeType(uint opCode)
        {
            return _opCodeDictionary.TryGetValue(opCode, out var type) ? type : null;
        }
#if FANTASY_NET
        internal int? GetCustomRouteType(uint protocolCode)
        {
            return _customRouteDictionary.TryGetValue(protocolCode, out var type) ? type : null;
        }
#endif
        #endregion
    }
}