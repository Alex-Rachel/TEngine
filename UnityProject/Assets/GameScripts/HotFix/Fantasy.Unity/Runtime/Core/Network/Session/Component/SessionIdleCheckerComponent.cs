#if FANTASY_NET
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using Fantasy.Helper;
using Fantasy.Platform.Net;
using Fantasy.Timer;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy.Network;

public class SessionIdleCheckerComponentAwakeSystem : AwakeSystem<SessionIdleCheckerComponent>
{
    protected override void Awake(SessionIdleCheckerComponent self)
    {
        self.TimerComponent = self.Scene.TimerComponent;
    }
}

/// <summary>
/// 负责检查会话空闲超时的组件。
/// </summary>
public class SessionIdleCheckerComponent : Entity
{
    /// <summary>
    /// 空闲超时时间（毫秒）
    /// </summary>
    private long _timeOut;  
    /// <summary>
    /// 检查计时器的 ID
    /// </summary>
    private long _timerId; 
    /// <summary>
    /// 用于确保组件完整性的自身运行时 ID
    /// </summary>
    private long _selfRuntimeId; 
    /// <summary>
    /// 对会话对象的引用
    /// </summary>
    private Session _session; 
    public TimerComponent TimerComponent;

    /// <summary>
    /// 重写 Dispose 方法以释放资源。
    /// </summary>
    public override void Dispose()
    {
        Stop(); // 停止检查计时器
        _timeOut = 0; // 重置空闲超时时间
        _selfRuntimeId = 0; // 重置自身运行时 ID
        _session = null; // 清除会话引用
        base.Dispose();
    }

    /// <summary>
    /// 使用指定的间隔和空闲超时时间启动空闲检查功能。
    /// </summary>
    /// <param name="interval">以毫秒为单位的检查间隔。</param>
    /// <param name="timeOut">以毫秒为单位的空闲超时时间。</param>
    internal void Start(int interval, int timeOut)
    {
        _timeOut = timeOut;
        _session = (Session)Parent;
        _selfRuntimeId = RuntimeId;
        // 安排重复计时器，在指定的间隔内执行 Check 方法
        _timerId = TimerComponent.Net.RepeatedTimer(interval, Check);
    }

    /// <summary>
    /// 重新开始心跳检查
    /// </summary>
    /// <param name="interval">以毫秒为单位的检查间隔。</param>
    /// <param name="timeOut">以毫秒为单位的空闲超时时间。</param>
    public void Restart(int interval, int timeOut)
    {
        Stop();
        Start(interval, timeOut);
    }

    /// <summary>
    /// 停止空闲检查功能。
    /// </summary>
    private void Stop()
    {
        if (_timerId == 0)
        {
            return;
        }

        TimerComponent.Net.Remove(ref _timerId);
    }

    /// <summary>
    /// 执行空闲检查操作。
    /// </summary>
    private void Check()
    {
        if (_selfRuntimeId != RuntimeId || IsDisposed || _session == null)
        {
            Stop();
            return;
        }

        var timeNow = TimeHelper.Now;
        
        if (timeNow - _session.LastReceiveTime < _timeOut)
        {
            return;
        }
#if FANTASY_DEBUG
        Log.Warning($"session timeout id:{Id} timeNow:{timeNow} _session.LastReceiveTime:{_session.LastReceiveTime} _timeOut:{_timeOut}");
#endif
        _session.Dispose();
    }
}
#endif