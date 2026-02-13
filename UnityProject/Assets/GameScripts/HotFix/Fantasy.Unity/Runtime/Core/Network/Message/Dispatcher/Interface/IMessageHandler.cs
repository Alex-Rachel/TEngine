using System;
using Fantasy.Async;
using Fantasy.Pool;

// ReSharper disable InconsistentNaming

namespace Fantasy.Network.Interface
{
    /// <summary>
    /// 表示外网消息处理器的接口。
    /// </summary>
    public interface IMessageHandler
    {
        /// <summary>
        /// 获取处理的消息类型。
        /// </summary>
        /// <returns>消息类型。</returns>
        public Type Type();
        /// <summary>
        /// 处理消息的方法。
        /// </summary>
        /// <param name="session">会话对象。</param>
        /// <param name="rpcId">RPC标识。</param>
        /// <param name="message">要处理的消息。</param>
        /// <returns>异步任务。</returns>
        FTask Handle(Session session, uint rpcId, object message);
    }

    /// <summary>
    /// 泛型消息基类，实现了 <see cref="IMessageHandler"/> 接口。
    /// </summary>
    public abstract class Message<T> : IMessageHandler where T : AMessage, IMessage, new()
    {
        /// <summary>
        /// 获取处理的消息类型。
        /// </summary>
        /// <returns>消息类型。</returns>
        public Type Type()
        {
            return typeof(T);
        }

        /// <summary>
        /// 处理消息的方法。
        /// </summary>
        /// <param name="session">会话对象。</param>
        /// <param name="rpcId">RPC标识。</param>
        /// <param name="message">要处理的消息。</param>
        /// <returns>异步任务。</returns>
        public async FTask Handle(Session session, uint rpcId, object message)
        {
            var messageObject = (T)message;
            try
            {
                await Run(session, messageObject);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
            finally
            {
                messageObject.Dispose();
            }
        }

        /// <summary>
        /// 运行消息处理逻辑。
        /// </summary>
        /// <param name="session">会话对象。</param>
        /// <param name="message">要处理的消息。</param>
        /// <returns>异步任务。</returns>
        protected abstract FTask Run(Session session, T message);
    }
    
    /// <summary>
    /// 泛型消息RPC基类，实现了 <see cref="IMessageHandler"/> 接口，用于处理请求和响应类型的消息。
    /// </summary>
    public abstract class MessageRPC<TRequest, TResponse> : IMessageHandler where TRequest : IRequest where TResponse : AMessage, IResponse, new()
    {
        /// <summary>
        /// 获取处理的消息类型。
        /// </summary>
        /// <returns>消息类型。</returns>
        public Type Type()
        {
            return typeof(TRequest);
        }

        /// <summary>
        /// 处理消息的方法。
        /// </summary>
        /// <param name="session">会话对象。</param>
        /// <param name="rpcId">RPC标识。</param>
        /// <param name="message">要处理的消息。</param>
        /// <returns>异步任务。</returns>
        public async FTask Handle(Session session, uint rpcId, object message)
        {
            if (message is not TRequest request)
            {
                Log.Error($"消息类型转换错误: {message.GetType().Name} to {typeof(TRequest).Name}");
                return;
            }
            
            var response = MessageObjectPool<TResponse>.Rent();
            var isReply = false;

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
                await Run(session, request, response, Reply);
            }
            catch (Exception e)
            {
                Log.Error(e);
                response.ErrorCode = InnerErrorCode.ErrRpcFail;
            }
            finally
            {
                Reply();
                request.Dispose();
            }
        }

        /// <summary>
        /// 运行消息处理逻辑。
        /// </summary>
        /// <param name="session">会话对象。</param>
        /// <param name="request">请求消息。</param>
        /// <param name="response">响应消息。</param>
        /// <param name="reply">发送响应的方法。</param>
        /// <returns>异步任务。</returns>
        protected abstract FTask Run(Session session, TRequest request, TResponse response, Action reply);
    }
}