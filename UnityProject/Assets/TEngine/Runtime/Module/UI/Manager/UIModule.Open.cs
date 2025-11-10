using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TEngine;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    readonly struct LayerData
    {
        public readonly List<UIMetadata> OrderList; // 维护插入顺序
        public readonly HashSet<RuntimeTypeHandle> HandleSet; // O(1)存在性检查

        public LayerData(int initialCapacity)
        {
            OrderList = new List<UIMetadata>(initialCapacity);
            HandleSet = new HashSet<RuntimeTypeHandle>();
        }
    }

    internal sealed partial class UIModule
    {
        private readonly LayerData[] _openUI = new LayerData[(int)UILayer.All];

        private async UniTask<UIBase> ShowUIImplAsync(UIMetadata meta, params object[] userDatas)
        {
            var metaInfo = GetOrCreateMeta(meta);
            await UIHolderFactory.CreateUIResource(metaInfo, UICacheLayer);
            return await FinalizeShow(metaInfo, userDatas);
        }

        private async UniTask CloseUIImpl(UIMetadata meta, bool force)
        {
            if (meta.State == UIState.Uninitialized || meta.State == UIState.CreatedUI)
            {
                return;
            }
            await meta.View.InternalClose();
            Pop(meta);
            SortWindowVisible(meta.MetaInfo.UILayer);
            SortWindowDepth(meta.MetaInfo.UILayer);
            CacheWindow(meta, force);
        }


        private UIBase GetUIImpl(UIMetadata meta)
        {
            return meta.View;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private UIMetadata GetOrCreateMeta(UIMetadata meta)
        {
            if (meta.State == UIState.Uninitialized) meta.CreateUI();
            return meta;
        }


        private async UniTask<UIBase> FinalizeShow(UIMetadata meta, object[] userDatas)
        {
            if (meta.InCache)
            {
                RemoveFromCache(meta.MetaInfo.RuntimeTypeHandle);
                Push(meta);
            }
            else
            {
                switch (meta.State)
                {
                    case UIState.Loaded:
                        Push(meta);
                        break;
                    case UIState.Opened:
                        MoveToTop(meta);
                        break;
                }
            }

            meta.View.RefreshParams(userDatas);
            await UpdateVisualState(meta);
            return meta.View;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Push(UIMetadata meta)
        {
            ref var layer = ref _openUI[meta.MetaInfo.UILayer];
            if (layer.HandleSet.Add(meta.MetaInfo.RuntimeTypeHandle))
            {
                layer.OrderList.Add(meta);
                UpdateLayerParent(meta);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Pop(UIMetadata meta)
        {
            ref var layer = ref _openUI[meta.MetaInfo.UILayer];
            if (layer.HandleSet.Remove(meta.MetaInfo.RuntimeTypeHandle))
            {
                layer.OrderList.Remove(meta);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateLayerParent(UIMetadata meta)
        {
            if (meta.View?.Holder)
            {
                var layerRect = GetLayerRect(meta.MetaInfo.UILayer);
                meta.View.Holder.transform.SetParent(layerRect);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MoveToTop(UIMetadata meta)
        {
            ref var layer = ref _openUI[meta.MetaInfo.UILayer];
            int lastIdx = layer.OrderList.Count - 1;
            int currentIdx = layer.OrderList.IndexOf(meta);

            if (currentIdx != lastIdx && currentIdx >= 0)
            {
                layer.OrderList.RemoveAt(currentIdx);
                layer.OrderList.Add(meta);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async UniTask UpdateVisualState(UIMetadata meta)
        {
            SortWindowVisible(meta.MetaInfo.UILayer);
            SortWindowDepth(meta.MetaInfo.UILayer);
            if (meta.State == UIState.Loaded)
            {
                await meta.View.InternalInitlized();
            }

            await meta.View.InternalOpen();
        }

        private void SortWindowVisible(int layer)
        {
            var list = _openUI[layer].OrderList;
            bool shouldHide = false;

            // 反向遍历避免GC分配
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var meta = list[i];
                meta.View.Visible = !shouldHide;
                shouldHide |= meta.MetaInfo.FullScreen && meta.State == UIState.Opened;
            }
        }

        private void SortWindowDepth(int layer)
        {
            var list = _openUI[layer].OrderList;
            int baseDepth = layer * LAYER_DEEP;

            for (int i = 0; i < list.Count; i++)
            {
                list[i].View.Depth = baseDepth + i * WINDOW_DEEP;
            }
        }
    }
}
