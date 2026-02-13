#if FANTASY_NET
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.InnerMessage;
using Fantasy.Pool;

// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

namespace Fantasy.Network.Interface;

/// <summary>
/// 泛型可寻址路由基类，实现了 <see cref="IAddressMessageHandler"/> 接口，用于处理特定实体和可寻址路由消息类型的路由。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TMessage">可寻址路由消息类型。</typeparam>
public abstract class Addressable<TEntity, TMessage> : IAddressMessageHandler where TEntity : Entity where TMessage : IAddressableMessage
{
    /// <summary>
    /// 获取消息类型。
    /// </summary>
    /// <returns>消息类型。</returns>
    public Type Type()
    {
        return typeof(TMessage);
    }

    /// <summary>
    /// 处理可寻址Address消息。
    /// </summary>
    /// <param name="session">会话。</param>
    /// <param name="entity">实体。</param>
    /// <param name="rpcId">RPC标识。</param>
    /// <param name="addressMessage">可寻址Address消息。</param>
    public async FTask Handle(Session session, Entity entity, uint rpcId, object addressMessage)
    {
        if (addressMessage is not TMessage tAddressMessage)
        {
            Log.Error($"Message type conversion error: {addressMessage.GetType().FullName} to {typeof(TMessage).Name}");
            return;
        }

        if (entity is not TEntity tEntity)
        {
            Log.Error($"{this.GetType().Name} Addressable type conversion error: {entity.GetType().Name} to {typeof(TEntity).Name}");
            return;
        }

        try
        {
            await Run(tEntity, tAddressMessage);
        }
        catch (Exception e)
        {
            if (entity is not Scene scene)
            {
                scene = entity.Scene;
            }
                
            Log.Error($"SceneConfigId:{session.Scene.SceneConfigId} ProcessConfigId:{scene.Process.Id} SceneType:{scene.SceneType} EntityId {tEntity.Id} : Error {e}");
        }
        finally
        {
            session.Send(MessageObjectPool<AddressResponse>.Rent(), rpcId);
            tAddressMessage.Dispose();
        }
    }

    /// <summary>
    /// 运行处理可寻址Address消息。
    /// </summary>
    /// <param name="entity">实体。</param>
    /// <param name="message">可寻址Address消息。</param>
    protected abstract FTask Run(TEntity entity, TMessage message);
}

/// <summary>
/// 泛型可寻址RPC路由基类，实现了 <see cref="IAddressMessageHandler"/> 接口，用于处理特定实体和可寻址RPC路由请求类型的路由。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TAddressRequest">可寻址RPC路由请求类型。</typeparam>
/// <typeparam name="TAddressResponse">可寻址RPC路由响应类型。</typeparam>
public abstract class AddressableRPC<TEntity, TAddressRequest, TAddressResponse> : IAddressMessageHandler
    where TEntity : Entity
    where TAddressRequest : IAddressableRequest
    where TAddressResponse : AMessage, IAddressableResponse, new()
{
    /// <summary>
    /// 获取消息类型。
    /// </summary>
    /// <returns>消息类型。</returns>
    public Type Type()
    {
        return typeof(TAddressRequest);
    }

    /// <summary>
    /// 处理可寻址RPC路由请求。
    /// </summary>
    /// <param name="session">会话。</param>
    /// <param name="entity">实体。</param>
    /// <param name="rpcId">RPC标识。</param>
    /// <param name="routeMessage">可寻址RPCAddress请求。</param>
    public async FTask Handle(Session session, Entity entity, uint rpcId, object routeMessage)
    {
        if (routeMessage is not TAddressRequest tAddressRequest)
        {
            Log.Error(
                $"Message type conversion error: {routeMessage.GetType().FullName} to {typeof(TAddressRequest).Name}");
            return;
        }

        if (entity is not TEntity tEntity)
        {
            Log.Error(
                $"{this.GetType().Name} Addressable type conversion error: {entity.GetType().Name} to {typeof(TEntity).Name}");
            return;
        }

        var isReply = false;
        var response = MessageObjectPool<TAddressResponse>.Rent();

        void Reply()
        {
            if (isReply)
            {
                return;
            }

            isReply = true;

            if (session.IsDisposed)
            {
                return;
            }

            session.Send(response, rpcId);
        }

        try
        {
            await Run(tEntity, tAddressRequest, response, Reply);
        }
        catch (Exception e)
        {
            if (entity is not Scene scene)
            {
                scene = entity.Scene;
            }

            Log.Error($"SceneConfigId:{session.Scene.SceneConfigId} ProcessConfigId:{scene.Process.Id} SceneType:{scene.SceneType} EntityId {tEntity.Id} : Error {e}");
            response.ErrorCode = InnerErrorCode.ErrRpcFail;
        }
        finally
        {
            Reply();
            tAddressRequest.Dispose();
        }
    }

    /// <summary>
    /// 运行处理可寻址RPC路由请求。
    /// </summary>
    /// <param name="entity">实体。</param>
    /// <param name="request">可寻址RPC路由请求。</param>
    /// <param name="response">可寻址RPC路由响应。</param>
    /// <param name="reply">回复操作。</param>
    protected abstract FTask Run(TEntity entity, TAddressRequest request, TAddressResponse response, Action reply);
}
#endif
