#if FANTASY_NET
using System;
using System.Linq.Expressions;
using Fantasy.Async;
using Fantasy.Database;
using Fantasy.Entitas;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
namespace Fantasy.SeparateTable;

/// <summary>
/// 分表组件扩展方法类，为实体和数据库提供便捷的分表数据加载和保存操作。
/// </summary>
/// <remarks>
/// <para>此类提供了一组扩展方法，简化了分表实体的数据库操作流程。</para>
/// <para>所有扩展方法内部都调用 <see cref="SeparateTableComponent"/> 的相应方法，提供更简洁的 API。</para>
/// </remarks>
public static class SeparateTableGeneratedExtensions
{
    /// <summary>
    /// 从数据库加载指定实体的所有分表数据，并自动建立父子关系。
    /// </summary>
    /// <typeparam name="T">实体的泛型类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="entity">需要加载分表数据的聚合根实体实例。</param>
    /// <param name="database">数据库实例。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// 此方法会自动查找实体配置的所有分表，并从数据库加载对应的分表实体作为组件添加到父实体上。
    /// </remarks>
    /// <example>
    /// <code>
    /// var player = await db.Query&lt;Player&gt;(playerId);
    /// await player.LoadWithSeparateTables(db);
    /// </code>
    /// </example>
    public static FTask LoadWithSeparateTables<T>(this T entity, IDatabase database) where T : Entity, new()
    {
        return entity.Scene.SeparateTableComponent.LoadWithSeparateTables(entity, database);
    }

    /// <summary>
    /// 从数据库加载指定实体的单个分表数据，并作为组件添加到实体上。
    /// </summary>
    /// <typeparam name="T">要加载的分表实体类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="entity">聚合根实体实例。</param>
    /// <param name="database">数据库实例。</param>
    /// <returns>如果成功加载并添加分表组件则返回 true，否则返回 false。</returns>
    /// <remarks>
    /// 此方法只加载指定类型的分表实体，适用于按需加载场景。
    /// </remarks>
    /// <example>
    /// <code>
    /// var player = await db.Query&lt;Player&gt;(playerId);
    /// var loaded = await LoadWithSeparateTable&lt;PlayerInventory&gt;(player, db);
    /// if (loaded)
    /// {
    ///     var inventory = player.GetComponent&lt;PlayerInventory&gt;();
    /// }
    /// </code>
    /// </example>
    public static FTask<bool> LoadWithSeparateTable<T>(Entity entity, IDatabase database) where T : Entity, new()
    {
        return entity.Scene.SeparateTableComponent.LoadWithSeparateTable<T>(entity, database);
    }

    /// <summary>
    /// 从数据库查询指定 ID 的实体，并自动加载其所有分表数据。
    /// </summary>
    /// <typeparam name="T">实体的泛型类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="database">数据库实例。</param>
    /// <param name="id">实体的唯一标识 ID。</param>
    /// <returns>加载了分表数据的实体实例，如果实体不存在则返回 null。</returns>
    /// <remarks>
    /// <para>此方法是一个便捷方法，组合了实体查询和分表加载两个步骤。</para>
    /// <para>等同于先调用 <c>database.Query&lt;T&gt;(id)</c>，再调用 <c>entity.LoadWithSeparateTables(database)</c>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 一步完成查询和分表加载
    /// var player = await db.LoadWithSeparateTables&lt;Player&gt;(playerId);
    /// if (player != null)
    /// {
    ///     var inventory = player.GetComponent&lt;PlayerInventory&gt;();
    ///     var skills = player.GetComponent&lt;PlayerSkills&gt;();
    /// }
    /// </code>
    /// </example>
    public static async FTask<T> LoadWithSeparateTables<T>(this IDatabase database, long id) where T : Entity, new()
    {
        var entity = await database.Query<T>(id, true);

        if (entity == null)
        {
            return null!;
        }

        await entity.Scene.SeparateTableComponent.LoadWithSeparateTables(entity, database);
        return entity;
    }

    /// <summary>
    /// 从数据库查询满足条件的第一个实体，并自动加载其所有分表数据。
    /// </summary>
    /// <typeparam name="T">实体的泛型类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="database">数据库实例。</param>
    /// <param name="filter">查询过滤条件的 Lambda 表达式。</param>
    /// <returns>加载了分表数据的实体实例，如果没有满足条件的实体则返回 null。</returns>
    /// <remarks>
    /// <para>此方法是一个便捷方法，组合了条件查询和分表加载两个步骤。</para>
    /// <para>等同于先调用 <c>database.First&lt;T&gt;(filter)</c>，再调用 <c>entity.LoadWithSeparateTables(database)</c>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 根据条件查询并加载分表数据
    /// var player = await db.LoadWithSeparateTables&lt;Player&gt;(p => p.Name == "张三");
    /// if (player != null)
    /// {
    ///     var inventory = player.GetComponent&lt;PlayerInventory&gt;();
    /// }
    /// </code>
    /// </example>
    public static async FTask<T> LoadWithSeparateTables<T>(this IDatabase database, Expression<Func<T, bool>> filter) where T : Entity, new()
    {
        var entity = await database.First<T>(filter, true);

        if (entity == null)
        {
            return null!;
        }

        await entity.Scene.SeparateTableComponent.LoadWithSeparateTables(entity, database);
        return entity;
    }
    
    /// <summary>
    /// 将实体及其所有分表组件保存到数据库中。
    /// </summary>
    /// <typeparam name="T">聚合根实体的泛型类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="entity">需要保存的聚合根实体实例。</param>
    /// <param name="isSaveSelf">是否将聚合根实体本身也一起保存到数据库。</param>
    /// <param name="database">数据库实例。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// 此方法会收集实体上所有需要分表存储的组件，统一批量保存到数据库。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 只保存分表组件，不保存聚合根本身
    /// await player.PersistAggregate(false, db);
    /// </code>
    /// </example>
    public static FTask PersistAggregate<T>(this T entity, bool isSaveSelf, IDatabase database) where T : Entity, new()
    {
        return entity.Scene.SeparateTableComponent.PersistAggregate(entity, isSaveSelf, database);
    }

    /// <summary>
    /// 将实体及其所有分表组件保存到数据库中（包括聚合根本身）。
    /// </summary>
    /// <typeparam name="T">聚合根实体的泛型类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="entity">需要保存的聚合根实体实例。</param>
    /// <param name="database">数据库实例。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// <para>这是最常用的保存方法，会保存聚合根实体本身及其所有分表组件。</para>
    /// <para>等同于调用 <c>entity.PersistAggregate(true, database)</c>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var player = await db.Query&lt;Player&gt;(playerId);
    /// player.Level = 10;
    /// player.GetComponent&lt;PlayerInventory&gt;().AddItem(item);
    /// // 保存玩家及所有分表组件
    /// await player.PersistAggregate(db);
    /// </code>
    /// </example>
    public static FTask PersistAggregate<T>(this T entity, IDatabase database) where T : Entity, new()
    {
        return entity.Scene.SeparateTableComponent.PersistAggregate(entity, true, database);
    }

    /// <summary>
    /// 将实体的指定分表组件保存到数据库中（不包括聚合根本身）。
    /// </summary>
    /// <typeparam name="T">聚合根实体的类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <typeparam name="TEntity">要保存的分表实体类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="entity">聚合根实体实例。</param>
    /// <param name="database">数据库实例。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// <para>此方法只保存指定的单个分表组件，不保存聚合根本身。</para>
    /// <para>适用于只修改了某个分表组件的场景，避免不必要的数据库写入。</para>
    /// <para>等同于调用 <c>entity.PersistAggregate&lt;T, TEntity&gt;(false, database)</c>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var player = await db.Query&lt;Player&gt;(playerId);
    /// var inventory = player.GetComponent&lt;PlayerInventory&gt;();
    /// inventory.AddItem(newItem);
    /// // 只保存 PlayerInventory，不保存 Player 本身
    /// await player.PersistAggregate&lt;Player, PlayerInventory&gt;(db);
    /// </code>
    /// </example>
    public static FTask PersistAggregate<T, TEntity>(this T entity, IDatabase database)
        where T : Entity, new()
        where TEntity : Entity, new()
    {
        return entity.Scene.SeparateTableComponent.PersistAggregate<T, TEntity>(entity, false, database);
    }

    /// <summary>
    /// 将实体的指定分表组件保存到数据库中。
    /// </summary>
    /// <typeparam name="T">聚合根实体的类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <typeparam name="TEntity">要保存的分表实体类型，必须继承自 <see cref="Entity"/> 并具有无参构造函数。</typeparam>
    /// <param name="entity">聚合根实体实例。</param>
    /// <param name="isSaveSelf">是否将聚合根实体本身也一起保存到数据库。</param>
    /// <param name="database">数据库实例。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// <para>此方法只保存指定的单个分表组件。</para>
    /// <para>通过 <paramref name="isSaveSelf"/> 参数控制是否同时保存聚合根本身。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var player = await db.Query&lt;Player&gt;(playerId);
    /// player.Level = 10;  // 修改了聚合根
    /// var inventory = player.GetComponent&lt;PlayerInventory&gt;();
    /// inventory.AddItem(newItem);  // 修改了分表组件
    /// // 保存聚合根和指定的分表组件
    /// await player.PersistAggregate&lt;Player, PlayerInventory&gt;(true, db);
    /// </code>
    /// </example>
    public static FTask PersistAggregate<T, TEntity>(this T entity, bool isSaveSelf, IDatabase database)
        where T : Entity, new()
        where TEntity : Entity, new()
    {
        return entity.Scene.SeparateTableComponent.PersistAggregate<T, TEntity>(entity, isSaveSelf, database);
    }
}
#endif
