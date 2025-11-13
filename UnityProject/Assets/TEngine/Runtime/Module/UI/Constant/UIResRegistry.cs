using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace TEngine
{
    public static class UIResRegistry
    {
        private static readonly Dictionary<RuntimeTypeHandle, UIResInfo> _typeHandleMap = new();

        public readonly struct UIResInfo
        {
            public readonly string Location;
            public readonly EUIResLoadType LoadType;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public UIResInfo(string location, EUIResLoadType loadType)
            {
                Location = location;
                LoadType = loadType;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Register(Type holderType, string location, EUIResLoadType loadType)
        {
            var handle = holderType.TypeHandle;
            _typeHandleMap[handle] = new UIResInfo(location, loadType);
        }

        public static bool TryGet(RuntimeTypeHandle handle, out UIResInfo info)
        {
            if (_typeHandleMap.TryGetValue(handle, out info))
                return true;

            var t = Type.GetTypeFromHandle(handle);

            if (TryReflectAndRegister(t, out info))
                return true;

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryReflectAndRegister(Type holderType, out UIResInfo info)
        {
            var cad = CustomAttributeData.GetCustomAttributes(holderType)
                .FirstOrDefault(a => a.AttributeType.Name == nameof(UIResAttribute));
            string resLocation = string.Empty;
            EUIResLoadType resLoadType = EUIResLoadType.AssetBundle;
            if (cad != null)
            {
                var args = cad.ConstructorArguments;
                if (args.Count > 0) resLocation = (string)(args[0].Value ?? string.Empty);
                if (args.Count > 1) resLoadType = (EUIResLoadType)(args[1].Value ?? EUIResLoadType.AssetBundle);
                Register(holderType, resLocation, resLoadType);
                info = _typeHandleMap[holderType.TypeHandle];
                return true;
            }

            info = default;
            return false;
        }
    }
}