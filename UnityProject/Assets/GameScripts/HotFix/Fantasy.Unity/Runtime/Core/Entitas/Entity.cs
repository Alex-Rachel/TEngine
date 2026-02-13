using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Fantasy.Entitas.Interface;
using Fantasy.IdFactory;
using Fantasy.Pool;
using LightProto;
using MemoryPack;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable MergeIntoPattern
// ReSharper disable SuspiciousTypeConversion.Global
// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
// ReSharper disable CheckNamespace
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Entitas
{
    /// <summary>
    /// 用来表示一个Entity
    /// </summary>
    public interface IEntity : IDisposable, IPool { }
    
    /// <summary>
    /// Entity的抽象类，任何Entity必须继承这个接口才可以使用
    /// </summary>
    [MemoryPackable(GenerateType.NoGenerate)]
    public abstract partial class Entity : IEntity
    {
        #region Members
        
        /// <summary>
        /// 实体的Id
        /// </summary>
        [BsonId]
        [BsonElement]
        [BsonIgnoreIfDefault]
        [BsonDefaultValue(0L)]
        public long Id { get; protected set; }
        /// <summary>
        /// 实体的RunTimeId
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        [IgnoreDataMember]
        [ProtoIgnore]
        [MemoryPackIgnore]
        public long RuntimeId { get; protected set; }
        /// <summary>
        /// 当前实体是否已经被销毁
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        [IgnoreDataMember]
        [ProtoIgnore]
        [MemoryPackIgnore]
        public bool IsDisposed => RuntimeId == 0;
        /// <summary>
        /// 当前实体所归属的Scene
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        [IgnoreDataMember]
        [ProtoIgnore]
        [MemoryPackIgnore]
        public Scene Scene { get; protected set; }
        /// <summary>
        /// 实体的父实体
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        [IgnoreDataMember]
        [ProtoIgnore]
        [MemoryPackIgnore]
        public Entity Parent { get; protected set; }
        /// <summary>
        /// 实体的真实Type
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        [IgnoreDataMember]
        [ProtoIgnore]
        [MemoryPackIgnore]
        public Type Type { get; protected set; }
        /// <summary>
        /// 实体的真实Type的HashCode
        /// </summary>
        public long TypeHashCode { get; protected set; }

        [BsonElement("t")] [BsonIgnoreIfNull] [MemoryPackInclude] protected EntityTreeCollection Tree;
        [BsonElement("m")] [BsonIgnoreIfNull] [MemoryPackInclude] protected EntityMultiCollection Multi;
        
        /// <summary>
        /// 获得父Entity
        /// </summary>
        /// <typeparam name="T">父实体的泛型类型</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetParent<T>() where T : Entity, new()
        {
            return Parent as T;
        }

        /// <summary>
        /// 获取当前实体的网络地址。
        /// </summary>
        public long Address => RuntimeId;

        #endregion

        #region Create

        /// <summary>
        /// 创建一个实体,默认在对象池创建,执行组件事件。
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <param name="type">实体的Type</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity Create(Scene scene, Type type)
        {
            return Create(scene, type, scene.EntityIdFactory.Create, true, true);
        }

        /// <summary>
        /// 创建一个实体,默认执行组件事件。
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <param name="type">实体的Type</param>
        /// <param name="isPool">是否从对象池创建，如果选择的是，销毁的时候同样会进入对象池</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity Create(Scene scene, Type type, bool isPool)
        {
            return Create(scene, type, scene.EntityIdFactory.Create, isPool, true);
        }

        /// <summary>
        /// 创建一个实体
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <param name="type">实体的Type</param>
        /// <param name="isPool">是否从对象池创建，如果选择的是，销毁的时候同样会进入对象池</param>
        /// <param name="isRunEvent">是否执行实体事件</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity Create(Scene scene, Type type, bool isPool, bool isRunEvent)
        {
            return Create(scene, type, scene.EntityIdFactory.Create, isPool, isRunEvent);
        }

        /// <summary>
        /// 创建一个实体
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <param name="type">实体的Type</param>
        /// <param name="id">指定实体的Id</param>
        /// <param name="isPool">是否从对象池创建，如果选择的是，销毁的时候同样会进入对象池</param>
        /// <param name="isRunEvent">是否执行实体事件</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity Create(Scene scene, Type type, long id, bool isPool, bool isRunEvent)
        {
            if (!typeof(Entity).IsAssignableFrom(type))
            {
                throw new NotSupportedException($"Type:{type.FullName} must inherit from Entity");
            }
            
            Entity entity = null;
            
            if (isPool)
            {
                entity = (Entity)scene.EntityPool.Rent(scene, type);
            }
            else
            {
                entity = scene.PoolGeneratorComponent.Create<Entity>(type);
            }
            
            entity.Scene = scene;
            entity.Type = type;
            entity.TypeHashCode = TypeHashCache.GetHashCode(type);
            entity.SetIsPool(isPool);
            entity.Id = id;
            entity.RuntimeId = scene.RuntimeIdFactory.Create(isPool);
            scene.AddEntity(entity);
            
            if (isRunEvent)
            {
                scene.EntityComponent.Awake(entity);
                scene.EntityComponent.RegisterUpdate(entity);
#if FANTASY_UNITY
                scene.EntityComponent.RegisterLateUpdate(entity);
#endif
            }
            
            return entity;
        }

        /// <summary>
        /// 创建一个实体,默认在对象池创建,执行组件事件。
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <typeparam name="T">要创建的实体泛型类型</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Create<T>(Scene scene) where T : Entity, new()
        {
            return Create<T>(scene, scene.EntityIdFactory.Create, true, true);
        }

        /// <summary>
        /// 创建一个实体,默认执行组件事件。
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <param name="isPool">是否从对象池创建，如果选择的是，销毁的时候同样会进入对象池</param>
        /// <typeparam name="T">要创建的实体泛型类型</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Create<T>(Scene scene, bool isPool) where T : Entity, new()
        {
            return Create<T>(scene, scene.EntityIdFactory.Create, isPool, true);
        }
        
        /// <summary>
        /// 创建一个实体
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <param name="isPool">是否从对象池创建，如果选择的是，销毁的时候同样会进入对象池</param>
        /// <param name="isRunEvent">是否执行实体事件</param>
        /// <typeparam name="T">要创建的实体泛型类型</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Create<T>(Scene scene, bool isPool, bool isRunEvent) where T : Entity, new()
        {
            return Create<T>(scene, scene.EntityIdFactory.Create, isPool, isRunEvent);
        }
        
        /// <summary>
        /// 创建一个实体
        /// </summary>
        /// <param name="scene">所属的Scene</param>
        /// <param name="id">指定实体的Id</param>
        /// <param name="isPool">是否从对象池创建，如果选择的是，销毁的时候同样会进入对象池</param>
        /// <param name="isRunEvent">是否执行实体事件</param>
        /// <typeparam name="T">要创建的实体泛型类型</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Create<T>(Scene scene, long id, bool isPool, bool isRunEvent) where T : Entity, new()
        {
            var entity = isPool ? scene.EntityPool.Rent<T>() : new T();
            entity.Scene = scene;
            entity.Type = typeof(T);
            entity.TypeHashCode = TypeHashCache<T>.HashCode;
            entity.SetIsPool(isPool);
            entity.Id = id;
            entity.RuntimeId = scene.RuntimeIdFactory.Create(isPool);
            scene.AddEntity(entity);
            
            if (isRunEvent)
            {
                scene.EntityComponent.Awake(entity);
                scene.EntityComponent.RegisterUpdate(entity);
#if FANTASY_UNITY
                scene.EntityComponent.RegisterLateUpdate(entity);
#endif
            }

            return entity;
        }

        #endregion
        
        #region AddComponent

        /// <summary>
        /// 添加一个组件到当前实体上
        /// </summary>
        /// <param name="isPool">是否从对象池里创建</param>
        /// <typeparam name="T">要添加组件的泛型类型</typeparam>
        /// <returns>返回添加到实体上组件的实例</returns>
        public T AddComponent<T>(bool isPool = true) where T : Entity, new()
        {
            var id = EntitySupportedChecker<T>.IsMulti ? Scene.EntityIdFactory.Create : Id;
            var entity = Create<T>(Scene, id, isPool, false);
            AddComponent(entity);
            Scene.EntityComponent.Awake(entity);
            Scene.EntityComponent.RegisterUpdate(entity);
#if FANTASY_UNITY
            Scene.EntityComponent.RegisterLateUpdate(entity);
#endif
            return entity;
        }

        /// <summary>
        /// 添加一个组件到当前实体上
        /// </summary>
        /// <param name="id">要添加组件的Id</param>
        /// <param name="isPool">是否从对象池里创建</param>
        /// <typeparam name="T">要添加组件的泛型类型</typeparam>
        /// <returns>返回添加到实体上组件的实例</returns>
        public T AddComponent<T>(long id, bool isPool = true) where T : Entity, new()
        {
            var entity = Create<T>(Scene, id, isPool, false);
            AddComponent(entity);
            Scene.EntityComponent.Awake(entity);
            Scene.EntityComponent.RegisterUpdate(entity);
#if FANTASY_UNITY
            Scene.EntityComponent.RegisterLateUpdate(entity);
#endif
            return entity;
        }

        /// <summary>
        /// 添加一个组件到当前实体上
        /// </summary>
        /// <param name="component">要添加的实体实例</param>
        public void AddComponent(Entity component)
        {
            if (this == component)
            {
                Log.Error("Cannot add oneself to one's own components");
                return;
            }

            if (component.IsDisposed)
            {
                Log.Error($"component is Disposed {component.Type.FullName}");
                return;
            }

            var type = component.Type;
            component.Parent?.RemoveComponent(component, false);

            if (component is ISupportedMultiEntity)
            {
                Multi ??= EntityMultiCollection.Create(true);
                Multi.Add(component.Id, component);
            }
            else
            {
                var typeHashCode = component.TypeHashCode;
                
                if (Tree == null)
                {
                    Tree = EntityTreeCollection.Create(true);
                }
                else if (Tree.ContainsKey(typeHashCode))
                {
                    Log.Error($"type:{type.FullName} If you want to add multiple components of the same type, please implement IMultiEntity");
                    return;
                }
                
                Tree.Add(typeHashCode, component);
            }
            
            component.Parent = this;
            component.Scene = Scene;
        }

        /// <summary>
        /// 添加一个组件到当前实体上
        /// </summary>
        /// <param name="component">要添加的实体实例</param>
        /// <typeparam name="T">要添加组件的泛型类型</typeparam>
        public void AddComponent<T>(T component) where T : Entity
        {
            if (this == component)
            {
                Log.Error("Cannot add oneself to one's own components");
                return;
            }

            if (component.IsDisposed)
            {
                Log.Error($"component is Disposed {typeof(T).FullName}");
                return;
            }
            
            component.Parent?.RemoveComponent(component, false);
            
            if (EntitySupportedChecker<T>.IsMulti)
            {
                Multi ??= EntityMultiCollection.Create(true);
                Multi.Add(component.Id, component);
            }
            else
            {
                var typeHashCode = component.TypeHashCode;
                
                if (Tree == null)
                {
                    Tree = EntityTreeCollection.Create(true);
                }
                else if (Tree.ContainsKey(typeHashCode))
                {
                    Log.Error($"type:{typeof(T).FullName} If you want to add multiple components of the same type, please implement IMultiEntity");
                    return;
                }
                
                Tree.Add(typeHashCode, component);
            }
            
            component.Parent = this;
            component.Scene = Scene;
        }

        /// <summary>
        /// 添加一个组件到当前实体上
        /// </summary>
        /// <param name="type">组件的类型</param>
        /// <param name="isPool">是否在对象池创建</param>
        /// <returns></returns>
        public Entity AddComponent(Type type, bool isPool = true)
        {
            var id = typeof(ISupportedMultiEntity).IsAssignableFrom(type) ? Scene.EntityIdFactory.Create : Id;
            var entity = Entity.Create(Scene, type, id, isPool, false);
            AddComponent(entity);
            Scene.EntityComponent.Awake(entity);
            Scene.EntityComponent.RegisterUpdate(entity);
#if FANTASY_UNITY
            Scene.EntityComponent.RegisterLateUpdate(entity);
#endif
            return entity;
        }

        #endregion

        #region HasComponent

        /// <summary>
        /// 当前实体上是否有指定类型的组件
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>() where T : Entity, new()
        {
            if (Tree == null)
            {
                return false;
            }
            
            return Tree.ContainsKey(TypeHashCache<T>.HashCode);
        }

        /// <summary>
        /// 当前实体上是否有指定类型的组件
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent(Type type)
        {
            if (Tree == null)
            {
                return false;
            }

            return Tree.ContainsKey(TypeHashCache.GetHashCode(type));
        }

        /// <summary>
        /// 当前实体上是否有指定类型的组件
        /// </summary>
        /// <param name="id"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>(long id) where T : Entity, ISupportedMultiEntity, new()
        {
            if (Multi == null)
            {
                return false;
            }

            return Multi.ContainsKey(id);
        }

        #endregion

        #region GetComponent

        /// <summary>
        /// 当前实体上查找一个子实体
        /// </summary>
        /// <typeparam name="T">要查找实体泛型类型</typeparam>
        /// <returns>查找的实体实例</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetComponent<T>() where T : Entity, new()
        {
            if (Tree == null)
            {
                return null;
            }
            
            return Tree.TryGetValue(TypeHashCache<T>.HashCode, out var component) ? (T)component : null;
        }

        /// <summary>
        /// 当前实体上查找一个子实体
        /// </summary>
        /// <param name="type">要查找实体类型</param>
        /// <returns>查找的实体实例</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetComponent(Type type)
        {
            if (Tree == null)
            {
                return null;
            }
            
            return Tree.GetValueOrDefault(TypeHashCache.GetHashCode(type));
        }

        /// <summary>
        /// 当前实体上查找一个子实体
        /// </summary>
        /// <param name="id">要查找实体的Id</param>
        /// <typeparam name="T">要查找实体泛型类型</typeparam>
        /// <returns>查找的实体实例</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetComponent<T>(long id) where T : Entity, ISupportedMultiEntity, new()
        {
            if (Multi == null)
            {
                return null;
            }

            return Multi.TryGetValue(id, out var entity) ? (T)entity : null;
        }

        /// <summary>
        /// 当前实体上查找一个子实体，如果没有就创建一个新的并添加到当前实体上
        /// </summary>
        /// <param name="isPool">是否从对象池创建</param>
        /// <typeparam name="T">要查找或添加实体泛型类型</typeparam>
        /// <returns>查找的实体实例</returns>
        public T GetOrAddComponent<T>(bool isPool = true) where T : Entity, new()
        {
            return GetComponent<T>() ?? AddComponent<T>(isPool);
        }

        #endregion

        #region RemoveComponent

        /// <summary>
        /// 分离一个组件但不销毁它
        /// </summary>
        /// <param name="type">组件的类型</param>
        /// <param name="component">返回分离的组件实例</param>
        /// <returns>返回是否分离成功</returns>
        public bool DetachComponent(Type type, out Entity component)
        {
            component = null;
            
            if (Tree == null)
            {
                return false;
            }

            var typeHashCode = TypeHashCache.GetHashCode(type);

            if (!Tree.Remove(typeHashCode, out component!))
            {
                return false;
            }

            if (Tree.Count != 0)
            {
                return true;
            }
            
            Tree.Dispose();
            Tree = null;
            return true;
        }

        /// <summary>
        /// 分离一个组件但不销毁它,该组件需实现ISupportedMultiEntity接口
        /// </summary>
        /// <param name="id">要分离的实体Id</param>
        /// <param name="component">返回分离的组件实例</param>
        /// <returns>返回是否分离成功</returns>
        public bool DetachComponent(long id, out Entity component)
        {
            component = null;

            if (Multi == null)
            {
                return false;
            }

            if (!Multi.Remove(id, out component!))
            {
                return false;
            }

            if (Multi.Count != 0)
            {
                return true;
            }
            
            Multi.Dispose();
            Multi = null;
            return true;

        }

        /// <summary>
        /// 当前实体下删除一个实体
        /// </summary>
        /// <param name="isDispose">是否执行删除实体的Dispose方法</param>
        /// <typeparam name="T">实体的泛型类型</typeparam>
        /// <exception cref="NotSupportedException"></exception>
        public void RemoveComponent<T>(bool isDispose = true) where T : Entity, new()
        {
            if (EntitySupportedChecker<T>.IsMulti)
            {
                throw new NotSupportedException($"{typeof(T).FullName} message:Cannot delete components that implement the ISupportedMultiEntity interface");
            }
            
            if (Tree == null)
            {
                return;
            }
            
            var typeHashCode = TypeHashCache<T>.HashCode;

            if (Tree.Remove(typeHashCode, out var component))
            {
                if (Tree.Count == 0)
                {
                    Tree.Dispose();
                    Tree = null;
                }
            }
            
            if (isDispose)
            {
                component.Dispose();
            }
        }

        /// <summary>
        /// 当前实体下删除一个实体
        /// </summary>
        /// <param name="id">要删除的实体Id</param>
        /// <param name="isDispose">是否执行删除实体的Dispose方法</param>
        /// <typeparam name="T">实体的泛型类型</typeparam>
        public void RemoveComponent<T>(long id, bool isDispose = true) where T : Entity, ISupportedMultiEntity, new()
        {
            if (Multi == null)
            {
                return;
            }

            if (Multi.Remove(id, out var component))
            {
                if (Multi.Count == 0)
                {
                    Multi.Dispose();
                    Multi = null;
                }
            }
            
            if (isDispose)
            {
                component.Dispose();
            }
        }

        /// <summary>
        /// 当前实体下删除一个实体
        /// </summary>
        /// <param name="component">要删除的实体实例</param>
        /// <param name="isDispose">是否执行删除实体的Dispose方法</param>
        public void RemoveComponent(Entity component, bool isDispose = true)
        {
            if (this == component)
            {
                return;
            }
            
            if (component is ISupportedMultiEntity)
            {
                if (Multi != null)
                {
                    if (Multi.Remove(component.Id))
                    {
                        if (Multi.Count == 0)
                        {
                            Multi.Dispose();
                            Multi = null;
                        }
                    }
                }
            }
            else if (Tree != null)
            {
                var typeHashCode = component.TypeHashCode;

                if (Tree.Remove(typeHashCode))
                {
                    if (Tree.Count == 0)
                    {
                        Tree.Dispose();
                        Tree = null;
                    }
                }
            }
            
            if (isDispose)
            {
                component.Dispose();
            }
        }

        /// <summary>
        /// 当前实体下删除一个实体
        /// </summary>
        /// <param name="component">要删除的实体实例</param>
        /// <param name="isDispose">是否执行删除实体的Dispose方法</param>
        /// <typeparam name="T">实体的泛型类型</typeparam>
        public void RemoveComponent<T>(T component, bool isDispose = true) where T : Entity
        {
            if (this == component)
            {
                return;
            }
            
            if (EntitySupportedChecker<T>.IsMulti)
            {
                if (Multi != null)
                {
                    if (Multi.Remove(component.Id))
                    {
                        if (Multi.Count == 0)
                        {
                            Multi.Dispose();
                            Multi = null;
                        }
                    }
                }
            }
            else if (Tree != null)
            {
                var typeHashCode = TypeHashCache<T>.HashCode;

                if (Tree.Remove(typeHashCode))
                {
                    if (Tree.Count == 0)
                    {
                        Tree.Dispose();
                        Tree = null;
                    }
                }
            }
            
            if (isDispose)
            {
                component.Dispose();
            }
        }

        #endregion

        #region Deserialize

        /// <summary>
        /// 反序列化当前实体，因为在数据库加载过来的或通过协议传送过来的实体并没有跟当前Scene做关联。
        /// 所以必须要执行一下这个反序列化的方法才可以使用。
        /// </summary>
        /// <param name="scene">Scene</param>
        /// <param name="resetId">是否是重新生成实体的Id,如果是数据库加载过来的一般是不需要的</param>
        public void Deserialize(Scene scene, bool resetId = false)
        {
            if (RuntimeId != 0)
            {
                return;
            }

            try
            {
                Scene = scene;
                Type ??= GetType();
                TypeHashCode = TypeHashCache.GetHashCode(Type);
                RuntimeId = Scene.RuntimeIdFactory.Create(false);
                if (resetId)
                {
                    Id = RuntimeId;
                }

                if (Tree != null && Tree.Count > 0)
                {
                    foreach (var (_, entity) in Tree)
                    {
                        entity.Parent = this;
                        entity.Type = entity.GetType();
                        entity.Deserialize(scene, resetId);
                    }
                }

                if (Multi != null && Multi.Count > 0)
                {
                    foreach (var (_, entity) in Multi)
                    {
                        entity.Parent = this;
                        entity.Deserialize(scene, resetId);
                    }
                }

                scene.AddEntity(this);
                scene.EntityComponent.Deserialize(this);
                scene.EntityComponent.RegisterUpdate(this);
#if FANTASY_UNITY
                scene.EntityComponent.RegisterLateUpdate(this);
#endif
            }
            catch (Exception e)
            {
                if (RuntimeId != 0)
                {
                    scene.RemoveEntity(RuntimeId);
                }

                Log.Error(e);
            }
        }

        #endregion

        #region ForEach
        
        /// <summary>
        /// 查询当前实体下的实现了ISupportedMultiEntity接口的实体
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        [IgnoreDataMember]
        [ProtoIgnore]
        [MemoryPackIgnore]
        public IEnumerable<Entity> ForEachMultiEntity
        {
            get
            {
                if (Multi == null)
                {
                    yield break;
                }

                foreach (var (_, supportedMultiEntity) in Multi)
                {
                    yield return supportedMultiEntity;
                }
            }
        }
        /// <summary>
        /// 查找当前实体下的所有实体，不包括实现ISupportedMultiEntity接口的实体
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        [IgnoreDataMember]
        [ProtoIgnore]
        [MemoryPackIgnore]
        public IEnumerable<Entity> ForEachEntity
        {
            get
            {
                if (Tree == null)
                {
                    yield break;
                }

                foreach (var (_, entity) in Tree)
                {
                    yield return entity;
                }
            }
        }
        #endregion

        #region Dispose

        /// <summary>
        /// 销毁当前实体，销毁后会自动销毁当前实体下的所有实体。
        /// </summary>
        public virtual void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            
            var scene = Scene;
            var runTimeId = RuntimeId;
            RuntimeId = 0;
            
            if (Tree != null)
            {
                foreach (var (_, entity) in Tree)
                {
                    entity.Dispose();
                }
                
                Tree.Dispose();
                Tree = null;
            }
            
            if (Multi != null)
            {
                foreach (var (_, entity) in Multi)
                {
                    entity.Dispose();
                }

                Multi.Dispose();
                Multi = null;
            }

            scene.EntityComponent.Destroy(this);
            
            if (Parent != null && Parent != this && !Parent.IsDisposed)
            {
                Parent.RemoveComponent(this, false);
                Parent = null;
            }

            Id = 0;
            Scene = null;
            Parent = null;
            scene.RemoveEntity(runTimeId);
            scene.EntityPool.Return(Type, this);
            Type = null;
        }

        #endregion

        #region Pool

        /// <summary>
        /// 获取一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsPool()
        {
            return IdFactoryHelper.RuntimeIdTool.GetIsPool(RuntimeId); 
        }

        /// <summary>
        /// 设置一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <param name="isPool"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIsPool(bool isPool) { }

        #endregion
    }
}