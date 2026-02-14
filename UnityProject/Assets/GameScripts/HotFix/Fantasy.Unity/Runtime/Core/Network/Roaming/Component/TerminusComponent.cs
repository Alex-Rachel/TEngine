#if FANTASY_NET
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Entitas;
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Fantasy.Network.Roaming;

/// <summary>
/// 漫游终端创建类型枚举。
/// 用于区分漫游终端是首次创建还是重新连接。
/// </summary>
public enum CreateTerminusType
{
    /// <summary>
    /// 未指定类型。
    /// </summary>
    None = 0,
    /// <summary>
    /// 首次创建漫游终端。
    /// 当客户端第一次与目标服务器建立漫游连接时使用此类型。
    /// </summary>
    Link = 1,
    /// <summary>
    /// 重新连接漫游终端。
    /// 当目标服务器重启或客户端断线重连时使用此类型。
    /// </summary>
    ReLink = 2,
}

/// <summary>
/// 漫游终端创建完成时发送的事件参数。
/// 当 Terminus 首次创建（Link）或重新连接（ReLink）时，会发布此事件。
/// 业务层可以订阅此事件来执行玩家实体的创建或恢复逻辑。
/// </summary>
public struct OnCreateTerminus
{
    /// <summary>
    /// 获取与事件关联的场景实体。
    /// </summary>
    public readonly Scene Scene;
    /// <summary>
    /// 获取从 Gate 服务器传递过来的自定义参数。
    /// 可用于传递登录信息、玩家数据等业务数据。
    /// </summary>
    public readonly Entity? Args;
    /// <summary>
    /// 获取与事件关联的漫游终端实例。
    /// </summary>
    public readonly Terminus Terminus;
    /// <summary>
    /// 获取漫游终端的创建类型。
    /// 用于区分是首次创建（Link）还是重新连接（ReLink）。
    /// </summary>
    public readonly CreateTerminusType Type;

    /// <summary>
    /// 初始化一个新的 <see cref="OnCreateTerminus"/> 实例。
    /// </summary>
    /// <param name="scene">与事件关联的场景实体。</param>
    /// <param name="createTerminusType">漫游终端的创建类型。</param>
    /// <param name="terminus">漫游终端实例。</param>
    /// <param name="args">从 Gate 服务器传递的自定义参数。</param>
    public OnCreateTerminus(Scene scene, CreateTerminusType createTerminusType, Terminus terminus, Entity? args)
    {
        Args = args;
        Scene = scene;
        Terminus = terminus;
        Type = createTerminusType;
    }
}

/// <summary>
/// 漫游终端管理组件。
/// <para>负责管理当前 Scene 下所有漫游终端（Terminus）的生命周期。</para>
/// <para>此组件会在 Scene 启动时自动挂载，无需手动创建。</para>
/// <para>主要功能：</para>
/// <list type="bullet">
///   <item>创建漫游终端（Link）：当 Gate 服务器请求建立漫游连接时调用。</item>
///   <item>重新连接（ReLink）：当目标服务器重启或客户端断线重连时调用。</item>
///   <item>管理漫游终端的添加、查询和移除。</item>
/// </list>
/// </summary>
public sealed class TerminusComponent : Entity
{
    /// <summary>
    /// 漫游终端的实体集合。Key 为 roamingId（即 Terminus.Id），Value 为 Terminus 实例。
    /// </summary>
    private readonly Dictionary<long, Terminus> _terminals = new();

    /// <summary>
    /// 释放组件资源。销毁所有管理的漫游终端并清空集合。
    /// </summary>
    public override void Dispose()
    {
        foreach (var (_, terminus) in _terminals)
        {
            terminus.Dispose();
        }

        _terminals.Clear();
        base.Dispose();
    }

    /// <summary>
    /// 创建一个新的漫游终端。
    /// <para>当 Gate 服务器首次请求与目标服务器建立漫游连接时调用此方法。</para>
    /// <para>创建成功后会发布 <see cref="OnCreateTerminus"/> 事件，业务层可订阅此事件执行玩家实体创建逻辑。</para>
    /// </summary>
    /// <param name="roamingId">漫游唯一标识，通常使用玩家账号ID。不能为0。</param>
    /// <param name="roamingType">漫游类型，用于区分不同的目标服务器类型（如 Map、Chat 等）。</param>
    /// <param name="forwardSessionAddress">需要转发消息的 Session 地址（客户端连接的 Session）。</param>
    /// <param name="forwardSceneAddress">转发 Session 所在的 Scene 地址（通常是 Gate 服务器地址）。</param>
    /// <param name="args">可选的自定义参数，会传递给 <see cref="OnCreateTerminus"/> 事件。</param>
    /// <returns>返回元组：(错误码, Terminus实例)。错误码为0表示成功。</returns>
    internal async FTask<(uint, Terminus)> Create(long roamingId, int roamingType, long forwardSessionAddress, long forwardSceneAddress, Entity? args)
    {
        if (roamingId == 0)
        {
            return (InnerErrorCode.ErrCreateTerminusInvalidRoamingId, null);
        }
        
        if (_terminals.ContainsKey(roamingId))
        {
            return (InnerErrorCode.ErrAddRoamingTerminalAlreadyExists, null);
        }

        var terminus = Entity.Create<Terminus>(Scene, roamingId, false, true);
        
        terminus.IsDisposeTerminus = false;
        terminus.RoamingType = roamingType;
        terminus.ForwardSceneAddress = forwardSceneAddress;
        terminus.ForwardSessionAddress = forwardSessionAddress;
        terminus.RoamingMessageLock = Scene.CoroutineLockComponent.Create(terminus.Type.TypeHandle.Value.ToInt64());
        
        _terminals.Add(terminus.Id, terminus);

        await Scene.EventComponent.PublishAsync(new OnCreateTerminus(Scene, CreateTerminusType.Link, terminus, args));
        
        if (terminus.TerminusId == 0)
        {
            terminus.TerminusId = terminus.RuntimeId;
        }
        
        return (0U, terminus);
    }

    /// <summary>
    /// 重新连接漫游终端。
    /// <para>当目标服务器重启或客户端断线重连时调用此方法。</para>
    /// <para>如果指定 roamingId 的 Terminus 已存在，则更新其转发地址并重置状态；否则创建新的 Terminus。</para>
    /// <para>重连成功后会发布 <see cref="OnCreateTerminus"/> 事件（Type 为 ReLink），业务层可订阅此事件执行玩家状态恢复逻辑。</para>
    /// </summary>
    /// <param name="roamingId">漫游唯一标识，通常使用玩家账号ID。不能为0。</param>
    /// <param name="roamingType">漫游类型，用于区分不同的目标服务器类型（如 Map、Chat 等）。</param>
    /// <param name="forwardSessionAddress">需要转发消息的 Session 地址（客户端连接的新 Session）。</param>
    /// <param name="forwardSceneAddress">转发 Session 所在的 Scene 地址（通常是 Gate 服务器地址）。</param>
    /// <param name="args">可选的自定义参数，会传递给 <see cref="OnCreateTerminus"/> 事件。</param>
    /// <returns>返回元组：(错误码, Terminus实例)。错误码为0表示成功。</returns>
    internal async FTask<(uint, Terminus)> ReLink(long roamingId, int roamingType, long forwardSessionAddress, long forwardSceneAddress, Entity? args)
    {
        if (roamingId == 0)
        {
            return (InnerErrorCode.ErrCreateTerminusInvalidRoamingId, null);
        }

        if (!_terminals.TryGetValue(roamingId, out var terminus))
        {
            terminus = Entity.Create<Terminus>(Scene, roamingId, false, true);
        
            terminus.IsDisposeTerminus = false;
            terminus.RoamingType = roamingType;
            terminus.RoamingMessageLock = Scene.CoroutineLockComponent.Create(terminus.Type.TypeHandle.Value.ToInt64());
        
            _terminals.Add(terminus.Id, terminus);
        }
        else
        {
            terminus.TerminusId = 0;
            terminus.StopForwarding = false;
        }
        
        terminus.ForwardSceneAddress = forwardSceneAddress;
        terminus.ForwardSessionAddress = forwardSessionAddress;

        await Scene.EventComponent.PublishAsync(new OnCreateTerminus(Scene, CreateTerminusType.ReLink, terminus, args));

        if (terminus.TerminusId == 0)
        {
            terminus.TerminusId = terminus.RuntimeId;
        }
        
        return (0U, terminus);
    }

    /// <summary>
    /// 添加一个漫游终端到管理集合。
    /// <para>通常用于 Terminus 传送（Transfer）完成后，在目标 Scene 中注册 Terminus。</para>
    /// </summary>
    /// <param name="terminus">要添加的漫游终端实例。</param>
    internal void AddTerminus(Terminus terminus)
    {
        _terminals.Add(terminus.Id, terminus);
    }

    /// <summary>
    /// 尝试根据 roamingId 获取漫游终端。
    /// </summary>
    /// <param name="roamingId">漫游唯一标识。</param>
    /// <param name="terminus">如果找到则返回对应的 Terminus 实例，否则返回 null。</param>
    /// <returns>如果找到返回 true，否则返回 false。</returns>
    internal bool TryGetTerminus(long roamingId, out Terminus terminus)
    {
        return _terminals.TryGetValue(roamingId, out terminus);
    }
    
    /// <summary>
    /// 根据 roamingId 获取漫游终端。如果不存在会抛出异常。
    /// </summary>
    /// <param name="roamingId">漫游唯一标识。</param>
    /// <returns>对应的 Terminus 实例。</returns>
    /// <exception cref="KeyNotFoundException">当指定的 roamingId 不存在时抛出。</exception>
    internal Terminus GetTerminus(long roamingId)
    {
        return _terminals[roamingId];
    }

    /// <summary>
    /// 根据 roamingId 移除漫游终端。
    /// </summary>
    /// <param name="roamingId">漫游唯一标识。</param>
    /// <param name="isDispose">是否同时销毁 Terminus 实例。如果为 true，会调用 Terminus.Dispose()。</param>
    internal void RemoveTerminus(long roamingId, bool isDispose)
    {
        if (!_terminals.Remove(roamingId, out var terminus))
        {
            return;
        }

        if (isDispose)
        {
            terminus.Dispose();
        }
    }
}
#endif
