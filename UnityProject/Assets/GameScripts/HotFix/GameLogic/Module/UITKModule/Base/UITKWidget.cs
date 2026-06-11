using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// UIToolkit Widget 组件基类。独立 UXML，有完整生命周期。
    /// </summary>
    public class UITKWidget : UITKBase
    {
        public override UIType Type => UIType.Widget;

        private VisualTreeAsset _widgetAsset;
        private bool _fromResource;

        /// <summary>
        /// 获取所属 Window（向上遍历）。
        /// </summary>
        public UITKWindow OwnerWindow
        {
            get
            {
                var current = _parent;
                while (current != null)
                {
                    if (current.Type == UIType.Window)
                        return current as UITKWindow;
                    current = current.Parent;
                }
                return null;
            }
        }

        // ━━━ 列表虚拟化支持 ━━━

        /// <summary>
        /// ListView 虚拟化绑定数据。
        /// </summary>
        public virtual void OnBindData(object data, int index) { }

        /// <summary>
        /// ListView 虚拟化解绑数据。
        /// </summary>
        public virtual void OnUnbindData() { }

        // ━━━ 创建路径 ━━━

        /// <summary>
        /// 通过已存在的 VisualElement 创建（同步）。
        /// </summary>
        internal bool Create(UITKBase parent, VisualElement widgetRoot, bool visible)
        {
            _parent = parent;
            RootElement = widgetRoot;

            if (RootElement == null)
            {
                Log.Error($"UITKWidget Create failed: widgetRoot is null for {GetType().Name}");
                return false;
            }

            InitWidget(visible);
            return true;
        }

        /// <summary>
        /// 通过资源路径异步创建。
        /// </summary>
        internal async UniTask CreateByPath(UITKBase parent, VisualElement parentElement, string location, bool visible)
        {
            _parent = parent;

            _widgetAsset = await UITKModule.Resource.LoadVisualTreeAssetAsync(location);
            RootElement = _widgetAsset.CloneTree();
            parentElement.Add(RootElement);

            InitWidget(visible);
        }

        /// <summary>
        /// 通过 Resources 同步创建。
        /// </summary>
        internal bool CreateFromResources(UITKBase parent, VisualElement parentElement, string location, bool visible)
        {
            _parent = parent;
            _fromResource = true;

            _widgetAsset = UnityEngine.Resources.Load<VisualTreeAsset>(location);
            if (_widgetAsset == null)
            {
                Log.Error($"UITKWidget CreateFromResources failed: {location} not found");
                return false;
            }

            RootElement = _widgetAsset.CloneTree();
            parentElement.Add(RootElement);

            InitWidget(visible);
            return true;
        }

        private void InitWidget(bool visible)
        {
            _parent.ListChild.Add(this);
            _parent.SetUpdateDirty();

            __UITKAutoBind(RootElement);  // 自动绑定 UI 元素
            __UITKAutoBindEvents();       // 自动绑定事件
            Inject();
            OnCreate();
            RegisterEvent();
            OnRefresh();

            IsPrepare = true;

            if (!visible)
            {
                RootElement.style.display = DisplayStyle.None;
            }
        }

        // ━━━ 销毁 ━━━

        /// <summary>
        /// 销毁 Widget。
        /// </summary>
        public void Destroy()
        {
            _parent?.ListChild.Remove(this);
            _parent?.SetUpdateDirty();
            InternalDestroy();
        }

        internal void InternalDestroy()
        {
            OnDestroy();
            __UITKAutoUnbindEvents();     // 自动解绑事件
            RemoveAllUIEvent();

            for (int i = ListChild.Count - 1; i >= 0; i--)
            {
                ListChild[i].Destroy();
            }
            ListChild.Clear();

            RootElement?.RemoveFromHierarchy();

            if (_widgetAsset != null && !_fromResource)
            {
                UITKModule.Resource.Unload(_widgetAsset);
                _widgetAsset = null;
            }
        }

        // ━━━ Update ━━━

        internal void InternalUpdate()
        {
            if (!IsPrepare) return;
            OnUpdate();
        }
    }
}
