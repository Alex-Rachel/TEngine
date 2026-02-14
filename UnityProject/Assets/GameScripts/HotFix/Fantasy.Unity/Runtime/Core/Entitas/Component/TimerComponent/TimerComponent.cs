// ReSharper disable ForCanBeConvertedToForeach

using Fantasy.Entitas;
using Fantasy.Entitas.Interface;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#if FANTASY_UNITY
using UnityEngine;
#endif
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy.Timer
{
    public sealed class TimerComponentUpdateSystem : UpdateSystem<TimerComponent>
    {
        protected override void Update(TimerComponent self)
        {
            self.Update();
        }
    }

    /// <summary>
    /// 时间调度组件
    /// </summary>
    public sealed class TimerComponent : Entity
    {
        /// <summary>
        /// 使用系统时间创建的计时器核心。
        /// </summary>
        public TimerSchedulerNet Net { get; private set; }
#if FANTASY_UNITY
        /// <summary>
        /// 使用 Unity 时间创建的计时器核心。
        /// </summary>
        public TimerSchedulerNetUnity Unity { get; private set; }
#endif
        internal TimerComponent Initialize()
        {
            Net = new TimerSchedulerNet(Scene);
#if FANTASY_UNITY
            Unity = new TimerSchedulerNetUnity(Scene);
#endif
            return this;
        }
        public void Update()
        {
            Net.Update();
#if FANTASY_UNITY
            Unity.Update();
#endif
        }
    }
}