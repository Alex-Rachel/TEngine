using Cysharp.Threading.Tasks;

namespace TEngine
{
    internal partial class ResourceExtComponent
    {
        /// <summary>
        /// 资源组件。
        /// </summary>
        private static IResourceModule _resourceModule;

        private LoadAssetCallbacks _loadAssetCallbacks;
        
        public static IResourceModule ResourceModule => _resourceModule;

        private void InitializedResources()
        {
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            _loadAssetCallbacks = new LoadAssetCallbacks(OnLoadAssetSuccess, OnLoadAssetFailure);
        }

        private void OnLoadAssetFailure(string assetName, LoadResourceStatus status, string errormessage, object userdata)
        {
            _assetLoadingList.Remove(assetName);
            Log.Error("Can not load asset from '{1}' with error message '{2}'.", assetName, errormessage);
        }

        private void OnLoadAssetSuccess(string assetName, object asset, float duration, object userdata)
        {
            _assetLoadingList.Remove(assetName);
            ISetAssetObject setAssetObject = (ISetAssetObject)userdata;
            UnityEngine.Object assetObject = asset as UnityEngine.Object;
            if (assetObject != null)
            {
                _assetItemPool.Register(AssetItemObject.Create(setAssetObject.Location, assetObject), true);
                SetAsset(setAssetObject, assetObject);
            }
            else
            {
                Log.Error($"Load failure asset type is {asset.GetType()}.");
            }
        }

        /// <summary>
        /// 通过资源系统设置资源。
        /// </summary>
        /// <param name="setAssetObject">需要设置的对象。</param>
        public async UniTaskVoid SetAssetByResources<T>(ISetAssetObject setAssetObject) where T : UnityEngine.Object
        {
            await TryWaitingLoading(setAssetObject.Location);
            
            if (_assetItemPool.CanSpawn(setAssetObject.Location))
            {
                var assetObject = (T)_assetItemPool.Spawn(setAssetObject.Location).Target;
                SetAsset(setAssetObject, assetObject);
            }
            else
            {
                _assetLoadingList.Add(setAssetObject.Location);
                _resourceModule.LoadAssetAsync(setAssetObject.Location, typeof(T), 0, _loadAssetCallbacks, setAssetObject);
            }
        }
    }
}