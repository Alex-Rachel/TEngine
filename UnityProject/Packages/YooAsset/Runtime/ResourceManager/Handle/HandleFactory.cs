using System;
using System.Collections.Generic;

namespace YooAsset
{
    internal static class HandleFactory
    {
        private static readonly Dictionary<Type, Func<ProviderOperation, HandleBase>> _handleFactory = new Dictionary<Type, Func<ProviderOperation, HandleBase>>()
        {
            { typeof(AssetHandle), op => new AssetHandle(op) },
            { typeof(SceneHandle), op => new SceneHandle(op) },
            { typeof(SubAssetsHandle), op => new SubAssetsHandle(op) },
            { typeof(AllAssetsHandle), op => new AllAssetsHandle(op) },
            { typeof(RawFileHandle), op => new RawFileHandle(op) }
        };

        public static HandleBase CreateHandle(ProviderOperation operation, Type type)
        {
            if (_handleFactory.TryGetValue(type, out var factory) == false)
            {
                throw new NotImplementedException($"Handle type {type.FullName} is not supported.");
            }
            return factory(operation);
        }
    }
}