namespace TEngine
{
    public class AssetItemObject : ObjectBase
    {
        public static AssetItemObject Create(string location, UnityEngine.Object target)
        {
            AssetItemObject item = MemoryPool.Acquire<AssetItemObject>();
            item.Initialize(location, target);
            return item;
        }

        protected internal override void Release(bool isShutdown)
        {
            if (Target == null)
            {
                return;
            }
            ResourceExtComponent.ResourceModule?.UnloadAsset(Target);
        }
    }
}