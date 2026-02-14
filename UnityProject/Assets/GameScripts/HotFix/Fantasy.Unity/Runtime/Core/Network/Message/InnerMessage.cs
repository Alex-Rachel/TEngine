using Fantasy.Network.Interface;
using Fantasy.Pool;
using LightProto;
using MemoryPack;
#if FANTASY_NET
using Fantasy.Entitas;
using Fantasy.Network.Roaming;
using Fantasy.Sphere;
// ReSharper disable RedundantNameQualifier
// ReSharper disable CheckNamespace
// ReSharper disable MemberCanBePrivate.Global
#endif
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
// ReSharper disable InconsistentNaming
// ReSharper disable PropertyCanBeMadeInitOnly.Global
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.InnerMessage
{
    [ProtoContract]
    public sealed partial class BenchmarkMessage : AMessage, IMessage
    {
        public static BenchmarkMessage Create()
        {
            return MessageObjectPool<BenchmarkMessage>.Rent();
        }

        public void Dispose()
        {
            MessageObjectPool<BenchmarkMessage>.Return(this);
        }

        public uint OpCode()
        {
            return Fantasy.Network.OpCode.BenchmarkMessage;
        }
    }

    [ProtoContract]
    public partial class BenchmarkRequest : AMessage, IRequest
    {
        public static BenchmarkRequest Create()
        {
            return MessageObjectPool<BenchmarkRequest>.Rent();
        }

        public void Dispose()
        {
            RpcId = 0;
            MessageObjectPool<BenchmarkRequest>.Return(this);
        }

        public uint OpCode()
        {
            return Fantasy.Network.OpCode.BenchmarkRequest;
        }
        [ProtoIgnore]
        public BenchmarkResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public long RpcId { get; set; }
    }

    [ProtoContract]
    public partial class BenchmarkResponse : AMessage, IResponse
    {
        public static BenchmarkResponse Create()
        {
            return MessageObjectPool<BenchmarkResponse>.Rent();
        }

        public void Dispose()
        {
            RpcId = 0;
            ErrorCode = 0;
            MessageObjectPool<BenchmarkResponse>.Return(this);
        }

        public uint OpCode()
        {
            return Fantasy.Network.OpCode.BenchmarkResponse;
        }
        [ProtoMember(1)]
        public long RpcId { get; set; }
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public sealed partial class Response : AMessage, IResponse
    {
        public static Response Create()
        {
            return MessageObjectPool<Response>.Rent();
        }

        public void Dispose()
        {
            RpcId = 0;
            ErrorCode = 0;
            MessageObjectPool<Response>.Return(this);
        }

        public uint OpCode()
        {
            return Fantasy.Network.OpCode.DefaultResponse;
        }
        [ProtoMember(1)]
        public long RpcId { get; set; }
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public sealed partial class AddressResponse : AMessage, IAddressResponse
    {
        public static AddressResponse Create()
        {
            return MessageObjectPool<AddressResponse>.Rent();
        }

        public void Dispose()
        {
            RpcId = 0;
            ErrorCode = 0;
            MessageObjectPool<AddressResponse>.Return(this);
        }

        public uint OpCode()
        {
            return Fantasy.Network.OpCode.DefaultAddressResponse;
        }
        [ProtoMember(1)]
        public long RpcId { get; set; }
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
    }

    [ProtoContract]
    public partial class PingRequest : AMessage, IRequest
    {
        public static PingRequest Create()
        {
            return MessageObjectPool<PingRequest>.Rent();
        }

        public void Dispose()
        {
            RpcId = 0;
            MessageObjectPool<PingRequest>.Return(this);
        }

        public uint OpCode()
        {
            return Fantasy.Network.OpCode.PingRequest;
        }
        [ProtoIgnore]
        public PingResponse ResponseType { get; set; }
        [ProtoMember(1)]
        public long RpcId { get; set; }
    }

    [ProtoContract]
    public partial class PingResponse : AMessage, IResponse
    {
        public static PingResponse Create()
        {
            return MessageObjectPool<PingResponse>.Rent();
        }

        public void Dispose()
        {
            RpcId = 0;
            ErrorCode = 0;
            Now = 0;
            MessageObjectPool<PingResponse>.Return(this);
        }

        public uint OpCode()
        {
            return Fantasy.Network.OpCode.PingResponse;
        }
        [ProtoMember(1)]
        public long RpcId { get; set; }
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        [ProtoMember(3)]
        public long Now;
    }
    [ProtoContract]
    public partial class I_AddressableAdd_Request : AMessage, IAddressRequest
    {
        public static I_AddressableAdd_Request Create()
        {
            return MessageObjectPool<I_AddressableAdd_Request>.Rent();
        }

        public void Dispose()
        {
            AddressableId = 0;
            Address = 0;
            IsLock = false;
            MessageObjectPool<I_AddressableAdd_Request>.Return(this);
        }

        [ProtoIgnore]
        public I_AddressableAdd_Response ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.AddressableAddRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long AddressableId { get; set; }
        [ProtoMember(2)]
        public long Address { get; set; }
        [ProtoMember(3)]
        public bool IsLock { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableAdd_Response : AMessage, IAddressResponse
    {
        public static I_AddressableAdd_Response Create()
        {
            return MessageObjectPool<I_AddressableAdd_Response>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_AddressableAdd_Response>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.AddressableAddResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableGet_Request : AMessage, IAddressRequest
    {
        public static I_AddressableGet_Request Create()
        {
            return MessageObjectPool<I_AddressableGet_Request>.Rent();
        }

        public void Dispose()
        {
            AddressableId = 0;
            MessageObjectPool<I_AddressableGet_Request>.Return(this);
        }

        [ProtoIgnore]
        public I_AddressableGet_Response ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.AddressableGetRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long AddressableId { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableGet_Response : AMessage, IAddressResponse
    {
        public static I_AddressableGet_Response Create()
        {
            return MessageObjectPool<I_AddressableGet_Response>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            Address = 0;
            MessageObjectPool<I_AddressableGet_Response>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.AddressableGetResponse; }
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
        [ProtoMember(1)]
        public long Address { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableRemove_Request : AMessage, IAddressRequest
    {
        public static I_AddressableRemove_Request Create()
        {
            return MessageObjectPool<I_AddressableRemove_Request>.Rent();
        }

        public void Dispose()
        {
            AddressableId = 0;
            MessageObjectPool<I_AddressableRemove_Request>.Return(this);
        }

        [ProtoIgnore]
        public I_AddressableRemove_Response ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.AddressableRemoveRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long AddressableId { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableRemove_Response : AMessage, IAddressResponse
    {
        public static I_AddressableRemove_Response Create()
        {
            return MessageObjectPool<I_AddressableRemove_Response>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_AddressableRemove_Response>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.AddressableRemoveResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableLock_Request : AMessage, IAddressRequest
    {
        public static I_AddressableLock_Request Create()
        {
            return MessageObjectPool<I_AddressableLock_Request>.Rent();
        }

        public void Dispose()
        {
            AddressableId = 0;
            MessageObjectPool<I_AddressableLock_Request>.Return(this);
        }

        [ProtoIgnore]
        public I_AddressableLock_Response ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.AddressableLockRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long AddressableId { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableLock_Response : AMessage, IAddressResponse
    {
        public static I_AddressableLock_Response Create()
        {
            return MessageObjectPool<I_AddressableLock_Response>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_AddressableLock_Response>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.AddressableLockResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableUnLock_Request : AMessage, IAddressRequest
    {
        public static I_AddressableUnLock_Request Create()
        {
            return MessageObjectPool<I_AddressableUnLock_Request>.Rent();
        }

        public void Dispose()
        {
            AddressableId = 0;
            Address = 0;
            Source = string.Empty;
            MessageObjectPool<I_AddressableUnLock_Request>.Return(this);
        }

        [ProtoIgnore]
        public I_AddressableUnLock_Response ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.AddressableUnLockRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long AddressableId { get; set; }
        [ProtoMember(2)]
        public long Address { get; set; }
        [ProtoMember(3)]
        public string Source { get; set; }
    }
    [ProtoContract]
    public partial class I_AddressableUnLock_Response : AMessage, IAddressResponse
    {
        public static I_AddressableUnLock_Response Create()
        {
            return MessageObjectPool<I_AddressableUnLock_Response>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_AddressableUnLock_Response>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.AddressableUnLockResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
#if FANTASY_NET
    [MemoryPackable]
    public sealed partial class I_LinkRoamingRequest : AMessage, IAddressRequest
    {
        public static I_LinkRoamingRequest Create()
        {
            return MessageObjectPool<I_LinkRoamingRequest>.Rent();
        }

        public void Dispose()
        {
            RoamingId = 0;
            RoamingType = 0;
            ForwardSessionAddress = 0;
            SceneAddress = 0;
            MessageObjectPool<I_LinkRoamingRequest>.Return(this);
        }

        [MemoryPackIgnore]
        public I_LinkRoamingResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.LinkRoamingRequest; }
        public long RouteTypeOpCode() { return 1; }
        [MemoryPackOrder(1)]
        public long RoamingId { get; set; }
        [MemoryPackOrder(2)]
        public int RoamingType { get; set; }
        [MemoryPackOrder(3)]
        public long ForwardSessionAddress { get; set; }
        [MemoryPackOrder(4)]
        public long SceneAddress { get; set; }
        /// <summary>
        /// Link类型
        /// 0 为创建 Link
        /// 1:重连 Link
        /// </summary>
        [MemoryPackOrder(5)]
        public int LinkType { get; set; } 
        [MemoryPackOrder(6)]
        public Entity Args { get; set; }
    }
    [MemoryPackable]
    public sealed partial class I_LinkRoamingResponse : AMessage, IAddressResponse
    {
        public static I_LinkRoamingResponse Create()
        {
            return MessageObjectPool<I_LinkRoamingResponse>.Rent();
        }

        public void Dispose()
        {
            TerminusId = 0;
            ErrorCode = 0;
            MessageObjectPool<I_LinkRoamingResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.LinkRoamingResponse; }
        [MemoryPackOrder(1)]
        public long TerminusId { get; set; }
        [MemoryPackOrder(2)]
        public uint ErrorCode { get; set; }
        [MemoryPackOrder(3)]
        public Entity? Args { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_UnLinkRoamingRequest : AMessage, IAddressRequest
    {
        public static I_UnLinkRoamingRequest Create()
        {
            return MessageObjectPool<I_UnLinkRoamingRequest>.Rent();
        }

        public void Dispose()
        {
            RoamingId = 0;
            DisposeRoaming = false;
            MessageObjectPool<I_UnLinkRoamingRequest>.Return(this);
        }

        [ProtoIgnore]
        public I_UnLinkRoamingResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.UnLinkRoamingRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long RoamingId { get; set; }
        [ProtoMember(2)]
        public bool DisposeRoaming { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_UnLinkRoamingResponse : AMessage, IAddressResponse
    {
        public static I_UnLinkRoamingResponse Create()
        {
            return MessageObjectPool<I_UnLinkRoamingResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_UnLinkRoamingResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.UnLinkRoamingResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public partial class I_LockTerminusIdRequest : AMessage, IAddressRequest
    {
        public static I_LockTerminusIdRequest Create()
        {
            return MessageObjectPool<I_LockTerminusIdRequest>.Rent();
        }

        public void Dispose()
        {
            RoamingId = 0;
            RoamingType = 0;
            MessageObjectPool<I_LockTerminusIdRequest>.Return(this);
        }

        [ProtoIgnore]
        public I_LockTerminusIdResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.LockTerminusIdRequest; }
        [ProtoMember(1)]
        public long RoamingId { get; set; }
        [ProtoMember(2)]
        public int RoamingType { get; set; }
    }
    [ProtoContract]
    public partial class I_LockTerminusIdResponse : AMessage, IAddressResponse
    {
        public static I_LockTerminusIdResponse Create()
        {
            return MessageObjectPool<I_LockTerminusIdResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_LockTerminusIdResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.LockTerminusIdResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_UnLockTerminusIdRequest : AMessage, IAddressRequest
    {
        public static I_UnLockTerminusIdRequest Create()
        {
            return MessageObjectPool<I_UnLockTerminusIdRequest>.Rent();
        }

        public void Dispose()
        {
            RoamingId = 0;
            RoamingType = 0;
            TerminusId = 0;
            TargetSceneAddress = 0;
            MessageObjectPool<I_UnLockTerminusIdRequest>.Return(this);
        }

        [ProtoIgnore]
        public I_UnLockTerminusIdResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.UnLockTerminusIdRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long RoamingId { get; set; }
        [ProtoMember(2)]
        public int RoamingType { get; set; }
        [ProtoMember(3)]
        public long TerminusId { get; set; }
        [ProtoMember(4)]
        public long TargetSceneAddress { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_UnLockTerminusIdResponse : AMessage, IAddressResponse
    {
        public static I_UnLockTerminusIdResponse Create()
        {
            return MessageObjectPool<I_UnLockTerminusIdResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_UnLockTerminusIdResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.UnLockTerminusIdResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 漫游传送终端的请求
    /// </summary>
    [MemoryPackable]
    public partial class I_TransferTerminusRequest : AMessage, IAddressRequest
    {
        public static I_TransferTerminusRequest Create()
        {
            return MessageObjectPool<I_TransferTerminusRequest>.Rent();
        }

        public void Dispose()
        {
            Terminus = null;
            MessageObjectPool<I_TransferTerminusRequest>.Return(this);
        }

        [MemoryPackIgnore]
        public I_TransferTerminusResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.TransferTerminusRequest; }
        public Terminus Terminus { get; set; }
    }
    [MemoryPackable]
    public partial class I_TransferTerminusResponse : AMessage, IAddressResponse
    {
        public static I_TransferTerminusResponse Create()
        {
            return MessageObjectPool<I_TransferTerminusResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_TransferTerminusResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.TransferTerminusResponse; }
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 用于服务器之间获取漫游的TerminusId。
    /// </summary>
    [ProtoContract]
    public partial class I_GetTerminusIdRequest : AMessage, IAddressRequest
    {
        public static I_GetTerminusIdRequest Create()
        {
            return MessageObjectPool<I_GetTerminusIdRequest>.Rent();
        }

        public void Dispose()
        {
            RoamingType = 0;
            RoamingId = 0;
            MessageObjectPool<I_GetTerminusIdRequest>.Return(this);
        }

        [ProtoIgnore]
        public I_GetTerminusIdResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.GetTerminusIdRequest; }
        [ProtoMember(1)]
        public int RoamingType { get; set; }
        [ProtoMember(2)]
        public long RoamingId { get; set; }
    }
    [ProtoContract]
    public partial class I_GetTerminusIdResponse : AMessage, IAddressResponse
    {
        public static I_GetTerminusIdResponse Create()
        {
            return MessageObjectPool<I_GetTerminusIdResponse>.Rent();
        }

        public void Dispose()
        {
            TerminusId = 0;
            ErrorCode = 0;
            MessageObjectPool<I_GetTerminusIdResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.GetTerminusIdResponse; }
        [ProtoMember(1)]
        public long TerminusId { get; set; }
        [ProtoMember(2)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_SetForwardSessionAddressRequest : AMessage, IAddressRequest
    {
        public static I_SetForwardSessionAddressRequest Create()
        {
            return MessageObjectPool<I_SetForwardSessionAddressRequest>.Rent();
        }

        public void Dispose()
        {
            RoamingId = 0;
            ForwardSessionAddress = 0;
            MessageObjectPool<I_SetForwardSessionAddressRequest>.Return(this);
        }

        [ProtoIgnore]
        public I_SetForwardSessionAddressResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.SetForwardSessionAddressRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long RoamingId { get; set; }
        [ProtoMember(2)]
        public long ForwardSessionAddress { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_SetForwardSessionAddressResponse : AMessage, IAddressResponse
    {
        public static I_SetForwardSessionAddressResponse Create()
        {
            return MessageObjectPool<I_SetForwardSessionAddressResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_SetForwardSessionAddressResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.SetForwardSessionAddressResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_StopForwardingRequest : AMessage, IAddressRequest
    {
        public static I_StopForwardingRequest Create()
        {
            return MessageObjectPool<I_StopForwardingRequest>.Rent();
        }

        public void Dispose()
        {
            RoamingId = 0;
            MessageObjectPool<I_StopForwardingRequest>.Return(this);
        }

        [ProtoIgnore]
        public I_StopForwardingResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.StopForwardingRequest; }
        public long RouteTypeOpCode() { return 1; }
        [ProtoMember(1)]
        public long RoamingId { get; set; }
    }
    [ProtoContract]
    public sealed partial class I_StopForwardingResponse : AMessage, IAddressResponse
    {
        public static I_StopForwardingResponse Create()
        {
            return MessageObjectPool<I_StopForwardingResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_StopForwardingResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.StopForwardingResponse; }
        [ProtoMember(1)]
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 订阅一个领域事件
    /// </summary>
    [MemoryPackable]
    public partial class I_SubscribeSphereEventRequest : AMessage, IAddressRequest
    {
        public static I_SubscribeSphereEventRequest Create()
        {
            return MessageObjectPool<I_SubscribeSphereEventRequest>.Rent();
        }

        public void Dispose()
        {
            Address = 0;
            TypeHashCode = 0;
            MessageObjectPool<I_SubscribeSphereEventRequest>.Return(this);
        }

        [MemoryPackIgnore]
        public I_SubscribeSphereEventResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.SubscribeSphereEventRequest; }
        public long Address { get; set; }
        public long TypeHashCode { get; set; }
    }
    [MemoryPackable]
    public partial class I_SubscribeSphereEventResponse : AMessage, IAddressResponse
    {
        public static I_SubscribeSphereEventResponse Create()
        {
            return MessageObjectPool<I_SubscribeSphereEventResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_SubscribeSphereEventResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.SubscribeSphereEventResponse; }
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 取消订阅一个领域事件
    /// </summary>
    [MemoryPackable]
    public partial class I_UnsubscribeSphereEventRequest : AMessage, IAddressRequest
    {
        public static I_UnsubscribeSphereEventRequest Create()
        {
            return MessageObjectPool<I_UnsubscribeSphereEventRequest>.Rent();
        }

        public void Dispose()
        {
            Address = 0;
            TypeHashCode = 0;
            MessageObjectPool<I_UnsubscribeSphereEventRequest>.Return(this);
        }

        [MemoryPackIgnore]
        public I_UnsubscribeSphereEventResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.UnsubscribeSphereEventRequest; }
        public long Address { get; set; }
        public long TypeHashCode { get; set; }
    }
    [MemoryPackable]
    public partial class I_UnsubscribeSphereEventResponse : AMessage, IAddressResponse
    {
        public static I_UnsubscribeSphereEventResponse Create()
        {
            return MessageObjectPool<I_UnsubscribeSphereEventResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_UnsubscribeSphereEventResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.UnsubscribeSphereEventResponse; }
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 撤销远程订阅者的订阅领域事件
    /// </summary>
    [MemoryPackable]
    public partial class I_RevokeRemoteSubscriberRequest : AMessage, IAddressRequest
    {
        public static I_RevokeRemoteSubscriberRequest Create()
        {
            return MessageObjectPool<I_RevokeRemoteSubscriberRequest>.Rent();
        }

        public void Dispose()
        {
            Address = 0;
            TypeHashCode = 0;
            MessageObjectPool<I_RevokeRemoteSubscriberRequest>.Return(this);
        }

        [MemoryPackIgnore]
        public I_RevokeRemoteSubscriberResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.RevokeRemoteSubscriberRequest; }
        public long Address { get; set; }
        public long TypeHashCode { get; set; }
    }
    [MemoryPackable]
    public partial class I_RevokeRemoteSubscriberResponse : AMessage, IAddressResponse
    {
        public static I_RevokeRemoteSubscriberResponse Create()
        {
            return MessageObjectPool<I_RevokeRemoteSubscriberResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_RevokeRemoteSubscriberResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.RevokeRemoteSubscriberResponse; }
        public uint ErrorCode { get; set; }
    }
    /// <summary>
    /// 发送一个领域事件
    /// </summary>
    [MemoryPackable]
    public partial class I_PublishSphereEventRequest : AMessage, IAddressRequest
    {
        public static I_PublishSphereEventRequest Create()
        {
            return MessageObjectPool<I_PublishSphereEventRequest>.Rent();
        }

        public void Dispose()
        {
            Address = 0;
            SphereEventArgs = null;
            MessageObjectPool<I_PublishSphereEventRequest>.Return(this);
        }

        [MemoryPackIgnore]
        public I_PublishSphereEventResponse ResponseType { get; set; }
        public uint OpCode() { return Fantasy.Network.OpCode.PublishSphereEventRequest; }
        public long Address { get; set; }
        public SphereEventArgs SphereEventArgs { get; set; }
    }
    [MemoryPackable]
    public partial class I_PublishSphereEventResponse : AMessage, IAddressResponse
    {
        public static I_PublishSphereEventResponse Create()
        {
            return MessageObjectPool<I_PublishSphereEventResponse>.Rent();
        }

        public void Dispose()
        {
            ErrorCode = 0;
            MessageObjectPool<I_PublishSphereEventResponse>.Return(this);
        }

        public uint OpCode() { return Fantasy.Network.OpCode.PublishSphereEventResponse; }
        public uint ErrorCode { get; set; }
    }
#endif
}
