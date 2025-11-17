using System;
using Cysharp.Threading.Tasks;
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

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            for (int layerIndex = 0; layerIndex < _openUI.Length; layerIndex++)
            {
                var layer = _openUI[layerIndex];
                int count = layer.OrderList.Count;
                if (count == 0) continue;
                for (int i = 0; i < count; i++)
                {
                    if (layer.OrderList.Count != count)
                    {
                        break;
                    }

                    var window = layer.OrderList[i];
                    if (window.MetaInfo.NeedUpdate)
                        window.View.InternalUpdate();
                }
            }
        }

        public UniTask<UIBase>? ShowUI(string type, params object[] userDatas)
        {
            if (UIMetaRegistry.TryGet(type, out var metaRegistry))
            {
                UIMetadata metadata = UIMetadataFactory.GetWindowMetadata(metaRegistry.RuntimeTypeHandle);
                return ShowUI(metadata, userDatas);
            }

            return null;
        }

        public T ShowUISync<T>(params object[] userDatas) where T : UIBase
        {
            return (T)ShowUIImplSync(UIMetadataFactory.GetWindowMetadata<T>(), userDatas);
        }

        public async UniTask<T> ShowUI<T>(params System.Object[] userDatas) where T : UIBase
        {
            return (T)await ShowUIAsync(UIMetadataFactory.GetWindowMetadata<T>(), userDatas);
        }


        public void CloseUI<T>(bool force = false) where T : UIBase
        {
            CloseUIImpl(UIMetadataFactory.GetWindowMetadata<T>(), force).Forget();
        }

        public T GetUI<T>() where T : UIBase
        {
            return (T)GetUIImpl(UIMetadataFactory.GetWindowMetadata<T>());
        }


        private UniTask<UIBase> ShowUI(UIMetadata meta, params System.Object[] userDatas)
        {
            return ShowUIImplAsync(meta, userDatas);
        }

        private async UniTask<UIBase> ShowUIAsync(UIMetadata meta, params System.Object[] userDatas)
        {
            return await ShowUIImplAsync(meta, userDatas);
        }


        public void CloseUI(RuntimeTypeHandle handle, bool force = false)
        {
            var metadata = UIMetadataFactory.GetWindowMetadata(handle);
            if (metadata.State != UIState.Uninitialized && metadata.State != UIState.Destroying)
            {
                CloseUIImpl(metadata, force).Forget();
            }
        }


        void IUIModule.SetTimerManager(ITimerModule timerModule)
        {
            _timerModule = timerModule;
        }
    }
}