#if FANTASY_NET
using System;
namespace Fantasy.SeparateTable;

/// <summary>
/// 分表存储特性，用于标记需要进行数据库分表存储的实体类型。
/// 当实体标记此特性后，该实体将作为父实体的子组件，并在数据库中使用独立的集合进行存储。
/// </summary>
/// <remarks>
/// 使用场景：
/// - 当父实体的某些数据量较大，需要拆分到独立的数据库表中存储时
/// - 需要优化数据库查询性能，避免单表数据过大时
/// - Source Generator 会自动生成注册代码，无需手动反射处理
/// </remarks>
/// <example>
/// <code>
/// [SeparateTable(typeof(Player), "PlayerInventory")]
/// public class PlayerInventoryEntity : Entity
/// {
///     // 实体字段...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public class SeparateTableAttribute : Attribute
{
    /// <summary>
    /// 获取父实体的类型，指示此实体属于哪个父实体的子集合。
    /// 通过此属性建立父子实体的逻辑关联关系。
    /// </summary>
    public readonly Type RootType;

    /// <summary>
    /// 获取在数据库中使用的集合名称（表名）。
    /// 此实体的数据将单独存储到此命名的集合中。
    /// </summary>
    public readonly string CollectionName;

    /// <summary>
    /// 初始化 <see cref="SeparateTableAttribute"/> 类的新实例，指定父实体类型和数据库集合名称。
    /// </summary>
    /// <param name="rootType">父实体的类型，表示此分表实体从属于哪个父实体。</param>
    /// <param name="collectionName">在数据库中存储此实体的集合名称（表名）。</param>
    public SeparateTableAttribute(Type rootType, string collectionName)
    {
        RootType = rootType;
        CollectionName = collectionName;
    }
}
#endif
