using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TEngine
{
    public static class UIMetaRegistry
    {
        public readonly struct UIMetaInfo
        {
            public readonly RuntimeTypeHandle RuntimeTypeHandle;
            public readonly RuntimeTypeHandle HolderRuntimeTypeHandle;
            public readonly int UILayer;
            public readonly bool FullScreen;
            public readonly int CacheTime;

            public UIMetaInfo(RuntimeTypeHandle runtimeTypeHandle, RuntimeTypeHandle holderRuntimeTypeHandle, UILayer windowLayer, bool fullScreen, int cacheTime)
            {
                RuntimeTypeHandle = runtimeTypeHandle;
                HolderRuntimeTypeHandle = holderRuntimeTypeHandle;
                UILayer = (int)windowLayer;
                FullScreen = fullScreen;
                CacheTime = cacheTime;
            }
        }

        private static readonly Dictionary<RuntimeTypeHandle, UIMetaInfo> _typeHandleMap = new();
        private static readonly Dictionary<string, RuntimeTypeHandle> _stringHandleMap = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Register(Type uiType, Type holderType, UILayer layer = UILayer.UI, bool fullScreen = false, int cacheTime = 0)
        {
            var holderHandle = holderType.TypeHandle;
            var uiHandle = uiType.TypeHandle;
            _typeHandleMap[uiHandle] = new UIMetaInfo(uiHandle, holderHandle, layer, fullScreen, cacheTime);
            _stringHandleMap[uiType.Name] = uiHandle;
        }


        public static bool TryGet(RuntimeTypeHandle handle, out UIMetaInfo info)
        {
            return _typeHandleMap.TryGetValue(handle, out info);
        }

        public static bool TryGet(string type, out UIMetaInfo info)
        {
            RuntimeTypeHandle typeHandle;
            if (_stringHandleMap.TryGetValue(type, out typeHandle))
            {
            }

            return _typeHandleMap.TryGetValue(typeHandle, out info);
        }
    }
}
