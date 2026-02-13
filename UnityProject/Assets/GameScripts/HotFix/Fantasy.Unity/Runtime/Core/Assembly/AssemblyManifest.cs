using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Fantasy.Async;
using Fantasy.DataStructure.Collection;
using Fantasy.Entitas.Interface;
#if FANTASY_NET
using System.Collections.Frozen;
#endif

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.Assembly
{
    /// <summary>
    /// 程序集清单类
    /// 封装程序集的元数据和各种系统注册器，用于统一管理程序集的生命周期和系统注册
    /// </summary>
    public sealed class AssemblyManifest
    {
        /// <summary>
        /// 程序集唯一标识符
        /// 通过程序集名称的哈希值生成
        /// </summary>
        public long AssemblyManifestId { get; private set; }

        /// <summary>
        /// 程序集实例
        /// </summary>
        public System.Reflection.Assembly Assembly { get; private set; }

        /// <summary>
        /// 网络协议 序列化类型注册器
        /// </summary>
        internal INetworkProtocolRegistrar NetworkProtocolRegistrar { get; private set; }
        
        /// <summary>
        /// ProtoBuf 分发器注册器接口用于解决 Native AOT 和 IL2CPP 下 ProtoBuf 反射问题
        /// </summary>
        internal IProtoBufDispatcherRegistrar ProtoBufDispatcherRegistrar { get; private set; }

        /// <summary>
        /// 事件系统注册器
        /// </summary>
        internal IEventSystemRegistrar EventSystemRegistrar { get; private set; }

        /// <summary>
        /// 实体系统注册器
        /// </summary>
        internal IEntitySystemRegistrar EntitySystemRegistrar { get; private set; }

        /// <summary>
        /// 消息分发器注册器
        /// </summary>
        internal IMessageHandlerResolver MessageHandlerResolver { get; private set; }

        /// <summary>
        /// 实体类型集合注册器
        /// </summary>
        internal IEntityTypeCollectionRegistrar EntityTypeCollectionRegistrar { get; private set; }
        
        /// <summary>
        /// 网络协议 OpCode 注册器
        /// </summary>
        internal IOpCodeRegistrar OpCodeRegistrar { get; private set; }
        
        /// <summary>
        /// 网络协议Response注册器
        /// </summary>
        internal IResponseTypeRegistrar ResponseTypeRegistrar { get; private set; }
        
        /// <summary>
        /// 自定义接口
        /// </summary>
        internal ICustomInterfaceRegistrar CustomInterfaceRegistrar { get; private set; }

        /// <summary>
        /// 池生成器注册器
        /// </summary>
        internal IPoolCreatorGenerator PoolCreatorGenerator { get; set; }

        /// <summary>
        /// MemoryPack注册器
        /// </summary>
        internal IMemoryPackEntityGenerator MemoryPackEntityGenerator { get; set; }
#if FANTASY_NET
        /// <summary>
        /// 分表注册器
        /// </summary>
        internal ISeparateTableRegistrar SeparateTableRegistrar { get; private set; }
        internal ISphereEventRegistrar SphereEventRegistrar { get; private set; }
#endif
        private readonly OneToManyList<RuntimeTypeHandle, Type> _customInterfaces = new();
#if FANTASY_WEBGL
        /// <summary>
        /// 程序集清单集合（WebGL 单线程版本）
        /// Key: 程序集唯一标识, Value: 程序集清单对象
        /// </summary>
        private static readonly Dictionary<long, AssemblyManifest> Manifests = new Dictionary<long, AssemblyManifest>();
#else
        /// <summary>
        /// 程序集清单集合（线程安全版本）
        /// Key: 程序集唯一标识, Value: 程序集清单对象
        /// </summary>
        internal static readonly ConcurrentDictionary<long, AssemblyManifest> Manifests = new ConcurrentDictionary<long, AssemblyManifest>();
#endif
        /// <summary>
        /// 清理程序集清单内部资源
        /// 释放所有注册器并清空引用
        /// </summary>
        internal void Clear()
        {
            Assembly = null;
            NetworkProtocolRegistrar = null;
            EventSystemRegistrar = null;
            EntitySystemRegistrar = null;
            MessageHandlerResolver = null;
            EntityTypeCollectionRegistrar = null;
            PoolCreatorGenerator = null;
#if FANTASY_NET
            SeparateTableRegistrar = null;
            SphereEventRegistrar = null;
#endif
        }
        
        #region static

#if FANTASY_NET
        /// <summary>
        /// 注册程序集清单
        /// 此方法由 Source Generator 生成的 ModuleInitializer 自动调用
        /// 直接创建并缓存完整的 AssemblyManifest
        /// </summary>
        /// <param name="assemblyManifestId">程序集唯一标识（通过程序集名称哈希生成）</param>
        /// <param name="assembly">程序集实例</param>
        /// <param name="networkProtocolRegistrar">网络协议注册器</param>
        /// <param name="eventSystemRegistrar">事件系统注册器</param>
        /// <param name="entitySystemRegistrar">实体系统注册器</param>
        /// <param name="messageHandlerResolver">消息分发器注册器</param>
        /// <param name="entityTypeCollectionRegistrar">实体类型集合注册器</param>
        /// <param name="separateTableRegistrar">分表注册器</param>
        /// <param name="opCodeRegistrar">网络协议 OpCode 注册器</param>
        /// <param name="responseTypeRegistrar">网络协议Response注册器</param>
        /// <param name="sphereEventRegistrar">领域事件系统注册器</param>
        /// <param name="customInterfaceRegistrar">自定接口注册器</param>
        /// <param name="fantasyConfigRegistrar">Fantasy配置注册器</param>
        /// <param name="poolCreatorGenerator">池生成器注册器</param>
        /// <param name="protoBufDispatcherRegistrar">Protobuf消息分发器</param>
        /// <param name="memoryPackEntityGenerator">memoryPack生成器</param>
        public static void Register(
            long assemblyManifestId,
            System.Reflection.Assembly assembly,
            INetworkProtocolRegistrar networkProtocolRegistrar,
            IEventSystemRegistrar eventSystemRegistrar,
            IEntitySystemRegistrar entitySystemRegistrar,
            IMessageHandlerResolver messageHandlerResolver,
            IEntityTypeCollectionRegistrar entityTypeCollectionRegistrar,
            ISeparateTableRegistrar separateTableRegistrar,
            IOpCodeRegistrar opCodeRegistrar,
            IResponseTypeRegistrar responseTypeRegistrar,
            ISphereEventRegistrar sphereEventRegistrar,
            ICustomInterfaceRegistrar customInterfaceRegistrar,
            IFantasyConfigRegistrar fantasyConfigRegistrar,
            IPoolCreatorGenerator poolCreatorGenerator,
            IProtoBufDispatcherRegistrar protoBufDispatcherRegistrar,
            IMemoryPackEntityGenerator memoryPackEntityGenerator)
        {
            var manifest = new AssemblyManifest
            {
                Assembly = assembly,
                AssemblyManifestId = assemblyManifestId,
                NetworkProtocolRegistrar = networkProtocolRegistrar,
                EventSystemRegistrar = eventSystemRegistrar,
                EntitySystemRegistrar = entitySystemRegistrar,
                MessageHandlerResolver = messageHandlerResolver,
                EntityTypeCollectionRegistrar = entityTypeCollectionRegistrar,
                SeparateTableRegistrar = separateTableRegistrar,
                OpCodeRegistrar = opCodeRegistrar,
                ResponseTypeRegistrar = responseTypeRegistrar,
                SphereEventRegistrar = sphereEventRegistrar,
                CustomInterfaceRegistrar = customInterfaceRegistrar,
                PoolCreatorGenerator = poolCreatorGenerator,
                ProtoBufDispatcherRegistrar = protoBufDispatcherRegistrar,
                MemoryPackEntityGenerator = memoryPackEntityGenerator
            };

            // 设置数据库名字字典
            var databaseNameDictionary = fantasyConfigRegistrar.GetDatabaseNameDictionary();
            if (databaseNameDictionary.Any())
            {
                Fantasy.Database.DataBaseHelper.DatabaseDbName = databaseNameDictionary.ToFrozenDictionary();
            }
            // 设置SceneType字典
            var sceneTypeDictionary = fantasyConfigRegistrar.GetSceneTypeDictionary();
            if (sceneTypeDictionary.Any())
            { 
                Scene.SceneTypeDictionary = sceneTypeDictionary.ToFrozenDictionary();
            }
            customInterfaceRegistrar.Register(manifest._customInterfaces);
            Manifests.TryAdd(assemblyManifestId, manifest);
            AssemblyLifecycle.OnLoad(manifest).Coroutine();
        }
#endif
#if FANTASY_UNITY
        /// <summary>
        /// 注册程序集清单
        /// 此方法由 Source Generator 生成的 ModuleInitializer 自动调用
        /// 直接创建并缓存完整的 AssemblyManifest
        /// </summary>
        /// <param name="assemblyManifestId">程序集唯一标识（通过程序集名称哈希生成）</param>
        /// <param name="assembly">程序集实例</param>
        /// <param name="networkProtocolRegistrar">网络协议注册器</param>
        /// <param name="eventSystemRegistrar">事件系统注册器</param>
        /// <param name="entitySystemRegistrar">实体系统注册器</param>
        /// <param name="messageHandlerResolver">消息分发器注册器</param>
        /// <param name="entityTypeCollectionRegistrar">实体类型集合注册器</param>
        /// <param name="opCodeRegistrar">网络协议 OpCode 注册器</param>
        /// <param name="responseTypeRegistrar">网络协议 Response 注册器</param>
        /// <param name="customInterfaceRegistrar">自定接口注册器</param>
        /// <param name="poolCreatorGenerator">池生成器注册器</param>
        /// <param name="protoBufDispatcherRegistrar">Protobuf消息分发器</param>
        /// <param name="memoryPackEntityGenerator">memoryPack生成器</param>
        public static void Register(
            long assemblyManifestId,
            System.Reflection.Assembly assembly,
            INetworkProtocolRegistrar networkProtocolRegistrar,
            IEventSystemRegistrar eventSystemRegistrar,
            IEntitySystemRegistrar entitySystemRegistrar,
            IMessageHandlerResolver messageHandlerResolver,
            IEntityTypeCollectionRegistrar entityTypeCollectionRegistrar,
            IOpCodeRegistrar opCodeRegistrar,
            IResponseTypeRegistrar responseTypeRegistrar,
            ICustomInterfaceRegistrar customInterfaceRegistrar,
            IPoolCreatorGenerator poolCreatorGenerator,
            IProtoBufDispatcherRegistrar protoBufDispatcherRegistrar,
            IMemoryPackEntityGenerator memoryPackEntityGenerator)
        {
            var manifest = new AssemblyManifest
            {
                Assembly = assembly,
                AssemblyManifestId = assemblyManifestId,
                NetworkProtocolRegistrar = networkProtocolRegistrar,
                EventSystemRegistrar = eventSystemRegistrar,
                EntitySystemRegistrar = entitySystemRegistrar,
                MessageHandlerResolver = messageHandlerResolver,
                EntityTypeCollectionRegistrar = entityTypeCollectionRegistrar,
                OpCodeRegistrar = opCodeRegistrar,
                ResponseTypeRegistrar = responseTypeRegistrar,
                CustomInterfaceRegistrar = customInterfaceRegistrar,
                PoolCreatorGenerator = poolCreatorGenerator,
                ProtoBufDispatcherRegistrar = protoBufDispatcherRegistrar,
                MemoryPackEntityGenerator = memoryPackEntityGenerator
            };
#if FANTASY_WEBGL
            Manifests[assemblyManifestId] = manifest;
#else
            Manifests.TryAdd(assemblyManifestId, manifest);
#endif
            customInterfaceRegistrar.Register(manifest._customInterfaces);
            AssemblyLifecycle.OnLoad(manifest).Coroutine();
        }
#endif
        /// <summary>
        /// 取消注册指定程序集的清单
        /// </summary>
        /// <param name="assemblyManifestId">程序集唯一标识</param>
        public static void Unregister(long assemblyManifestId)
        {
#if FANTASY_WEBGL
            if (Manifests.TryGetValue(assemblyManifestId, out var manifest))
            {
                AssemblyLifecycle.OnUnLoad(manifest).Coroutine();
                Manifests.Remove(assemblyManifestId);
                manifest.CustomInterfaceRegistrar.UnRegister(manifest.CustomInterfaces);
            }
#else
            if (Manifests.TryRemove(assemblyManifestId, out var manifest))
            {
                AssemblyLifecycle.OnUnLoad(manifest).Coroutine();
                manifest.CustomInterfaceRegistrar.UnRegister(manifest._customInterfaces);
            }
#endif
        }

        /// <summary>
        /// 获取当前框架注册的所有程序集清单
        /// 通过迭代器模式返回所有已注册的程序集清单对象
        /// </summary>
        public static IEnumerable<AssemblyManifest> GetAssemblyManifest
        {
            get
            {
                foreach (var (_, assemblyManifest) in Manifests)
                {
                    yield return assemblyManifest;
                }
            }
        }

        /// <summary>
        /// 获取所有程序集中实现ICustomRegistrar接口中指定类型的所有类型。
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns></returns>
        public static IEnumerable<Type> ForEach(Type type)
        {
            foreach (var (_, assemblyManifest) in Manifests)
            {
                if (!assemblyManifest._customInterfaces.TryGetValue(type.TypeHandle, out var customRegistrars))
                {
                    continue;
                }

                foreach (var customRegistrar in customRegistrars)
                {
                    yield return customRegistrar;
                }
            }
        }
        

        /// <summary>
        /// 获取所有程序集中实现ICustomRegistrar接口中指定类型的所有类型。
        /// </summary>
        /// <returns>所有程序集中实现ICustomRegistrar接口中指定类型的所有类型。</returns>
        public static IEnumerable<Type> ForEach<T>()
        {
            foreach (var (_, assemblyManifest) in Manifests)
            {
                if (!assemblyManifest._customInterfaces.TryGetValue(typeof(T).TypeHandle, out var customRegistrars))
                {
                    continue;
                }

                foreach (var customRegistrar in customRegistrars)
                {
                    yield return customRegistrar;
                }
            }
        }

        /// <summary>
        /// 获取指定程序集中实现指定类型的所有类型。
        /// </summary>
        /// <param name="assemblyManifest">程序集清单。</param>
        /// <param name="type">类型</param>
        /// <returns></returns>
        public static IEnumerable<Type> ForEach(AssemblyManifest assemblyManifest, Type type)
        {
            if (!assemblyManifest._customInterfaces.TryGetValue(type.TypeHandle, out var customRegistrars))
            {
                yield break;
            }
            
            foreach (var customType in customRegistrars)
            {
                yield return customType;
            }
        }
        
        /// <summary>
        /// 获取指定程序集中实现指定类型的所有类型。
        /// </summary>
        /// <param name="assemblyManifest">程序集清单。</param>
        /// <returns>指定程序集中实现指定类型的类型。</returns>
        public static IEnumerable<Type> ForEach<T>(AssemblyManifest assemblyManifest)
        {
            if (!assemblyManifest._customInterfaces.TryGetValue(typeof(T).TypeHandle, out var customRegistrars))
            {
                yield break;
            }
            
            foreach (var type in customRegistrars)
            {
                yield return type;
            }
        }

        /// <summary>
        /// 释放所有程序集清单资源
        /// 卸载所有已注册的程序集，触发卸载事件，清理所有注册器和生命周期回调
        /// </summary>
        /// <returns>异步任务</returns>
        public static async FTask Dispose()
        {
            foreach (var (_, assemblyManifest) in Manifests)
            {
                await AssemblyLifecycle.OnUnLoad(assemblyManifest);
                assemblyManifest.Clear();
            }
            
            Manifests.Clear();
            AssemblyLifecycle.Dispose();
        }

        #endregion
    }
}