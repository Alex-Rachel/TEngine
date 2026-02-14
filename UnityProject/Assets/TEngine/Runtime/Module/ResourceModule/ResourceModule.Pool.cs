namespace TEngine
{
    internal partial class ResourceModule
    {
        private IObjectPool<AssetObject> _assetPool;
        
        /// <summary>
        /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public float AssetAutoReleaseInterval
        {
            get => _assetPool.AutoReleaseInterval;
            set => _assetPool.AutoReleaseInterval = value;
        }

        /// <summary>
        /// 获取或设置资源对象池的容量。
        /// </summary>
        public int AssetCapacity
        {
            get => _assetPool.Capacity;
            set => _assetPool.Capacity = value;
        }

        /// <summary>
        /// 获取或设置资源对象池对象过期秒数。
        /// </summary>
        public float AssetExpireTime
        {
            get => _assetPool.ExpireTime;
            set => _assetPool.ExpireTime = value;
        }

        /// <summary>
        /// 获取或设置资源对象池的优先级。
        /// </summary>
        public int AssetPriority
        {
            get => _assetPool.Priority;
            set => _assetPool.Priority = value;
        }
        
        /// <summary>
        /// 卸载资源。
        /// </summary>
        /// <param name="asset">要卸载的资源。</param>
        public void UnloadAsset(object asset)
        {
            if (_assetPool != null)
            {
                _assetPool.Unspawn(asset);
            }
        }
        
        /// <summary>
        /// 设置对象池管理器。
        /// </summary>
        /// <param name="objectPoolModule">对象池管理器。</param>
        public void SetObjectPoolModule(IObjectPoolModule objectPoolModule)
        {
            if (objectPoolModule == null)
            {
                throw new GameFrameworkException("Object pool manager is invalid.");
            }
            _assetPool = objectPoolModule.CreateMultiSpawnObjectPool<AssetObject>("Asset Pool");
        }
    }
}