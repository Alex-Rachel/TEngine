using System;
using System.Runtime.InteropServices;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8625
#pragma warning disable CS8618

namespace Fantasy.Timer
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TimerAction
    {
        public long TimerId;
        public long StartTime;
        public long TriggerTime;
        public readonly object Callback;
        public readonly TimerType TimerType;
        public TimerAction(long timerId, TimerType timerType, long startTime, long triggerTime, object callback) 
        {
            TimerId = timerId;
            Callback = callback;
            TimerType = timerType;
            StartTime = startTime;
            TriggerTime = triggerTime;
        }
    }
}