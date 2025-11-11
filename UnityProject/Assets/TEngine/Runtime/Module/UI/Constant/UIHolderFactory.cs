using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TEngine
{
    public static class UIHolderFactory
    {
        private static readonly IResourceModule ResourceModule;

        static UIHolderFactory()
        {
            ResourceModule = ModuleSystem.GetModule<IResourceModule>();
        }

        public static async UniTask<T> CreateUIHolder<T>(Transform parent) where T : UIHolderObjectBase
        {
            if (UIResRegistry.TryGet(typeof(T).TypeHandle, out UIResRegistry.UIResInfo resInfo))
            {
                GameObject obj = await LoadUIResourcesAsync(resInfo, parent);

                return obj.GetComponent<T>();
            }

            return null;
        }

        internal static async UniTask<GameObject> LoadUIResourcesAsync(UIResRegistry.UIResInfo resInfo, Transform parent)
        {
            return resInfo.LoadType == EUIResLoadType.AssetBundle
                ? await ResourceModule.LoadGameObjectAsync(resInfo.Location, parent)
                : await InstantiateResourceAsync(resInfo.Location, parent);
        }

        internal static async UniTask CreateUIResource(UIMetadata meta, Transform parent, UIBase owner = null)
        {
            if (meta.State != UIState.CreatedUI) return;
            GameObject obj = await LoadUIResourcesAsync(meta.ResInfo, parent);
            ValidateAndBind(meta, obj, owner);
        }

        internal static void LoadUIResourcesSync(UIMetadata meta, Transform parent, UIBase owner = null)
        {
            if (meta.State != UIState.CreatedUI) return;

            GameObject obj = meta.ResInfo.LoadType == EUIResLoadType.AssetBundle
                ? ResourceModule.LoadGameObject(meta.ResInfo.Location, parent)
                : Object.Instantiate(Resources.Load<GameObject>(meta.ResInfo.Location), parent);

            ValidateAndBind(meta, obj, owner);
        }


        private static void ValidateAndBind(UIMetadata meta, GameObject holderObject, UIBase owner)
        {
            if (!holderObject) throw new NullReferenceException($"UI resource load failed: {meta.ResInfo.Location}");

            var holder = (UIHolderObjectBase)holderObject.GetComponent(meta.View.UIHolderType);

            if (holder == null)
            {
                throw new InvalidCastException($"资源{holderObject.name}上不存在{meta.View.UIHolderType.FullName}");
            }

            meta.View?.BindUIHolder(holder, owner);
        }

        private static async UniTask<GameObject> InstantiateResourceAsync(string location, Transform parent)
        {
            GameObject prefab = (GameObject)await Resources.LoadAsync<GameObject>(location);
            return Object.Instantiate(prefab, parent);
        }
    }
}
