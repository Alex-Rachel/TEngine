using System;
using System.Buffers;
using LightProto;

namespace Fantasy.Serialize
{
    /// <summary>
    /// 序列化接口，定义了 Fantasy 框架中所有序列化器必须实现的统一接口。
    /// 支持 BSON、ProtoBuf、MemoryPack 等多种序列化方式。
    /// </summary>
    public interface ISerialize
    {
        /// <summary>
        /// 获取序列化器的名称标识，用于在网络协议中指定使用哪种序列化方式。
        /// </summary>
        /// <value>序列化器名称，如 "Bson"、"ProtoBuf"、"MemoryPack"</value>
        string SerializeName { get; }

        /// <summary>
        /// 将字节数组反序列化为指定类型的对象。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="bytes">包含序列化数据的字节数组</param>
        /// <returns>反序列化后的对象实例</returns>
        T Deserialize<T>(byte[] bytes) where T : IProtoParser<T>;

        /// <summary>
        /// 从 MemoryStreamBuffer 中反序列化为指定类型的对象。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="buffer">包含序列化数据的内存流缓冲区</param>
        /// <returns>反序列化后的对象实例</returns>
        T Deserialize<T>(MemoryStreamBuffer buffer) where T : IProtoParser<T>;

        /// <summary>
        /// 将字节数组反序列化为指定类型的对象（非泛型版本）。
        /// </summary>
        /// <param name="type">目标对象的类型</param>
        /// <param name="bytes">包含序列化数据的字节数组</param>
        /// <returns>反序列化后的对象实例</returns>
        object Deserialize(Type type, byte[] bytes);

        /// <summary>
        /// 从 MemoryStreamBuffer 中反序列化为指定类型的对象（非泛型版本）。
        /// </summary>
        /// <param name="type">目标对象的类型</param>
        /// <param name="buffer">包含序列化数据的内存流缓冲区</param>
        /// <returns>反序列化后的对象实例</returns>
        object Deserialize(Type type, MemoryStreamBuffer buffer);

        /// <summary>
        /// 从字节数组的指定范围反序列化为指定类型的对象。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="bytes">包含序列化数据的字节数组</param>
        /// <param name="index">反序列化数据的起始索引</param>
        /// <param name="count">要反序列化的字节数</param>
        /// <returns>反序列化后的对象实例</returns>
        T Deserialize<T>(byte[] bytes, int index, int count) where T : IProtoParser<T>;

        /// <summary>
        /// 从字节数组的指定范围反序列化为指定类型的对象（非泛型版本）。
        /// </summary>
        /// <param name="type">目标对象的类型</param>
        /// <param name="bytes">包含序列化数据的字节数组</param>
        /// <param name="index">反序列化数据的起始索引</param>
        /// <param name="count">要反序列化的字节数</param>
        /// <returns>反序列化后的对象实例</returns>
        object Deserialize(Type type, byte[] bytes, int index, int count);

        /// <summary>
        /// 将对象序列化为字节数组。
        /// </summary>
        /// <param name="type">要序列化的对象类型</param>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>包含序列化数据的字节数组</returns>
        byte[] Serialize(Type type, object obj);

        /// <summary>
        /// 将指定类型的对象序列化为字节数组。
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="object">要序列化的对象</param>
        /// <returns>包含序列化数据的字节数组</returns>
        byte[] Serialize<T>(T @object) where T : IProtoParser<T>;

        /// <summary>
        /// 将指定类型的对象序列化到 IBufferWriter 中，避免额外的内存分配。
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="object">要序列化的对象</param>
        /// <param name="buffer">用于写入序列化数据的缓冲区</param>
        void Serialize<T>(T @object, IBufferWriter<byte> buffer) where T : IProtoParser<T>;

        // /// <summary>
        // /// 将对象序列化到 IBufferWriter 中，避免额外的内存分配。
        // /// </summary>
        // /// <param name="object">要序列化的对象</param>
        // /// <param name="buffer">用于写入序列化数据的缓冲区</param>
        // void Serialize(object @object, IBufferWriter<byte> buffer);

        /// <summary>
        /// 将指定类型的对象序列化到 IBufferWriter 中，避免额外的内存分配。
        /// </summary>
        /// <param name="type">对象的类型</param>
        /// <param name="object">要序列化的对象</param>
        /// <param name="buffer">用于写入序列化数据的缓冲区</param>
        void Serialize(Type type, object @object, IBufferWriter<byte> buffer);

        /// <summary>
        /// 通过序列化和反序列化创建对象的深度克隆。
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="t">要克隆的对象</param>
        /// <returns>克隆后的新对象实例</returns>
        T Clone<T>(T t) where T : IProtoParser<T>;
        
        /// <summary>
        /// 通过序列化和反序列化创建对象的深度克隆。
        /// </summary>
        /// <param name="type">对象类型</param>
        /// <param name="object">要克隆的对象</param>
        /// <returns>克隆后的新对象实例</returns>
        object Clone(Type type, object @object);
    }
}