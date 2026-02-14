using System;

namespace Fantasy
{
    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class SceneAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        public int SceneType;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sceneType"></param>
        public SceneAttribute(int sceneType)
        {
            SceneType = sceneType;
        }
    }
}