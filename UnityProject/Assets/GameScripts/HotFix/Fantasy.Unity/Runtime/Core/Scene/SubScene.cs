using System.Collections.Generic;
using System.Linq;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Network;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy
{
    /// <summary>
    /// 代表一个Scene下的子Scene
    /// </summary>
    public sealed partial class SubScene : Scene
    {
        /// <summary>
        /// 子Scene的根Scene
        /// </summary>
        public Scene RootScene { get; internal set; }
        /// <summary>
        /// 存储当前Scene下管理的实体。
        /// </summary>
        private readonly Dictionary<long, Entity> _entities = new Dictionary<long, Entity>();

        internal void Initialize(Scene rootScene)
        {
            EntityPool = rootScene.EntityPool;
            EventAwaiterPool = rootScene.EventAwaiterPool;
            SceneUpdate = rootScene.SceneUpdate;
#if FANTASY_UNITY
            SceneLateUpdate = rootScene.SceneLateUpdate;
#endif
            TimerComponent = rootScene.TimerComponent;
            EventComponent = rootScene.EventComponent;
            EntityComponent = rootScene.EntityComponent;
            CoroutineLockComponent = rootScene.CoroutineLockComponent;
            MessageDispatcherComponent = rootScene.MessageDispatcherComponent;
            PoolGeneratorComponent = rootScene.PoolGeneratorComponent;
#if FANTASY_NET
            NetworkMessagingComponent = rootScene.NetworkMessagingComponent;
            SeparateTableComponent = rootScene.SeparateTableComponent;
            RoamingComponent = rootScene.RoamingComponent;
            TerminusComponent = rootScene.TerminusComponent;
            SphereEventComponent = rootScene.SphereEventComponent;
#endif
            ThreadSynchronizationContext = rootScene.ThreadSynchronizationContext;
        }

        /// <summary>
        /// Scene的关闭方法
        /// </summary>
        public override async FTask Close()
        {
            Dispose();
            await FTask.CompletedTask;
        }

        /// <summary>
        /// 当子Scene销毁时执行
        /// </summary>
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            
            ThreadSynchronizationContext.Post(() =>
            {
                if (IsDisposed)
                {
                    return;
                }
                
                foreach (var (runtimeId, entity) in _entities.ToList())
                {
                    if (runtimeId != entity.RuntimeId)
                    {
                        continue;
                    }
                    entity.Dispose();
                }
                
                _entities.Clear();
                base.Dispose();
            });
        }

        /// <summary>
        /// 添加一个实体到当前Scene下
        /// </summary>
        /// <param name="entity">实体实例</param>
        public override void AddEntity(Entity entity)
        {
            _entities.Add(entity.RuntimeId, entity);
            RootScene.AddEntity(entity);
        }
        
        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <returns>返回的实体</returns>
        public override Entity GetEntity(long runTimeId)
        {
            return _entities.GetValueOrDefault(runTimeId);
        }

        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <param name="entity">实体实例</param>
        /// <returns>返回一个bool值来提示是否查找到这个实体</returns>
        public override bool TryGetEntity(long runTimeId, out Entity entity)
        {
            return _entities.TryGetValue(runTimeId, out entity);
        }
        
        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <typeparam name="T">要查询实体的泛型类型</typeparam>
        /// <returns>返回的实体</returns>
        public override T GetEntity<T>(long runTimeId)
        {
            return _entities.TryGetValue(runTimeId, out var entity) ? (T)entity : null;
        }

        /// <summary>
        /// 根据RunTimeId查询一个实体
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <param name="entity">实体实例</param>
        /// <typeparam name="T">要查询实体的泛型类型</typeparam>
        /// <returns>返回一个bool值来提示是否查找到这个实体</returns>
        public override bool TryGetEntity<T>(long runTimeId, out T entity)
        {
            if (_entities.TryGetValue(runTimeId, out var getEntity))
            {
                entity = (T)getEntity;
                return true;
            }

            entity = null;
            return false;
        }

        /// <summary>
        /// 删除一个实体，仅是删除不会指定实体的销毁方法
        /// </summary>
        /// <param name="runTimeId">实体的RunTimeId</param>
        /// <returns>返回一个bool值来提示是否删除了这个实体</returns>
        public override bool RemoveEntity(long runTimeId)
        {
            return _entities.Remove(runTimeId) && RootScene.RemoveEntity(runTimeId);
        }

        /// <summary>
        /// 删除一个实体，仅是删除不会指定实体的销毁方法
        /// </summary>
        /// <param name="entity">实体实例</param>
        /// <returns>返回一个bool值来提示是否删除了这个实体</returns>
        public override bool RemoveEntity(Entity entity)
        {
            return RemoveEntity(entity.RuntimeId);
        }

#if FANTASY_NET
        /// <summary>
        /// 根据runTimeId获得Session
        /// </summary>
        /// <param name="runTimeId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        internal override Session GetSession(long runTimeId)
        {
            return RootScene.GetSession(runTimeId);
        }
    #endif
    }
}