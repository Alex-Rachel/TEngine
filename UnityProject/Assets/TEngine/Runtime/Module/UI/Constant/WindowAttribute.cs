using System;
using UnityEngine;

namespace TEngine
{

    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        /// <summary>
        /// 窗口层级
        /// </summary>
        public readonly UILayer WindowLayer;

        /// <summary>
        /// 全屏窗口标记。
        /// </summary>
        public readonly bool FullScreen;

        /// <summary>
        /// 延时关闭
        /// </summary>
        public readonly int CacheTime;

        /// <summary>
        ///
        /// </summary>
        /// <param name="windowLayer">显示层级</param>
        /// <param name="fullScreen">是否全屏遮挡</param>
        /// <param name="cacheTime">缓存时间/s  -1永久 0不 >=1生效</param>
        public WindowAttribute(UILayer windowLayer, bool fullScreen = false, int cacheTime = 0)
        {
            WindowLayer = windowLayer;
            FullScreen = fullScreen;
            CacheTime = cacheTime;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class UIResAttribute : Attribute
    {
        public readonly string ResLocation;
        public readonly EUIResLoadType ResLoadType;

        public UIResAttribute(string location, EUIResLoadType loadType)
        {
            ResLocation = location;
            ResLoadType = loadType;
        }
    }

    public enum EUIResLoadType:byte
    {
        Resources,
        AssetBundle
    }
}
