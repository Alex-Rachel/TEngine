using System;
using System.Collections.Generic;
using TEngine;

namespace TEngine
{
    internal sealed partial class UIModule
    {
        private readonly Dictionary<RuntimeTypeHandle, (UIMetadata, int)> m_CacheWindow = new();

        private void CacheWindow(UIMetadata uiMetadata, bool force)
        {
            if (uiMetadata == null || uiMetadata.View == null)
            {
                Log.Error(" ui not exist!");
                return;
            }

            if (force || uiMetadata.MetaInfo.CacheTime == 0)
            {
                uiMetadata.Dispose();
                return;
            }

            RemoveFromCache(uiMetadata.MetaInfo.RuntimeTypeHandle);
            int tiemrId = -1;

            uiMetadata.View.Holder.transform.SetParent(UICacheLayer);
            if (uiMetadata.MetaInfo.CacheTime > 0)
            {
                tiemrId = _timerModule.AddTimer(OnTimerDiposeWindow, uiMetadata.MetaInfo.CacheTime, false, true, uiMetadata);
            }

            uiMetadata.InCache = true;
            m_CacheWindow.Add(uiMetadata.MetaInfo.RuntimeTypeHandle, (uiMetadata, tiemrId));
        }

        private void OnTimerDiposeWindow(object[] args)
        {
            UIMetadata meta = args[0] as UIMetadata;
            meta?.Dispose();
            RemoveFromCache(meta.MetaInfo.RuntimeTypeHandle);
        }

        private void RemoveFromCache(RuntimeTypeHandle typeHandle)
        {
            if (m_CacheWindow.TryGetValue(typeHandle, out var result))
            {
                m_CacheWindow.Remove(typeHandle);
                result.Item1.InCache = false;
                if (result.Item2 > 0)
                {
                    _timerModule.RemoveTimer(result.Item2);
                }
            }
        }
    }
}
