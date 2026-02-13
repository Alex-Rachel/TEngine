using System;
using System.Buffers;
using System.IO;
using Fantasy.Serialize;
using LightProto;

namespace Fantasy.Assembly
{
    /// <summary>
    /// ProtoBuf 分发器注册器接口
    /// 由 Source Generator 自动生成实现类，用于解决 IL2CPP 下 ProtoBuf 反射问题
    /// 通过将运行时 Type 参数转换为编译时泛型调用，实现 AOT 兼容性
    /// </summary>
    public interface IProtoBufDispatcherRegistrar
    {
        /// <summary>
        /// 获取所有网络协议消息类型的句柄数组
        /// 用于建立 Type 到序列化/反序列化方法的映射
        /// </summary>
        /// <returns>RuntimeTypeHandle 数组，每个元素对应一个网络协议消息类型</returns>
        RuntimeTypeHandle[] TypeHandles();

        /// <summary>
        /// 获取所有网络协议消息的序列化委托数组
        /// 每个委托封装了对应类型的泛型序列化方法调用（IL2CPP 安全）
        /// </summary>
        /// <returns>序列化委托数组，索引与 TypeHandles() 返回的数组一一对应</returns>
        Action<IBufferWriter<byte>, object>[] SerializeDelegates();

        /// <summary>
        /// 获取所有网络协议消息的反序列化委托数组
        /// 每个委托封装了对应类型的泛型反序列化方法调用（IL2CPP 安全）
        /// </summary>
        /// <returns>反序列化委托数组，索引与 TypeHandles() 返回的数组一一对应</returns>
        Func<Stream, object>[] DeserializeDelegates();

        /// <summary>
        /// 获取所有网络协议消息的 ProtoReader 数组
        /// 用于 Unity 等不支持静态抽象接口成员的平台
        /// </summary>
        /// <returns>ProtoReader 对象数组，索引与 TypeHandles() 返回的数组一一对应</returns>
        object[] ProtoReaders();

        /// <summary>
        /// 获取所有网络协议消息的 ProtoWriter 数组
        /// 用于 Unity 等不支持静态抽象接口成员的平台
        /// </summary>
        /// <returns>ProtoWriter 对象数组，索引与 TypeHandles() 返回的数组一一对应</returns>
        object[] ProtoWriters();
    }
}