using System;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace TEngine
{
    [Serializable]
    public class LoadAssetObject
    {
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public ISetAssetObject AssetObject { get; }
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public UnityEngine.Object AssetTarget { get; }
#if UNITY_EDITOR
        public bool IsSelect { get; set; }
#endif
        public LoadAssetObject(ISetAssetObject obj, UnityEngine.Object assetTarget)
        {
            AssetObject = obj;
            AssetTarget = assetTarget;
        }
    }
}