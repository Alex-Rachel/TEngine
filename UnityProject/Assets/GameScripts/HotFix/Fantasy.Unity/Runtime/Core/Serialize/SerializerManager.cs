using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fantasy.Async;
using LightProto;

#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace Fantasy.Serialize
{
    /// <summary>
    /// 框架内置的序列化器类型
    /// </summary>
    public static class FantasySerializerType
    {
        /// <summary>
        /// ProtoBuf在SerializerManager的数组下标
        /// </summary>
        public const int ProtoBuf = 0;
        /// <summary>
        /// Bson在SerializerManager的数组下标
        /// </summary>
        public const int Bson = 1;
        /// <summary>
        /// MemoryPack在SerializerManager的数组下标
        /// </summary>
        public const int MemoryPack = 2;
    }
    
    /// <summary>
    /// 管理序列化静态方法，主要是优化网络协议时使用。
    /// </summary>
    public static class SerializerManager
    {
        /// <summary>
        /// ProtoBuf 序列化
        /// </summary>
        public static ProtoBufHelper ProtoBufHelper { get; private set; }
#if FANTASY_NET
        /// <summary>
        /// BSON 序列化
        /// </summary>
        public static BsonPackHelper BsonPack { get; private set; }
#endif
        /// <summary>
        /// MemoryPack 序列化
        /// </summary>
        public static MemoryPackHelper MemoryPackHelper { get; private set; }
        
        private static volatile bool _isInitialized = false;
        
        /// <summary>
        /// 初始化方法
        /// </summary>
        public static async FTask Initialize()
        {
            if (_isInitialized)
            {
                return;
            }
            
            ProtoBufHelper = await new ProtoBufHelper().Initialize();
#if FANTASY_NET
            BsonPack = await new BsonPackHelper().Initialize();
#endif
            MemoryPackHelper = await new MemoryPackHelper().Initialize();
            _isInitialized = true;
        }

        #region Fast Methods - 避免虚方法调用的快速路径

        /// <summary>
        /// 尝试反序列化 - 从字节数组反序列化
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDeserialize<T>(uint opCodeProtocolType, byte[] bytes, out T result, out string error) where T : IProtoParser<T>
        {
            error = null;
            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    result = ProtoBufHelper.Deserialize<T>(bytes);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    result = BsonPack.Deserialize<T>(bytes);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = default!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    result = MemoryPackHelper.Deserialize<T>(bytes);
                    return true;
                }
                default:
                {
                    result = default!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试反序列化 - 从 MemoryStreamBuffer 反序列化
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDeserialize<T>(uint opCodeProtocolType, MemoryStreamBuffer buffer, out T result, out string error) where T : IProtoParser<T>
        {
            error = null;
            
            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    result = ProtoBufHelper.Deserialize<T>(buffer);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    result = BsonPack.Deserialize<T>(buffer);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = default!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    result = MemoryPackHelper.Deserialize<T>(buffer);
                    return true;
                }
                default:
                {
                    result = default!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试反序列化 - 从字节数组反序列化（非泛型版本）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDeserialize(uint opCodeProtocolType, Type type, byte[] bytes, out object result, out string error)
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    result = ProtoBufHelper.Deserialize(type, bytes);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    result = BsonPack.Deserialize(type, bytes);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = null!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    result = MemoryPackHelper.Deserialize(type, bytes);
                    return true;
                }
                default:
                {
                    result = null!;
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试反序列化 - 从 MemoryStreamBuffer 反序列化（非泛型版本）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDeserialize(uint opCodeProtocolType, Type type, MemoryStreamBuffer buffer, out object result, out string error)
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    result = ProtoBufHelper.Deserialize(type, buffer);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    result = BsonPack.Deserialize(type, buffer);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = null!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    result = MemoryPackHelper.Deserialize(type, buffer);
                    return true;
                }
                default:
                {
                    result = null!;
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试反序列化 - 从字节数组的指定范围反序列化
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDeserialize<T>(uint opCodeProtocolType, byte[] bytes, int index, int count, out T result, out string error) where T : IProtoParser<T>
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    result = ProtoBufHelper.Deserialize<T>(bytes, index, count);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    result = BsonPack.Deserialize<T>(bytes, index, count);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = default!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    result = MemoryPackHelper.Deserialize<T>(bytes, index, count);
                    return true;
                }
                default:
                {
                    result = default!;
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试反序列化 - 从字节数组的指定范围反序列化（非泛型版本）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDeserialize(uint opCodeProtocolType, Type type, byte[] bytes, int index, int count, out object result, out string error)
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    result = ProtoBufHelper.Deserialize(type, bytes, index, count);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    result = BsonPack.Deserialize(type, bytes, index, count);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = null!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    result = MemoryPackHelper.Deserialize(type, bytes, index, count);
                    return true;
                }
                default:
                {
                    result = null!;
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试序列化 - 序列化泛型对象到字节数组
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TrySerialize<T>(uint opCodeProtocolType, T obj, out byte[] result, out string error) where T : IProtoParser<T>
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    result = ProtoBufHelper.Serialize<T>(obj);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    result = BsonPack.Serialize<T>(obj);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = null!;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    result = MemoryPackHelper.Serialize<T>(obj);
                    return true;
                }
                default:
                {
                    result = null!;
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试序列化 - 序列化泛型对象到 IBufferWriter
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TrySerialize<T>(uint opCodeProtocolType, T obj, IBufferWriter<byte> buffer, out string error) where T : IProtoParser<T>
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    ProtoBufHelper!.Serialize<T>(obj, buffer);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    BsonPack.Serialize<T>(obj, buffer);
                    return true;   
#endif
#if FANTASY_UNITY
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    MemoryPackHelper.Serialize<T>(obj, buffer);
                    return true;
                }
                default:
                {
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试序列化 - 序列化指定类型对象到 IBufferWriter
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TrySerialize(uint opCodeProtocolType, Type type, object obj, IBufferWriter<byte> buffer, out string error)
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    ProtoBufHelper!.Serialize(type, obj, buffer);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    BsonPack.Serialize(type, obj, buffer);
                    return true;   
#endif
#if FANTASY_UNITY
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    MemoryPackHelper.Serialize(type, obj, buffer);
                    return true;
                }
                default:
                {
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试克隆 - 通过序列化和反序列化创建对象深拷贝
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryClone<T>(uint opCodeProtocolType, T obj, out T result, out string error) where T : IProtoParser<T>
        {
            error = null;

            switch (opCodeProtocolType)
            {
                case FantasySerializerType.ProtoBuf:
                {
                    error = null;
                    result = ProtoBufHelper.Clone<T>(obj);
                    return true;
                }
                case FantasySerializerType.Bson:
                {
#if FANTASY_NET
                    error = null;
                    result = BsonPack.Clone<T>(obj);
                    return true;   
#endif
#if FANTASY_UNITY
                    result = default;
                    error =  $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
#endif
                }
                case FantasySerializerType.MemoryPack:
                {
                    error = null;
                    result = MemoryPackHelper.Clone<T>(obj);
                    return true;
                }
                default:
                {
                    result = default!;
                    error = $"Unknown protocol type: {opCodeProtocolType}";
                    return false;
                }
            }
        }

        #endregion

        /// <summary>
        /// 销毁方法
        /// </summary>
        public static void Dispose()
        {
            _isInitialized = false;
            ProtoBufHelper = null;
#if FANTASY_NET
            BsonPack = null;
#endif
            MemoryPackHelper = null;
        }
    }
}