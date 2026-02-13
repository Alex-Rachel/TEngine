// ReSharper disable MemberCanBePrivate.Global

#if FANTASY_CONSOLE

using System;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using Fantasy.Helper;
using Fantasy.InnerMessage;
using Fantasy.Timer;
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Network
{
    public class SessionHeartbeatComponentAwakeSystem : AwakeSystem<SessionHeartbeatComponent>
    {
        protected override void Awake(SessionHeartbeatComponent self)
        {
            self.TimerComponent = self.Scene.TimerComponent;
        }
    }

    /// <summary>
    /// 负责管理会话心跳的组件。
    /// </summary>
    public class SessionHeartbeatComponent : Entity
    {
        public int TimeOut;
        public long TimerId;  
        public long LastTime;
        public long SelfRunTimeId;
        public long TimeOutTimerId;
        public long SessionRunTimeId;
        public TimerComponent TimerComponent; 
        public EntityReference<Session> Session;
        private readonly PingRequest _pingRequest = new PingRequest(); 
        
        public int Ping { get; private set; }
        
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            Stop();
            Ping = 0;
            Session = null;
            TimeOut = 0;
            SelfRunTimeId = 0;
            base.Dispose();
        }

        /// <summary>
        /// 使用指定的间隔启动心跳功能。
        /// </summary>
        /// <param name="interval">以毫秒为单位的心跳请求发送间隔。</param>
        /// <param name="timeOut">设置与服务器的通信超时时间，如果超过这个时间限制，将自动断开会话(Session)。</param>
        /// <param name="timeOutInterval">用于检测与服务器连接超时频率。</param>
        public void Start(int interval, int timeOut = 2000, int timeOutInterval = 3000)
        {
            TimeOut = timeOut + interval;
            Session = (Session)Parent;
            SelfRunTimeId = RuntimeId;
            LastTime = TimeHelper.Now;

            if (TimerComponent == null)
            {
                Log.Error("请在Unity的菜单执行Fantasy->Generate link.xml再重新打包");
                return;
            }
            
            TimerId = TimerComponent.Net.RepeatedTimer(interval, () => RepeatedSend().Coroutine());
            TimeOutTimerId = TimerComponent.Net.RepeatedTimer(timeOutInterval, CheckTimeOut);
        }

        private void CheckTimeOut()
        {
            if (TimeHelper.Now - LastTime < TimeOut)
            {
                return;
            }

            Session entityReference = Session;

            if (entityReference == null)
            {
                return;
            }

            entityReference.Dispose();
        }

        /// <summary>
        /// 停止心跳功能。
        /// </summary>
        public void Stop()
        {
            if (TimerId != 0)
            {
                TimerComponent?.Net.Remove(ref TimerId);
            }
            
            if (TimeOutTimerId != 0)
            {
                TimerComponent?.Net.Remove(ref TimeOutTimerId);
            }
        }

        /// <summary>
        /// 异步发送心跳请求并处理响应。
        /// </summary>
        /// <returns>表示进行中操作的异步任务。</returns>
        private async FTask RepeatedSend()
        {
            if (SelfRunTimeId != RuntimeId)
            {
                Stop();
                return;
            }
            
            Session session = Session;

            if (session == null)
            {
                Dispose();
                return;
            }

            try
            {
                var requestTime = TimeHelper.Now;

                var pingResponse = (PingResponse)await session.Call(_pingRequest);

                if (pingResponse.ErrorCode != 0)
                {
                    return;
                }

                var responseTime = TimeHelper.Now;
                LastTime = responseTime;
                Ping = (int)(responseTime - requestTime) / 2;
                TimeHelper.TimeDiff = pingResponse.Now + Ping - responseTime;
            }
            catch (Exception)
            {
                Dispose();
            }
        }
    }
}
#endif