using System.Runtime.CompilerServices;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Network.Interface;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

#if FANTASY_NET
namespace Fantasy.Network.Roaming;

/// <summary>
/// Terminus 扩展方法帮助类。
/// 提供便捷的 Terminus 操作方法，让 Entity 可以直接调用 Terminus 的功能。
/// </summary>
public static class TerminusHelper
{
    #region Entity 关联查询

    /// <summary>
    /// 获取实体关联的 Terminus。
    /// </summary>
    /// <param name="entity">实体</param>
    /// <returns>关联的 Terminus</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Terminus GetLinkTerminus(this Entity entity)
    {
        return entity.GetComponent<TerminusFlagComponent>().Terminus;
    }

    /// <summary>
    /// 尝试获取实体关联的 Terminus。
    /// </summary>
    /// <param name="entity">实体</param>
    /// <param name="terminus">关联的 Terminus（如果存在）</param>
    /// <returns>是否成功获取</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetLinkTerminus(this Entity entity, out Terminus terminus)
    {
        var terminusFlagComponent = entity.GetComponent<TerminusFlagComponent>();

        if (terminusFlagComponent == null)
        {
            terminus = null;
            return false;
        }

        terminus = terminusFlagComponent.Terminus;
        return terminus != null;
    }

    #endregion

    #region Message 方法

    /// <summary>
    /// 通过关联的 Terminus 发送一个消息给客户端。
    /// </summary>
    /// <remarks>
    /// 性能建议：如果需要频繁发送消息，建议先获取 Terminus 后直接调用其 Send 方法，可以避免重复的组件查找开销。
    /// <para>获取 Terminus 的方式：</para>
    /// <code>
    /// // 方式 1：直接获取（如果确定存在）
    /// var terminus = entity.GetLinkTerminus();
    /// terminus.Send(message);
    ///
    /// // 方式 2：安全获取（推荐）
    /// if (entity.TryGetLinkTerminus(out var terminus))
    /// {
    ///     terminus.Send(message);
    /// }
    /// </code>
    /// </remarks>
    /// <param name="entity">已关联 Terminus 的实体</param>
    /// <param name="message">要发送的消息</param>
    /// <typeparam name="T">消息类型</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Send<T>(this Entity entity, T message) where T : IRoamingMessage
    {
        var terminusFlagComponent = entity.GetComponent<TerminusFlagComponent>();

        if (terminusFlagComponent == null)
        {
            Log.Error($"Entity {entity.Id} has no linked Terminus, cannot send message");
            return;
        }

        Terminus terminus = terminusFlagComponent.Terminus;

        if (terminus == null)
        {
            Log.Error($"Entity {entity.Id} TerminusFlagComponent.Terminus is null, cannot send message");
            return;
        }

        terminus.Send(message);
    }

    /// <summary>
    /// 通过关联的 Terminus 发送一个漫游消息到指定的漫游类型。
    /// </summary>
    /// <remarks>
    /// 性能建议：如果需要频繁发送消息，建议先获取 Terminus 后直接调用其 Send 方法，可以避免重复的组件查找开销。
    /// <para>获取 Terminus 的方式：</para>
    /// <code>
    /// // 方式 1：直接获取（如果确定存在）
    /// var terminus = entity.GetLinkTerminus();
    /// terminus.Send(roamingType, message);
    ///
    /// // 方式 2：安全获取（推荐）
    /// if (entity.TryGetLinkTerminus(out var terminus))
    /// {
    ///     terminus.Send(roamingType, message);
    /// }
    /// </code>
    /// </remarks>
    /// <param name="entity">已关联 Terminus 的实体</param>
    /// <param name="roamingType">目标漫游类型</param>
    /// <param name="message">要发送的消息</param>
    /// <typeparam name="T">消息类型</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Send<T>(this Entity entity, int roamingType, T message) where T : IRoamingMessage
    {
        var terminusFlagComponent = entity.GetComponent<TerminusFlagComponent>();

        if (terminusFlagComponent == null)
        {
            Log.Error($"Entity {entity.Id} has no linked Terminus, cannot send message");
            return;
        }

        Terminus terminus = terminusFlagComponent.Terminus;

        if (terminus == null)
        {
            Log.Error($"Entity {entity.Id} TerminusFlagComponent.Terminus is null, cannot send message");
            return;
        }

        terminus.Send(roamingType, message);
    }

    /// <summary>
    /// 通过关联的 Terminus 发送一个漫游 RPC 消息到指定的漫游类型。
    /// </summary>
    /// <remarks>
    /// 性能建议：如果需要频繁发送消息，建议先获取 Terminus 后直接调用其 Call 方法，可以避免重复的组件查找开销。
    /// <para>获取 Terminus 的方式：</para>
    /// <code>
    /// // 方式 1：直接获取（如果确定存在）
    /// var terminus = entity.GetLinkTerminus();
    /// var response = await terminus.Call(roamingType, request);
    ///
    /// // 方式 2：安全获取（推荐）
    /// if (entity.TryGetLinkTerminus(out var terminus))
    /// {
    ///     var response = await terminus.Call(roamingType, request);
    /// }
    /// </code>
    /// </remarks>
    /// <param name="entity">已关联 Terminus 的实体</param>
    /// <param name="roamingType">目标漫游类型</param>
    /// <param name="request">要发送的请求</param>
    /// <typeparam name="T">请求类型</typeparam>
    /// <returns>响应消息</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FTask<IResponse> Call<T>(this Entity entity, int roamingType, T request) where T : IRoamingMessage
    {
        var terminusFlagComponent = entity.GetComponent<TerminusFlagComponent>();

        if (terminusFlagComponent == null)
        {
            Log.Error($"Entity {entity.Id} has no linked Terminus, cannot call message");
            return FTask<IResponse>.FromResult(entity.Scene.MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoaming));
        }

        Terminus terminus = terminusFlagComponent.Terminus;

        if (terminus == null)
        {
            Log.Error($"Entity {entity.Id} TerminusFlagComponent.Terminus is null, cannot call message");
            return FTask<IResponse>.FromResult(entity.Scene.MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoaming));
        }

        return terminus.Call(roamingType, request);
    }

    #endregion

    #region Transfer 方法

    /// <summary>
    /// 传送关联的 Terminus 到目标场景。
    /// 传送完成后，Terminus 和关联的实体都会被销毁。
    /// </summary>
    /// <param name="entity">已关联 Terminus 的实体</param>
    /// <param name="targetSceneAddress">目标场景地址</param>
    /// <returns>错误码，0 表示成功</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FTask<uint> StartTransfer(this Entity entity, long targetSceneAddress)
    {
        var terminusFlagComponent = entity.GetComponent<TerminusFlagComponent>();

        if (terminusFlagComponent == null)
        {
            Log.Error($"Entity {entity.Id} has no linked Terminus, cannot start transfer");
            return FTask<uint>.FromResult(InnerErrorCode.ErrNotFoundRoaming);
        }

        Terminus terminus = terminusFlagComponent.Terminus;

        if (terminus == null)
        {
            Log.Error($"Entity {entity.Id} TerminusFlagComponent.Terminus is null, cannot start transfer");
            return FTask<uint>.FromResult(InnerErrorCode.ErrNotFoundRoaming);
        }

        return terminus.StartTransfer(targetSceneAddress);
    }

    #endregion
}
#endif
