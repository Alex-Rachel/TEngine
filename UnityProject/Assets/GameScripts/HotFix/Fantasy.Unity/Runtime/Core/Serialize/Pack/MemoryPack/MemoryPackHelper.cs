using System;
using System.Buffers;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.DataStructure.Dictionary;
using Fantasy.Entitas;
using LightProto;
using MemoryPack;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.Serialize
{
    /// <summary>
    /// MemoryPack 序列化实现，提供高性能的二进制序列化/反序列化功能。
    /// 使用 Cysharp.MemoryPack 库，支持零编码极致性能的序列化。
    /// </summary>
    public sealed class MemoryPackHelper : ISerialize, IAssemblyLifecycle
    {
        /// <inheritdoc/>
        public string SerializeName { get; } = "MemoryPack";
        
        private static volatile Int64FrozenDictionary<Type> _types;
        private static readonly Int64MergerFrozenDictionary<Type> TypesMerger = new();
        
        public static Int64FrozenDictionary<Type> TypeDictionary => _types;

        #region AssemblyManifest

        /// <summary>
        /// 初始化 ProtoBuf 序列化器并注册到程序集生命周期管理。
        /// </summary>
        /// <returns>初始化后的 ProtoBufPack 实例</returns>
        internal async FTask<MemoryPackHelper> Initialize()
        {
            if (_types == null)
            {
                Interlocked.CompareExchange(
                    ref _types,
                    new Int64FrozenDictionary<Type>(new long [1] { 1 }, new Type[1] { typeof(MemoryPackHelper) }),
                    null);
            }
#if FANTASY_UNITY
            MemoryPackUnityFormatterProviderInitializer.RegisterInitialFormatters();
#endif
#if FANTASY_NET
            MemoryPackFormatterProvider.Register(new SphereEventArgsFormatter());
#endif
            MemoryPackFormatterProvider.Register(new EntityFormatter());
            MemoryPackFormatterProvider.Register(new EntityTreeCollectionFormatter());
            MemoryPackFormatterProvider.Register(new EntityMultiCollectionFormatter());
            await AssemblyLifecycle.Add(this);
            return this;
        }

        /// <summary>
        /// 程序集加载时的回调
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        public async FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            var memoryPackEntityGenerator = assemblyManifest.MemoryPackEntityGenerator;
            memoryPackEntityGenerator.Initialize();
            
            if (!memoryPackEntityGenerator.EntityTypes().Any())
            {
                return;
            }
            
            if (ProgramDefine.IsAppRunning)
            {
                var tcs = FTask.Create(false);
                ThreadScheduler.MainScheduler.ThreadSynchronizationContext.Post(() =>
                {
                    InnerOnLoad(assemblyManifest.AssemblyManifestId, memoryPackEntityGenerator);
                    tcs.SetResult();
                });
                await tcs;
                return;
            }
            
            InnerOnLoad(assemblyManifest.AssemblyManifestId, memoryPackEntityGenerator);
        }

        private void InnerOnLoad(long assemblyManifestId,IMemoryPackEntityGenerator memoryPackEntityGenerator)
        {
            TypesMerger.Add(
                assemblyManifestId,
                memoryPackEntityGenerator.EntityTypeHashCodes(),
                memoryPackEntityGenerator.EntityTypes());
                
            Interlocked.Exchange(ref _types, TypesMerger.GetFrozenDictionary());
        }

        /// <summary>
        /// 程序集卸载时的回调。
        /// </summary>
        /// <param name="assemblyManifest">程序集清单对象，包含程序集的元数据和注册器</param>
        public FTask OnUnload(AssemblyManifest assemblyManifest)
        {
            var tcs = FTask.Create(false);
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;
            
            ThreadScheduler.MainScheduler.ThreadSynchronizationContext.Post(() =>
            {
                if (TypesMerger.Remove(assemblyManifestId))
                {
                    Interlocked.Exchange(ref _types, TypesMerger.GetFrozenDictionary());
                }
                tcs.SetResult();
            });
            
            return tcs;
        }

        #endregion

        /// <inheritdoc/>
        public T Deserialize<T>(byte[] bytes) where T : IProtoParser<T>
        {
            return MemoryPackSerializer.Deserialize<T>(bytes)!;
        }

        /// <inheritdoc/>
        public T Deserialize<T>(MemoryStreamBuffer buffer) where T : IProtoParser<T>
        {
            var span = new ReadOnlySpan<byte>(buffer.GetBuffer(), (int)buffer.Position,
                (int)(buffer.Length - buffer.Position));
            return MemoryPackSerializer.Deserialize<T>(span)!;
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, byte[] bytes)
        {
            return MemoryPackSerializer.Deserialize(type, bytes)!;
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, MemoryStreamBuffer buffer)
        {
            var span = new ReadOnlySpan<byte>(buffer.GetBuffer(), (int)buffer.Position,
                (int)(buffer.Length - buffer.Position));
            return MemoryPackSerializer.Deserialize(type, span)!;
        }

        /// <inheritdoc/>
        public T Deserialize<T>(byte[] bytes, int index, int count) where T : IProtoParser<T>
        {
            var span = new ReadOnlySpan<byte>(bytes, index, count);
            return MemoryPackSerializer.Deserialize<T>(span)!;
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, byte[] bytes, int index, int count)
        {
            var span = new ReadOnlySpan<byte>(bytes, index, count);
            return MemoryPackSerializer.Deserialize(type, span)!;
        }

        /// <inheritdoc/>
        public byte[] Serialize(Type type, object obj)
        {
            return MemoryPackSerializer.Serialize(type, obj);
        }

        /// <inheritdoc/>
        public byte[] Serialize<T>(T @object) where T : IProtoParser<T>
        {
            return MemoryPackSerializer.Serialize(@object);
        }

        /// <inheritdoc/>
        public void Serialize<T>(T @object, IBufferWriter<byte> buffer) where T : IProtoParser<T>
        {
            MemoryPackSerializer.Serialize(buffer, @object);
        }

        /// <inheritdoc/>
        public void Serialize(Type type, object @object, IBufferWriter<byte> buffer)
        {
            MemoryPackSerializer.Serialize(type, buffer, @object);
        }

        /// <inheritdoc/>
        public T Clone<T>(T t) where T : IProtoParser<T>
        {
            var bytes = MemoryPackSerializer.Serialize(t);
            return MemoryPackSerializer.Deserialize<T>(bytes)!;
        }

        /// <inheritdoc/>
        public object Clone(Type type, object @object)
        {
            var bytes = MemoryPackSerializer.Serialize(type, @object);
            var span = new ReadOnlySpan<byte>(bytes, 0, bytes.Length);
            return MemoryPackSerializer.Deserialize(type, span)!;
        }
    }
}