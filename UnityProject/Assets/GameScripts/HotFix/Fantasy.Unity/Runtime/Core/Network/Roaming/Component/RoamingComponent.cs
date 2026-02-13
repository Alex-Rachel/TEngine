#if FANTASY_NET
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Timer;
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8603 // Possible null reference return.

namespace Fantasy.Network.Roaming;

/// <summary>
/// 创建漫游组件的状态
/// </summary>
public enum CreateRoamingStatus
{
    /// <summary>
    /// 新创建的漫游组件
    /// </summary>
    NewCreated,

    /// <summary>
    /// 使用已存在的漫游组件
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// 错误：当前Session已经创建了不同roamingId的漫游组件
    /// </summary>
    SessionAlreadyHasRoaming
}

/// <summary>
/// 创建漫游组件的返回结果
/// </summary>
public readonly struct CreateRoamingResult
{
    /// <summary>
    /// 创建状态
    /// </summary>
    public readonly CreateRoamingStatus Status;

    /// <summary>
    /// 漫游组件实例。如果创建失败则为null
    /// </summary>
    public readonly SessionRoamingComponent Roaming;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="status">创建状态</param>
    /// <param name="roamingComponent">漫游组件实例</param>
    public CreateRoamingResult(CreateRoamingStatus status, SessionRoamingComponent roamingComponent)
    {
        Status = status;
        Roaming = roamingComponent;
    }
}

/// <summary>
/// 漫游组件，用来管理当然Scene下的所有漫游消息。
/// 大多数是在Gate这样的转发服务器上创建的。
/// </summary>
public sealed class RoamingComponent : Entity
{
    private TimerSchedulerNet _timerSchedulerNet;
    private readonly Dictionary<long, SessionRoamingComponent> _sessionRoamingComponents = new();
    private readonly Dictionary<long, long> _delayRemoveTaskId = new();

    internal RoamingComponent Initialize()
    {
        _timerSchedulerNet = Scene.TimerComponent.Net;
        return this;
    }

    /// <summary>
    /// 销毁方法。
    /// </summary>
    public override void Dispose()
    {
        DisposeAsync().Coroutine();
    }

    private async FTask DisposeAsync()
    {
        foreach (var (_,taskId) in _delayRemoveTaskId)
        {
            _timerSchedulerNet.Remove(taskId);
        }
        
        _delayRemoveTaskId.Clear();
        
        foreach (var (_, sessionRoamingComponent) in _sessionRoamingComponents)
        {
            await sessionRoamingComponent.UnLinkAll();
            sessionRoamingComponent.Dispose();
        }
        
        _sessionRoamingComponents.Clear();
        _timerSchedulerNet = null;
        base.Dispose();
    }

    /// <summary>
    /// 给Session会话增加漫游功能
    /// 如果指定的roamingId已经存在漫游，会把这个漫游功能和当前Session会话关联起来。
    /// </summary>
    /// <param name="session"></param>
    /// <param name="roamingId">自定义roamingId，这个Id在漫游中并没有实际作用，但用户可以用这个id来进行标记。。</param>
    /// <param name="delayRemove">如果开启了自定断开漫游功能需要设置一个延迟多久执行断开。如果设置为0表示不会自动断开</param>
    /// <returns>创建成功会返回SessionRoamingComponent组件，这个组件提供漫游的所有功能。</returns>
    internal async FTask<CreateRoamingResult> Create(Session session, long roamingId, int delayRemove)
    {
        CreateRoamingStatus status;
        SessionRoamingComponent sessionRoamingComponent;

        if (session.SessionRoamingComponent != null)
        {
            if (session.SessionRoamingComponent.Id != roamingId)
            {
                Log.Error("The current session has created a SessionRoamingComponent.");
                return new CreateRoamingResult(CreateRoamingStatus.SessionAlreadyHasRoaming, null);
            }

            sessionRoamingComponent = session.SessionRoamingComponent;
            status = CreateRoamingStatus.AlreadyExists;
        }
        else
        {
            if (!_sessionRoamingComponents.TryGetValue(roamingId, out sessionRoamingComponent))
            {
                sessionRoamingComponent = Entity.Create<SessionRoamingComponent>(Scene, roamingId, true, true);
                sessionRoamingComponent.Initialize(session);
                _sessionRoamingComponents.Add(roamingId, sessionRoamingComponent);
                status = CreateRoamingStatus.NewCreated;
            }
            else
            {
                Session parentSession = sessionRoamingComponent.Session;

                if (parentSession != null)
                {
                    // 这里要打印一个警告,说明当前已经绑定了一个Session并且Session没有断开。这时候会替换到这个Session。
                    var sessionRoamingFlgComponent = parentSession.GetComponent<SessionRoamingFlgComponent>();
                    if (sessionRoamingFlgComponent != null)
                    {
                        // 设置组件不会在销毁的时候移除SessionRoamingComponent
                        sessionRoamingFlgComponent.DoNotRemove = true;
                        parentSession.RemoveComponent<SessionRoamingFlgComponent>();
                    }
                    parentSession.SessionRoamingComponent = null;
                }
                else
                {
                    // 有可能关联的Session已经断开过，需要清除下定时删除任务
                    session.Scene.RoamingComponent.CancelRemoveTask(roamingId);
                }

                sessionRoamingComponent.Session = session;
                session.SessionRoamingComponent = sessionRoamingComponent;
                // 重新设定ForwardSessionAddress
                await sessionRoamingComponent.SetForwardSessionAddress(session);
                status = CreateRoamingStatus.AlreadyExists;
            }
        }

        session.AddComponent<SessionRoamingFlgComponent>(roamingId).DelayRemove = delayRemove;

        return new CreateRoamingResult(status, sessionRoamingComponent!);
    }
    
    /// <summary>
    /// 根据自定义roamingId获取漫游组件
    /// </summary>
    /// <param name="roamingId">自定义roamingId，这个Id在漫游中并没有实际作用，但用户可以用这个id来进行标记。</param>
    /// <returns></returns>
    internal SessionRoamingComponent Get(long roamingId)
    {
        return _sessionRoamingComponents.GetValueOrDefault(roamingId);
    }

    /// <summary>
    /// 根据自定义roamingId获取漫游组件
    /// </summary>
    /// <param name="roamingId">自定义roamingId，这个Id在漫游中并没有实际作用，但用户可以用这个id来进行标记。</param>
    /// <param name="sessionRoamingComponent">SessionRoamingComponent组件，这个组件提供漫游的所有功能。</param>
    /// <returns></returns>
    internal bool TryGet(long roamingId, out SessionRoamingComponent sessionRoamingComponent)
    {
        return _sessionRoamingComponents.TryGetValue(roamingId, out sessionRoamingComponent);
    }

    /// <summary>
    /// 获取当前Session会话的漫游组件
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    internal SessionRoamingComponent Get(Session session)
    {
        var sessionRoamingFlgComponent = session.GetComponent<SessionRoamingFlgComponent>();
        
        if (sessionRoamingFlgComponent != null)
        {
            if (_sessionRoamingComponents.TryGetValue(sessionRoamingFlgComponent.Id, out var sessionRoamingComponent))
            {
                return sessionRoamingComponent;
            }

            Log.Error($"There is no SessionRoamingComponent with roamingId: {sessionRoamingFlgComponent.Id}.");
        }
        else
        {
            Log.Error("The current session has not created a roaming session yet, so you need to create one first.");
        }

        return null;
    }

    /// <summary>
    /// 获取当前Session会话的漫游组件
    /// </summary>
    /// <param name="session"></param>
    /// <param name="sessionRoamingComponent"></param>
    /// <returns></returns>
    internal bool TryGet(Session session, out SessionRoamingComponent sessionRoamingComponent)
    {
        var sessionRoamingFlgComponent = session.GetComponent<SessionRoamingFlgComponent>();

        if (sessionRoamingFlgComponent != null)
        {
            return _sessionRoamingComponents.TryGetValue(sessionRoamingFlgComponent.Id, out sessionRoamingComponent);
        }
        
        sessionRoamingComponent = null;
        return false;
    }

    /// <summary>
    /// 移动一个设置延迟移除的任务
    /// </summary>
    /// <param name="roamingId"></param>
    private void CancelRemoveTask(long roamingId)
    {
        if (_delayRemoveTaskId.Remove(roamingId, out var taskId))
        {
            _timerSchedulerNet.Remove(taskId);
        }
    }

    /// <summary>
    /// 移除一个漫游
    /// </summary>
    /// <param name="roamingId"></param>
    /// <param name="roamingType">要移除的RoamingType，默认不设置是移除所有漫游。</param>
    /// <param name="delayRemove">当设置了延迟移除时间后，会在设置的时间后再进行移除。</param>
    internal async FTask Remove(long roamingId, int roamingType, int delayRemove = 0)
    {
        CancelRemoveTask(roamingId);

        if (delayRemove <= 0)
        {
            await InnerRemove(roamingId, roamingType);
            return;
        }

        var taskId = _timerSchedulerNet.OnceTimer(delayRemove, () =>
        {
            InnerRemove(roamingId, roamingType).Coroutine();
        });
        
        _delayRemoveTaskId.Add(roamingId, taskId);
    }

    private async FTask InnerRemove(long roamingId, int roamingType)
    {
        if (!_sessionRoamingComponents.Remove(roamingId, out var sessionRoamingComponent))
        {
            return;
        }

        if (roamingType == 0)
        {
            await sessionRoamingComponent.UnLinkAll();
            sessionRoamingComponent.Dispose();
        }
        else
        {
            await sessionRoamingComponent.UnLink(roamingType, true);
        }
        
        _delayRemoveTaskId.Remove(roamingId);
    }
}

/// <summary>
/// 漫游Roaming帮助类
/// </summary>
public static class RoamingHelper
{
    /// <summary>
    /// 给Session会话增加漫游功能
    /// 如果指定的roamingId已经存在漫游，会把这个漫游功能和当前Session会话关联起来。
    /// </summary>
    /// <param name="session"></param>
    /// <param name="roamingId">自定义roamingId，这个Id在漫游中并没有实际作用，但用户可以用这个id来进行标记。</param>
    /// <param name="delayRemove">如果开启了自定断开漫游功能需要设置一个延迟多久执行断开。</param>
    /// <returns>创建成功会返回SessionRoamingComponent组件，这个组件提供漫游的所有功能。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async FTask<SessionRoamingComponent> CreateRoaming(this Session session, long roamingId, int delayRemove = 1000 * 60 * 3)
    {
        return (await session.Scene.RoamingComponent.Create(session, roamingId, delayRemove)).Roaming;
    }
    
    /// <summary>
    /// 给Session会话增加漫游功能，返回详细的创建状态信息
    /// 如果指定的roamingId已经存在漫游，会把这个漫游功能和当前Session会话关联起来。
    /// </summary>
    /// <param name="session"></param>
    /// <param name="roamingId">自定义roamingId，这个Id在漫游中并没有实际作用，但用户可以用这个id来进行标记。</param>
    /// <param name="delayRemove">如果开启了自定断开漫游功能需要设置一个延迟多久执行断开。</param>
    /// <returns>返回CreateRoamingResult结构体，包含创建状态和漫游组件。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FTask<CreateRoamingResult> TryCreateRoaming(this Session session, long roamingId, int delayRemove = 1000 * 60 * 3)
    {
        return session.Scene.RoamingComponent.Create(session, roamingId, delayRemove);
    }

    /// <summary>
    /// 获取指定的漫游组件
    /// </summary>
    /// <param name="scene"></param>
    /// <param name="roamingId">自定义roamingId</param>zg
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SessionRoamingComponent GetRoaming(Scene scene, long roamingId)
    {
        return scene.RoamingComponent.Get(roamingId);
    }
    
    /// <summary>
    /// 获取指定的漫游组件
    /// </summary>
    /// <param name="scene"></param>
    /// <param name="roamingId">自定义roamingId</param>
    /// <param name="sessionRoamingComponent"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetRoaming(Scene scene, long roamingId, out SessionRoamingComponent sessionRoamingComponent)
    {
        return scene.RoamingComponent.TryGet(roamingId, out sessionRoamingComponent);
    }

    /// <summary>
    /// 获取当前Session会话的漫游组件
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SessionRoamingComponent GetRoaming(this Session session)
    {
        return session.Scene.RoamingComponent.Get(session);
    }

    /// <summary>
    /// 获取当前Session会话的漫游组件
    /// </summary>
    /// <param name="session"></param>
    /// <param name="sessionRoamingComponent"></param>
    /// <returns>如果返回为false表示没有获取到漫游组件。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetRoaming(this Session session, out SessionRoamingComponent sessionRoamingComponent)
    {
        return session.Scene.RoamingComponent.TryGet(session, out sessionRoamingComponent);
    }

    /// <summary>
    /// 移除一个漫游
    /// </summary>
    /// <param name="session"></param>
    /// <param name="roamingType">要移除的RoamingType，默认不设置是移除所有漫游。</param>
    /// <param name="delayRemove">当设置了延迟移除时间后，会在设置的时间后再进行移除。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async FTask RemoveRoaming(this Session session, int roamingType = 0, int delayRemove = 0)
    {
        if (session.SessionRoamingComponent == null || session.SessionRoamingComponent.Id == 0)
        {
            return;
        }
        
        await session.Scene.RoamingComponent.Remove(session.SessionRoamingComponent.Id, roamingType, delayRemove);
    }

    /// <summary>
    /// 移除一个漫游
    /// </summary>
    /// <param name="scene"></param>
    /// <param name="roamingId"></param>
    /// <param name="roamingType">要移除的RoamingType，默认不设置是移除所有漫游。</param>
    /// <param name="delayRemove">当设置了延迟移除时间后，会在设置的时间后再进行移除。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async FTask RemoveRoaming(Scene scene, long roamingId, int roamingType = 0, int delayRemove = 0)
    {
        if (roamingId == 0)
        {
            return;
        }

        await scene.RoamingComponent.Remove(roamingId, roamingType, delayRemove);
    }
}
#endif