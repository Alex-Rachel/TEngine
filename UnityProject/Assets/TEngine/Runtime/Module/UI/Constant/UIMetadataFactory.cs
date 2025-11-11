using System;
using System.Collections.Generic;
using TEngine;

namespace TEngine
{
    internal static class MetaTypeCache<T> where T : UIBase
    {
        public static readonly UIMetadata Metadata;

        static MetaTypeCache()
        {
            var type = typeof(T);
            Metadata = UIMetadataFactory.GetMetadata(type.TypeHandle);
        }
    }

    internal static class UIMetadataFactory
    {
        private static readonly Dictionary<RuntimeTypeHandle, UIMetadata> UIWindowMetadata = new();

        internal static UIMetadata GetMetadata(RuntimeTypeHandle handle)
        {
            if (!UIWindowMetadata.TryGetValue(handle, out var meta))
            {
                meta = new UIMetadata(Type.GetTypeFromHandle(handle));
                UIWindowMetadata[handle] = meta;
            }

            return meta;
        }
    }
}
