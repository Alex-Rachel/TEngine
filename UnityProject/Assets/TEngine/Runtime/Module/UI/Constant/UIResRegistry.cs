using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            => _typeHandleMap.TryGetValue(handle, out info);
    }
}
