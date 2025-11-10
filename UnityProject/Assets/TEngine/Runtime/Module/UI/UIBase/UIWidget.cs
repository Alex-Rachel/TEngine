using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace TEngine
{
    public abstract class UIWidget : UIBase
    {
        internal UIBase _parent;
        internal UIBase Parent => _parent;

        public void Open(params System.Object[] userDatas)
        {
            RefreshParams(userDatas);
            InternalOpen().Forget();
        }

        public void Close()
        {
            InternalClose().Forget();
        }

        public void Destroy()
        {
            Parent.RemoveWidget(this).Forget();
        }
    }

    public abstract class UIWidget<T> : UIWidget where T : UIHolderObjectBase
    {
        protected T baseui => (T)Holder;
        internal sealed  override Type UIHolderType => typeof(T);

        internal sealed  override void BindUIHolder(UIHolderObjectBase holder, UIBase owner)
        {
            if (_state != UIState.CreatedUI)
                throw new InvalidOperationException("UI already Created");

            Holder = holder;
            _parent = owner;
            _canvas = Holder.transform.GetComponent<Canvas>();
            _raycaster = Holder.transform.GetComponent<GraphicRaycaster>();
            Depth = owner.Depth + 5;
            _state = UIState.Loaded;
        }
    }
}
