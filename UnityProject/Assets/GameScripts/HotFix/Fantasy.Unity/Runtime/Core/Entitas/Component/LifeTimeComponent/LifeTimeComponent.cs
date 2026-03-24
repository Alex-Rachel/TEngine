using Fantasy.Entitas;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy
{
    /// <summary>
    /// 生命周期组件，挂载到实体后可设定一个延迟时间，到期后自动销毁父实体。
    /// <para>可重复调用 <see cref="SetLifeTime"/> 来重置倒计时。</para>
    /// </summary>
    public sealed class LifeTimeComponent : Entity
    {
        /// <summary>当前定时器的 ID，为 0 表示没有正在运行的定时器。</summary>
        public long TimerId;

        /// <summary>
        /// 销毁组件时取消未触发的定时器，防止回调在组件销毁后仍然执行。
        /// </summary>
        public override void Dispose() 
        {
            if (IsDisposed)
            {
                return;
            }

            // 取消尚未触发的定时器，ref 重载会自动将 TimerId 置为 0
            if (TimerId != 0)
            {
                Scene.TimerComponent.Net.Remove(ref TimerId);
            }

            base.Dispose();
        }

        /// <summary>
        /// 设置父实体的存活时长，到期后自动调用 <see cref="Entity.Dispose"/> 销毁父实体。
        /// <para>若已有定时器在运行，会先取消旧定时器再重新计时。</para>
        /// </summary>
        /// <param name="milliseconds">延迟时长，单位：毫秒。</param>
        public void SetLifeTime(long milliseconds)
        {
            // 若已有定时器，先取消旧的再重新设置
            if (TimerId != 0)
            {
                Scene.TimerComponent.Net.Remove(ref TimerId);
            }

            if (Parent == null || Parent.IsDisposed)
            {
                Log.Warning($"{GetType().Name} SetLifeTime failed: Parent is null or disposed.");
                return;
            }

            // 使用 EntityReference 持有父实体的弱引用，避免父实体提前销毁后回调访问到无效对象
            EntityReference<Entity> entityReference = Parent;

            TimerId = Scene.TimerComponent.Net.OnceTimer(milliseconds, () =>
            {
                // 隐式转换：若父实体已被销毁，RuntimeId 不匹配，返回 null
                Entity entity = entityReference;

                if (entity == null)
                {
                    return;
                }

                TimerId = 0;
                entity.Dispose();
            });
        }
    }
}