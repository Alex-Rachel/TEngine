#if FANTASY_NET
using Fantasy.Async;
using Fantasy.DataStructure.Collection;
using Fantasy.Entitas;
using Fantasy.InnerMessage;
using Fantasy.Network.Interface;
using Fantasy.PacketParser.Interface;
using Fantasy.Platform.Net;
using Fantasy.Scheduler;
using Fantasy.Timer;
// ReSharper disable CheckNamespace
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace Fantasy.Network.Roaming;

/// <summary>
/// Session的漫游组件。
/// 用于关联对应的Session的功能。
/// 但这个组件并不会挂载到这个Session下。
/// </summary>
public sealed class SessionRoamingComponent : Entity
{
    internal EntityReference<Session> Session;
    
    private CoroutineLock? _roamingLock;
    private CoroutineLock? _roamingMessageLock;
    private TimerComponent _timerComponent;
    private NetworkMessagingComponent _networkMessagingComponent;
    private MessageDispatcherComponent _messageDispatcherComponent;
    
    /// <summary>
    /// 漫游的列表。
    /// </summary>
    private readonly Dictionary<int, Roaming> _roaming = new Dictionary<int, Roaming>();

    internal void Initialize(Session session)
    {
        var scene = session.Scene;
        _timerComponent = scene.TimerComponent;
        _networkMessagingComponent = scene.NetworkMessagingComponent;
        _messageDispatcherComponent = scene.MessageDispatcherComponent;
        _roamingLock = scene.CoroutineLockComponent.Create(this.GetType().TypeHandle.Value.ToInt64());
        _roamingMessageLock = scene.CoroutineLockComponent.Create(this.GetType().TypeHandle.Value.ToInt64());
        
        Session = session;
        session.SessionRoamingComponent = this;
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

        if (_roamingLock != null)
        {
            _roamingLock.Dispose();
            _roamingLock = null;
        }
        
        if (_roamingMessageLock != null)
        {
            _roamingMessageLock.Dispose();
            _roamingMessageLock = null;
        }
        
        Session.Clear();
        _timerComponent = null;
        _networkMessagingComponent = null;
        _messageDispatcherComponent = null;
        base.Dispose();
    }

    #region Get

    /// <summary>
    /// 尝试获取一个漫游。
    /// </summary>
    /// <param name="roamingType"></param>
    /// <param name="roaming"></param>
    /// <returns></returns>
    public bool TryGetRoaming(int roamingType, out Roaming roaming)
    {
        return _roaming.TryGetValue(roamingType, out roaming);
    }

    #endregion

    #region Remove

    internal void Remove(int roamingType, bool isDisponse)
    {
        if (!_roaming.Remove(roamingType, out var roaming))
        {
            return;
        }

        if (isDisponse)
        {
            roaming.Dispose();
        }
    }

    #endregion
    
    #region Link

    /// <summary>
    /// 重新设定ForwardSessionAddress
    /// </summary>
    /// <param name="session"></param>
    internal async FTask SetForwardSessionAddress(Session session)
    {
        using var tasks = ListPool<FTask>.Create();
        var forwardSessionAddress = session.RuntimeId;
        
        foreach (var (_, roaming) in _roaming)
        {
            tasks.Add(roaming.SetForwardSessionAddress(forwardSessionAddress));
        }

        await FTask.WaitAll(tasks);
    }

    /// <summary>
    /// 通知所有连接的Terminus不要再转发消息
    /// 用于session已经断开但实体还在工作的场景，避免框架报错
    /// </summary>
    internal async FTask StopForwarding()
    {
        using var tasks = ListPool<FTask>.Create();

        foreach (var (_, roaming) in _roaming)
        {
            tasks.Add(roaming.StopForwarding());
        }

        await FTask.WaitAll(tasks);
    }

    /// <summary>
    /// 建立漫游关系。
    /// </summary>
    /// <param name="session">要建立漫游协议的目标Scene的SceneConfig。</param>
    /// <param name="targetSceneConfig">需要转发的Session</param>
    /// <param name="roamingType">要创建的漫游协议类型。</param>
    /// <param name="args">要传递的Entity类型参数</param>
    /// <returns>如果建立完成会返回为0，其余不为0的都是发生错误了。可以通过InnerErrorCode.cs来查看错误。</returns>
    public async FTask<uint> Link(Session session, SceneConfig targetSceneConfig, int roamingType, Entity? args = null)
    {
        return await Link(targetSceneConfig.Address, session.RuntimeId, roamingType, args);
    }
    
    /// <summary>
    /// 建立漫游关系。
    /// </summary>
    /// <param name="targetSceneAddress">要建立漫游协议的目标Scene的Address。</param>
    /// <param name="forwardSessionAddress">需要转发的Session的Address。</param>
    /// <param name="roamingType">要创建的漫游协议类型。</param>
    /// <param name="args">要传递的Entity类型参数</param>
    /// <returns>如果建立完成会返回为0，其余不为0的都是发生错误了。可以通过InnerErrorCode.cs来查看错误。</returns>
    public async FTask<uint> Link(long targetSceneAddress, long forwardSessionAddress, int roamingType, Entity? args = null)
    {
        if (_roaming.ContainsKey(roamingType))
        {
            return InnerErrorCode.ErrLinkRoamingAlreadyExists;
        }

        var response = (I_LinkRoamingResponse)await Scene.NetworkMessagingComponent.Call(targetSceneAddress,
            new I_LinkRoamingRequest()
            {
                RoamingId = Id,
                RoamingType = roamingType,
                ForwardSessionAddress = forwardSessionAddress,
                SceneAddress = Scene.RuntimeId,
                LinkType = 0, 
                Args = args
            });
        
        if (response.ErrorCode != 0)
        {
            return response.ErrorCode;
        }

        var roaming = Entity.Create<Roaming>(Scene, true, true);
        roaming.TerminusId = response.TerminusId;
        roaming.TargetSceneAddress = targetSceneAddress;
        roaming.ForwardSessionAddress = forwardSessionAddress;
        roaming.SessionRoamingComponent = this;
        roaming.RoamingType = roamingType;
        roaming.RoamingLock = _roamingLock;
        _roaming.Add(roamingType, roaming);
        return 0;
    }


    /// <summary>
    /// 重新连接到目标服务器。用于目标服务器重启或迁移后重新建立连接。
    /// </summary>
    /// <param name="session">需要转发的Session</param>
    /// <param name="targetSceneConfig">要建立漫游协议的目标Scene的SceneConfig</param>
    /// <param name="roamingType">要重新连接的漫游类型</param>
    /// <param name="args">要传递的Entity类型参数</param>
    /// <returns></returns>
    public async FTask<uint> ReLink(Session session, SceneConfig targetSceneConfig, int roamingType, Entity? args = null)
    {
        return await ReLink(targetSceneConfig.Address, session.RuntimeId, roamingType, args);
    }

    /// <summary>
    /// 重新连接到目标服务器。用于目标服务器重启或迁移后重新建立连接。
    /// </summary>
    /// <param name="targetSceneAddress">要建立漫游协议的目标Scene的Address。</param>
    /// <param name="forwardSessionAddress">需要转发的Session的Address。</param>
    /// <param name="roamingType">要重新连接的漫游类型</param>
    /// <param name="args">要传递的Entity类型参数</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public async FTask<uint> ReLink(long targetSceneAddress, long forwardSessionAddress, int roamingType, Entity? args = null)
    {
        if (roamingType == 0)
        {
            throw new ArgumentException("roamingType cannot be 0.", nameof(roamingType));
        }
        
        if (!_roaming.TryGetValue(roamingType, out var roaming))
        {
            return InnerErrorCode.ErrNotFoundRoaming;
        }
        
        roaming.TerminusId = 0;
        roaming.TargetSceneAddress = targetSceneAddress;
        roaming.ForwardSessionAddress = forwardSessionAddress;
        
        var response = (I_LinkRoamingResponse)await Scene.NetworkMessagingComponent.Call(targetSceneAddress,
            new I_LinkRoamingRequest()
            {
                RoamingId = Id,
                RoamingType = roamingType,
                ForwardSessionAddress = forwardSessionAddress,
                SceneAddress = Scene.RuntimeId,
                LinkType = 1, 
                Args = args
            });
        
        if (response.ErrorCode != 0)
        {
            return response.ErrorCode;
        }
        
        roaming.TerminusId = response.TerminusId;
        return 0;
    }

    #endregion

    #region UnLink

    /// <summary>
    /// 断开当前的所有漫游关系。
    /// </summary>
    public async FTask UnLinkAll()
    {
        foreach (var (roamingType,roaming) in _roaming)
        {
            try
            {
                var errorCode = await roaming.Disconnect();
                
                if (errorCode != 0)
                {
                    Log.Warning($"roaming roamingId:{Id} roamingType:{roamingType} disconnect  errorCode:{errorCode}");
                }
            }
            finally
            {
                roaming.Dispose();
            }
        }
        
        _roaming.Clear();
    }

    /// <summary>
    /// 断开当前的漫游关系。
    /// <param name="removeRoamingType">要移除的RoamingType，默认不设置是移除所有漫游。</param>
    /// <param name="disposeIfEmpty">如果断开后没有任何漫游连接，是否销毁整个组件</param>
    /// </summary>
    public async FTask UnLink(int removeRoamingType, bool disposeIfEmpty)
    {
        if (removeRoamingType == 0)
        {
            throw new ArgumentException("removeRoamingType cannot be 0. Use UnLinkAll() to remove all roaming connections.", nameof(removeRoamingType));
        }
        
        if (!_roaming.Remove(removeRoamingType, out var roaming))
        {
            return;
        }

        var errorCode = await roaming.Disconnect();
        
        if (errorCode != 0)
        {
            Log.Warning($"roaming roamingId:{Id} roamingType:{removeRoamingType} disconnect  errorCode:{errorCode}");
        }

        roaming.Dispose();
        
        if(disposeIfEmpty && _roaming.Count == 0)
        {
            Dispose();
        }
    }

    #endregion

    #region OuterMessage

    /// <summary>
    /// 发送一个消息给漫游终
    /// </summary>
    /// <param name="message"></param>
    public void Send<T>(T message) where T : IRoamingMessage
    {
        Call(message.RouteType, message).Coroutine();
    }

    /// <summary>
    /// 发送一个消息给漫游终端
    /// </summary>
    /// <param name="roamingType"></param>
    /// <param name="message"></param>
    public void Send<T>(int roamingType, T message) where T : IAddressMessage
    {
        Call(roamingType, message).Coroutine();
    }

    /// <summary>
    /// 发送一个RPC消息给漫游终端
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public async FTask<IResponse> Call<T>(T message) where T : IRoamingMessage
    {
        return await Call(message.RouteType, message);
    }
    
    /// <summary>
    /// 发送一个RPC消息给漫游终端
    /// </summary>
    /// <param name="roamingType"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public async FTask<IResponse> Call<T>(int roamingType, T message) where T : IAddressMessage
    {
        if (!_roaming.TryGetValue(roamingType, out var roaming))
        {
            return _messageDispatcherComponent.CreateResponse(message.OpCode(), InnerErrorCode.ErrNotFoundRoaming);
        }

        var failCount = 0;
        var runtimeId = RuntimeId;
        var address = roaming.TerminusId;
        
        IResponse iRouteResponse = null;

        using (await _roamingMessageLock!.Wait(roaming.RoamingType, "RoamingComponent Call MemoryStream"))
        {
            while (!IsDisposed)
            {
                if (address == 0)
                {
                    address = await roaming.GetTerminusId();
                }

                if (address == 0)
                {
                    return _messageDispatcherComponent.CreateResponse(message.OpCode(), InnerErrorCode.ErrNotFoundRoaming);
                }

                iRouteResponse = await _networkMessagingComponent.Call(address, message);

                if (runtimeId != RuntimeId)
                {
                    iRouteResponse.ErrorCode = InnerErrorCode.ErrRoamingTimeout;
                }

                switch (iRouteResponse.ErrorCode)
                {
                    case InnerErrorCode.ErrRouteTimeout:
                    case InnerErrorCode.ErrRoamingTimeout:
                    {
                        return iRouteResponse;
                    }
                    case InnerErrorCode.ErrNotFoundRoute:
                    case InnerErrorCode.ErrNotFoundRoaming:
                    {
                        if (++failCount > 20)
                        {
                            Log.Error($"RoamingComponent.Call failCount > 20 route send message fail, LinkRoamingId: {address}");
                            return iRouteResponse;
                        }

                        await _timerComponent.Net.WaitAsync(100);

                        if (runtimeId != RuntimeId)
                        {
                            iRouteResponse.ErrorCode = InnerErrorCode.ErrNotFoundRoaming;
                        }

                        address = 0;
                        continue;
                    }
                    default:
                    {
                        return iRouteResponse; // 对于其他情况，直接返回响应，无需额外处理
                    }
                }
            }
        }
        
        return iRouteResponse;
    }

    #endregion

    #region InnerMessage

    internal async FTask Send(int roamingType, Type requestType, APackInfo packInfo)
    {
        await Call(roamingType, requestType, packInfo);
    }

    internal async FTask<IResponse> Call(int roamingType, Type requestType, APackInfo packInfo)
    {
        if (IsDisposed)
        {
            return _messageDispatcherComponent.CreateResponse(packInfo.ProtocolCode, InnerErrorCode.ErrNotFoundRoaming);
        }

        packInfo.IsDisposed = true;
        
        if (!_roaming.TryGetValue(roamingType, out var roaming))
        {
            return _messageDispatcherComponent.CreateResponse(packInfo.ProtocolCode, InnerErrorCode.ErrNotFoundRoaming);
        }
        
        var failCount = 0;
        var runtimeId = RuntimeId;
        var address = roaming.TerminusId;
        IResponse iRouteResponse = null;
        
        try
        {
            using (await _roamingMessageLock!.Wait(roamingType, "RoamingComponent Call MemoryStream"))
            {
                while (!IsDisposed)
                {
                    if (address == 0)
                    {
                        address = await roaming.GetTerminusId();
                    }

                    if (address == 0)
                    {
                        return _messageDispatcherComponent.CreateResponse(packInfo.ProtocolCode, InnerErrorCode.ErrNotFoundRoaming);
                    }

                    iRouteResponse = await _networkMessagingComponent.Call(address, requestType, packInfo);

                    if (runtimeId != RuntimeId)
                    {
                        iRouteResponse.ErrorCode = InnerErrorCode.ErrRoamingTimeout;
                    }

                    switch (iRouteResponse.ErrorCode)
                    {
                        case InnerErrorCode.ErrRouteTimeout:
                        case InnerErrorCode.ErrRoamingTimeout:
                        {
                            return iRouteResponse;
                        }
                        case InnerErrorCode.ErrNotFoundRoute:
                        case InnerErrorCode.ErrNotFoundRoaming:
                        {
                            if (++failCount > 20)
                            {
                                Log.Error($"RoamingComponent.Call failCount > 20 route send message fail, LinkRoamingId: {address}");
                                return iRouteResponse;
                            }

                            await _timerComponent.Net.WaitAsync(100);

                            if (runtimeId != RuntimeId)
                            {
                                iRouteResponse.ErrorCode = InnerErrorCode.ErrNotFoundRoaming;
                            }
                            address = 0;
                            continue;
                        }
                        default:
                        {
                            return iRouteResponse; // 对于其他情况，直接返回响应，无需额外处理
                        }
                    }
                }
            }
        }
        finally
        {
            packInfo.Dispose();
        }

        return iRouteResponse;
    }

    #endregion
}
#endif