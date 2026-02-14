using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// UI资源加载器接口。
    /// </summary>
    public interface IUIResourceLoader
    {
        /// <summary>
        /// 同步加载游戏物体并实例化。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="parent">资源实例父节点。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源实例。</returns>
        /// <remarks>会实例化资源到场景，无需主动UnloadAsset，Destroy时自动UnloadAsset。</remarks>
        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "");

        /// <summary>
        /// 异步加载游戏物体并实例化。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="parent">资源实例父节点。</param>
        /// <param name="cancellationToken">取消操作Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>异步游戏物体实例。</returns>
        /// <remarks>会实例化资源到场景，无需主动UnloadAsset，Destroy时自动UnloadAsset。</remarks>
        public UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "");
    }
    
    /// <summary>
    /// 默认UI资源加载器。
    /// </summary>
    public class UIResourceLoader : IUIResourceLoader
    {
        private readonly IResourceModule _resourceLoaderImp = ModuleSystem.GetModule<IResourceModule>();

        /// <summary>
        /// 同步加载游戏物体并实例化。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="parent">资源实例父节点。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源实例。</returns>
        /// <remarks>会实例化资源到场景，无需主动UnloadAsset，Destroy时自动UnloadAsset。</remarks>
        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            return _resourceLoaderImp.LoadGameObject(location, parent, packageName);
        }

        /// <summary>
        /// 异步加载游戏物体并实例化。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="parent">资源实例父节点。</param>
        /// <param name="cancellationToken">取消操作Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>异步游戏物体实例。</returns>
        /// <remarks>会实例化资源到场景，无需主动UnloadAsset，Destroy时自动UnloadAsset。</remarks>
        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            return await _resourceLoaderImp.LoadGameObjectAsync(location, parent, cancellationToken, packageName);
        }
    }
}