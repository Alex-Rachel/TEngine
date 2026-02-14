using System;
using System.Collections.Generic;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 实体类型集合注册器接口
    /// 由 Source Generator 自动生成实现类，用于收集和提供程序集中定义的所有实体类型
    /// </summary>
    public interface IEntityTypeCollectionRegistrar
    {
        /// <summary>
        /// 获取该程序集中定义的所有实体类型
        /// 返回继承自 Entity 的所有类型列表
        /// </summary>
        /// <returns>实体类型列表</returns>
        List<Type> GetEntityTypes();
    }
}