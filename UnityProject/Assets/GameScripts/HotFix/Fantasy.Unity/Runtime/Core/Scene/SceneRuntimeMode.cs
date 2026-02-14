namespace Fantasy
{
    /// <summary>
    /// Scene的运行类型
    /// </summary>
    public class SceneRuntimeMode
    {
        /// <summary>
        /// Scene在主线程中运行.
        /// </summary>
        public const string MainThread = "MainThread";
        /// <summary>
        /// Scene在一个独立的线程中运行.
        /// </summary>
        public const string MultiThread = "MultiThread";
        /// <summary>
        /// Scene在一个根据当前CPU核心数创建的线程池中运行.
        /// </summary>
        public const string ThreadPool = "ThreadPool";
    }
}