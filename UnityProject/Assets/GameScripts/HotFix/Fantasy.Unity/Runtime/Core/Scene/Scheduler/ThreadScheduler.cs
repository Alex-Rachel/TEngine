// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace Fantasy
{
    /// <summary>
    /// 线程调度器
    /// </summary>
    internal static class ThreadScheduler
    {
        /// <summary>
        /// 主线程调度器
        /// </summary>
        public static MainScheduler MainScheduler { get; private set; }
        /// <summary>
        /// 多线程调度器，根据当前CPU核心数量创建的固定线程。
        /// </summary>
        public static ISceneScheduler MultiThreadScheduler { get; private set; }
        /// <summary>
        /// 线程池调度器
        /// </summary>
        public static ISceneScheduler ThreadPoolScheduler { get; private set; }

        static ThreadScheduler()
        {
            MainScheduler = new MainScheduler();
        }
        
        internal static void Update()
        {
            MainScheduler.Update();
        }

#if FANTASY_UNITY
        internal static void LateUpdate()
        {
            MainScheduler.LateUpdate();
        }
#endif

        internal static void AddMainScheduler(Scene scene)
        {
            MainScheduler.Add(scene);
        }

        internal static void AddToMultiThreadScheduler(Scene scene)
        {
            if (MultiThreadScheduler == null)
            {
#if FANTASY_SINGLETHREAD || FANTASY_WEBGL
                MultiThreadScheduler = MainScheduler;
#else
                MultiThreadScheduler = new MultiThreadScheduler();
#endif
            }

            MultiThreadScheduler.Add(scene);
        }

        internal static void AddToThreadPoolScheduler(Scene scene)
        {
            if (ThreadPoolScheduler == null)
            {
#if FANTASY_SINGLETHREAD || FANTASY_WEBGL
                ThreadPoolScheduler = MainScheduler;
#else
                ThreadPoolScheduler = new ThreadPoolScheduler();
#endif
            }
            
            ThreadPoolScheduler.Add(scene);
        }
    }
}