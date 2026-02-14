#if FANTASY_NET
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.InnerMessage;
using Fantasy.Network.Interface;
using LightProto;
using MemoryPack;
using MongoDB.Bson.Serialization.Attributes;
// ReSharper disable UnassignedField.Global
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable CheckNamespace
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8603 // Possible null reference return.
namespace Fantasy.Network.Roaming;

/// <summary>
/// Terminus 传送完成事件参数。
/// 在 Terminus 完成传送并在目标场景中恢复后触发此事件。
/// </summary>
public struct OnTerminusTransferComplete
{
    /// <summary>
    /// 获取传送目标场景。
    /// </summary>
    public readonly Scene Scene;

    /// <summary>
    /// 获取完成传送的 Terminus 实例。
    /// </summary>
    public readonly Terminus Terminus;

    /// <summary>
    /// 获取 Terminus 关联的实体（如果存在）。
    /// </summary>
    public readonly Entity LinkEntity;

    /// <summary>
    /// 初始化一个新的 OnTerminusTransferComplete 实例。
    /// </summary>
    /// <param name="scene">传送目标场景</param>
    /// <param name="terminus">完成传送的 Terminus</param>
    /// <param name="linkEntity">Terminus 关联的实体</param>
    public OnTerminusTransferComplete(Scene scene, Terminus terminus, Entity linkEntity)
    {
        Scene = scene;
        Terminus = terminus;
        LinkEntity = linkEntity;
    }
}

/// <summary>
/// 漫游终端实体
/// </summary>
[MemoryPackable]
public sealed partial class Terminus : Entity
{
    /// <summary>
    /// 当前漫游终端的TerminusId。
    /// 可以通过 TerminusId 发送消息给这个漫游终端。
    /// 也可以理解为实体的RuntimeId。
    /// </summary>
    internal long TerminusId;
    /// <summary>
    /// 标记是否销毁过Terminus
    /// </summary>
    internal bool IsDisposeTerminus;
    /// <summary>
    /// 当前漫游终端的类型。
    /// </summary>
    public int RoamingType { get; internal set; }
    /// <summary>
    /// 漫游转发Session所在的Scene的Address。
    /// </summary>
    public long ForwardSceneAddress{ get; internal set; }
    /// <summary>
    /// 漫游转发Session的Address。
    /// 不知道原理千万不要手动赋值这个。
    /// </summary>
    public long ForwardSessionAddress{ get; internal set; }
    /// <summary>
    /// 关联的玩家实体
    /// </summary>
    public Entity? TerminusEntity { get; private set; }
    /// <summary>
    /// 漫游消息锁。
    /// </summary>
    [ProtoIgnore]
    [MemoryPackIgnore]
    internal CoroutineLock? RoamingMessageLock;
    /// <summary>
    /// 是否停止发送转发代码到Roaming
    /// </summary>
    [ProtoIgnore]
    [MemoryPackIgnore]
    internal bool StopForwarding;
    /// <summary>
    /// 存放其他漫游终端的Id。
    /// 通过这个Id可以发送消息给它。
    /// </summary>
    [ProtoIgnore]
    [MemoryPackIgnore]
    private readonly Dictionary<int, long> _roamingTerminusId = new Dictionary<int, long>();

    /// <summary>
    /// 销毁
    /// </summary>
    public override void Dispose()
    {
        if (IsDisposed || IsDisposeTerminus)
        {
            return;
        }
        
        IsDisposeTerminus = true;
        Scene.TerminusComponent.RemoveTerminus(Id, false);

        TerminusId = 0;
        RoamingType = 0;
        ForwardSceneAddress = 0;
        ForwardSessionAddress = 0;
        TerminusEntity = null;
        
        if (RoamingMessageLock != null)
        {
            RoamingMessageLock.Dispose();
            RoamingMessageLock = null;
        }
        
        _roamingTerminusId.Clear();
        base.Dispose();
    }

    #region Link

    /// <summary>
    /// 创建并关联一个终端实体。
    /// 关联后，所有发送给 Terminus 的消息将转由该实体处理。
    /// 注意：销毁关联实体不会自动销毁 Terminus，需通过 autoDispose 参数控制生命周期。
    /// </summary>
    /// <param name="autoDispose">Terminus 销毁时是否自动销毁该关联实体</param>
    /// <typeparam name="T">要创建的实体类型</typeparam>
    /// <returns>创建的实体实例，如果已存在关联实体则返回 null</returns>
    public async FTask<T> LinkTerminusEntity<T>(bool autoDispose) where T : Entity, new()
    {
        if (!IsCanLink())
        {
            return null;
        }

        var linkEntity = Entity.Create<T>(Scene, true, true);
        await LinkEntity(linkEntity, autoDispose);
        return linkEntity;
    }

    /// <summary>
    /// 关联一个已存在的实体到此终端。
    /// 关联后，所有发送给 Terminus 的消息将转由该实体处理。
    /// 注意：销毁关联实体不会自动销毁 Terminus，需通过 autoDispose 参数控制生命周期。
    /// </summary>
    /// <param name="entity">要关联的实体</param>
    /// <param name="autoDispose">Terminus 销毁时是否自动销毁该关联实体</param>
    public async FTask LinkTerminusEntity(Entity entity, bool autoDispose)
    {
        if (entity == null)
        {
            Log.Error("Entity cannot be empty");
            return;
        }
        
        if (!IsCanLink())
        {
            return;
        }
        
        // 如果需要关联的实体已经关联过其他实体
        
        var terminusFlagComponent = entity.GetComponent<TerminusFlagComponent>();
        
        if (terminusFlagComponent != null)
        {
            Terminus terminus = terminusFlagComponent.Terminus;
            
            if (terminus != null)
            {
                Log.Error($"Entity {entity.Id} is already linked to Terminus {terminus.Id}");
                return;
            }
            else
            {
                // 如果关联的Terminus已经销毁了那就删除掉就可以了
                // 因为这情况不会影响逻辑，一般是在Terminus已经断开了
                // 这个情况是允许的
                entity.RemoveComponent<TerminusFlagComponent>();
            }
        }

        await LinkEntity(entity, autoDispose);
    }

    private async FTask LinkEntity(Entity entity, bool autoDispose)
    {
        var isLocked = false;
        var syncRoaming = TerminusId != 0;
        var entityRuntimeId = entity.RuntimeId;
        
        try
        {
            if (syncRoaming)
            {
                // 连接之前要先锁定避免中间会有消息发送
                var lockErrorCode = await Lock();

                if (lockErrorCode != 0)
                {
                    // 锁定失败，关联实体操作中止
                    Log.Error($"Failed to lock Terminus {Id} before linking entity. ErrorCode: {lockErrorCode}. Link operation aborted.");
                    return;
                }
            }

            isLocked = true;
            TerminusEntity = entity;
            TerminusId = entityRuntimeId;
            
            // 给当前实体添加组件用来代表是已经关联了Terminus
            
            entity.AddComponent<TerminusFlagComponent>().Terminus = this;
            
            // 只有autoDispose = true的时候当前Terminus添加组件来代表已经关联了Entity
            
            if (autoDispose)
            {
                AddComponent<TerminusEntityFlagComponent>().LinkEntity = entity;
            }

            if (syncRoaming)
            {
                // 操作完成执行解锁
                await UnLock();
            }
            
            isLocked = false;
        }
        catch (Exception e)
        {
            Log.Error(e);
            
            if (syncRoaming && isLocked)
            {
                await UnLock();
            }
        }
    }
    
    private bool IsCanLink()
    {
        // 如果TerminusEntity存在，表示已经关联过Entity
        
        if (TerminusEntity != null)
        {
            Log.Error($"TerminusEntity:{TerminusEntity.Type.FullName} Already exists!");
            return false;
        }
        
        // 如果当前已经挂载了TerminusEntityFlagComponent表示以前连接到某个实体
        
        var terminusEntityFlagComponent = GetComponent<TerminusEntityFlagComponent>();

        if (terminusEntityFlagComponent == null)
        {
            return true;
        }
        
        Entity linkEntity = terminusEntityFlagComponent.LinkEntity;

        if (linkEntity != null)
        {
            Log.Error($"TerminusEntity:{linkEntity.Type.FullName} Already exists!");
            return false;
        }
        
        // 如果当前关联过的实体已经被销毁了
        // 正常情况是不会出现这个问题的如果有加打印一个警告出来便于后期维护
        Log.Warning($"Terminus {Id} has TerminusEntityFlagComponent but LinkEntity is null. This should not happen normally. The linked entity may have been disposed without properly cleaning up the Terminus relationship. Cleaning up orphaned component.");
        RemoveComponent<TerminusEntityFlagComponent>();
        return true;
    }

    #endregion

    #region Transfer

    /// <summary>
    /// 传送漫游终端
    /// 传送完成后，漫游终端和关联的玩家实体都会被销毁。
    /// 所以如果有其他组件关联这个实体，要提前记录好Id，方便传送后清理。
    /// </summary>
    /// <returns></returns>
    public async FTask<uint> StartTransfer(long targetSceneAddress)
    {
        var currentSceneAddress = Scene.SceneConfig.Address;
        
        if (targetSceneAddress == currentSceneAddress)
        {
            Log.Warning($"Unable to teleport to your own scene targetSceneAddress:{targetSceneAddress} == currentSceneAddress:{currentSceneAddress}");
            return 0;
        }
        
        var isLocked = false;
        
        try
        {
            // 传送目标服务器之前要先锁定，防止再传送过程中还有其他消息发送过来。
            var lockErrorCode = await Lock();
            if (lockErrorCode != 0)
            {
                Log.Error($"Failed to lock Terminus {Id} before transfer. ErrorCode: {lockErrorCode}");
                return lockErrorCode;
            }
            isLocked = true;
            // 开始执行传送请求。
            var response = (I_TransferTerminusResponse)await Scene.NetworkMessagingComponent.Call(
                targetSceneAddress,
                new I_TransferTerminusRequest()
                {
                    Terminus = this
                });
            if (response.ErrorCode != 0)
            {
                // 如果传送出现异常，需要先解锁，不然会出现一直卡死的问题。
                await UnLock();
                isLocked = false;
                return response.ErrorCode;
            }
            // 在当前Scene下移除漫游终端。
            Scene.TerminusComponent.RemoveTerminus(Id, true);
        }
        catch (Exception e)
        {
            Log.Error(e);
            
            // 如果代码执行出现任何异常，要先去解锁，避免会出现卡死的问题。
            if (isLocked)
            {
                await UnLock();
            }
            
            return InnerErrorCode.ErrTerminusStartTransfer;
        }

        return 0;
    }

    /// <summary>
    /// 传送完成。
    /// 当传送完成后，需要清理漫游终端。
    /// </summary>
    /// <returns></returns>
    public async FTask<uint> TransferComplete(Scene scene)
    {
        // 首先恢复漫游终端的序列化数据。并且注册到框架中。
        
        Deserialize(scene);
        TerminusId = RuntimeId;
        
        if (TerminusEntity != null)
        {
            TerminusEntity.Deserialize(scene);
            TerminusId = TerminusEntity.RuntimeId;
        }
        
        // 然后要解锁下漫游
        
        var result = await UnLock();
        
        if (result != InnerErrorCode.Success)
        {
            return result;
        }
        
        // 发送传送成功的事件
        await scene.EventComponent.PublishAsync(new OnTerminusTransferComplete(scene, this, TerminusEntity!));
        return result;
    }

    /// <summary>
    /// 锁定漫游当执行锁定了后，所有消息都会被暂时放入队列中不会发送。
    /// 必须要解锁后才能继续发送消息。
    /// </summary>
    /// <returns></returns>
    private async FTask<uint> Lock()
    {
        var response = await Scene.NetworkMessagingComponent.Call(ForwardSceneAddress,
            new I_LockTerminusIdRequest()
            {
                RoamingId = Id,
                RoamingType = RoamingType
            });
        return response.ErrorCode;
    }
    
    /// <summary>
    /// 解锁漫游
    /// </summary>
    /// <returns></returns>
    private async FTask<uint> UnLock()
    {
        var response = await Scene.NetworkMessagingComponent.Call(ForwardSceneAddress,
            new I_UnLockTerminusIdRequest()
            {
                RoamingId = Id,
                RoamingType = RoamingType,
                TerminusId = TerminusId,
                TargetSceneAddress = Scene.Address
            });
        return response.ErrorCode;
    }

    #endregion

    #region Message

    private async FTask<long> GetTerminusId(int roamingType)
    {
        if (IsDisposed)
        {
            return 0;
        }

        var response = (I_GetTerminusIdResponse)await Scene.NetworkMessagingComponent.Call(
            ForwardSceneAddress,
            new I_GetTerminusIdRequest()
            {
                RoamingId = Id,
                RoamingType = roamingType
            });
        return response.TerminusId;
    }

    /// <summary>
    /// 发送一个消息给客户端
    /// </summary>
    /// <param name="message"></param>
    public void Send<T>(T message) where T : IRoamingMessage
    {
        if (StopForwarding)
        {
            return;
        }
        
        Scene.NetworkMessagingComponent.Send(ForwardSessionAddress, message);
    }
    /// <summary>
    /// 发送一个漫游消息
    /// </summary>
    /// <param name="roamingType"></param>
    /// <param name="message"></param>
    public void Send<T>(int roamingType, T message) where T : IRoamingMessage
    {
        Call(roamingType,  message).Coroutine();
    }

    /// <summary>
    /// 发送一个漫游RPC消息。
    /// </summary>
    /// <param name="roamingType"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    public async FTask<IResponse> Call<T>(int roamingType, T request) where T : IRoamingMessage
    {
        if (IsDisposed)
        {
            return Scene.MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoaming);
        }

        if (roamingType == RoamingType)
        {
            Log.Warning($"Does not support sending messages to the same scene as roamingType currentRoamingType:{RoamingType} roamingType:{roamingType}");
            return Scene.MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoaming);
        }

        var failCount = 0;
        var runtimeId = RuntimeId;
        var iRouteResponse = Scene.MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoaming);
        _roamingTerminusId.TryGetValue(roamingType, out var address);

        using (await RoamingMessageLock!.Wait(roamingType, "Terminus Call request"))
        {
            while (!IsDisposed)
            {
                if (address == 0)
                {
                    address = await GetTerminusId(roamingType);
                    
                    if (address != 0)
                    {
                        _roamingTerminusId[roamingType] = address;
                    }
                    else
                    {
                        return Scene.MessageDispatcherComponent.CreateResponse(request.OpCode(), InnerErrorCode.ErrNotFoundRoaming);
                    }
                }

                iRouteResponse = await Scene.NetworkMessagingComponent.Call(address, request);

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
                            Log.Error($"Terminus.Call failCount > 20 route send message fail, TerminusId: {address}");
                            return iRouteResponse;
                        }

                        await Scene.TimerComponent.Net.WaitAsync(100);

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
}
#endif