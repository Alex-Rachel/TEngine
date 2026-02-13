#if FANTASY_NET
using System;
using System.Collections.Generic;
using Fantasy.Assembly;
using Fantasy.Async;
using Fantasy.Database;
using Fantasy.DataStructure.Collection;
using Fantasy.DataStructure.Dictionary;
using Fantasy.Entitas;
// ReSharper disable SuspiciousTypeConversion.Global
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
// ReSharper disable CheckNamespace
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.SeparateTable
{
    /// <summary>
    /// 分表组件，用于管理实体的数据库分表存储功能。
    /// 负责注册、加载和保存标记了 <see cref="SeparateTableAttribute"/> 的实体数据。
    /// </summary>
    /// <remarks>
    /// <para>此组件实现了程序集生命周期接口，会在程序集加载/卸载时自动注册/反注册分表信息。</para>
    /// <para>通过 Source Generator 生成的注册器自动管理分表实体的元数据，避免运行时反射，提供零反射性能。</para>
    /// <para>分表机制允许将实体的子组件存储在独立的数据库表中，实现聚合根模式和更灵活的数据组织。</para>
    /// </remarks>
    public sealed class SeparateTableComponent : Entity, IAssemblyLifecycle
    {
        /// <summary>
        /// 分表信息的冻结字典，外层 key 为聚合根实体类型句柄，value 为该聚合根下所有分表的映射字典
        /// 内层字典 key 为分表实体类型句柄，value 为实体类型和表名元组
        /// 使用冻结字典提供高性能的无分配查找
        /// </summary>
        private RuntimeTypeHandleFrozenDictionary<Dictionary<RuntimeTypeHandle, (Type EntityType, string TableName)>> _separateTables;

        /// <summary>
        /// 分表信息合并器，用于在程序集加载/卸载时合并和移除分表注册信息
        /// 支持多程序集热重载场景
        /// </summary>
        private readonly TypeHandleMergerFrozenOneToManyDic<RuntimeTypeHandle, (Type EntityType, string TableName)> _separateMerger = new();
        
        #region AssemblyManifest

        /// <summary>
        /// 初始化分表组件，将其注册到程序集生命周期管理器中。
        /// </summary>
        /// <returns>返回初始化后的分表组件实例。</returns>
        internal async FTask<SeparateTableComponent> Initialize()
        {
            await AssemblyLifecycle.Add(this);
            return this;
        }

        /// <summary>
        /// 当程序集加载时的回调方法，负责注册该程序集中的所有分表信息。
        /// </summary>
        /// <param name="assemblyManifest">加载的程序集清单，包含该程序集的所有元数据。</param>
        /// <returns>异步任务。</returns>
        /// <remarks>
        /// <para>此方法在线程同步上下文中执行，确保线程安全。</para>
        /// <para>如果程序集已被加载过（如热重载场景），会先卸载旧的注册信息再重新注册。</para>
        /// <para>注册完成后会重新生成冻结字典，以保证最佳查找性能。</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">当 assemblyManifest 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">当 Scene 或 SeparateTableRegistrar 为 null 时抛出。</exception>
        public async FTask OnLoad(AssemblyManifest assemblyManifest)
        {
            if (assemblyManifest == null)
            {
                throw new ArgumentNullException(nameof(assemblyManifest), "Assembly manifest cannot be null");
            }

            if (Scene == null)
            {
                throw new InvalidOperationException("Scene is null, cannot register separate table information");
            }

            var separateTableRegistrar = assemblyManifest.SeparateTableRegistrar;
            if (separateTableRegistrar == null)
            {
                throw new InvalidOperationException($"SeparateTableRegistrar is null in assembly {assemblyManifest.Assembly.FullName}");
            }

            var tcs = FTask.Create(false);
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;

            Scene.ThreadSynchronizationContext.Post(() =>
            {
                try
                {
                    _separateMerger.Add(
                        assemblyManifestId,
                        separateTableRegistrar.RootTypes(),
                        separateTableRegistrar.EntityTypeHandles(),
                        separateTableRegistrar.SeparateTables()
                    );

                    // 重新生成冻结字典以获得最佳查找性能
                    _separateTables = _separateMerger.GetFrozenDictionary();

                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            await tcs;
        }

        /// <summary>
        /// 当程序集卸载时的回调方法，负责反注册该程序集中的所有分表信息。
        /// </summary>
        /// <param name="assemblyManifest">卸载的程序集清单。</param>
        /// <returns>异步任务。</returns>
        /// <remarks>
        /// <para>此方法在线程同步上下文中执行，确保线程安全。</para>
        /// <para>卸载完成后会重新生成冻结字典，移除已卸载程序集的分表信息。</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">当 assemblyManifest 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">当 Scene 为 null 时抛出。</exception>
        public async FTask OnUnload(AssemblyManifest assemblyManifest)
        {
            if (assemblyManifest == null)
            {
                throw new ArgumentNullException(nameof(assemblyManifest), "Assembly manifest cannot be null");
            }

            if (Scene == null)
            {
                return;
                // throw new InvalidOperationException("Scene is null, cannot unregister separate table information");
            }

            var task = FTask.Create(false);
            var assemblyManifestId = assemblyManifest.AssemblyManifestId;

            Scene.ThreadSynchronizationContext.Post(() =>
            {
                try
                {
                    if (_separateMerger.Remove(assemblyManifestId))
                    {
                        _separateTables = _separateMerger.GetFrozenDictionary();
                    }

                    task.SetResult();
                }
                catch (Exception ex)
                {
                    task.SetException(ex);
                }
            });

            await task;
        }

        #endregion
        
        #region LoadWithSeparateTable

        /// <summary>
        /// 从数据库加载指定实体的单个分表数据，并作为组件添加到实体上。
        /// </summary>
        /// <typeparam name="T">要加载的分表实体类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
        /// <param name="entity">聚合根实体实例。</param>
        /// <param name="database">数据库实例。</param>
        /// <returns>如果成功加载并添加分表组件则返回 true，否则返回 false。</returns>
        /// <remarks>
        /// <para>此方法会检查实体类型是否配置了对应的分表信息，如果配置了则从数据库加载。</para>
        /// <para>加载的分表实体会通过 AddComponent 方法添加到父实体上，建立父子关系。</para>
        /// <para>性能优化：使用冻结字典进行无分配查找，避免 GC 压力。</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">当 entity 或 database 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var player = await db.Query&lt;Player&gt;(playerId);
        /// var loaded = await separateTableComponent.LoadWithSeparateTable&lt;PlayerInventory&gt;(player, db);
        /// if (loaded)
        /// {
        ///     var inventory = player.GetComponent&lt;PlayerInventory&gt;();
        ///     // 使用 inventory...
        /// }
        /// </code>
        /// </example>
        public async FTask<bool> LoadWithSeparateTable<T>(Entity entity, IDatabase database) where T : Entity, new()
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity), "Entity cannot be null");
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database), "Database cannot be null");
            }

            // 检查该实体类型是否配置了分表
            if (!_separateTables.TryGetValue(entity.Type.TypeHandle, out var separateTables))
            {
                return false;
            }

            // 检查是否存在指定类型的分表配置
            if (!separateTables.TryGetValue(typeof(T).TypeHandle, out var separateTable))
            {
                return false;
            }

            // 从数据库加载分表实体
            var separateTableEntity = await database.Query<T>(entity.Id, true, separateTable.TableName);

            if (separateTableEntity == null)
            {
                return false;
            }

            // 将加载的分表实体作为组件添加到父实体上
            entity.AddComponent(separateTableEntity);
            return true;
        }

        /// <summary>
        /// 从数据库加载指定实体的所有分表数据，并自动建立父子关系。
        /// </summary>
        /// <typeparam name="T">实体的泛型类型，必须继承自 <see cref="Entity"/>。</typeparam>
        /// <param name="entity">需要加载分表数据的聚合根实体实例。</param>
        /// <param name="database">数据库实例。</param>
        /// <returns>异步任务。</returns>
        /// <remarks>
        /// <para>此方法会根据实体类型查找其关联的所有分表配置，逐个从数据库中加载对应的分表实体，</para>
        /// <para>并通过 AddComponent 方法将这些分表实体作为组件添加到父实体上，建立父子关系。</para>
        /// <para>如果实体类型没有配置分表信息，则直接返回不做任何操作。</para>
        /// <para>性能优化：使用冻结字典和无分配枚举，减少 GC 压力。</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">当 entity 或 database 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var player = await db.Query&lt;Player&gt;(playerId);
        /// await separateTableComponent.LoadWithSeparateTables(player, db);
        /// // 此时 player 上所有配置的分表组件都已加载
        /// var inventory = player.GetComponent&lt;PlayerInventory&gt;();
        /// var skills = player.GetComponent&lt;PlayerSkills&gt;();
        /// </code>
        /// </example>
        public async FTask LoadWithSeparateTables<T>(T entity, IDatabase database) where T : Entity
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity), "Entity cannot be null");
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database), "Database cannot be null");
            }

            // 检查该实体类型是否配置了分表
            if (!_separateTables.TryGetValue(entity.Type.TypeHandle, out var separateTables))
            {
                return;
            }

            var entityId = entity.Id;

            // 遍历所有分表配置，逐个加载
            // 使用 foreach 进行无分配枚举
            foreach (var (_, (_, tableName)) in separateTables)
            {
                // 使用实体 ID 作为查询条件，加载分表实体
                var separateTableEntity = await database.QueryNotLock<Entity>(entityId, true, tableName);

                if (separateTableEntity == null)
                {
                    continue;
                }

                // 将加载的分表实体作为组件添加到父实体上
                entity.AddComponent(separateTableEntity);
            }
        }
        
        #endregion
        
        #region PersistAggregate

        /// <summary>
        /// 将实体及其指定的单个分表组件保存到数据库中。
        /// </summary>
        /// <typeparam name="T">聚合根实体的类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
        /// <typeparam name="TEntity">要保存的分表实体类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
        /// <param name="entity">聚合根实体实例。</param>
        /// <param name="isSaveSelf">是否将聚合根实体本身也一起保存到数据库。</param>
        /// <param name="database">数据库实例。</param>
        /// <returns>异步任务。</returns>
        /// <remarks>
        /// <para>此方法会检查实体是否配置了对应的分表信息，如果配置了则保存指定的分表组件。</para>
        /// <para>如果实体上没有该分表组件，则不执行任何操作。</para>
        /// <para>性能优化：使用对象池创建临时列表，避免 GC 分配。</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">当 entity 或 database 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var player = await db.Query&lt;Player&gt;(playerId);
        /// var inventory = player.GetComponent&lt;PlayerInventory&gt;();
        /// inventory.AddItem(newItem);
        /// // 只保存 PlayerInventory，不保存 Player 本身
        /// await separateTableComponent.PersistAggregate&lt;Player, PlayerInventory&gt;(player, false, db);
        /// </code>
        /// </example>
        public async FTask PersistAggregate<T, TEntity>(T entity, bool isSaveSelf, IDatabase database)
            where T : Entity, new()
            where TEntity : Entity, new()
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity), "Entity cannot be null");
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database), "Database cannot be null");
            }

            // 检查该实体类型是否配置了分表
            if (!_separateTables.TryGetValue(entity.Type.TypeHandle, out var separateTables))
            {
                if (isSaveSelf)
                {
                    await database.Save(entity);
                }

                return;
            }

            // 检查是否存在指定类型的分表配置
            if (!separateTables.TryGetValue(typeof(TEntity).TypeHandle, out var separateTable))
            {
                if (isSaveSelf)
                {
                    await database.Save(entity);
                }

                return;
            }

            var component = entity.GetComponent<TEntity>();

            if (component == null)
            {
                if (isSaveSelf)
                {
                    await database.Save(entity);
                }

                return;
            }

            // 使用对象池创建列表，避免 GC
            using var saveSeparateTables = ListPool<(Entity, string)>.Create();

            if (isSaveSelf)
            {
                saveSeparateTables.Add((entity, typeof(T).Name));
            }

            saveSeparateTables.Add((component, separateTable.TableName));

            // 批量保存
            await database.Save(entity.Id, saveSeparateTables);
        }

        /// <summary>
        /// 将实体及其所有分表组件保存到数据库中。
        /// </summary>
        /// <typeparam name="T">聚合根实体的泛型类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
        /// <param name="entity">需要保存的聚合根实体实例。</param>
        /// <param name="isSaveSelf">是否将聚合根实体本身也一起保存到数据库。</param>
        /// <param name="database">数据库实例。</param>
        /// <param name="removeSeparateTable">保存后是否移除分表组件。</param>
        /// <returns>异步任务。</returns>
        /// <remarks>
        /// <para>此方法会检查实体是否配置了分表信息：</para>
        /// <list type="bullet">
        /// <item><description>如果没有配置分表且 isSaveSelf 为 true，则直接保存实体本身到数据库。</description></item>
        /// <item><description>如果配置了分表，会收集实体上所有需要分表存储的组件，统一批量保存到数据库。</description></item>
        /// </list>
        /// <para>性能优化：</para>
        /// <list type="bullet">
        /// <item><description>使用对象池优化列表分配，避免频繁 GC。</description></item>
        /// <item><description>使用冻结字典进行无分配查找。</description></item>
        /// <item><description>批量保存所有分表组件，减少数据库往返次数。</description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="ArgumentNullException">当 entity 或 database 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var player = await db.Query&lt;Player&gt;(playerId);
        /// var inventory = player.GetComponent&lt;PlayerInventory&gt;();
        /// inventory.AddItem(newItem);
        /// var skills = player.GetComponent&lt;PlayerSkills&gt;();
        /// skills.LearnSkill(newSkill);
        /// // 保存玩家及所有分表组件
        /// await separateTableComponent.PersistAggregate(player, true, db);
        /// </code>
        /// </example>
        public async FTask PersistAggregate<T>(T entity, bool isSaveSelf, IDatabase database, bool removeSeparateTable = true) where T : Entity, new()
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity), "Entity cannot be null");
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database), "Database cannot be null");
            }

            // 检查该实体类型是否配置了分表
            if (!_separateTables.TryGetValue(entity.Type.TypeHandle, out var separateTables))
            {
                if (isSaveSelf)
                {
                    // 没有分表配置，直接保存实体
                    await database.Save(entity);
                }

                return;
            }

            // 使用对象池创建列表，避免 GC
            using var saveSeparateTables = ListPool<(Entity, string)>.Create();

            if (isSaveSelf)
            {
                saveSeparateTables.Add((entity, typeof(T).Name));
            }

            var entityId = entity.Id;

            // 收集所有需要分表保存的组件
            // 使用 foreach 进行无分配枚举
            foreach (var (_, (entityType, tableName)) in separateTables)
            {
                var separateTableEntity = entity.GetComponent(entityType);

                if (separateTableEntity == null)
                {
                    continue;
                }

                saveSeparateTables.Add((separateTableEntity, tableName));
            }

            // 批量保存实体及其所有分表组件
            await database.Save(entityId, saveSeparateTables);
        }
        
        #endregion
    }
}

#endif