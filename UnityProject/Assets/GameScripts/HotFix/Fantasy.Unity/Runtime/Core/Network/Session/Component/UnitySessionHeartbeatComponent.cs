// ReSharper disable MemberCanBePrivate.Global

using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using Fantasy.Helper;
using Fantasy.InnerMessage;
using Fantasy.Timer;

#if FANTASY_UNITY

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
        public TimerComponent TimerComponent; 
        public EntityReference<Session> Session;
        private readonly PingRequest _pingRequest = new PingRequest();
        
        // Ping滑动窗口及其累加和
        private int _pingSum;
        private int _maxPingSamples;
        private readonly Queue<int> _pingSamples = new Queue<int>();
        /// <summary>
        /// 当前Ping延迟（毫秒，滑动均值）
        /// </summary>
        public int PingMilliseconds { get; private set; }
        /// <summary>
        /// 当前Ping延迟（秒，浮点数，通常用于调试）
        /// </summary>
        public float PingSeconds => PingMilliseconds / 1000f;
        
        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            Stop();
            Session = null;
            TimeOut = 0;
            LastTime = 0;
            SelfRunTimeId = 0;
            base.Dispose();
        }

        /// <summary>
        /// 使用指定的间隔启动心跳功能。
        /// </summary>
        /// <param name="interval">以毫秒为单位的心跳请求发送间隔。</param>
        /// <param name="timeOut">设置与服务器的通信超时时间，如果超过这个时间限制，将自动断开会话(Session)。</param>
        /// <param name="timeOutInterval">用于检测与服务器连接超时频率。</param>
        /// <param name="maxPingSamples">Ping包的采样数量</param>
        public void Start(int interval, int timeOut = 5000, int timeOutInterval = 3000, int maxPingSamples = 8)
        {
            TimeOut = timeOut + interval;
            Session = (Session)Parent;
            SelfRunTimeId = RuntimeId;
            LastTime = TimeHelper.Now;
            _maxPingSamples = maxPingSamples;
            
            if (TimerComponent == null)
            {
                Log.Error("请在Unity的菜单执行Fantasy->Generate link.xml再重新打包");
                return;
            }
            
            TimerId = TimerComponent.Unity.RepeatedTimer(interval, () =>
            {
                RepeatedSend().Coroutine();
            });
            TimeOutTimerId = TimerComponent.Unity.RepeatedTimer(timeOutInterval, CheckTimeOut);
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
                TimerComponent?.Unity.Remove(ref TimerId);
            }
            
            if (TimeOutTimerId != 0)
            {
                TimerComponent?.Unity.Remove(ref TimeOutTimerId);
            }

            _pingSum = 0;
            PingMilliseconds = 0;
            _pingSamples.Clear();
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
                
                // 计算Ping（毫秒）
                var rtt = (int)(responseTime - requestTime);
                var ping = rtt / 2;
                
                // 平滑滑动均值
                _pingSamples.Enqueue(ping);
                _pingSum += ping;
                if (_pingSamples.Count > _maxPingSamples)
                {
                    _pingSum -= _pingSamples.Dequeue();
                }

                PingMilliseconds = Math.Max(0, _pingSamples.Count > 0 ? _pingSum / _pingSamples.Count : 0);

                // 校正服务器时间（可选）
                TimeHelper.TimeDiff = pingResponse.Now + ping - responseTime;
            }
            catch (Exception)
            {
                Dispose();
            }
        }
    }
}
#endif