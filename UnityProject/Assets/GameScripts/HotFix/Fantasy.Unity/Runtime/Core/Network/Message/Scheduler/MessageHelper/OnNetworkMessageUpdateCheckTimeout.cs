using Fantasy.Helper;
using Fantasy.Timer;

#if FANTASY_NET
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
namespace Fantasy.Scheduler
{
    /// <summary>
    /// 网络消息更新检查超时。
    /// </summary>
    public sealed class OnNetworkMessageUpdateCheckTimeout : TimerHandler<NetworkMessageUpdate>
    {
        /// <summary>
        /// 超时时间（毫秒）。
        /// </summary>
        private const long Timeout = 40000;
    
        /// <summary>
        /// 处理网络消息更新检查超时。
        /// </summary>
        /// <param name="self"></param>
        /// <exception cref="NotImplementedException"></exception>
        protected override void Handler(NetworkMessageUpdate self)
        {
            var timeNow = TimeHelper.Now;
            var selfNetworkMessagingComponent = self.NetworkMessagingComponent;
        
            // 遍历请求回调字典，检查是否有超时的请求，将超时请求添加到超时消息发送列表中。
        
            foreach (var (rpcId, value) in selfNetworkMessagingComponent.RequestCallback)
            {
                if (timeNow < value.CreateTime + Timeout)
                {
                    break;
                }

                selfNetworkMessagingComponent.TimeoutRouteMessageSenders.Add(rpcId, value);
            }

            // 如果没有超时的请求，直接返回。
        
            if (selfNetworkMessagingComponent.TimeoutRouteMessageSenders.Count == 0)
            {
                return;
            }
        
            // 处理超时的请求，根据请求类型生成相应的响应消息，并进行处理。
        
            foreach (var (rpcId, routeMessageSender) in selfNetworkMessagingComponent.TimeoutRouteMessageSenders)
            {
                selfNetworkMessagingComponent.ReturnMessageSender(rpcId, routeMessageSender);
            }

            // 清空超时消息发送列表。
        
            selfNetworkMessagingComponent.TimeoutRouteMessageSenders.Clear();
        }
    }
}
#endif