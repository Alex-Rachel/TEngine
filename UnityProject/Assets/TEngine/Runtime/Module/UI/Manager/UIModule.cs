using System;
using Cysharp.Threading.Tasks;
using dnlib.DotNet;
using TEngine;
using UnityEngine;

namespace TEngine
{
    internal sealed partial class UIModule : Module, IUIModule
    {
        public int Priority { get; }

        public override void OnInit()
        {
        }

        public override void Shutdown()
        {
        }

        private ITimerModule _timerModule;


        public UniTask<UIBase>? ShowUI(string type, params object[] userDatas)
        {
            if (UIMetaRegistry.TryGet(type, out var metaRegistry))
            {
                UIMetadata metadata = UIMetadataFactory.GetMetadata(metaRegistry.RuntimeTypeHandle);
                return ShowUI(metadata, userDatas);
            }

            return null;
        }


        public UniTask<UIBase> ShowUI<T>(params System.Object[] userDatas) where T : UIBase
        {
            return ShowUI(MetaTypeCache<T>.Metadata, userDatas);
        }

        public async UniTask<T> ShowUIAsync<T>(params System.Object[] userDatas) where T : UIBase
        {
            return (T)await ShowUIAsync(MetaTypeCache<T>.Metadata, userDatas);
        }


        public void CloseUI<T>(bool force = false) where T : UIBase
        {
            CloseUIImpl(MetaTypeCache<T>.Metadata, force).Forget();
        }

        public T GetUI<T>() where T : UIBase
        {
            return (T)GetUI(MetaTypeCache<T>.Metadata);
        }


        private UniTask<UIBase> ShowUI(UIMetadata meta, params System.Object[] userDatas)
        {
            return ShowUIImplAsync(meta, userDatas);
        }

        private UniTask<UIBase> ShowUIAsync(UIMetadata meta, params System.Object[] userDatas)
        {
            return ShowUIImplAsync(meta, userDatas);
        }


        public void CloseUI(RuntimeTypeHandle handle, bool force = false)
        {
            var metadata = UIMetadataFactory.GetMetadata(handle);
            if (metadata.State != UIState.Uninitialized && metadata.State != UIState.Destroying)
            {
                CloseUIImpl(metadata, force).Forget();
            }
        }

        private UIBase GetUI(UIMetadata meta)
        {
            return (UIBase)GetUIImpl(meta);
        }


        void IUIModule.SetTimerManager(ITimerModule timerModule)
        {
            _timerModule = timerModule;
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            for (int layerIndex = 0; layerIndex < _openUI.Length; layerIndex++)
            {
                ref var layer = ref _openUI[layerIndex];
                var list = layer.OrderList;
                int count = list.Count;
                if (count == 0) continue;
                for (int i = 0; i < count; i++)
                {
                    if (list.Count != count) break;
                    var window = list[i];
                    window?.View.InternalUpdate();
                }
            }
        }
    }
}