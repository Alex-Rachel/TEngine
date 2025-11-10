using System;
using System.Buffers;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.Pool;

namespace TEngine
{
    public abstract partial class UIBase
    {
        private readonly Dictionary<UIBase, UIMetadata> _children = new();

        private void UpdateChildren()
        {
            var values = _children.Values;
            foreach (var meta in values)
            {
                if (meta.View.State == UIState.Opened)
                {
                    meta.View.InternalUpdate();
                }
            }
        }

        private async UniTask DestroyAllChildren()
        {
            var temp = ArrayPool<UIMetadata>.Shared.Rent(_children.Count);
            try
            {
                int i = 0;
                foreach (var kvp in _children)
                {
                    temp[i++] = kvp.Value;
                }

                for (int j = 0; j < i; j++)
                {
                    if (temp[j].View.Visible) await temp[j].View.InternalClose();
                    temp[j].Dispose();
                }
            }
            finally
            {
                ArrayPool<UIMetadata>.Shared.Return(temp);
            }

            _children.Clear();
        }

        private void ChildVisible(bool value)
        {
            foreach (var meta in _children.Values)
            {
                var view = meta.View;
                if (view.State == UIState.Opened)
                {
                    view.Visible = value;
                }
            }
        }

        internal async UniTask<UIBase> CreateWidget(UIMetadata metadata, Transform parent, bool visible)
        {
            metadata.CreateUI();
            await UIHolderFactory.CreateUIResource(metadata, parent, this);
            await ProcessWidget(metadata, visible);
            return (UIBase)metadata.View;
        }

        protected async UniTask<UIBase> CreateWidget(string typeName, Transform parent, bool visible = true)
        {
            UIMetaRegistry.TryGet(typeName, out var metaRegistry);
            UIMetadata metadata = UIMetadataFactory.GetMetadata(metaRegistry.RuntimeTypeHandle);
            return await CreateWidget(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidget<T>(Transform parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = MetaTypeCache<T>.Metadata;
            return (T)await CreateWidget(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidget<T>(UIHolderObjectBase holder) where T : UIBase
        {
            UIMetadata metadata = MetaTypeCache<T>.Metadata;
            metadata.CreateUI();
            UIBase widget = (UIBase)metadata.View;
            widget.BindUIHolder(holder, this);
            await ProcessWidget(metadata, true);
            return (T)widget;
        }

        private async UniTask ProcessWidget(UIMetadata meta, bool visible)
        {
            AddWidget(meta);
            await meta.View.InternalInitlized();
            meta.View.Visible = visible;
            if (meta.View.Visible)
            {
                await meta.View.InternalOpen();
            }
        }

        private void AddWidget(UIMetadata meta)
        {
            if (!_children.TryAdd(meta.View, meta))
            {
                Log.Warning("Already has widget:{0}", meta.View);
                meta.Dispose();
            }
        }

        public async UniTask RemoveWidget(UIBase widget)
        {
            if (_children.Remove(widget, out var meta))
            {
                await widget.InternalClose();
                meta.Dispose();
            }
        }
    }
}
