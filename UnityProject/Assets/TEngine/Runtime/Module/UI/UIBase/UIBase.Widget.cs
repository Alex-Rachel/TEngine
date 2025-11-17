using System;
using System.Buffers;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
                if (meta.View.State == UIState.Opened && meta.MetaInfo.NeedUpdate)
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
                ArrayPool<UIMetadata>.Shared.Return(temp, true);
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

        internal async UniTask<UIBase> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible)
        {
            metadata.CreateUI();
            await UIHolderFactory.CreateUIResourceAsync(metadata, parent, this);
            await ProcessWidget(metadata, visible);
            return (UIBase)metadata.View;
        }

        internal UIBase CreateWidgetUISync(UIMetadata metadata, Transform parent, bool visible)
        {
            metadata.CreateUI();
            UIHolderFactory.CreateUIResourceSync(metadata, parent, this);
            ProcessWidget(metadata, visible).Forget();
            return (UIBase)metadata.View;
        }

        #region CreateWidget

        #region Async

        protected async UniTask<UIBase> CreateWidgetAsync(string typeName, Transform parent, bool visible = true)
        {
            UIMetaRegistry.TryGet(typeName, out var metaRegistry);
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(metaRegistry.RuntimeTypeHandle);
            return await CreateWidgetUIAsync(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(Transform parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata =UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)await CreateWidgetUIAsync(metadata, parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(UIHolderObjectBase holder) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            metadata.CreateUI();
            UIBase widget = (UIBase)metadata.View;
            widget.BindUIHolder(holder, this);
            await ProcessWidget(metadata, true);
            return (T)widget;
        }

        #endregion


        #region Sync

        protected UIBase CreateWidgetSync(string typeName, Transform parent, bool visible = true)
        {
            UIMetaRegistry.TryGet(typeName, out var metaRegistry);
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(metaRegistry.RuntimeTypeHandle);
            return CreateWidgetUISync(metadata, parent, visible);
        }

        protected T CreateWidgetSync<T>(Transform parent, bool visible = true) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            return (T)CreateWidgetUISync(metadata, parent, visible);
        }

        protected T CreateWidgetSync<T>(UIHolderObjectBase holder) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata<T>();
            metadata.CreateUI();
            UIBase widget = (UIBase)metadata.View;
            widget.BindUIHolder(holder, this);
            ProcessWidget(metadata, true).Forget();
            return (T)widget;
        }

        #endregion

        #endregion


        private async UniTask ProcessWidget(UIMetadata meta, bool visible)
        {
            if (!AddWidget(meta)) return;
            await meta.View.InternalInitlized();
            meta.View.Visible = visible;
            if (meta.View.Visible)
            {
                await meta.View.InternalOpen();
            }
        }

        private bool AddWidget(UIMetadata meta)
        {
            if (!_children.TryAdd(meta.View, meta))
            {
                Log.Warning("Already has widget:{0}", meta.View);
                meta.Dispose();
                UIMetadataFactory.ReturnToPool(meta);
                return false;
            }

            return true;
        }

        public async UniTask RemoveWidget(UIBase widget)
        {
            if (_children.Remove(widget, out var meta))
            {
                await widget.InternalClose();
                meta.Dispose();
                UIMetadataFactory.ReturnToPool(meta);
            }
        }
    }
}
