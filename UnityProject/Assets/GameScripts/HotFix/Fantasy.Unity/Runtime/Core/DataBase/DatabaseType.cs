// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global
#if FANTASY_NET

namespace Fantasy.Database
{
    /// <summary>
    /// 数据库类型
    /// </summary>
    public enum DatabaseType
    {
        /// <summary>
        /// 无
        /// </summary>
        None = 0,
        /// <summary>
        /// PostgreSQL, 适用于强关系型数据的存储、复杂跨表聚合、强事务性操作
        /// </summary>
        PostgreSQL = 1,
        /// <summary>
        /// MongoDB, 适用于弱关系型、非结构化的、文档型数据存储和查询
        /// </summary>
        MongoDB = 2,
    }
}
#endif