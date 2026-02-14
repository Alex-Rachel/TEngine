using System;

namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// 支持再一个组件里添加多个同类型组件
    /// </summary>
    public interface ISupportedMultiEntity : IDisposable
    {
    }
    
    /// <summary>
    /// Entity支持数据库/序列化
    /// </summary>
    public interface ISupportedSerialize
    {
    }
    
#if FANTASY_NET
    
    // Entity支持分表存储、保存到数据库的时候不会跟随父组件保存在一个表里、会单独保存在一个表里
    // 需要配合SeparateTableAttribute一起使用、如在Entity类头部定义SeparateTableAttribute(typeOf(Unit), "UnitBag")
    // SeparateTableAttribute用来定义这个Entity是属于哪个Entity的子集以及表名
    /// <summary>
    /// 定义实体支持分表存储的接口。当实体需要单独存储在一个数据库表中，并且在保存到数据库时不会与父实体一起保存在同一个表中时，应实现此接口。
    /// </summary>
    public interface ISupportedSeparateTable
    {
    }

    /// <summary>
    /// Entity支持传送
    /// </summary>
    public interface ISupportedTransfer
    {
    }
    
#endif
}