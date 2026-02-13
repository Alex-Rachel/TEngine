using System;
using System.Runtime.Serialization;
using Fantasy.Async;
using Fantasy.Pool;
using Fantasy.Serialize;
using LightProto;
using MemoryPack;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network.Interface
{
    public abstract class AMessage : IPool
    {
        [JsonIgnore] 
        [IgnoreDataMember] 
        [ProtoIgnore]
        [MemoryPackIgnore]
        private bool _isPool;

        [JsonIgnore] 
        [IgnoreDataMember] 
        [ProtoIgnore] 
        [MemoryPackIgnore]
        protected bool AutoReturn = true;

        public bool IsPool()
        {
            return _isPool;
        }

        public void SetIsPool(bool isPool)
        {
            _isPool = isPool;
        }
    }
    
    /// <summary>
    /// 表示通用消息接口。
    /// </summary>
    public interface IMessage : IDisposable
    {
        /// <summary>
        /// 获取消息的操作代码。
        /// </summary>
        /// <returns>操作代码。</returns>
        uint OpCode();
    }

    /// <summary>
    /// 表示请求消息接口。
    /// </summary>
    public interface IRequest : IMessage { }

    /// <summary>
    /// 表示响应消息接口。
    /// </summary>
    public interface IResponse : IMessage
    {
        /// <summary>
        /// 获取或设置错误代码。
        /// </summary>
        uint ErrorCode { get; set; }
    }
    
    /// <summary>
    /// 内网消息的接口，继承自请求接口。
    /// </summary>
    public interface IAddressMessage : IRequest { }
    /// <summary>
    /// 内网消息请求接口，继承自普通内网消息接口。
    /// </summary>
    public interface IAddressRequest : IAddressMessage { }
    /// <summary>
    /// 内网消息响应接口，继承自响应接口。
    /// </summary>
    public interface IAddressResponse : IResponse { }
    
    // 可寻址协议
    /// <summary>
    /// 表示可寻址协议的普通路由消息接口，继承自普通路由消息接口。
    /// </summary>
    public interface IAddressableMessage : IAddressMessage { }
    /// <summary>
    /// 可寻址协议的普通路由请求接口，继承自可寻址协议的普通路由消息接口。
    /// </summary>
    public interface IAddressableRequest : IAddressRequest { }
    /// <summary>
    /// 可寻址协议的普通路由响应接口，继承自普通路由响应接口。
    /// </summary>
    public interface IAddressableResponse : IAddressResponse { }
    
    // 自定义Route协议
    public interface ICustomRoute : IMessage
    {
        int RouteType { get; }
    }
    /// <summary>
    /// 表示自定义Route协议的普通路由消息接口，继承自普通路由消息接口。
    /// </summary>
    public interface ICustomRouteMessage : IAddressMessage, ICustomRoute { }
    /// <summary>
    /// 自定义Route协议的普通路由请求接口，继承自自定义Route协议的普通路由消息接口。
    /// </summary>
    public interface ICustomRouteRequest : IAddressRequest, ICustomRoute { }
    /// <summary>
    /// 自定义Route协议的普通路由响应接口，继承自普通路由响应接口。
    /// </summary>
    public interface ICustomRouteResponse : IAddressResponse { }
    
    /// <summary>
    /// 表示漫游协议的普通路由消息接口，继承自普通路由消息接口。
    /// </summary>
    public interface IRoamingMessage : IAddressMessage, ICustomRoute { }
    /// <summary>
    /// 漫游协议的普通路由请求接口，继承自自定义Route协议的普通路由消息接口。
    /// </summary>
    public interface IRoamingRequest : IRoamingMessage { }
    /// <summary>
    /// 漫游协议的普通路由响应接口，继承自普通路由响应接口。
    /// </summary>
    public interface IRoamingResponse : IAddressResponse { }
}