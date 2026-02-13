using System;
using System.Buffers;
using System.IO;
using LightProto;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.DataStructure.Dictionary;
// ReSharper disable CheckNamespace
// ReSharper disable RedundantTypeArgumentsOfMethod

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace Fantasy.Serialize
{
    /// <summary>
    /// ProtoBuf 序列化实现，基于 LightProto 提供高性能的二进制序列化/反序列化功能。
    /// 支持 ASerialize 接口的序列化生命周期回调，适用于网络通信和数据持久化。
    /// </summary>
    public sealed class ProtoBufHelper : ISerialize, IAssemblyLifecycle
    {
        /// <inheritdoc/>
        public string SerializeName { get; } = "ProtoBuf";

        private static RuntimeTypeHandleFrozenDictionary<object> _readers;
        private static RuntimeTypeHandleFrozenDictionary<object> _writers;
        private static RuntimeTypeHandleFrozenDictionary<Func<Stream, object>> _deserializes;
        private static RuntimeTypeHandleFrozenDictionary<Action<IBufferWriter<byte>, object>> _serializes;
        
        private static readonly TypeHandleMergerFrozenDictionary<object> ReaderMerger = new();
        private static readonly TypeHandleMergerFrozenDictionary<object> WriterMerger = new();
        private static readonly TypeHandleMergerFrozenDictionary<Func<Stream, object>> DeserializeMerger = new();
        private static readonly TypeHandleMergerFrozenDictionary<Action<IBufferWriter<byte>, object>> SerializeMerger = new();
        
        [ThreadStatic] private static MemoryStreamBuffer? _cachedStream;

        #region AssemblyManifest

        /// <summary>
        /// 初始化 ProtoBuf 序列化器并注册到程序集生命周期管理。
        /// </summary>
        /// <returns>初始化后的 ProtoBufPack 实例</returns>
        internal async FTask<ProtoBufHelper> Initialize()
        {
            await AssemblyLifecycle.Add(this);
            return this;
        }
        
        /// <summary>
        /// 程序集加载时的回调，注册所有网络协议类型到 ProtoBuf 运行时模型。
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        public async FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            var protoBufTypes = assemblyManifest.NetworkProtocolRegistrar.GetNetworkProtocolTypes();

            if (protoBufTypes.Count == 0)
            {
                return;
            }
            
            // 因为网络协议再服务器部分热重载没有意义。
            // 所以基本不会在运行的时候热重载协议的，这样就不会有线程安全问题。
            // 所以这里没有任何关于线程安全的相关处理。
            // 如果要热更协议，这里就要做线程安全相关的处理。
            
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;
            var protoBufDispatcherRegistrar = assemblyManifest.ProtoBufDispatcherRegistrar;

            if (ProgramDefine.IsAppRunning)
            {
                var tcs = FTask.Create(false);
                ThreadScheduler.MainScheduler.ThreadSynchronizationContext.Post(() =>
                {
                    InnerOnLoad(assemblyManifestId, protoBufDispatcherRegistrar);
                });
                await tcs;
                return;
            }
            
            InnerOnLoad(assemblyManifestId, protoBufDispatcherRegistrar);
        }

        private void InnerOnLoad(long assemblyManifestId, IProtoBufDispatcherRegistrar protoBufDispatcherRegistrar)
        {
            var runtimeTypeHandles = protoBufDispatcherRegistrar.TypeHandles();

            ReaderMerger.Add(
                assemblyManifestId,
                runtimeTypeHandles,
                protoBufDispatcherRegistrar.ProtoReaders());

            WriterMerger.Add(
                assemblyManifestId,
                runtimeTypeHandles,
                protoBufDispatcherRegistrar.ProtoWriters());

            SerializeMerger.Add(
                assemblyManifestId,
                runtimeTypeHandles,
                protoBufDispatcherRegistrar.SerializeDelegates()
            );

            DeserializeMerger.Add(
                assemblyManifestId,
                runtimeTypeHandles,
                protoBufDispatcherRegistrar.DeserializeDelegates()
            );

            _readers = ReaderMerger.GetFrozenDictionary();
            _writers = WriterMerger.GetFrozenDictionary();
            _serializes = SerializeMerger.GetFrozenDictionary();
            _deserializes = DeserializeMerger.GetFrozenDictionary();
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

        #region ProtoParser

        public static IProtoReader<T> GetReader<T>()
        {
            if (_readers.TryGetValue(typeof(T).TypeHandle, out var reader))
            {
                return (IProtoReader<T>)reader;
            }
            throw new InvalidOperationException($"Type {typeof(T).Name} does not have ProtoReader registered. Make sure it has [ProtoContract] attribute.");
        }
        
        public static IProtoWriter<T> GetWriter<T>()
        {
            if (_writers.TryGetValue(typeof(T).TypeHandle, out var writer))
            {
                return (IProtoWriter<T>)writer;
            }
            throw new InvalidOperationException($"Type {typeof(T).Name} does not have ProtoWriter registered. Make sure it has [ProtoContract] attribute.");
        }

        #endregion

        #region Deserialize

        /// <inheritdoc/>
        public T Deserialize<T>(byte[] bytes) where T : IProtoParser<T>
        {
#if NET7_0_OR_GREATER
            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(bytes), T.ProtoReader);
#else
            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(bytes), ProtoParserAccessor<T>.Reader);
#endif
        }

        /// <inheritdoc/>
        public T Deserialize<T>(MemoryStreamBuffer buffer) where T : IProtoParser<T>
        {
#if NET7_0_OR_GREATER
            return Serializer.Deserialize<T>(buffer, T.ProtoReader);
#else
            return Serializer.Deserialize<T>(buffer, ProtoParserAccessor<T>.Reader);
#endif
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, byte[] bytes)
        {
            return _deserializes[type.TypeHandle](new MemoryStreamBuffer(bytes));
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, MemoryStreamBuffer buffer)
        {
            return _deserializes[type.TypeHandle](buffer);
        }

        /// <inheritdoc/>
        public T Deserialize<T>(byte[] bytes, int index, int count) where T : IProtoParser<T>
        {
#if NET7_0_OR_GREATER
            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(bytes, index, count), T.ProtoReader);
#else
            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(bytes, index, count), ProtoParserAccessor<T>.Reader);
#endif
        }
        
        /// <inheritdoc/>
        public unsafe object Deserialize(Type type, byte[] bytes, int index, int count)
        {
            fixed (byte* ptr = &bytes[index])
            {
                using var stream = new UnmanagedMemoryStream(ptr, count);
                return _deserializes[type.TypeHandle](stream);
            }
        }

        #endregion

        #region Serialize

        /// <inheritdoc/>
        public void Serialize<T>(T @object, IBufferWriter<byte> buffer) where T : IProtoParser<T>
        {
#if NET7_0_OR_GREATER
            Serializer.Serialize<T>(buffer, @object, T.ProtoWriter);
#else
            Serializer.Serialize<T>(buffer, @object, ProtoParserAccessor<T>.Writer);
#endif
        }

        /// <inheritdoc/>
        public void Serialize(Type type, object @object, IBufferWriter<byte> buffer)
        {
            _serializes[type.TypeHandle](buffer, @object);
        }

        /// <inheritdoc/>
        public byte[] Serialize(Type type, object @object)
        {
            if (_cachedStream == null)
            {
                _cachedStream = new MemoryStreamBuffer();
            }
            else
            {
                _cachedStream.SetLength(0);
                _cachedStream.Position = 0;
            }

            _serializes[type.TypeHandle](_cachedStream, @object);
            return _cachedStream.ToArray();
        }

        /// <inheritdoc/>
        public byte[] Serialize<T>(T @object) where T : IProtoParser<T>
        {
#if NET7_0_OR_GREATER
            return @object.ToByteArray();
#else
            return @object.ToByteArray(ProtoParserAccessor<T>.Writer);
#endif
        }

        #endregion

        #region Clone

        /// <inheritdoc/>
        public T Clone<T>(T t) where T : IProtoParser<T>
        {
            return Deserialize<T>(Serialize(t));
        }
        
        /// <inheritdoc/>
        public object Clone(Type type, object @object)
        {
            if (_cachedStream == null)
            {
                _cachedStream = new MemoryStreamBuffer();
            }
            else
            {
                _cachedStream.SetLength(0);
                _cachedStream.Position = 0;
            }

            _serializes[type.TypeHandle](_cachedStream, @object);
            return _deserializes[type.TypeHandle](_cachedStream);
        }

        #endregion
    }
}
