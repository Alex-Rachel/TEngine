#if FANTASY_NET
namespace Fantasy.Assembly;

/// <summary>
/// 分表实体注册器接口
/// 由 Source Generator 自动生成实现类，用于在程序集加载时注册带有 [SeparateTable] 特性的实体
/// 分表机制支持将实体的子集合存储在独立的数据库表中，实现聚合根模式和更灵活的数据组织
/// </summary>
public interface ISeparateTableRegistrar
{
    /// <summary>
    /// 获取所有聚合根实体类型的句柄数组
    /// 聚合根是包含分表子实体的顶层实体，作为数据加载和持久化的入口点
    /// 这些类型用于在加载或保存数据时识别哪些实体需要进行聚合操作
    /// </summary>
    /// <returns>RuntimeTypeHandle 数组，每个元素对应一个包含分表子实体的聚合根实体类型</returns>
    RuntimeTypeHandle[] RootTypes();

    /// <summary>
    /// 获取所有分表实体类型的句柄数组
    /// 包含所有标记了 [SeparateTable] 特性的实体类型句柄，用于快速类型查找和注册
    /// 注意：可能包含重复的实体类型（如果同一实体类型被多次标记为不同表）
    /// </summary>
    /// <returns>RuntimeTypeHandle 数组，每个元素对应一个分表实体类型</returns>
    RuntimeTypeHandle[] EntityTypeHandles();

    /// <summary>
    /// 获取所有分表实体的类型和表名映射数组
    /// 每个分表实体都会映射到数据库中的独立表（集合），通过自定义表名实现数据隔离
    /// 表名通常基于实体类型名自动生成，或通过 [SeparateTable] 特性的参数自定义
    /// </summary>
    /// <returns>元组数组，每个元组包含：
    /// - EntityType: 标记了 [SeparateTable] 特性的实体类型
    /// - TableName: 在数据库中使用的集合（表）名称</returns>
    (Type EntityType, string TableName)[] SeparateTables();
}


#endif
