#if FANTASY_NET
using System;
using System.Collections.Generic;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
namespace Fantasy.Network;

/// <summary>
/// RouteComponent的AwakeSystem
/// </summary>
public sealed class RouteComponentAwakeSystem : AwakeSystem<RouteComponent>
{
    /// <summary>
    /// Awake
    /// </summary>
    /// <param name="self"></param>
    /// <exception cref="NotImplementedException"></exception>
    protected override void Awake(RouteComponent self)
    {
        ((Session)self.Parent).RouteComponent = self;
    }
}

/// <summary>
/// 自定义Route组件、如果要自定义Route协议必须使用这个组件
/// </summary>
public sealed class RouteComponent : Entity
{
    /// <summary>
    /// 存储路由类型和路由ID的映射关系。
    /// </summary>
    public readonly Dictionary<long, long> RouteAddress = new Dictionary<long, long>();

    /// <summary>
    /// 添加路由类型和路由ID的映射关系。
    /// </summary>
    /// <param name="routeType">路由类型。</param>
    /// <param name="address">Address。</param>
    public void AddAddress(long routeType, long address)
    {
        RouteAddress.Add(routeType, address);
    }

    /// <summary>
    /// 移除指定路由类型的映射关系。
    /// </summary>
    /// <param name="routeType">路由类型。</param>
    public void RemoveAddress(long routeType)
    {
        RouteAddress.Remove(routeType);
    }

    /// <summary>
    /// 获取指定路由类型的路由ID。
    /// </summary>
    /// <param name="routeType">路由类型。</param>
    /// <returns>Address。</returns>
    public long GetAddress(long routeType)
    {
        return RouteAddress.GetValueOrDefault(routeType, 0);
    }

    /// <summary>
    /// 尝试获取指定路由类型的Address。
    /// </summary>
    /// <param name="routeType">路由类型。</param>
    /// <param name="address">Address。</param>
    /// <returns>如果获取成功返回true，否则返回false。</returns>
    public bool TryGetAddress(int routeType, out long address)
    {
        return RouteAddress.TryGetValue(routeType, out address);
    }

    /// <summary>
    /// 释放组件资源，清空映射关系。
    /// </summary>
    public override void Dispose()
    {
        RouteAddress.Clear();
        base.Dispose();
    }
}
#endif