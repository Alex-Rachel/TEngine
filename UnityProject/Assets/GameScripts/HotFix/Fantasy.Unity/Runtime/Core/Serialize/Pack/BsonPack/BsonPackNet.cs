#if FANTASY_NET
using System.Buffers;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.Entitas;
using LightProto;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Fantasy.Serialize
{
    /// <summary>
    /// BSON 序列化实现，基于 MongoDB.Bson 提供 BSON 格式的序列化/反序列化功能。
    /// 支持 ASerialize 接口的序列化生命周期回调，适用于数据库持久化和网络传输。
    /// </summary>
    public sealed class BsonPackHelper : ISerialize, IAssemblyLifecycle
    {
        /// <inheritdoc/>
        public string SerializeName { get; } = "Bson";

        /// <summary>
        /// 初始化 BSON 序列化器，配置序列化约定和自定义序列化器。
        /// </summary>
        public BsonPackHelper()
        {
            BsonSerializer.RegisterSerializer(typeof(EntityTreeCollection), new EntityTreeCollectionSerializer());
            BsonSerializer.RegisterSerializer(typeof(EntityMultiCollection), new EntityMultiCollectionSerializer());
            // 初始化 ConventionRegistry，注册 IgnoreExtraElements 约定，忽略反序列化时多余的字段
            ConventionRegistry.Register("IgnoreExtraElements",
                new ConventionPack { new IgnoreExtraElementsConvention(true) }, type => true);
            // 注册通用 Object 序列化器，允许所有类型的对象序列化
            // 可以在此处注册自定义结构体序列化器，例如：
            // BsonSerializer.TryRegisterSerializer(typeof(float2), new StructBsonSerialize<float2>());
            // BsonSerializer.TryRegisterSerializer(typeof(float3), new StructBsonSerialize<float3>());
            // BsonSerializer.TryRegisterSerializer(typeof(float4), new StructBsonSerialize<float4>());
            // BsonSerializer.TryRegisterSerializer(typeof(quaternion), new StructBsonSerialize<quaternion>());
            BsonSerializer.RegisterSerializer(new ObjectSerializer(x => true));
        }

        #region AssemblyManifest

        /// <summary>
        /// 初始化 BSON 序列化器并注册到程序集生命周期管理。
        /// </summary>
        /// <returns>初始化后的 BsonPackHelper 实例</returns>
        internal async FTask<BsonPackHelper> Initialize()
        {
            await AssemblyLifecycle.Add(this);
            return this;
        }

        /// <summary>
        /// 程序集加载时的回调，注册所有实体类型的 BSON ClassMap。
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        public async FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            var entityTypes = assemblyManifest.EntityTypeCollectionRegistrar.GetEntityTypes();
            
            if (entityTypes.Any())
            {
                foreach (var entityType in entityTypes)
                {
                    if (BsonClassMap.IsClassMapRegistered(entityType))
                    {
                        continue;
                    }
            
                    BsonClassMap.LookupClassMap(entityType);
                }
            }

            await FTask.CompletedTask;
        }

        /// <summary>
        /// 程序集卸载时的回调。
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        public async FTask OnUnload(AssemblyManifest assemblyManifest)
        {
            await FTask.CompletedTask;
        }

        #endregion

        /// <inheritdoc/>
        public T Deserialize<T>(byte[] bytes) where T : IProtoParser<T>
        {
            return BsonSerializer.Deserialize<T>(bytes);
        }

        /// <inheritdoc/>
        public T Deserialize<T>(MemoryStreamBuffer buffer) where T : IProtoParser<T>
        {
            return BsonSerializer.Deserialize<T>(buffer);
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, byte[] bytes)
        {
            return BsonSerializer.Deserialize(bytes, type);
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, MemoryStreamBuffer buffer)
        {
            return BsonSerializer.Deserialize(buffer, type);
        }

        /// <inheritdoc/>
        public unsafe T Deserialize<T>(byte[] bytes, int index, int count) where T : IProtoParser<T>
        {
            fixed (byte* ptr = &bytes[index])
            {
                using var stream = new UnmanagedMemoryStream(ptr, count);
                return BsonSerializer.Deserialize<T>(stream);
            }
        }

        /// <inheritdoc/>
        public unsafe object Deserialize(Type type, byte[] bytes, int index, int count)
        {
            fixed (byte* ptr = &bytes[index])
            {
                using var stream = new UnmanagedMemoryStream(ptr, count);
                return BsonSerializer.Deserialize(stream, type);
            }
        }

        /// <inheritdoc/>
        public void Serialize<T>(T @object, IBufferWriter<byte> buffer) where T : IProtoParser<T>
        {
            var bsonWriter =
                new BsonBinaryWriter((MemoryStream)buffer, BsonBinaryWriterSettings.Defaults);
            BsonSerializer.Serialize(bsonWriter, @object);
            bsonWriter.Flush();
        }

        // /// <inheritdoc/>
        // public void Serialize(object @object, IBufferWriter<byte> buffer)
        // {
        //     var bsonWriter =
        //         new BsonBinaryWriter((MemoryStream)buffer, BsonBinaryWriterSettings.Defaults);
        //     BsonSerializer.Serialize(bsonWriter, @object.GetType(), @object);
        //     bsonWriter.Flush();
        // }

        /// <inheritdoc/>
        public void Serialize(Type type, object @object, IBufferWriter<byte> buffer)
        {
            var bsonWriter =
                new BsonBinaryWriter((MemoryStream)buffer, BsonBinaryWriterSettings.Defaults);
            BsonSerializer.Serialize(bsonWriter, type, @object);
            bsonWriter.Flush();
        }

        /// <summary>
        /// 序列化对象到 MemoryStreamBuffer 并返回序列化后的数据长度。
        /// </summary>
        /// <param name="type">对象的类型</param>
        /// <param name="object">要序列化的对象</param>
        /// <param name="buffer">用于写入序列化数据的缓冲区</param>
        /// <returns>序列化后的数据长度（字节数）</returns>
        public int SerializeAndReturnLength(Type type, object @object, MemoryStreamBuffer buffer)
        {
            var bsonWriter = new BsonBinaryWriter(buffer, BsonBinaryWriterSettings.Defaults);
            BsonSerializer.Serialize(bsonWriter, type, @object);
            bsonWriter.Flush();
            return (int)buffer.Length;
        }

        /// <inheritdoc/>
        public byte[] Serialize(Type type, object @object)
        {
            return @object.ToBson(@object.GetType());
        }

        /// <inheritdoc/>
        public byte[] Serialize<T>(T @object) where T : IProtoParser<T>
        {
            return @object.ToBson<T>();
        }

        /// <inheritdoc/>
        public T Clone<T>(T t) where T : IProtoParser<T>
        {
            return BsonSerializer.Deserialize<T>(t.ToBson<T>());
        }

        /// <inheritdoc/>
        public object Clone(Type type, object @object)
        {
            return BsonSerializer.Deserialize(@object.ToBsonDocument(), type);
        }

        /// <summary>
        /// 克隆Entity
        /// </summary>
        /// <param name="t"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T CloneEntity<T>(T t) where T : Entity
        {
            return BsonSerializer.Deserialize<T>(t.ToBson<T>());
        }
    }
}
#endif