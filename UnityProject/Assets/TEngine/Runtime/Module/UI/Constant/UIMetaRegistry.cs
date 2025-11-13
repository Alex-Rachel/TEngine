using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(RuntimeTypeHandle handle, out UIMetaInfo info)
        {
            if (_typeHandleMap.TryGetValue(handle, out info))
                return true;

            var t = Type.GetTypeFromHandle(handle);

            if (TryReflectAndRegister(t, out info))
                return true;

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(string typeName, out UIMetaInfo info)
        {
            if (_stringHandleMap.TryGetValue(typeName, out var handle))
                return TryGet(handle, out info);


            var type = TEngine.Utility.Assembly.GetType(typeName);

            if (type != null && TryReflectAndRegister(type, out info))
                return true;

            info = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegister(Type uiType, out UIMetaInfo info)
        {
            Log.Warning($"[UI] UI未注册[{uiType.FullName}] 反射进行缓存");
            Type baseType = uiType;
            Type? holderType = baseType.GetGenericArguments()[0];

            UILayer layer = UILayer.UI;
            bool fullScreen = false;
            int cacheTime = 0;

            var cad = CustomAttributeData.GetCustomAttributes(uiType)
                .FirstOrDefault(a => a.AttributeType.Name == nameof(WindowAttribute));

            if (cad != null)
            {
                var args = cad.ConstructorArguments;
                if (args.Count > 0) layer = (UILayer)(args[0].Value ?? UILayer.UI);
                if (args.Count > 1) fullScreen = (bool)(args[1].Value ?? false);
                if (args.Count > 2) cacheTime = (int)(args[2].Value ?? 0);
            }

            if (holderType != null)
            {
                Register(uiType, holderType, layer, fullScreen, cacheTime);
                info = _typeHandleMap[uiType.TypeHandle];
                return true;
            }

            info = default;
            return false;
        }
    }
}
