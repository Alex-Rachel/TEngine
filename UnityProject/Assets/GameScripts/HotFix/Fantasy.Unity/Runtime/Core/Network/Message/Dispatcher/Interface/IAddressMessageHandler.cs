#if FANTASY_NET
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Pool;

// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

namespace Fantasy.Network.Interface;

/// <summary>
/// 表示Address消息处理器的接口。
/// </summary>
public interface IAddressMessageHandler
{
    /// <summary>
    /// 获取处理的消息类型。
    /// </summary>
    /// <returns>消息类型。</returns>
    public Type Type();
    /// <summary>
    /// 处理Address消息的方法。
    /// </summary>
    /// <param name="session">会话对象。</param>
    /// <param name="entity">实体对象。</param>
    /// <param name="rpcId">RPC标识。</param>
    /// <param name="routeMessage">要处理的Address消息。</param>
    /// <returns>异步任务。</returns>
    FTask Handle(Session session, Entity entity, uint rpcId, object routeMessage);
}

/// <summary>
/// 泛型Address基类，实现了 <see cref="IAddressMessageHandler"/> 接口，用于处理特定实体的Address消息。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TMessage">内网消息类型。</typeparam>
public abstract class Address<TEntity, TMessage> : IAddressMessageHandler where TEntity : Entity where TMessage : IAddressMessage
{
    /// <summary>
    /// 获取处理的消息类型。
    /// </summary>
    /// <returns>消息类型。</returns>
    public Type Type()
    {
        return typeof(TMessage);
    }

    /// <summary>
    /// 处理Address消息的方法。
    /// </summary>
    /// <param name="session">会话对象。</param>
    /// <param name="entity">实体对象。</param>
    /// <param name="rpcId">RPC标识。</param>
    /// <param name="addressMessage">要处理的Address消息。</param>
    /// <returns>异步任务。</returns>
    public async FTask Handle(Session session, Entity entity, uint rpcId, object addressMessage)
    {
        if (addressMessage is not TMessage tAddressMessage)
        {
            Log.Error($"Message type conversion error: {addressMessage.GetType().FullName} to {typeof(TMessage).Name}");
            return;
        }

        if (entity is not TEntity tEntity)
        {
            Log.Error($"{this.GetType().Name} Address type conversion error: {entity.GetType().Name} to {typeof(TEntity).Name}");
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
            tAddressMessage.Dispose();
        }
    }
    
    /// <summary>
    /// 运行Address消息处理逻辑。
    /// </summary>
    /// <param name="entity">实体对象。</param>
    /// <param name="message">要处理的Address消息。</param>
    /// <returns>异步任务。</returns>
    protected abstract FTask Run(TEntity entity, TMessage message);
}

/// <summary>
/// 泛型AddressRPC基类，实现了 <see cref="IAddressMessageHandler"/> 接口。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TAddressRequest">Address请求类型。</typeparam>
/// <typeparam name="TAddressResponse">Address响应类型。</typeparam>
public abstract class AddressRPC<TEntity, TAddressRequest, TAddressResponse> : IAddressMessageHandler where TEntity : Entity
    where TAddressRequest : IAddressRequest
    where TAddressResponse : AMessage, IAddressResponse, new()
{
    /// <summary>
    /// 获取处理的消息类型。
    /// </summary>
    /// <returns>消息类型。</returns>
    public Type Type()
    {
        return typeof(TAddressRequest);
    }

    /// <summary>
    /// 处理路由消息的方法。
    /// </summary>
    /// <param name="session">会话对象。</param>
    /// <param name="entity">实体对象。</param>
    /// <param name="rpcId">RPC标识。</param>
    /// <param name="addressMessage">要处理的Address消息。</param>
    /// <returns>异步任务。</returns>
    public async FTask Handle(Session session, Entity entity, uint rpcId, object addressMessage)
    {
        if (addressMessage is not TAddressRequest tAddressRequest)
        {
            Log.Error($"Message type conversion error: {addressMessage.GetType().FullName} to {typeof(TAddressRequest).Name}");
            return;
        }

        if (entity is not TEntity tEntity)
        {
            Log.Error($"{this.GetType().Name} Address type conversion error: {entity.GetType().Name} to {typeof(TEntity).Name}");
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
    /// 运行Address消息处理逻辑。
    /// </summary>
    /// <param name="entity">实体对象。</param>
    /// <param name="request">请求Address消息。</param>
    /// <param name="response">响应Address消息。</param>
    /// <param name="reply">发送响应的方法。</param>
    /// <returns>异步任务。</returns>
    protected abstract FTask Run(TEntity entity, TAddressRequest request, TAddressResponse response, Action reply);
}
#endif
