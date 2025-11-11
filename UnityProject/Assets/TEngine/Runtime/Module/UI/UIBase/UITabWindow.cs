using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace TEngine
{
    public abstract class UITabWindow<T> : UIBase where T : UIHolderObjectBase
    {
        // 当前激活的Tab页
        private UIWidget _activeTab;

        // 类型顺序索引（根据初始化顺序）
        private readonly List<RuntimeTypeHandle> _typeOrder = new();

        // 页面缓存字典（类型 - 父节点）
        private readonly Dictionary<RuntimeTypeHandle, Transform> _tabCache = new();

        // 已加载的Tab实例缓存
        private readonly Dictionary<RuntimeTypeHandle, UIWidget> _loadedTabs = new();

        // 加载状态字典
        private readonly Dictionary<RuntimeTypeHandle, bool> _loadingFlags = new();

        protected T baseui => (T)Holder;

        internal sealed override Type UIHolderType => typeof(T);

        protected void CloseSelf(bool forceClose = false)
        {
            ModuleSystem.GetModule<IUIModule>().CloseUI(RuntimeTypeHandler, forceClose);
        }

        internal sealed override void BindUIHolder(UIHolderObjectBase holder, UIBase owner)
        {
            if (_state != UIState.CreatedUI)
                throw new InvalidOperationException("UI already Created");

            Holder = holder;
            _canvas = Holder.transform.GetComponent<Canvas>();
            _canvas.overrideSorting = true;
            _raycaster = Holder.transform.GetComponent<GraphicRaycaster>();
            Holder.RectTransform.localPosition = Vector3.zero;
            Holder.RectTransform.pivot = new Vector2(0.5f, 0.5f);
            Holder.RectTransform.anchorMin = Vector2.zero;
            Holder.RectTransform.anchorMax = Vector2.one;
            Holder.RectTransform.offsetMin = Vector2.zero;
            Holder.RectTransform.offsetMax = Vector2.zero;
            Holder.RectTransform.localScale = Vector3.one;
            _state = UIState.Loaded;
        }

        // 初始化方法（泛型版本）
        protected void InitTabVirtuallyView<TTab>(Transform parent = null) where TTab : UIWidget
        {
            var metadata = MetaTypeCache<TTab>.Metadata;
            CacheTabMetadata(metadata, parent);
        }

        // 初始化方法（类型名版本）
        protected void InitTabVirtuallyView(string typeName, Transform parent = null)
        {
            if (UIMetaRegistry.TryGet(typeName, out var metaRegistry))
            {
                var metadata = UIMetadataFactory.GetMetadata(metaRegistry.RuntimeTypeHandle);
                CacheTabMetadata(metadata, parent);
            }
        }

        private void CacheTabMetadata(UIMetadata metadata, Transform parent)
        {
            var typeHandle = metadata.MetaInfo.RuntimeTypeHandle;

            if (!_tabCache.ContainsKey(typeHandle))
            {
                _typeOrder.Add(typeHandle);
                _tabCache[typeHandle] = parent ?? baseui.RectTransform;
            }
        }

        public void SwitchTab(int index, params System.Object[] userDatas)
        {
            if (!ValidateIndex(index)) return;

            var typeHandle = _typeOrder[index];
            if (_loadingFlags.TryGetValue(typeHandle, out var isLoading) && isLoading) return;

            if (_loadedTabs.TryGetValue(typeHandle, out var loadedTab))
            {
                SwitchToLoadedTab(loadedTab, userDatas);
                return;
            }

            StartAsyncLoading(typeHandle, userDatas).Forget();
        }

        private async UniTaskVoid StartAsyncLoading(RuntimeTypeHandle typeHandle, params System.Object[] userDatas)
        {
            _loadingFlags[typeHandle] = true;

            try
            {
                var metadata = UIMetadataFactory.GetMetadata(typeHandle);
                var parent = _tabCache[typeHandle];

                var widget = await CreateWidget(metadata, parent, false);
                if (widget is not UIWidget tabWidget) return;

                _loadedTabs[typeHandle] = tabWidget;
                SwitchToLoadedTab(tabWidget, userDatas);
            }
            catch (Exception e)
            {
                Debug.LogError($"Tab load failed: {e}");
            }
            finally
            {
                _loadingFlags.Remove(typeHandle);
            }
        }

        private void SwitchToLoadedTab(UIWidget targetTab, params System.Object[] userDatas)
        {
            if (_activeTab == targetTab) return;

            _activeTab?.Close();
            _activeTab = targetTab;
            targetTab.Open(userDatas);
        }

        private bool ValidateIndex(int index)
        {
            if (index >= 0 && index < _typeOrder.Count) return true;

            Debug.LogError($"Invalid tab index: {index}");
            return false;
        }

    }
}
