using System;
using System.Runtime.CompilerServices;
using TEngine;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    internal sealed class UIMetadata
    {
        public UIBase View { get; private set; }
        public readonly UIMetaRegistry.UIMetaInfo MetaInfo;
        public readonly UIResRegistry.UIResInfo ResInfo;
        public readonly Type UILogicType;
        public bool InCache = false;

        public UIState State
        {
            get
            {
                if (View == null) return UIState.Uninitialized;
                return View.State;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateUI()
        {
            if (View is null)
            {
                View = (UIBase)InstanceFactory.CreateInstanceOptimized(UILogicType);
            }
        }

        public void Dispose()
        {
            DisposeAsync().Forget();
        }

        private async UniTask DisposeAsync()
        {
            if (State != UIState.Uninitialized && State != UIState.Destroying)
            {
                await View.InternalDestroy();
                View = null;
            }
        }

        public UIMetadata(Type uiType)
        {
            UILogicType = uiType;

            UIMetaRegistry.TryGet(UILogicType.TypeHandle, out MetaInfo);

            UIResRegistry.TryGet(MetaInfo.HolderRuntimeTypeHandle, out ResInfo);
        }
    }
}
