#if FANTASY_NET
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.InnerMessage;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.Network.Roaming;

/// <summary>
/// 漫游实体
/// </summary>
public sealed class Roaming : Entity
{
    /// <summary>
    /// 连接到漫游TerminusId。
    /// 也可以理解为目标实体的Address。
    /// </summary>
    internal long TerminusId;
    /// <summary>
    /// 漫游目标Scene的Address。
    /// </summary>
    public long TargetSceneAddress { get; internal set; }
    /// <summary>
    /// 漫游转发Session的Address。
    /// </summary>
    public long ForwardSessionAddress { get; internal set; }
    /// <summary>
    /// 当前漫游类型。
    /// </summary>
    public int RoamingType { get; internal set; }
    /// <summary>
    /// 协程锁。
    /// </summary>
    internal CoroutineLock RoamingLock;
    /// <summary>
    /// 当前正在执行的协程锁。
    /// </summary>
    private WaitCoroutineLock? _waitCoroutineLock;
    /// <summary>
    /// 关联Session的漫游组件。
    /// </summary>
    internal SessionRoamingComponent? SessionRoamingComponent;
    /// <summary>
    /// 获得当前漫游对应终端的TerminusId。
    /// </summary>
    /// <returns></returns>
    internal async FTask<long> GetTerminusId()
    {
        using (await RoamingLock.Wait(RoamingType,"Roaming.cs GetTerminusId"))
        {
            return TerminusId;
        }
    }
    /// <summary>
    /// 设置当前漫游对应的终端的TerminusId。
    /// </summary>
    /// <param name="terminusId"></param>
    internal async FTask SetTerminusId(long terminusId)
    {
        using (await RoamingLock.Wait(RoamingType,"Roaming.cs SetTerminusId"))
        {
            TerminusId = terminusId;
        }
    }
    /// <summary>
    /// 锁定TerminusId。
    /// </summary>
    internal async FTask LockTerminusId()
    {
        _waitCoroutineLock = await RoamingLock.Wait(RoamingType,"Roaming.cs LockTerminusId");
    }

    /// <summary>
    /// 解锁TerminusId。
    /// </summary>
    /// <param name="terminusId"></param>
    /// <param name="targetSceneAddress"></param>
    internal void UnLockTerminusId(long terminusId, long targetSceneAddress)
    {
        if (_waitCoroutineLock == null)
        {
            Log.Error("terminusId unlock waitCoroutineLock is null");
            return;
        }

        TerminusId = terminusId;
        TargetSceneAddress = targetSceneAddress;
        _waitCoroutineLock.Dispose();
        _waitCoroutineLock = null;
    }

    /// <summary>
    /// 通知Terminus更新ForwardSessionAddress
    /// </summary>
    /// <param name="forwardSessionAddress"></param>
    /// <returns></returns>
    internal async FTask SetForwardSessionAddress(long forwardSessionAddress)
    {
        var response = await Scene.NetworkMessagingComponent.Call(
            TargetSceneAddress,
            new I_SetForwardSessionAddressRequest()
            {
                RoamingId = SessionRoamingComponent!.Id,
                ForwardSessionAddress = forwardSessionAddress
            });

        if (response.ErrorCode == 0)
        {
            ForwardSessionAddress = forwardSessionAddress;
            return;
        }

        Log.Warning($"SetForwardSessionAddress failed with ErrorCode: {response.ErrorCode}, RoamingId: {SessionRoamingComponent!.Id}, TargetSceneAddress: {TargetSceneAddress}");
    }

    /// <summary>
    /// 通知Terminus停止转发消息到ForwardSession。
    /// 用于Session已断开但实体还在工作的场景，避免框架报错。
    /// </summary>
    /// <returns></returns>
    internal async FTask StopForwarding()
    {
        var response = await Scene.NetworkMessagingComponent.Call(
            TargetSceneAddress,
            new I_StopForwardingRequest()
            {
                RoamingId = SessionRoamingComponent!.Id
            });

        if (response.ErrorCode == 0)
        {
            return;
        }

        Log.Warning($"StopForwarding failed with ErrorCode: {response.ErrorCode}, RoamingId: {SessionRoamingComponent!.Id}, TargetSceneAddress: {TargetSceneAddress}");
    }

    /// <summary>
    /// 断开当前漫游的连接。
    /// </summary>
    /// <returns></returns>
    internal async FTask<uint> Disconnect()
    {
        var response =
            await Scene.NetworkMessagingComponent.Call(TargetSceneAddress, new I_UnLinkRoamingRequest()
            {
                RoamingId = SessionRoamingComponent!.Id, DisposeRoaming = true
            });
        return response.ErrorCode;
    }
    /// <summary>
    /// 销毁方法
    /// </summary>
    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        if (_waitCoroutineLock != null)
        {
            _waitCoroutineLock.Dispose();
            _waitCoroutineLock = null;
        }
        
        if (SessionRoamingComponent != null)
        {
            SessionRoamingComponent.Remove(RoamingType, false);
            SessionRoamingComponent = null;
        }
       
        TerminusId = 0;
        RoamingType = 0;
        TargetSceneAddress = 0;
        ForwardSessionAddress = 0;
        
        RoamingLock = null;
        base.Dispose();
    }
}
#endif