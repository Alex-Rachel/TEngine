using System.Diagnostics;
using UnityEngine;

namespace YooAsset
{
    internal class YooAssetsDriver : MonoBehaviour
    {
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnRuntimeInitialize()
        {
            LastestUpdateFrame = 0;
        }
#endif

        private static int LastestUpdateFrame = 0;

        void Update()
        {
            DebugCheckDuplicateDriver();
            YooAssets.Update();
        }

#if UNITY_EDITOR
        void OnApplicationQuit()
        {
            // 说明：在编辑器下确保播放被停止时IO类操作被终止。
            YooAssets.ClearAllPackageOperation();
        }
#endif

        [Conditional("DEBUG")]
        private void DebugCheckDuplicateDriver()
        {
            if (LastestUpdateFrame > 0)
            {
                if (LastestUpdateFrame == Time.frameCount)
                    YooLogger.Warning($"There are two {nameof(YooAssetsDriver)} in the scene. Please ensure there is always exactly one driver in the scene.");
            }

            LastestUpdateFrame = Time.frameCount;
        }
    }
}