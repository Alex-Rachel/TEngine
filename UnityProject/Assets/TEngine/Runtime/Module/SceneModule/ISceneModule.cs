using System;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace TEngine
{
    public interface ISceneModule
    {
        /// <summary>
        /// 当前主场景名称。
        /// </summary>
        public string CurrentMainSceneName { get; }

        /// <summary>
        /// 加载场景。
        /// </summary>
        /// <param name="location">场景的定位地址</param>
        /// <param name="sceneMode">场景加载模式</param>
        /// <param name="suspendLoad">加载完毕时是否主动挂起</param>
        /// <param name="priority">优先级</param>
        /// <param name="gcCollect">加载主场景是否回收垃圾。</param>
        /// <param name="progressCallBack">加载进度回调。</param>
        public UniTask<Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100, bool gcCollect = true,
            Action<float> progressCallBack = null);

        /// <summary>
        /// 加载场景。
        /// </summary>
        /// <param name="location">场景的定位地址</param>
        /// <param name="sceneMode">场景加载模式</param>
        /// <param name="suspendLoad">加载完毕时是否主动挂起</param>
        /// <param name="priority">优先级</param>
        /// <param name="callBack">加载回调。</param>
        /// <param name="gcCollect">加载主场景是否回收垃圾。</param>
        /// <param name="progressCallBack">加载进度回调。</param>
        public void LoadScene(string location,
            LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false,
            uint priority = 100,
            Action<Scene> callBack = null,
            bool gcCollect = true,
            Action<float> progressCallBack = null);

        /// <summary>
        /// 激活场景（当同时存在多个场景时用于切换激活场景）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否操作成功。</returns>
        public bool ActivateScene(string location);

        /// <summary>
        /// 解除场景加载挂起操作。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否操作成功。</returns>
        public bool UnSuspend(string location);

        /// <summary>
        /// 是否为主场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否主场景。</returns>
        public bool IsMainScene(string location);

        /// <summary>
        /// 异步卸载子场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null);
        
        /// <summary>
        /// 异步卸载子场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public void Unload(string location, Action callBack = null, Action<float> progressCallBack = null);

        /// <summary>
        /// 是否包含场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否包含场景。</returns>
        public bool IsContainScene(string location);
    }
}