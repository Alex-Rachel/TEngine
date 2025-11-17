using System;
using System.Collections.Generic;

namespace TEngine
{
    internal static class UIMetadataFactory
    {
        private static readonly Dictionary<RuntimeTypeHandle, UIMetadata> UIWindowMetadata = new();

        private static readonly IObjectPool<UIMetadataObject> m_UIMetadataPool;

        static UIMetadataFactory()
        {
            m_UIMetadataPool = ModuleSystem.GetModule<IObjectPoolModule>().CreateSingleSpawnObjectPool<UIMetadataObject>("UI Metadata Pool", 60, 16, 60f, 0);
        }

        internal static UIMetadata GetWindowMetadata<T>()
        {
            return GetWindowMetadata(typeof(T).TypeHandle);
        }

        internal static UIMetadata GetWindowMetadata(RuntimeTypeHandle handle)
        {
            if (!UIWindowMetadata.TryGetValue(handle, out var meta))
            {
                meta = new UIMetadata(Type.GetTypeFromHandle(handle));
                UIWindowMetadata[handle] = meta;
            }

            return meta;
        }

        internal static UIMetadata GetWidgetMetadata<T>()
        {
            return GetWidgetMetadata(typeof(T));
        }

        internal static UIMetadata GetWidgetMetadata(RuntimeTypeHandle handle)
        {
            return GetFromPool(Type.GetTypeFromHandle(handle));
        }

        internal static UIMetadata GetWidgetMetadata(Type type)
        {
            return GetFromPool(type);
        }

        private static UIMetadata GetFromPool(Type type)
        {
            if (type == null) return null;


            string typeHandleKey = type.FullName;


            UIMetadataObject metadataObj = m_UIMetadataPool.Spawn(typeHandleKey);

            if (metadataObj != null && metadataObj.Target != null)
            {
                return (UIMetadata)metadataObj.Target;
            }


            UIMetadata newMetadata = new UIMetadata(type);
            UIMetadataObject newMetadataObj = UIMetadataObject.Create(newMetadata, typeHandleKey);

            m_UIMetadataPool.Register(newMetadataObj, true);

            return newMetadata;
        }


        internal static void ReturnToPool(UIMetadata metadata)
        {
            if (metadata == null) return;
            m_UIMetadataPool.Unspawn(metadata);
        }
    }
}
