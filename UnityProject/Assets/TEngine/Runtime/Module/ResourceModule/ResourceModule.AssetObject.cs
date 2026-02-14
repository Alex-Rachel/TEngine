using System.Collections.Generic;
using YooAsset;

namespace TEngine
{
    internal partial class ResourceModule
    {
        /// <summary>
        /// 资源对象。
        /// </summary>
        private sealed class AssetObject : ObjectBase
        {
            private AssetHandle _assetHandle = null;
            private ResourceModule _resourceModule;

            public static AssetObject Create(string name, object target, object assetHandle, ResourceModule resourceModule)
            {
                if (assetHandle == null)
                {
                    throw new GameFrameworkException("Resource is invalid.");
                }

                if (resourceModule == null)
                {
                    throw new GameFrameworkException("Resource Manager is invalid.");
                }

                AssetObject assetObject = MemoryPool.Acquire<AssetObject>();
                assetObject.Initialize(name, target);
                assetObject._assetHandle = (AssetHandle)assetHandle;
                assetObject._resourceModule = resourceModule;
                return assetObject;
            }

            public override void Clear()
            {
                base.Clear();
                _assetHandle = null;
            }

            protected internal override void OnUnspawn()
            {
                base.OnUnspawn();
            }

            protected internal override void Release(bool isShutdown)
            {
                if (!isShutdown)
                {
                    AssetHandle handle = _assetHandle;
                    if (handle is { IsValid: true })
                    {
                        handle.Dispose();
                    }
                    _assetHandle = null;
                }
            }
        }
    }
}