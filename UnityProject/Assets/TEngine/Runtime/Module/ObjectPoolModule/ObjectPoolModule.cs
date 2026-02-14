using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 对象池管理器。
    /// </summary>
    internal sealed partial class ObjectPoolModule : Module, IObjectPoolModule, IUpdateModule
    {
        private const int DEFAULT_CAPACITY = int.MaxValue;
        private const float DEFAULT_EXPIRE_TIME = float.MaxValue;
        private const int DEFAULT_PRIORITY = 0;

        private readonly Dictionary<TypeNamePair, ObjectPoolBase> _objectPools;
        private readonly List<ObjectPoolBase> _cachedAllObjectPools;
        private readonly Comparison<ObjectPoolBase> _objectPoolComparer;
        
        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        public override int Priority => 6;

        /// <summary>
        /// 获取对象池数量。
        /// </summary>
        public int Count => _objectPools.Count;

        /// <summary>
        /// 对象池管理器轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                objectPool.Value.Update(elapseSeconds, realElapseSeconds);
            }
        }
        
        /// <summary>
        /// 初始化对象池管理器的新实例。
        /// </summary>
        public ObjectPoolModule()
        {
            _objectPools = new Dictionary<TypeNamePair, ObjectPoolBase>();
            _cachedAllObjectPools = new List<ObjectPoolBase>();
            _objectPoolComparer = ObjectPoolComparer;
        }

        public override void OnInit()
        {
            Log.Info("Object pool system onInit.");
        }

        /// <summary>
        /// 关闭并清理对象池管理器。
        /// </summary>
        public override void Shutdown()
        {
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                objectPool.Value.Shutdown();
            }

            _objectPools.Clear();
            _cachedAllObjectPools.Clear();
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool<T>() where T : ObjectBase
        {
            return InternalHasObjectPool(new TypeNamePair(typeof(T)));
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool(Type objectType)
        {
            if (objectType == null)
            {
                throw new GameFrameworkException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameFrameworkException(Utility.Text.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalHasObjectPool(new TypeNamePair(objectType));
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalHasObjectPool(new TypeNamePair(typeof(T), name));
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool(Type objectType, string name)
        {
            if (objectType == null)
            {
                throw new GameFrameworkException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameFrameworkException(Utility.Text.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalHasObjectPool(new TypeNamePair(objectType, name));
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <param name="condition">要检查的条件。</param>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool(Predicate<ObjectPoolBase> condition)
        {
            if (condition == null)
            {
                throw new GameFrameworkException("Condition is invalid.");
            }

            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <returns>要获取的对象池。</returns>
        public IObjectPool<T> GetObjectPool<T>() where T : ObjectBase
        {
            return (IObjectPool<T>)InternalGetObjectPool(new TypeNamePair(typeof(T)));
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <returns>要获取的对象池。</returns>
        public ObjectPoolBase GetObjectPool(Type objectType)
        {
            if (objectType == null)
            {
                throw new GameFrameworkException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameFrameworkException(Utility.Text.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalGetObjectPool(new TypeNamePair(objectType));
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <returns>要获取的对象池。</returns>
        public IObjectPool<T> GetObjectPool<T>(string name) where T : ObjectBase
        {
            return (IObjectPool<T>)InternalGetObjectPool(new TypeNamePair(typeof(T), name));
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <returns>要获取的对象池。</returns>
        public ObjectPoolBase GetObjectPool(Type objectType, string name)
        {
            if (objectType == null)
            {
                throw new GameFrameworkException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameFrameworkException(Utility.Text.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalGetObjectPool(new TypeNamePair(objectType, name));
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        /// <param name="condition">要检查的条件。</param>
        /// <returns>要获取的对象池。</returns>
        public ObjectPoolBase GetObjectPool(Predicate<ObjectPoolBase> condition)
        {
            if (condition == null)
            {
                throw new GameFrameworkException("Condition is invalid.");
            }

            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    return objectPool.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        /// <param name="condition">要检查的条件。</param>
        /// <returns>要获取的对象池。</returns>
        public ObjectPoolBase[] GetObjectPools(Predicate<ObjectPoolBase> condition)
        {
            if (condition == null)
            {
                throw new GameFrameworkException("Condition is invalid.");
            }

            List<ObjectPoolBase> results = new List<ObjectPoolBase>();
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    results.Add(objectPool.Value);
                }
            }

            return results.ToArray();
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        /// <param name="condition">要检查的条件。</param>
        /// <param name="results">要获取的对象池。</param>
        public void GetObjectPools(Predicate<ObjectPoolBase> condition, List<ObjectPoolBase> results)
        {
            if (condition == null)
            {
                throw new GameFrameworkException("Condition is invalid.");
            }

            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    results.Add(objectPool.Value);
                }
            }
        }

        /// <summary>
        /// 获取所有对象池。
        /// </summary>
        /// <returns>所有对象池。</returns>
        public ObjectPoolBase[] GetAllObjectPools()
        {
            return GetAllObjectPools(false);
        }

        /// <summary>
        /// 获取所有对象池。
        /// </summary>
        /// <param name="results">所有对象池。</param>
        public void GetAllObjectPools(List<ObjectPoolBase> results)
        {
            GetAllObjectPools(false, results);
        }

        /// <summary>
        /// 获取所有对象池。
        /// </summary>
        /// <param name="sort">是否根据对象池的优先级排序。</param>
        /// <returns>所有对象池。</returns>
        public ObjectPoolBase[] GetAllObjectPools(bool sort)
        {
            if (sort)
            {
                List<ObjectPoolBase> results = new List<ObjectPoolBase>();
                foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
                {
                    results.Add(objectPool.Value);
                }

                results.Sort(_objectPoolComparer);
                return results.ToArray();
            }
            else
            {
                int index = 0;
                ObjectPoolBase[] results = new ObjectPoolBase[_objectPools.Count];
                foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
                {
                    results[index++] = objectPool.Value;
                }

                return results;
            }
        }

        /// <summary>
        /// 获取所有对象池。
        /// </summary>
        /// <param name="sort">是否根据对象池的优先级排序。</param>
        /// <param name="results">所有对象池。</param>
        public void GetAllObjectPools(bool sort, List<ObjectPoolBase> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                results.Add(objectPool.Value);
            }

            if (sort)
            {
                results.Sort(_objectPoolComparer);
            }
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>() where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name)
        {
            return InternalCreateObjectPool(objectType, name, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity)
        {
            return InternalCreateObjectPool(objectType, name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="autoReleaseInterval">对象池自动释放可释放对象的间隔秒数。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
            where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, autoReleaseInterval, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="autoReleaseInterval">对象池自动释放可释放对象的间隔秒数。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许单次获取的对象池。</returns>
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, autoReleaseInterval, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>() where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name)
        {
            return InternalCreateObjectPool(objectType, name, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity)
        {
            return InternalCreateObjectPool(objectType, name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <param name="autoReleaseInterval">对象池自动释放可释放对象的间隔秒数。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
            where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, autoReleaseInterval, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <param name="autoReleaseInterval">对象池自动释放可释放对象的间隔秒数。</param>
        /// <param name="capacity">对象池的容量。</param>
        /// <param name="expireTime">对象池对象过期秒数。</param>
        /// <param name="priority">对象池的优先级。</param>
        /// <returns>要创建的允许多次获取的对象池。</returns>
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, autoReleaseInterval, capacity, expireTime, priority);
        }

        /// <summary>
        /// 销毁对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <returns>是否销毁对象池成功。</returns>
        public bool DestroyObjectPool<T>() where T : ObjectBase
        {
            return InternalDestroyObjectPool(new TypeNamePair(typeof(T)));
        }

        /// <summary>
        /// 销毁对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <returns>是否销毁对象池成功。</returns>
        public bool DestroyObjectPool(Type objectType)
        {
            if (objectType == null)
            {
                throw new GameFrameworkException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameFrameworkException(Utility.Text.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalDestroyObjectPool(new TypeNamePair(objectType));
        }

        /// <summary>
        /// 销毁对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">要销毁的对象池名称。</param>
        /// <returns>是否销毁对象池成功。</returns>
        public bool DestroyObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalDestroyObjectPool(new TypeNamePair(typeof(T), name));
        }

        /// <summary>
        /// 销毁对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">要销毁的对象池名称。</param>
        /// <returns>是否销毁对象池成功。</returns>
        public bool DestroyObjectPool(Type objectType, string name)
        {
            if (objectType == null)
            {
                throw new GameFrameworkException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameFrameworkException(Utility.Text.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalDestroyObjectPool(new TypeNamePair(objectType, name));
        }

        /// <summary>
        /// 销毁对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="objectPool">要销毁的对象池。</param>
        /// <returns>是否销毁对象池成功。</returns>
        public bool DestroyObjectPool<T>(IObjectPool<T> objectPool) where T : ObjectBase
        {
            if (objectPool == null)
            {
                throw new GameFrameworkException("Object pool is invalid.");
            }

            return InternalDestroyObjectPool(new TypeNamePair(typeof(T), objectPool.Name));
        }

        /// <summary>
        /// 销毁对象池。
        /// </summary>
        /// <param name="objectPool">要销毁的对象池。</param>
        /// <returns>是否销毁对象池成功。</returns>
        public bool DestroyObjectPool(ObjectPoolBase objectPool)
        {
            if (objectPool == null)
            {
                throw new GameFrameworkException("Object pool is invalid.");
            }

            return InternalDestroyObjectPool(new TypeNamePair(objectPool.ObjectType, objectPool.Name));
        }

        /// <summary>
        /// 释放对象池中的可释放对象。
        /// </summary>
        public void Release()
        {
            Log.Info("Object pool release...");
            GetAllObjectPools(true, _cachedAllObjectPools);
            foreach (ObjectPoolBase objectPool in _cachedAllObjectPools)
            {
                objectPool.Release();
            }
        }

        /// <summary>
        /// 释放对象池中的所有未使用对象。
        /// </summary>
        public void ReleaseAllUnused()
        {
            Log.Info("Object pool release all unused...");
            GetAllObjectPools(true, _cachedAllObjectPools);
            foreach (ObjectPoolBase objectPool in _cachedAllObjectPools)
            {
                objectPool.ReleaseAllUnused();
            }
        }

        private bool InternalHasObjectPool(TypeNamePair typeNamePair)
        {
            return _objectPools.ContainsKey(typeNamePair);
        }

        private ObjectPoolBase InternalGetObjectPool(TypeNamePair typeNamePair)
        {
            ObjectPoolBase objectPool = null;
            if (_objectPools.TryGetValue(typeNamePair, out objectPool))
            {
                return objectPool;
            }

            return null;
        }

        private IObjectPool<T> InternalCreateObjectPool<T>(string name, bool allowMultiSpawn, float autoReleaseInterval, int capacity, float expireTime,
            int priority) where T : ObjectBase
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasObjectPool<T>(name))
            {
                throw new GameFrameworkException(Utility.Text.Format("Already exist object pool '{0}'.", typeNamePair));
            }

            ObjectPool<T> objectPool = new ObjectPool<T>(name, allowMultiSpawn, autoReleaseInterval, capacity, expireTime, priority);
            _objectPools.Add(typeNamePair, objectPool);
            return objectPool;
        }

        private ObjectPoolBase InternalCreateObjectPool(Type objectType, string name, bool allowMultiSpawn, float autoReleaseInterval, int capacity,
            float expireTime, int priority)
        {
            if (objectType == null)
            {
                throw new GameFrameworkException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameFrameworkException(Utility.Text.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            TypeNamePair typeNamePair = new TypeNamePair(objectType, name);
            if (HasObjectPool(objectType, name))
            {
                throw new GameFrameworkException(Utility.Text.Format("Already exist object pool '{0}'.", typeNamePair));
            }

            Type objectPoolType = typeof(ObjectPool<>).MakeGenericType(objectType);
            ObjectPoolBase objectPool =
                (ObjectPoolBase)Activator.CreateInstance(objectPoolType, name, allowMultiSpawn, autoReleaseInterval, capacity, expireTime, priority);
            _objectPools.Add(typeNamePair, objectPool);
            return objectPool;
        }

        private bool InternalDestroyObjectPool(TypeNamePair typeNamePair)
        {
            ObjectPoolBase objectPool = null;
            if (_objectPools.TryGetValue(typeNamePair, out objectPool))
            {
                objectPool.Shutdown();
                return _objectPools.Remove(typeNamePair);
            }

            return false;
        }

        private static int ObjectPoolComparer(ObjectPoolBase a, ObjectPoolBase b)
        {
            return a.Priority.CompareTo(b.Priority);
        }
    }
}