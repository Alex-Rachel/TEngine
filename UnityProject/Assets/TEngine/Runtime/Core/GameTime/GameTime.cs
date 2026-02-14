using UnityEngine;
// ReSharper disable InconsistentNaming
namespace TEngine
{
    /// <summary>
    /// 游戏时间。
    /// <remarks>提供从Unity获取时间信息的接口。</remarks>
    /// </summary>
    public static class GameTime
    {
        /// <summary>
        /// 此帧开始时的时间（只读）。
        /// </summary>
        public static float time;

        /// <summary>
        /// 从上一帧到当前帧的间隔（秒）（只读）。
        /// </summary>
        public static float deltaTime;

        /// <summary>
        /// timeScale从上一帧到当前帧的独立时间间隔（以秒为单位）（只读）。
        /// </summary>
        public static float unscaledDeltaTime;

        /// <summary>
        /// 执行物理和其他固定帧速率更新的时间间隔（以秒为单位）。
        /// <remarks>如MonoBehavior的MonoBehaviour.FixedUpdate。</remarks>
        /// </summary>
        public static float fixedDeltaTime;
        
        /// <summary>
        /// 自游戏开始以来的总帧数（只读）。
        /// </summary>
        public static float frameCount;
        
        /// <summary>
        /// timeScale此帧的独立时间（只读）。这是自游戏开始以来的时间（以秒为单位）。
        /// </summary>
        public static float unscaledTime;

        /// <summary>
        /// 采样一帧的时间。
        /// </summary>
        public static void StartFrame()
        {
            time = Time.time;
            deltaTime = Time.deltaTime;
            unscaledDeltaTime = Time.unscaledDeltaTime;
            fixedDeltaTime = Time.fixedDeltaTime;
            frameCount = Time.frameCount;
            unscaledTime = Time.unscaledTime;
        }
    }
}