using System;
using Fantasy.Async;
using Fantasy.Entitas;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy
{
    public static class LifeTimeExtendHelper
    {
        /// <summary>
        /// 设置父实体的存活时长，到期后自动调用 <see cref="Entity.Dispose"/> 销毁父实体。
        /// <para>若已有定时器在运行，会先取消旧定时器再重新计时。</para>
        /// </summary>
        /// <param name="self">父实体</param>
        /// <param name="milliseconds">延迟时长，单位：毫秒。</param>
        public static void SetLifeTime(this Entity self, long milliseconds)
        {
            var lifeTimeComponent = self.GetComponent<LifeTimeComponent>() ?? self.AddComponent<LifeTimeComponent>();
            lifeTimeComponent.SetLifeTime(milliseconds);
        }

        /// <summary>
        /// 设置父实体的延迟销毁定时器。
        /// 若已存在定时器，会先取消旧的再重新注册，实现"刷新超时"效果。
        /// </summary>
        /// <param name="self">父实体</param>
        /// <param name="timeout">超时时间（毫秒），默认 3000ms</param>
        /// <param name="task">超时触发后、销毁父实体前执行的可选异步任务</param>
#if FANTASY_UNITY
        public static void SetDestroyTimeout(this Entity self, int timeout = 3000, Func<FTask> task = null)
#endif
#if FANTASY_NET
        public static void SetDestroyTimeout(this Entity self, int timeout = 3000, Func<FTask>? task = null)
#endif
        {
            var entityTimeoutComponent = self.GetComponent<EntityTimeoutComponent>() ?? self.AddComponent<EntityTimeoutComponent>();
            entityTimeoutComponent.SetDestroyTimeout(timeout, task);
        }
    
        /// <summary>
        /// 取消当前的延迟销毁定时器。
        /// </summary>
        public static void CancelDestroyTimeout(this Entity self)
        {
            self.GetComponent<EntityTimeoutComponent>()?.CancelDestroyTimeout();
        }
    
        /// <summary>
        /// 检查实体是否挂载了延迟销毁组件 <see cref="EntityTimeoutComponent"/>。
        /// </summary>
        /// <param name="self">要检查的实体</param>
        /// <returns>如果挂载了组件则返回 true，否则返回 false。</returns>
        public static bool HasDestroyTimeout(this Entity self)
        {
            return self.GetComponent<EntityTimeoutComponent>() != null;
        }
    }
}