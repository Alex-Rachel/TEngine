using System;
using System.Collections.Generic;

namespace Fantasy.Assembly
{
    /// <summary>
    /// NetworkProtocol 类型注册器接口
    /// 由 Source Generator 自动生成实现类，用于收集和提供程序集中需要 NetworkProtocol 序列化的类型
    /// </summary>
    public interface INetworkProtocolRegistrar
    {
        /// <summary>
        /// 获取该程序集中需要 NetworkProtocol 序列化的所有类型
        /// 返回所有使用 NetworkProtocol 序列化特性标记的类型列表
        /// </summary>
        /// <returns>NetworkProtocol 序列化类型列表</returns>
        List<Type> GetNetworkProtocolTypes();
    }
}