#if FANTASY_NET
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.InnerMessage;
using Fantasy.Pool;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

namespace Fantasy.Network.Interface;

/// <summary>
/// 泛型漫游路由基类，实现了 <see cref="IAddressMessageHandler"/> 接口，用于处理特定实体和漫游路由消息类型的路由。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TMessage">漫游消息类型。</typeparam>
public abstract class Roaming<TEntity, TMessage> : IAddressMessageHandler where TEntity : Entity where TMessage : IRoamingMessage
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
    /// 处理漫游消息。
    /// </summary>
    /// <param name="session">会话。</param>
    /// <param name="entity">实体。</param>
    /// <param name="rpcId">RPC标识。</param>
    /// <param name="roamingMessage">漫游消息。</param>
    public async FTask Handle(Session session, Entity entity, uint rpcId, object roamingMessage)
    {
        if (roamingMessage is not TMessage rMessage)
        {
            Log.Error($"Message type conversion error: {roamingMessage.GetType().FullName} to {typeof(TMessage).Name}");
            return;
        }

        if (entity is not TEntity tEntity)
        {
            Log.Error($"{this.GetType().Name} Roaming type conversion error: {entity.GetType().Name} to {typeof(TEntity).Name}");
            return;
        }

        try
        {
            await Run(tEntity, rMessage);
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
            rMessage.Dispose();
        }
    }

    /// <summary>
    /// 运行处理漫游消息。
    /// </summary>
    /// <param name="terminus">终点实体。</param>
    /// <param name="message">漫游消息。</param>
    protected abstract FTask Run(TEntity terminus, TMessage message);
}

/// <summary>
/// 漫游RPC路由基类，实现了 <see cref="IAddressMessageHandler"/> 接口，用于处理特定实体和漫游RPC路由请求类型的路由。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TAddressRequest">漫游RPC路由请求类型。</typeparam>
/// <typeparam name="TAddressResponse">漫游RPC路由响应类型。</typeparam>
public abstract class RoamingRPC<TEntity, TAddressRequest, TAddressResponse> : IAddressMessageHandler
    where TEntity : Entity where TAddressRequest : IRoamingRequest where TAddressResponse : AMessage, IRoamingResponse, new()
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
    /// 处理漫游RPC路由请求。
    /// </summary>
    /// <param name="session">会话。</param>
    /// <param name="entity">实体。</param>
    /// <param name="rpcId">RPC标识。</param>
    /// <param name="addressMessage">漫游RPC路由请求。</param>
    public async FTask Handle(Session session, Entity entity, uint rpcId, object addressMessage)
    {
        if (addressMessage is not TAddressRequest aAddressRequest)
        {
            Log.Error($"Message type conversion error: {addressMessage.GetType().FullName} to {typeof(TAddressRequest).Name}");
            return;
        }

        if (entity is not TEntity tEntity)
        {
            Log.Error($"{this.GetType().Name} Roaming type conversion error: {entity.GetType().Name} to {typeof(TEntity).Name}");
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
            await Run(tEntity, aAddressRequest, response, Reply);
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
            aAddressRequest.Dispose();
        }
    }

    /// <summary>
    /// 运行处理漫游RPC路由请求。
    /// </summary>
    /// <param name="terminus">终点实体。</param>
    /// <param name="request">漫游RPC路由请求。</param>
    /// <param name="response">漫游RPC路由响应。</param>
    /// <param name="reply">回复操作。</param>
    protected abstract FTask Run(TEntity terminus, TAddressRequest request, TAddressResponse response, Action reply);
}

#endif
